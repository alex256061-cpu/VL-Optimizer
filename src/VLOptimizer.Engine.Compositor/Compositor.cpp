#include "Compositor.h"

#include <d3dcompiler.h>

#include <algorithm>
#include <vector>

namespace vl {

namespace {

constexpr wchar_t kWindowClass[] = L"VLCompositorWindow";
constexpr int kMaxResolveCandidates = 512;
constexpr int64_t kFpsWindowMs = 1000;

std::wstring GetLastErrorText(DWORD code) {
    wchar_t* buffer = nullptr;
    DWORD length = FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
                                  nullptr, code, 0, reinterpret_cast<wchar_t*>(&buffer), 0, nullptr);
    std::wstring text = (length > 0 && buffer != nullptr) ? std::wstring(buffer, length) : L"erro " + std::to_wstring(code);
    if (buffer != nullptr) LocalFree(buffer);
    while (!text.empty() && (text.back() == L'\n' || text.back() == L'\r')) text.pop_back();
    return text;
}

std::wstring ToLower(std::wstring value) {
    std::transform(value.begin(), value.end(), value.begin(), [](wchar_t c) { return static_cast<wchar_t>(towlower(c)); });
    return value;
}

bool WindowTitleMatches(HWND window, const std::wstring& wanted) {
    wchar_t title[512];
    int length = GetWindowTextW(window, title, static_cast<int>(std::size(title)));
    if (length <= 0) return false;
    return ToLower(std::wstring(title, length)).find(wanted) != std::wstring::npos;
}

bool ProcessNameMatches(HWND window, const std::wstring& wanted, uint32_t* processId) {
    DWORD owner = 0;
    GetWindowThreadProcessId(window, &owner);
    if (processId != nullptr) *processId = owner;
    if (owner == 0) return false;

    // Query the image name without opening the process with write rights.
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, owner);
    if (process == nullptr) return false;
    wchar_t path[MAX_PATH];
    DWORD size = MAX_PATH;
    BOOL ok = QueryFullProcessImageNameW(process, 0, path, &size);
    CloseHandle(process);
    if (!ok) return false;
    const wchar_t* name = wcsrchr(path, L'\\');
    std::wstring executable = ToLower(name != nullptr ? name + 1 : path);
    return executable == wanted || executable.find(wanted) != std::wstring::npos;
}

// Largest visible top-level window, used to skip splash screens and tool windows.
bool WindowArea(HWND window, RECT* rect) {
    if (!GetClientRect(window, rect)) return false;
    return (rect->right - rect->left) > 0 && (rect->bottom - rect->top) > 0;
}

// A bilinear/cubic scale pass in fullscreen-triangle form: no vertex buffer, the vertex shader
// derives positions from the vertex id. For id 0..2 the classic pattern yields uv (0,0),(2,0),(0,2)
// covering the screen; the covered half has uv 0..1, so the sample is scaled to the 0..1 range.
const char* kScaleVertexShader = R"(
struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
VSOut main(uint id : SV_VertexID) {
    VSOut o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.uv = uv;
    o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
})";

// Scale mode is a constant: 0 nearest, 1 bilinear, 2 bicubic (Catmull-Rom weight blend).
// HLSL constant buffers pack in 16-byte registers, so the uint must live in its own float4 slot;
// packing float2+float2+uint into 20 bytes would make the runtime reject the buffer size.
const char* kScalePixelShader = R"(
Texture2D source : register(t0);
SamplerState linearSampler : register(s0);
SamplerState pointSampler : register(s1);
cbuffer ScaleParams : register(b0) {
    float2 sourceSize;
    float2 pad;
    uint mode;
    float3 pad2;
};
float4 main(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
    if (mode == 0) return source.Sample(pointSampler, uv);
    if (mode == 1) return source.Sample(linearSampler, uv);
    // Bicubic via four bilinear taps weighted by Catmull-Rom; a cheap approximation that keeps
    // text crisp compared with a single bilinear tap.
    float2 texel = 1.0 / sourceSize;
    float2 f = frac(uv * sourceSize);
    float2 o = f - 0.5;
    float2 w0 = o * (o * (o * 1.5 - 2.5) + 1.0);
    float2 w1 = (o - 1.0) * ((o - 1.0) * ((o - 1.0) * 1.5 - 2.5) + 1.0);
    float2 w2 = (o + 1.0) * ((o + 1.0) * ((o + 1.0) * 1.5 - 2.5) + 1.0);
    float2 w3 = (o + 2.0) * ((o + 2.0) * ((o + 2.0) * 1.5 - 2.5) + 1.0);
    float2 g0 = w0 + w1;
    float2 g1 = w2 + w3;
    float2 h0 = (w1 / g0) - 0.5 + 2.0 / 6.0;
    float2 h1 = (w3 / g1) + 1.5;
    float2 base = uv - o * texel;
    float4 c00 = source.Sample(linearSampler, base - h0 * texel);
    float4 c10 = source.Sample(linearSampler, base + h1 * texel);
    float4 c01 = source.Sample(linearSampler, base - h0 * texel + float2(0, texel.y));
    float4 c11 = source.Sample(linearSampler, base + h1 * texel + float2(0, texel.y));
    return lerp(lerp(c00, c10, g0.x), lerp(c01, c11, g0.x), g0.y);
})";

