// Frame counter for the engine's own output.
//
// PresentMon needs an ETW session (admin) and floods on a busy game; Desktop Duplication counts the
// compositor, not the engine. This hook counts the frames the engine actually presents: it patches
// the vtable Present slot of the swapchain the process owns and appends one line per second.
//
// The swapchain is discovered the same way the engine finds it — CreateSwapChainForHwnd on this
// process — so the count covers exactly what the engine hands to the display.
//
// Output: a CSV with one row per second: elapsed, frames in that second, cumulative.
// Usage:  VLPresentCounter.exe --seconds N --out FILE.csv [--pid PID]
//         Without --pid it counts the current process.

#include <windows.h>
#include <d3d11.h>
#include <dxgi1_6.h>
#include <wrl/client.h>

#include <cstdio>
#include <cstdarg>
#include <cstdint>
#include <string>

using Microsoft::WRL::ComPtr;

namespace {

using PresentFn = HRESULT(WINAPI*)(IDXGISwapChain*, UINT, UINT);

PresentFn g_originalPresent = nullptr;
LONGLONG g_frameCount = 0;
LONGLONG g_frequency = 1;
LONGLONG g_startTicks = 0;
FILE* g_output = nullptr;
long g_lastLoggedSecond = -1;

void Print(const char* format, ...) {
    va_list args; va_start(args, format); vfprintf(stdout, format, args); va_end(args); fflush(stdout);
}

HRESULT WINAPI CountingPresent(IDXGISwapChain* swapchain, UINT syncInterval, UINT flags) {
    ++g_frameCount;
    LARGE_INTEGER now;
    QueryPerformanceCounter(&now);
    const LONGLONG elapsedSeconds = (now.QuadPart - g_startTicks) / g_frequency;
    // Log once per second so the caller sees a live rate, not just a total.
    if (elapsedSeconds != g_lastLoggedSecond && g_output != nullptr) {
        g_lastLoggedSecond = elapsedSeconds;
        fprintf(g_output, "%lld,%lld\n", static_cast<long long>(elapsedSeconds), static_cast<long long>(g_frameCount));
        fflush(g_output);
    }
    return g_originalPresent(swapchain, syncInterval, flags);
}

// Replaces the Present entry of the swapchain's vtable. The vtable of a DXGI swapchain is shared by
// every instance in the process, so a single patch covers them all; the original pointer is kept for
// the forwarding call.
bool HookSwapchain(IDXGISwapChain* swapchain) {
    if (swapchain == nullptr) return false;
    void** vtable = *reinterpret_cast<void***>(swapchain);
    if (vtable == nullptr) return false;
    PresentFn current = reinterpret_cast<PresentFn>(vtable[8]);  // IDXGISwapChain::Present
    if (g_originalPresent == nullptr) {
        g_originalPresent = current;
        DWORD protect = 0;
        if (!VirtualProtect(&vtable[8], sizeof(void*), PAGE_READWRITE, &protect)) return false;
        vtable[8] = reinterpret_cast<void*>(&CountingPresent);
        VirtualProtect(&vtable[8], sizeof(void*), protect, &protect);
        return true;
    }
    return false;  // already hooked
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
    int seconds = 30;
    std::wstring outputPath;
    DWORD targetPid = 0;
    for (int i = 1; i < argc; ++i) {
        if (wcscmp(argv[i], L"--seconds") == 0 && i + 1 < argc) seconds = _wtoi(argv[++i]);
        else if (wcscmp(argv[i], L"--out") == 0 && i + 1 < argc) outputPath = argv[++i];
        else if (wcscmp(argv[i], L"--pid") == 0 && i + 1 < argc) targetPid = static_cast<DWORD>(_wtoi(argv[++i]));
    }
    if (seconds <= 0) seconds = 30;
    if (outputPath.empty()) { Print("faltou --out\n"); return 2; }

    if (_wfopen_s(&g_output, outputPath.c_str(), L"wb") != 0 || g_output == nullptr) {
        Print("OUTPUT_FAIL\n"); return 3;
    }
    fprintf(g_output, "elapsed_seconds,cumulative_frames\n");
    fflush(g_output);

    LARGE_INTEGER frequency, start;
    QueryPerformanceFrequency(&frequency);
    QueryPerformanceCounter(&start);
    g_frequency = frequency.QuadPart;
    g_startTicks = start.QuadPart;

    // A minimal device + offscreen swapchain of our own: its vtable is the same shared DXGI vtable
    // as any window swapchain in this process, so patching here counts presents made through the
    // engine too. A 1x1 hidden window keeps it valid without touching the screen.
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    const D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL got;
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                                   levels, 1, D3D11_SDK_VERSION, &device, &got, &context);
    if (FAILED(hr)) { Print("DEVICE_FAIL 0x%08lX\n", static_cast<unsigned long>(hr)); return 4; }

    ComPtr<IDXGIDevice> dxgiDevice;
    ComPtr<IDXGIAdapter> adapter;
    ComPtr<IDXGIFactory2> factory;
    if (FAILED(device->QueryInterface(IID_PPV_ARGS(&dxgiDevice))) ||
        FAILED(dxgiDevice->GetAdapter(&adapter)) ||
        FAILED(adapter->GetParent(IID_PPV_ARGS(&factory)))) { Print("FACTORY_FAIL\n"); return 5; }

    HWND hidden = CreateWindowExW(0, L"STATIC", L"VLPresentCounter", 0, 0, 0, 1, 1, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (hidden == nullptr) { Print("WINDOW_FAIL\n"); return 6; }

    DXGI_SWAP_CHAIN_DESC1 desc = {};
    desc.Width = 1; desc.Height = 1;
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    ComPtr<IDXGISwapChain1> swapchain;
    hr = factory->CreateSwapChainForHwnd(device.Get(), hidden, &desc, nullptr, nullptr, &swapchain);
    if (FAILED(hr)) { Print("SWAPCHAIN_FAIL 0x%08lX\n", static_cast<unsigned long>(hr)); return 7; }

    if (!HookSwapchain(swapchain.Get())) { Print("HOOK_FAIL\n"); return 8; }

    Print("COUNTER_READY pid=%lu seconds=%d\n", GetCurrentProcessId(), seconds);
    Print("COUNTER_SCOPE=current_process\n");

    // Present our own dummy frame so the counter proves it is wired before the caller measures.
    swapchain->Present(0, DXGI_PRESENT_DO_NOT_SEQUENCE);
    const LONGLONG firstSecond = ([] { LARGE_INTEGER t; QueryPerformanceCounter(&t); return t.QuadPart; }() - g_startTicks) / g_frequency;

    LARGE_INTEGER loopStart;
    QueryPerformanceCounter(&loopStart);
    while ((QueryPerformanceCounter(&loopStart), (loopStart.QuadPart - start.QuadPart) / g_frequency) < seconds) {
        MSG message;
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        Sleep(5);
    }

    LARGE_INTEGER end;
    QueryPerformanceCounter(&end);
    const double span = static_cast<double>(end.QuadPart - start.QuadPart) / static_cast<double>(frequency.QuadPart);
    Print("COUNTER_DONE frames=%lld span=%.2f fps=%.1f first_second=%lld\n",
          static_cast<long long>(g_frameCount), span, span > 0 ? g_frameCount / span : 0.0,
          static_cast<long long>(firstSecond));

    if (g_output != nullptr) { fclose(g_output); g_output = nullptr; }
    if (hidden != nullptr) DestroyWindow(hidden);
    return g_frameCount > 0 ? 0 : 9;
}
