// Frame counter for the monitor that actually shows the target window.
//
// Desktop Duplication enumerates per OUTPUT, and a machine with two monitors has two of them. Reading
// output 0 blindly measures the wrong screen whenever the game is on the second monitor, which is how
// a smooth 60 FPS scene can be reported as ~12. This version picks the output whose desktop rectangle
// contains the target window's centre and reports which output it used.
//
// Usage: VLFrameCounter.exe --seconds N --label NAME [--pid PID] [--out FILE.csv]

#include <windows.h>
#include <d3d11.h>
#include <dxgi1_6.h>
#include <wrl/client.h>

#include <cstdio>
#include <cstdarg>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;

namespace {

void Print(const char* format, ...) {
    va_list args; va_start(args, format); vfprintf(stdout, format, args); va_end(args); fflush(stdout);
}

struct TargetWindow { HWND hwnd = nullptr; RECT rect = {}; };

BOOL CALLBACK CollectWindow(HWND window, LPARAM param) {
    auto* target = reinterpret_cast<TargetWindow*>(param);
    if (!IsWindowVisible(window)) return TRUE;
    RECT rect = {};
    if (!GetWindowRect(window, &rect)) return TRUE;
    const int area = (rect.right - rect.left) * (rect.bottom - rect.top);
    const int best = (target->rect.right - target->rect.left) * (target->rect.bottom - target->rect.top);
    if (area > best) { target->hwnd = window; target->rect = rect; }
    return TRUE;
}

TargetWindow FindTargetWindow(DWORD processId) {
    struct Search { DWORD pid; TargetWindow winner; } search{ processId, {} };
    EnumWindows([](HWND window, LPARAM param) -> BOOL {
        auto* state = reinterpret_cast<Search*>(param);
        DWORD owner = 0;
        GetWindowThreadProcessId(window, &owner);
        if (owner != state->pid || !IsWindowVisible(window)) return TRUE;
        RECT rect = {};
        if (!GetWindowRect(window, &rect)) return TRUE;
        const int area = (rect.right - rect.left) * (rect.bottom - rect.top);
        const int best = (state->winner.rect.right - state->winner.rect.left) * (state->winner.rect.bottom - state->winner.rect.top);
        if (area > best) { state->winner.hwnd = window; state->winner.rect = rect; }
        return TRUE;
    }, reinterpret_cast<LPARAM>(&search));
    return search.winner;
}

// Chooses the output whose desktop coordinates contain the window centre. Returns the index of the
// matching output, or 0 when nothing matches (single-monitor fallback).
UINT ChooseOutput(IDXGIAdapter1* adapter, const RECT& window, std::wstring* outputName) {
    const LONG cx = (window.left + window.right) / 2;
    const LONG cy = (window.top + window.bottom) / 2;
    for (UINT index = 0;; ++index) {
        ComPtr<IDXGIOutput> output;
        if (adapter->EnumOutputs(index, &output) != S_OK || output == nullptr) break;
        DXGI_OUTPUT_DESC desc = {};
        output->GetDesc(&desc);
        const RECT& r = desc.DesktopCoordinates;
        if (cx >= r.left && cx < r.right && cy >= r.top && cy < r.bottom) {
            if (outputName != nullptr) *outputName = desc.DeviceName;
            return index;
        }
    }
    if (outputName != nullptr) *outputName = L"<fallback 0>";
    return 0;
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
    int seconds = 10;
    std::wstring label = L"sample";
    std::wstring outputPath;
    DWORD targetPid = 0;
    for (int i = 1; i < argc; ++i) {
        if (wcscmp(argv[i], L"--seconds") == 0 && i + 1 < argc) seconds = _wtoi(argv[++i]);
        else if (wcscmp(argv[i], L"--label") == 0 && i + 1 < argc) label = argv[++i];
        else if (wcscmp(argv[i], L"--out") == 0 && i + 1 < argc) outputPath = argv[++i];
        else if (wcscmp(argv[i], L"--pid") == 0 && i + 1 < argc) targetPid = static_cast<DWORD>(_wtoi(argv[++i]));
    }
    if (seconds <= 0) seconds = 10;

    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    const D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL got;
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                                   levels, 1, D3D11_SDK_VERSION, &device, &got, &context);
    if (FAILED(hr)) { Print("DEVICE_FAIL 0x%08lX\n", static_cast<unsigned long>(hr)); return 2; }

