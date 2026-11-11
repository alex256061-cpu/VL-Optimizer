// Test host for the VL engine probe. The engine's ApplySettings writes into pipeline objects that
// only exist after Activate installed its swapchain hook against a real window. A console process has
// no usable HWND, so this host creates an actual top-level window with a D3D11 swapchain, then calls
// Init -> Activate(window) and stays alive so a second process can inject VL.EngineProbe.dll.
//
// Usage:  VLEngineHost.exe "<path to Lossless.dll>" [seconds-to-live]

#include <windows.h>
#include <d3d11.h>
#include <dxgi1_6.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <wchar.h>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")

typedef BOOL (__stdcall* PFN_Init)(void* reserved);
typedef BOOL (__stdcall* PFN_Activate)(HWND hwnd);

static void Print(const char* format, ...);

// Init stores its first argument into a global that Activate invokes on teardown
// (disassembly: `mov [global.0x1800505d8], rcx` in Init, `global.0x1800505d8(...)` in Activate).
// Passing NULL leaves a null function pointer the engine eventually calls. This callback is the
// minimal safe target for that call.
static void __stdcall EngineTeardownCallback(void* arg1, void* arg2, void* arg3, void* arg4) {
    (void)arg1; (void)arg2; (void)arg3; (void)arg4;
    Print("HOST_ENGINE_CALLBACK invoked\n");
}

static HWND g_window = NULL;
static ID3D11Device* g_device = NULL;
static ID3D11DeviceContext* g_context = NULL;
static IDXGISwapChain* g_swapchain = NULL;

static void Print(const char* format, ...) {
    va_list args;
    va_start(args, format);
    vfprintf(stderr, format, args);
    va_end(args);
    fflush(stderr);
}

static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
        case WM_CLOSE: ShowWindow(window, SW_HIDE); return 0;
        case WM_DESTROY: return 0;
        default: return DefWindowProcW(window, message, wParam, lParam);
    }
}

