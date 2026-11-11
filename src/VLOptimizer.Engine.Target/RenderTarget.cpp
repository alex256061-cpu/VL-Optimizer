// Synthetic D3D11 target for validating the scaling pipeline.
//
// It is a real game-like renderer from the engine's point of view: its own window, its own D3D11
// device, a FLIP_DISCARD swapchain presented every frame, and animated content with frame-to-frame
// motion. That is exactly what the scaling engine captures and what LSFG needs to interpolate;
// a static desktop or a text window cannot exercise frame generation at all.
//
// The scene also renders a frame counter, a moving marker and a high-frequency pattern, so a
// captured frame can be compared before/after scaling and checked for temporal correctness.
//
// Usage: VLRenderTarget.exe [--seconds N] [--width W] [--height H]

#include <windows.h>
#include <d3d11.h>
#include <dxgi1_6.h>
#include <d3dcompiler.h>
#include <wrl/client.h>

#include <cmath>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cwchar>

using Microsoft::WRL::ComPtr;

namespace {

constexpr wchar_t kClass[] = L"VLRenderTargetWindow";
constexpr float kPi = 3.14159265358979f;

struct Options { int seconds = 120; uint32_t width = 1280; uint32_t height = 720; };

HWND g_window = nullptr;

LRESULT CALLBACK Proc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_CLOSE) { ShowWindow(window, SW_HIDE); return 0; }
    return DefWindowProcW(window, message, wParam, lParam);
}

void Print(const char* format, ...) {
    va_list args; va_start(args, format); vfprintf(stderr, format, args); va_end(args); fflush(stderr);
}

// Fullscreen triangle; uv and position from the vertex id.
const char* kVertexShader = R"(
struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
VSOut main(uint id : SV_VertexID) {
    VSOut o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.uv = uv;
    o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
})";

// Animated scene: rotating bars (fast, high-contrast -> reveals temporal artifacts), a sliding
// square (slow, large motion -> reveals LSFG ghosting), and a fine grid (reveals sharpening).
const char* kPixelShader = R"(
cbuffer Params : register(b0) { float time; float width; float height; float frames; };
float4 main(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
    float2 p = uv * float2(width, height);

    // Rotating radial bars.
    float2 centered = uv - 0.5;
    float angle = atan2(centered.y, centered.x);
    float bars = 0.5 + 0.5 * sin(angle * 16.0 + time * 4.0);

    // Vertical gradient tinted by time so each frame differs.
    float3 color = lerp(float3(0.05, 0.08, 0.2), float3(0.2, 0.35, 0.6), uv.y);
    color += float3(0.35, 0.15, 0.0) * bars;

    // Sliding square: moves across the frame once per 2 seconds.
    float2 squareCenter = float2(frac(time * 0.5) * 1.2 - 0.1, 0.5);
    float2 d = abs(uv - squareCenter);
    float square = step(d.x, 0.07) * step(d.y, 0.07);
    color = lerp(color, float3(1.0, 0.85, 0.1), square);

    // Fine checker grid at 8-pixel pitch: aliases badly without scaling, crisp with it.
    float2 cell = floor(p / 8.0);
    float checker = fmod(cell.x + cell.y, 2.0);
    color = lerp(color, color * 0.75, checker * 0.5);

    // Frame counter as a horizontal bar whose length encodes the frame number.
    float frameBar = step(p.y, height * 0.05) * step(p.x, fmod(frames, 1000.0) / 1000.0 * width);
    color = lerp(color, float3(0.0, 1.0, 0.4), frameBar);

    return float4(color, 1.0);
})";