LRESULT CALLBACK CompositorWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
        case WM_CLOSE: ShowWindow(window, SW_HIDE); return 0;
        case WM_DESTROY: return 0;
        default: return DefWindowProcW(window, message, wParam, lParam);
    }
}

bool CompileShader(const char* source, const char* entry, const char* target, std::vector<uint8_t>* bytecode) {
    ComPtr<ID3DBlob> blob;
    ComPtr<ID3DBlob> errors;
    HRESULT hr = D3DCompile(source, strlen(source), nullptr, nullptr, nullptr, entry, target,
                            D3DCOMPILE_ENABLE_STRICTNESS | D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &blob, &errors);
    if (FAILED(hr)) return false;
    bytecode->assign(static_cast<uint8_t*>(blob->GetBufferPointer()),
                     static_cast<uint8_t*>(blob->GetBufferPointer()) + blob->GetBufferSize());
    return true;
}

}  // namespace

bool ResolveCaptureTarget(const std::wstring& executableOrTitle, CaptureTarget* out, std::wstring* error) {
    if (out == nullptr) return false;
    if (executableOrTitle.empty()) { if (error) *error = L"Nenhum alvo informado."; return false; }
    const std::wstring wanted = ToLower(executableOrTitle);

    struct Candidate { HWND window; uint32_t pid; long area; };
    std::vector<Candidate> candidates;
    candidates.reserve(kMaxResolveCandidates);

    EnumWindows([](HWND window, LPARAM param) -> BOOL {
        auto* list = reinterpret_cast<std::vector<Candidate>*>(param);
        if (!IsWindowVisible(window)) return TRUE;
        if (GetWindow(window, GW_OWNER) != nullptr) return TRUE;  // skip owned dialogs/tool windows
        if ((GetWindowLongW(window, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return TRUE;
        RECT rect;
        if (!WindowArea(window, &rect)) return TRUE;
        list->push_back({ window, 0, (rect.right - rect.left) * (rect.bottom - rect.top) });
        return TRUE;
    }, reinterpret_cast<LPARAM>(&candidates));

    const Candidate* best = nullptr;
    uint32_t bestPid = 0;
    for (const auto& candidate : candidates) {
        uint32_t pid = 0;
        bool match = ProcessNameMatches(candidate.window, wanted, &pid) || WindowTitleMatches(candidate.window, wanted);
        if (!match) continue;
        // Same executable name can appear twice (splash + real window): take the largest.
        if (best == nullptr || candidate.area > best->area) { best = &candidate; bestPid = pid; }
    }
    if (best == nullptr) {
        if (error) *error = L"Nenhuma janela visivel corresponde a \"" + executableOrTitle + L"\".";
        return false;
    }

    RECT client;
    GetClientRect(best->window, &client);
    out->Window = best->window;
    out->ProcessId = bestPid;
    out->Width = static_cast<uint32_t>(client.right - client.left);
    out->Height = static_cast<uint32_t>(client.bottom - client.top);

    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, bestPid);
    if (process != nullptr) {
        wchar_t path[MAX_PATH];
        DWORD size = MAX_PATH;
        if (QueryFullProcessImageNameW(process, 0, path, &size)) out->ProcessPath = path;
        CloseHandle(process);
    }
    return out->Width > 0 && out->Height > 0;
}

Compositor::Compositor() = default;
Compositor::~Compositor() { Stop(); }

bool Compositor::Start(const CompositorOptions& options, std::wstring* error) {
    options_ = options;
    if (!ResolveCaptureTarget(options_.TargetExecutableOrTitle, &target_, error)) return false;
    if (!CreateOutputWindow(error)) return false;
    if (!CreateDevice(error)) return false;
    if (!CreateSwapchain(error)) return false;
    if (!CreateScalePipeline(error)) return false;
    if (!CreateDuplication(error)) return false;
    return true;
}

bool Compositor::CreateOutputWindow(std::wstring* error) {
    WNDCLASSEXW wc = { sizeof(wc) };
    wc.lpfnWndProc = CompositorWindowProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = kWindowClass;
    wc.hCursor = LoadCursorW(nullptr, reinterpret_cast<LPCWSTR>(IDC_ARROW));
    wc.style = CS_OWNDC;
    if (!RegisterClassExW(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
        if (error) *error = L"RegisterClassExW: " + GetLastErrorText(GetLastError());
        return false;
    }
    const uint32_t width = options_.OutputWidth != 0 ? options_.OutputWidth : target_.Width;
    const uint32_t height = options_.OutputHeight != 0 ? options_.OutputHeight : target_.Height;
    window_ = CreateWindowExW(WS_EX_TOPMOST, kWindowClass, L"VL Scaling", WS_OVERLAPPEDWINDOW,
                              CW_USEDEFAULT, CW_USEDEFAULT, static_cast<int>(width), static_cast<int>(height),
                              nullptr, nullptr, wc.hInstance, nullptr);
    if (window_ == nullptr) {
        if (error) *error = L"CreateWindowExW: " + GetLastErrorText(GetLastError());
        return false;
    }
    ShowWindow(window_, SW_SHOW);
    outputWidth_ = width;
    outputHeight_ = height;
    return true;
}

bool Compositor::CreateDevice(std::wstring* error) {
    const D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL got;
    // BGRA_SUPPORT keeps the duplication texture compatible with the output swapchain format.
    UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags, levels, 2,
                                   D3D11_SDK_VERSION, &device_, &got, &context_);
    if (FAILED(hr)) {
        if (error) *error = L"D3D11CreateDevice falhou (o dispositivo precisa suportar copia BGRA).";
        return false;
    }
    return device_ != nullptr;
}

bool Compositor::CreateSwapchain(std::wstring* error) {
    ComPtr<IDXGIDevice> dxgiDevice;
    ComPtr<IDXGIAdapter> adapter;
    ComPtr<IDXGIFactory2> factory;
    if (FAILED(device_->QueryInterface(IID_PPV_ARGS(&dxgiDevice))) ||
        FAILED(dxgiDevice->GetAdapter(&adapter)) ||
        FAILED(adapter->GetParent(IID_PPV_ARGS(&factory)))) {
        if (error) *error = L"Falha ao obter IDXGIFactory2.";
        return false;
    }
    DXGI_SWAP_CHAIN_DESC1 desc = {};
    desc.Width = outputWidth_;
    desc.Height = outputHeight_;
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    desc.AlphaMode = DXGI_ALPHA_MODE_UNSPECIFIED;
    HRESULT hr = factory->CreateSwapChainForHwnd(device_.Get(), window_, &desc, nullptr, nullptr, &swapchain_);
    if (FAILED(hr)) {
        if (error) *error = L"CreateSwapChainForHwnd falhou.";
        return false;
    }
    return true;
}

bool Compositor::CreateScalePipeline(std::wstring* error) {
    std::vector<uint8_t> vertexBytes;
    std::vector<uint8_t> pixelBytes;
    if (!CompileShader(kScaleVertexShader, "main", "vs_5_0", &vertexBytes) ||
        !CompileShader(kScalePixelShader, "main", "ps_5_0", &pixelBytes)) {
        if (error) *error = L"Falha ao compilar shaders de escala (d3dcompiler_47.dll presente?).";
        return false;
    }
    if (FAILED(device_->CreateVertexShader(vertexBytes.data(), vertexBytes.size(), nullptr, &scaleVertexShader_)) ||
        FAILED(device_->CreatePixelShader(pixelBytes.data(), pixelBytes.size(), nullptr, &scalePixelShader_))) {
        if (error) *error = L"Create*(Shader) falhou.";
        return false;
    }

    D3D11_SAMPLER_DESC sampler = {};
    sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sampler.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampler.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampler.MaxLOD = D3D11_FLOAT32_MAX;
    if (FAILED(device_->CreateSamplerState(&sampler, &scaleSampler_))) { if (error) *error = L"CreateSamplerState(linear) falhou."; return false; }

    // The nearest-mode branch samples with s1; it must exist or the shader binds a null sampler.
    sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
    if (FAILED(device_->CreateSamplerState(&sampler, &pointSampler_))) { if (error) *error = L"CreateSamplerState(point) falhou."; return false; }

    // Two 16-byte registers: float2 sourceSize + float2 pad, then uint mode + float3 pad2.
    D3D11_BUFFER_DESC constants = {};
    constants.ByteWidth = 32;
    constants.Usage = D3D11_USAGE_DEFAULT;
    constants.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    if (FAILED(device_->CreateBuffer(&constants, nullptr, &scaleConstants_))) { if (error) *error = L"CreateBuffer(scale) falhou."; return false; }

    // Persistent scaled target: content is presented on the swapchain backbuffer, so this texture
    // only holds the scaling pass output between frames.
    D3D11_TEXTURE2D_DESC scaled = {};
    scaled.Width = outputWidth_;
    scaled.Height = outputHeight_;
    scaled.MipLevels = 1;
    scaled.ArraySize = 1;
    scaled.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    scaled.SampleDesc.Count = 1;
    scaled.Usage = D3D11_USAGE_DEFAULT;
    scaled.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    if (FAILED(device_->CreateTexture2D(&scaled, nullptr, &scaledTexture_))) { if (error) *error = L"CreateTexture2D(scaled) falhou."; return false; }
    if (FAILED(device_->CreateRenderTargetView(scaledTexture_.Get(), nullptr, &scaledTarget_))) { if (error) *error = L"CreateRenderTargetView falhou."; return false; }
    return true;
}

bool Compositor::CreateDuplication(std::wstring* error) {
    DestroyDuplication();

    ComPtr<IDXGIDevice> dxgiDevice;
    ComPtr<IDXGIAdapter1> adapter;
    if (FAILED(device_->QueryInterface(IID_PPV_ARGS(&dxgiDevice))) ||
        FAILED(dxgiDevice->GetAdapter(reinterpret_cast<IDXGIAdapter**>(adapter.GetAddressOf())))) {
        if (error) *error = L"Falha ao obter o adaptador da duplicacao.";
        return false;
    }
    duplicationAdapter_ = adapter.Detach();

    // Pick the output currently showing the target window's largest area, so the cropped region is
    // actually on screen.
    RECT targetRect = {};
    GetClientRect(target_.Window, &targetRect);
    POINT origin = { 0, 0 };
    ClientToScreen(target_.Window, &origin);
    targetRect.right = origin.x + (targetRect.right - targetRect.left);
    targetRect.bottom = origin.y + (targetRect.bottom - targetRect.top);
    targetRect.left = origin.x;
    targetRect.top = origin.y;

    UINT outputCount = 0;
    for (UINT index = 0;; ++index) {
        IDXGIOutput* output = nullptr;
        if (duplicationAdapter_->EnumOutputs(index, &output) != S_OK || output == nullptr) break;
        DXGI_OUTPUT_DESC outputDesc = {};
        output->GetDesc(&outputDesc);
        RECT overlap = {};
        if (IntersectRect(&overlap, &targetRect, &outputDesc.DesktopCoordinates)) {
            duplicationOutput_ = output;
            ++outputCount;
            break;
        }
        output->Release();
    }
    if (duplicationOutput_ == nullptr) {
        if (error) *error = L"A janela alvo nao esta em nenhum monitor visivel.";
        return false;
    }

    // The duplication object must come from the output's own adapter. When the output is on a
    // different GPU than the compositor device, DDA fails with DXGI_ERROR_UNSUPPORTED.
    ComPtr<IDXGIOutput1> output1;
    if (FAILED(duplicationOutput_->QueryInterface(IID_PPV_ARGS(&output1)))) {
        if (error) *error = L"IDXGIOutput1 indisponivel (Windows 8+ necessario).";
        return false;
    }
    HRESULT hr = output1->DuplicateOutput(device_.Get(), &duplication_);
    if (FAILED(hr)) {
        if (error) *error = hr == DXGI_ERROR_NOT_CURRENTLY_AVAILABLE
                                ? std::wstring(L"Duplicacao indisponivel: outro capturador esta ativo ou o conteudo e protegido.")
                                : std::wstring(L"DuplicateOutput falhou (0x") + std::to_wstring(static_cast<unsigned long>(hr)) + L").";
        return false;
    }

    DXGI_OUTDUPL_DESC dupDesc = {};
    duplication_->GetDesc(&dupDesc);
    captureWidth_ = dupDesc.ModeDesc.Width;
    captureHeight_ = dupDesc.ModeDesc.Height;
    return true;
}

void Compositor::DestroyDuplication() {
    duplication_.Reset();
    staging_.Reset();
    if (duplicationOutput_ != nullptr) { duplicationOutput_->Release(); duplicationOutput_ = nullptr; }
    if (duplicationAdapter_ != nullptr) { duplicationAdapter_->Release(); duplicationAdapter_ = nullptr; }
}

bool Compositor::RebindTarget(std::wstring* error) {
    CaptureTarget resolved;
    if (!ResolveCaptureTarget(options_.TargetExecutableOrTitle, &resolved, error)) return false;
    target_ = resolved;
    targetLost_ = false;
    pipelineDirty_ = true;
    return CreateDuplication(error);
}

// The crop+scale for one frame. The captured desktop frame contains the whole monitor; the target
// window's client rectangle is mapped into it so only game content is scaled.
bool Compositor::RenderFrame(std::wstring* error) {
    if (duplication_ == nullptr) {
        if (!CreateDuplication(error)) return false;
    }

    DXGI_OUTDUPL_FRAME_INFO frameInfo = {};
    ComPtr<IDXGIResource> resource;
    HRESULT hr = duplication_->AcquireNextFrame(0, &frameInfo, &resource);
    if (hr == DXGI_ERROR_WAIT_TIMEOUT) return false;  // desktop unchanged, not an error
    if (hr == DXGI_ERROR_ACCESS_LOST) {
        targetLost_ = true;
        if (error) *error = L"Captura perdida (resolucao alterada ou sessao bloqueada); reconectando.";
        DestroyDuplication();
        return false;
    }
    if (FAILED(hr) || resource == nullptr) {
        if (error) *error = L"AcquireNextFrame falhou.";
        return false;
    }

    ComPtr<ID3D11Texture2D> frameTexture;
    hr = resource.As(&frameTexture);
    if (FAILED(hr)) { duplication_->ReleaseFrame(); if (error) *error = L"Frame nao e uma textura D3D11."; return false; }

    if (!target_.Window || !IsWindow(target_.Window)) {
        duplication_->ReleaseFrame();
        targetLost_ = true;
        if (error) *error = L"A janela alvo foi fechada.";
        return false;
    }

    // Source region in desktop coordinates, clamped to the captured output.
    POINT origin = { 0, 0 };
    ClientToScreen(target_.Window, &origin);
    RECT client = {};
    GetClientRect(target_.Window, &client);
    const int width = client.right - client.left;
    const int height = client.bottom - client.top;
    if (width <= 0 || height <= 0) { duplication_->ReleaseFrame(); return false; }

    const int originX = static_cast<int>(origin.x);
    const int originY = static_cast<int>(origin.y);
    const int captureW = static_cast<int>(captureWidth_);
    const int captureH = static_cast<int>(captureHeight_);
    int left = (std::max)(0, (std::min)(originX, captureW - 1));
    int top = (std::max)(0, (std::min)(originY, captureH - 1));
    int copyWidth = (std::min)(width, captureW - left);
    int copyHeight = (std::min)(height, captureH - top);
    if (copyWidth <= 0 || copyHeight <= 0) { duplication_->ReleaseFrame(); return false; }

    // Recreate the cropped source texture when the target size changes.
    if (pipelineDirty_ || sourceWidth_ != static_cast<uint32_t>(copyWidth) || sourceHeight_ != static_cast<uint32_t>(copyHeight)) {
        D3D11_TEXTURE2D_DESC source = {};
        source.Width = static_cast<UINT>(copyWidth);
        source.Height = static_cast<UINT>(copyHeight);
        source.MipLevels = 1;
        source.ArraySize = 1;
        source.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        source.SampleDesc.Count = 1;
        source.Usage = D3D11_USAGE_DEFAULT;
        source.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        sourceTexture_.Reset();
        captureCopy_.Reset();
        sourceView_.Reset();
        captureCopyView_.Reset();
        if (FAILED(device_->CreateTexture2D(&source, nullptr, &sourceTexture_)) ||
            FAILED(device_->CreateShaderResourceView(sourceTexture_.Get(), nullptr, &sourceView_))) {
            duplication_->ReleaseFrame();
            if (error) *error = L"Falha ao criar a textura de origem.";
            return false;
        }
        sourceWidth_ = static_cast<uint32_t>(copyWidth);
        sourceHeight_ = static_cast<uint32_t>(copyHeight);
        pipelineDirty_ = false;
    }

    // CopyOnly between textures of the same format; the sub-rectangle maps the window into the frame.
    D3D11_BOX box = {};
    box.left = static_cast<UINT>(left);
    box.top = static_cast<UINT>(top);
    box.right = static_cast<UINT>(left + copyWidth);
    box.bottom = static_cast<UINT>(top + copyHeight);
    box.front = 0;
    box.back = 1;
    context_->CopySubresourceRegion(sourceTexture_.Get(), 0, 0, 0, 0, frameTexture.Get(), 0, &box);
    duplication_->ReleaseFrame();

    // Bind the scaled target and run the fullscreen scale pass. The source texture already holds the
    // cropped window region, so the triangle's 0..1 UV maps that region across the whole output.
    ID3D11RenderTargetView* targets[] = { scaledTarget_.Get() };
    context_->OMSetRenderTargets(1, targets, nullptr);
    const float clear[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    context_->ClearRenderTargetView(scaledTarget_.Get(), clear);
    D3D11_VIEWPORT viewport = {};
    viewport.Width = static_cast<float>(outputWidth_);
    viewport.Height = static_cast<float>(outputHeight_);
    viewport.MinDepth = 0.0f;
    viewport.MaxDepth = 1.0f;
    context_->RSSetViewports(1, &viewport);

    // pack=4 keeps the struct byte-identical to the HLSL cbuffer (two 16-byte registers).
    #pragma pack(push, 4)
    struct ScaleParams { float sourceWidth; float sourceHeight; float padX; float padY; uint32_t mode; float pad2[3]; };
    #pragma pack(pop)
    ScaleParams params = { static_cast<float>(sourceWidth_), static_cast<float>(sourceHeight_), 0.0f, 0.0f, static_cast<uint32_t>(options_.Scale), { 0.0f, 0.0f, 0.0f } };
    context_->UpdateSubresource(scaleConstants_.Get(), 0, nullptr, &params, 0, 0);
    context_->CSSetShader(nullptr, nullptr, 0);
    context_->VSSetShader(scaleVertexShader_.Get(), nullptr, 0);
    context_->PSSetShader(scalePixelShader_.Get(), nullptr, 0);
    ID3D11SamplerState* samplers[] = { scaleSampler_.Get(), pointSampler_.Get() };
    context_->PSSetSamplers(0, 2, samplers);
    ID3D11ShaderResourceView* views[] = { sourceView_.Get() };
    context_->PSSetShaderResources(0, 1, views);
    ID3D11Buffer* buffers[] = { scaleConstants_.Get() };
    context_->PSSetConstantBuffers(0, 1, buffers);
    context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context_->Draw(3, 0);

    // Present the scaled texture on the backbuffer.
    ComPtr<ID3D11Texture2D> backbuffer;
    if (SUCCEEDED(swapchain_->GetBuffer(0, IID_PPV_ARGS(&backbuffer)))) {
        context_->CopyResource(backbuffer.Get(), scaledTexture_.Get());
    }
    swapchain_->Present(0, 0);
    ++presented_;
    ++fpsWindowFrames_;
    MeasureFps();
    return true;
}

void Compositor::MeasureFps() {
    const int64_t now = GetTickCount64();
    if (fpsWindowStart_ == 0) { fpsWindowStart_ = now; return; }
    if (now - fpsWindowStart_ >= kFpsWindowMs) {
        const int64_t elapsed = now - fpsWindowStart_;
        fps_ = elapsed > 0 ? (static_cast<double>(fpsWindowFrames_) * 1000.0 / static_cast<double>(elapsed)) : 0.0;
        fpsWindowStart_ = now;
        fpsWindowFrames_ = 0;
    }
}

void Compositor::Stop() {
    DestroyDuplication();
    scaleConstants_.Reset();
    pointSampler_.Reset();
    scaleSampler_.Reset();
    scalePixelShader_.Reset();
    scaleVertexShader_.Reset();
    captureCopyView_.Reset();
    captureCopy_.Reset();
    sourceView_.Reset();
    sourceTexture_.Reset();
    scaledTarget_.Reset();
    scaledTexture_.Reset();
    swapchain_.Reset();
    context_.Reset();
    device_.Reset();
    if (window_ != nullptr) {
        DestroyWindow(window_);
        window_ = nullptr;
    }
    UnregisterClassW(kWindowClass, GetModuleHandleW(nullptr));
}

}  // namespace vl