// A real windowable swapchain is what the engine's Activate hooks. FLIP_DISCARD with two buffers
// A top-level window with no device yet. Split from the device creation because the engine must be
// loaded and initialised between the two: it resolves the D3D/DXGI entry points before the process
// uses them.
static HWND CreateHostWindow(int width, int height) {
    WNDCLASSEXW wc = { sizeof(wc) };
    wc.lpfnWndProc = WindowProc;
    wc.hInstance = GetModuleHandleW(NULL);
    wc.lpszClassName = L"VLEngineHostWindow";
    wc.hCursor = LoadCursorW(NULL, (LPCWSTR)IDC_ARROW);
    wc.style = CS_OWNDC;
    if (!RegisterClassExW(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
        Print("HOST_WINDOW=FAIL register error=%lu\n", GetLastError());
        return NULL;
    }
    g_window = CreateWindowExW(0, L"VLEngineHostWindow", L"VLEngineHost", WS_OVERLAPPEDWINDOW,
                               CW_USEDEFAULT, CW_USEDEFAULT, width, height, NULL, NULL, wc.hInstance, NULL);
    if (g_window == NULL) { Print("HOST_WINDOW=FAIL create error=%lu\n", GetLastError()); return NULL; }
    ShowWindow(g_window, SW_SHOWNOACTIVATE);
    UpdateWindow(g_window);
    Print("HOST_WINDOW=OK hwnd=%p\n", (void*)g_window);
    return g_window;
}

// Device and a modern presentation swapchain, created after the engine is inside the process.
static BOOL CreateDeviceAndSwapchain(HWND window, int width, int height) {
    const D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL got;
    HRESULT hr = D3D11CreateDevice(NULL, D3D_DRIVER_TYPE_HARDWARE, NULL, 0, levels, 2, D3D11_SDK_VERSION,
                                   &g_device, &got, &g_context);
    if (FAILED(hr)) { Print("HOST_DEVICE=FAIL hr=0x%08lX\n", (unsigned long)hr); return FALSE; }
    Print("HOST_DEVICE=OK featurelevel=0x%X\n", (unsigned)got);

    IDXGIDevice* dxgiDevice = NULL;
    IDXGIAdapter* adapter = NULL;
    IDXGIFactory2* factory = NULL;
    if (FAILED(g_device->QueryInterface(__uuidof(IDXGIDevice), (void**)&dxgiDevice))) { Print("HOST_DXGI=FAIL qi device\n"); return FALSE; }
    if (FAILED(dxgiDevice->GetAdapter(&adapter))) { Print("HOST_DXGI=FAIL get adapter\n"); dxgiDevice->Release(); return FALSE; }
    if (FAILED(adapter->GetParent(__uuidof(IDXGIFactory2), (void**)&factory))) { Print("HOST_DXGI=FAIL get factory\n"); adapter->Release(); dxgiDevice->Release(); return FALSE; }

    DXGI_SWAP_CHAIN_DESC1 desc = {};
    desc.Width = (UINT)width;
    desc.Height = (UINT)height;
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    desc.AlphaMode = DXGI_ALPHA_MODE_UNSPECIFIED;
    hr = factory->CreateSwapChainForHwnd(g_device, window, &desc, NULL, NULL, (IDXGISwapChain1**)&g_swapchain);
    factory->Release(); adapter->Release(); dxgiDevice->Release();
    if (FAILED(hr)) { Print("HOST_SWAPCHAIN=FAIL hr=0x%08lX\n", (unsigned long)hr); return FALSE; }
    Print("HOST_SWAPCHAIN=OK\n");
    return TRUE;
}

int wmain(int argc, wchar_t** argv) {
    if (argc < 2) { Print("usage: VLEngineHost.exe <Lossless.dll> [seconds]\n"); return 2; }
    DWORD ttlSeconds = (argc >= 3) ? (DWORD)_wtoi(argv[2]) : 120;
    if (ttlSeconds == 0) ttlSeconds = 120;

    // Order matters and is load-bearing: the engine resolves CreateSwapChainForHwnd and
    // D3D11CreateDevice at Init time and hooks whatever the process creates afterwards. Loading it
    // after the swapchain exists leaves its swapchain object null, and Activate reports success while
    // having nothing to hook. So: window -> engine -> Init -> swapchain -> Activate.
    HWND window = CreateHostWindow(1280, 720);
    if (window == NULL) return 4;

    HMODULE engine = LoadLibraryExW(argv[1], NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (engine == NULL) { Print("HOST_LOAD=FAIL error=%lu\n", GetLastError()); return 3; }
    Print("HOST_LOAD=OK module=%p\n", (void*)engine);

    PFN_Init init = (PFN_Init)GetProcAddress(engine, "Init");
    PFN_Activate activate = (PFN_Activate)GetProcAddress(engine, "Activate");
    Print("HOST_INIT_EXPORT=%p HOST_ACTIVATE_EXPORT=%p\n", (void*)init, (void*)activate);
    if (init == NULL || activate == NULL) { Print("HOST_EXPORTS=FAIL\n"); return 5; }

    BOOL initialized = init((void*)EngineTeardownCallback);
    Print("HOST_INIT_RESULT=%d\n", initialized);
    if (!initialized) { Print("HOST_ABORT=init refused\n"); return 6; }

    // Created after Init so any device/swapchain hook the engine installed is in place for it.
    if (!CreateDeviceAndSwapchain(window, 1280, 720)) return 7;

    // Activate installs the window-event and swapchain hooks; ApplySettings is only meaningful after.
    BOOL activated = activate(window);
    Print("HOST_ACTIVATE_RESULT=%d\n", activated);

    Print("HOST_READY pid=%lu hwnd=%p swapchain=%p ttl=%lu\n", GetCurrentProcessId(), (void*)window, (void*)g_swapchain, ttlSeconds);

    DWORD start = GetTickCount();
    while ((GetTickCount() - start) < ttlSeconds * 1000) {
        MSG message;
        while (PeekMessageW(&message, NULL, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        if (g_swapchain != NULL) g_swapchain->Present(0, 0);
        Sleep(16);
    }
    Print("HOST_ENDING pid=%lu\n", GetCurrentProcessId());
    return 0;
}