bool Compile(const char* source, const char* entry, const char* target, ComPtr<ID3DBlob>* blob) {
    ComPtr<ID3DBlob> errors;
    HRESULT hr = D3DCompile(source, strlen(source), nullptr, nullptr, nullptr, entry, target,
                            D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, blob->GetAddressOf(), &errors);
    if (FAILED(hr)) {
        if (errors) Print("SHADER=%s\n", static_cast<const char*>(errors->GetBufferPointer()));
        return false;
    }
    return true;
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
    Options options;
    for (int i = 1; i < argc; ++i) {
        if (wcscmp(argv[i], L"--seconds") == 0 && i + 1 < argc) options.seconds = _wtoi(argv[++i]);
        else if (wcscmp(argv[i], L"--width") == 0 && i + 1 < argc) options.width = static_cast<uint32_t>(_wtoi(argv[++i]));
        else if (wcscmp(argv[i], L"--height") == 0 && i + 1 < argc) options.height = static_cast<uint32_t>(_wtoi(argv[++i]));
    }
    if (options.seconds <= 0) options.seconds = 120;

    WNDCLASSEXW wc = { sizeof(wc) };
    wc.lpfnWndProc = Proc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = kClass;
    wc.hCursor = LoadCursorW(nullptr, reinterpret_cast<LPCWSTR>(IDC_ARROW));
    wc.style = CS_OWNDC;
    RegisterClassExW(&wc);

    g_window = CreateWindowExW(0, kClass, L"VLRenderTarget", WS_OVERLAPPEDWINDOW,
                               CW_USEDEFAULT, CW_USEDEFAULT, static_cast<int>(options.width), static_cast<int>(options.height),
                               nullptr, nullptr, wc.hInstance, nullptr);
    if (g_window == nullptr) { Print("WINDOW=FAIL %lu\n", GetLastError()); return 2; }
    ShowWindow(g_window, SW_SHOW);

    const D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL got;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                                   levels, 2, D3D11_SDK_VERSION, &device, &got, &context);
    if (FAILED(hr)) { Print("DEVICE=FAIL 0x%08lX\n", static_cast<unsigned long>(hr)); return 3; }

    ComPtr<IDXGIDevice> dxgiDevice;
    ComPtr<IDXGIAdapter> adapter;
    ComPtr<IDXGIFactory2> factory;
    if (FAILED(device->QueryInterface(IID_PPV_ARGS(&dxgiDevice))) ||
        FAILED(dxgiDevice->GetAdapter(&adapter)) ||
        FAILED(adapter->GetParent(IID_PPV_ARGS(&factory)))) { Print("DXGI=FAIL\n"); return 4; }

    DXGI_SWAP_CHAIN_DESC1 desc = {};
    desc.Width = options.width;
    desc.Height = options.height;
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    desc.AlphaMode = DXGI_ALPHA_MODE_UNSPECIFIED;
    ComPtr<IDXGISwapChain1> swapchain;
    hr = factory->CreateSwapChainForHwnd(device.Get(), g_window, &desc, nullptr, nullptr, &swapchain);
    if (FAILED(hr)) { Print("SWAPCHAIN=FAIL 0x%08lX\n", static_cast<unsigned long>(hr)); return 5; }

    ComPtr<ID3D11Texture2D> backbuffer;
    ComPtr<ID3D11RenderTargetView> target;
    if (FAILED(swapchain->GetBuffer(0, IID_PPV_ARGS(&backbuffer))) ||
        FAILED(device->CreateRenderTargetView(backbuffer.Get(), nullptr, &target))) { Print("RTV=FAIL\n"); return 6; }

    ComPtr<ID3DBlob> vertexBlob;
    ComPtr<ID3DBlob> pixelBlob;
    if (!Compile(kVertexShader, "main", "vs_5_0", &vertexBlob) || !Compile(kPixelShader, "main", "ps_5_0", &pixelBlob)) return 7;
    ComPtr<ID3D11VertexShader> vertexShader;
    ComPtr<ID3D11PixelShader> pixelShader;
    if (FAILED(device->CreateVertexShader(vertexBlob->GetBufferPointer(), vertexBlob->GetBufferSize(), nullptr, &vertexShader)) ||
        FAILED(device->CreatePixelShader(pixelBlob->GetBufferPointer(), pixelBlob->GetBufferSize(), nullptr, &pixelShader))) return 8;

    struct Params { float time; float width; float height; float frames; };
    D3D11_BUFFER_DESC bufferDesc = {};
    bufferDesc.ByteWidth = sizeof(Params);
    bufferDesc.Usage = D3D11_USAGE_DEFAULT;
    bufferDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    ComPtr<ID3D11Buffer> params;
    if (FAILED(device->CreateBuffer(&bufferDesc, nullptr, &params))) return 9;

    D3D11_VIEWPORT viewport = {};
    viewport.Width = static_cast<float>(options.width);
    viewport.Height = static_cast<float>(options.height);
    viewport.MaxDepth = 1.0f;

    Print("TARGET_READY pid=%lu hwnd=%p size=%ux%u seconds=%d\n",
          GetCurrentProcessId(), static_cast<void*>(g_window), options.width, options.height, options.seconds);

    LARGE_INTEGER frequency, start, now;
    QueryPerformanceFrequency(&frequency);
    QueryPerformanceCounter(&start);
    float elapsed = 0.0f;
    float frames = 0.0f;
    double presented = 0;
    int64_t fpsMark = 0;

    while (true) {
        MSG message;
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        if (!IsWindowVisible(g_window)) break;

        QueryPerformanceCounter(&now);
        elapsed = static_cast<float>(now.QuadPart - start.QuadPart) / static_cast<float>(frequency.QuadPart);
        if (elapsed > static_cast<float>(options.seconds)) break;

        Params values = { elapsed, static_cast<float>(options.width), static_cast<float>(options.height), frames };
        context->UpdateSubresource(params.Get(), 0, nullptr, &values, 0, 0);

        const float clear[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
        context->ClearRenderTargetView(target.Get(), clear);
        context->OMSetRenderTargets(1, target.GetAddressOf(), nullptr);
        context->RSSetViewports(1, &viewport);
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(vertexShader.Get(), nullptr, 0);
        context->PSSetShader(pixelShader.Get(), nullptr, 0);
        context->PSSetConstantBuffers(0, 1, params.GetAddressOf());
        context->Draw(3, 0);

        swapchain->Present(1, 0);  // vsync: a stable, comparable base rate
        ++presented;
        ++frames;

        const int64_t elapsedMs = static_cast<int64_t>(elapsed * 1000.0f);
        if (elapsedMs - fpsMark >= 1000) {
            fpsMark = elapsedMs;
            Print("TARGET_FPS presented=%.0f\n", presented);
            presented = 0;
        }
    }

    Print("TARGET_ENDING pid=%lu rendered=%0.f\n", GetCurrentProcessId(), frames);
    return 0;
}