    ComPtr<IDXGIDevice> dxgiDevice;
    ComPtr<IDXGIAdapter1> adapter;
    if (FAILED(device->QueryInterface(IID_PPV_ARGS(&dxgiDevice))) ||
        FAILED(dxgiDevice->GetAdapter(reinterpret_cast<IDXGIAdapter**>(adapter.GetAddressOf())))) {
        Print("ADAPTER_FAIL\n"); return 3;
    }

    TargetWindow target;
    if (targetPid != 0) target = FindTargetWindow(targetPid);

    std::wstring outputName;
    const UINT outputIndex = ChooseOutput(adapter.Get(), target.rect, &outputName);
    Print("TARGET_PID=%lu TARGET_HWND=%p TARGET_RECT=(%ld,%ld)-(%ld,%ld)\n",
          static_cast<unsigned long>(targetPid), static_cast<void*>(target.hwnd),
          target.rect.left, target.rect.top, target.rect.right, target.rect.bottom);
    Print("OUTPUT_INDEX=%u OUTPUT=%ls\n", outputIndex, outputName.c_str());

    ComPtr<IDXGIOutput> chosen;
    if (FAILED(adapter->EnumOutputs(outputIndex, &chosen))) { Print("OUTPUT_FAIL\n"); return 4; }
    ComPtr<IDXGIOutput1> chosen1;
    if (FAILED(chosen.As(&chosen1))) { Print("OUTPUT1_FAIL\n"); return 5; }

    ComPtr<IDXGIOutputDuplication> duplication;
    hr = chosen1->DuplicateOutput(device.Get(), &duplication);
    if (FAILED(hr)) { Print("DUP_FAIL 0x%08lX\n", static_cast<unsigned long>(hr)); return 6; }

    LARGE_INTEGER frequency, start, now;
    QueryPerformanceFrequency(&frequency);
    QueryPerformanceCounter(&start);

    struct Sample { double seconds; uint64_t accumulated; uint32_t movedBytes; };
    std::vector<Sample> samples;
    uint64_t last = 0;
    bool haveFirst = false;

    while (true) {
        QueryPerformanceCounter(&now);
        const double elapsed = static_cast<double>(now.QuadPart - start.QuadPart) / static_cast<double>(frequency.QuadPart);
        if (elapsed > static_cast<double>(seconds)) break;

        DXGI_OUTDUPL_FRAME_INFO info = {};
        ComPtr<IDXGIResource> resource;
        hr = duplication->AcquireNextFrame(50, &info, &resource);
        if (hr == DXGI_ERROR_WAIT_TIMEOUT) continue;
        if (FAILED(hr)) { Print("ACQUIRE_FAIL 0x%08lX at %.2fs\n", static_cast<unsigned long>(hr), elapsed); break; }

        uint64_t delta = (haveFirst && info.AccumulatedFrames >= last) ? info.AccumulatedFrames - last : 0;
        last = info.AccumulatedFrames;
        haveFirst = true;
        samples.push_back({ elapsed, delta, info.TotalMetadataBufferSize });
        duplication->ReleaseFrame();
    }

    if (samples.empty()) { Print("NO_SAMPLES\n"); return 7; }

    uint64_t frames = 0;
    uint32_t moved = 0;
    for (const auto& s : samples) { frames += s.accumulated; if (s.movedBytes > 0) ++moved; }
    const double span = samples.back().seconds - samples.front().seconds;
    const double fps = span > 0 ? static_cast<double>(frames) / span : 0;

    Print("LABEL=%ls OUTPUT_INDEX=%u FRAMES=%llu SPAN=%.2f FPS=%.1f MOVED=%u ACQUIRES=%zu\n",
          label.c_str(), outputIndex, static_cast<unsigned long long>(frames), span, fps, moved, samples.size());

    if (!outputPath.empty()) {
        FILE* file = nullptr;
        if (_wfopen_s(&file, outputPath.c_str(), L"wb") == 0 && file != nullptr) {
            fprintf(file, "label,output,seconds,accumulated,movedBytes\n");
            for (const auto& s : samples) fprintf(file, "%ls,%u,%.4f,%llu,%u\n", label.c_str(), outputIndex, s.seconds, static_cast<unsigned long long>(s.accumulated), s.movedBytes);
            fclose(file);
        }
    }
    return frames > 0 ? 0 : 8;
}
