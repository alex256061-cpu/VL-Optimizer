// VL scaling compositor host. Kept as its own executable so the compositor can be shipped, scripted
// and tested without the WPF shell.
//
// Usage:
//   VLCompositor.exe --target "<exe-ou-titulo>" [--width N] [--height N] [--scale nearest|bilinear|bicubic]
//                    [--seconds N] [--no-fps]
//
// Exits on its own after --seconds (default 60) so a forgotten run never lingers.

#include "Compositor.h"

#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

namespace {

void Print(const char* format, ...) {
    va_list args;
    va_start(args, format);
    vfprintf(stderr, format, args);
    va_end(args);
    fflush(stderr);
}

std::wstring Widen(const char* value) {
    if (value == nullptr) return std::wstring();
    const int size = MultiByteToWideChar(CP_UTF8, 0, value, -1, nullptr, 0);
    if (size <= 1) return std::wstring();
    std::wstring wide(static_cast<size_t>(size - 1), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value, -1, &wide[0], size);
    return wide;
}

struct Args {
    std::wstring target;
    uint32_t width = 0;
    uint32_t height = 0;
    vl::ScaleMode scale = vl::ScaleMode::Bicubic;
    int seconds = 60;
    bool drawFps = true;
};

bool ParseArgs(int argc, char** argv, Args* args) {
    for (int i = 1; i < argc; ++i) {
        const char* arg = argv[i];
        if (std::strcmp(arg, "--target") == 0 && i + 1 < argc) { args->target = Widen(argv[++i]); }
        else if (std::strcmp(arg, "--width") == 0 && i + 1 < argc) { args->width = static_cast<uint32_t>(std::atoi(argv[++i])); }
        else if (std::strcmp(arg, "--height") == 0 && i + 1 < argc) { args->height = static_cast<uint32_t>(std::atoi(argv[++i])); }
        else if (std::strcmp(arg, "--seconds") == 0 && i + 1 < argc) { args->seconds = std::atoi(argv[++i]); }
        else if (std::strcmp(arg, "--no-fps") == 0) { args->drawFps = false; }
        else if (std::strcmp(arg, "--scale") == 0 && i + 1 < argc) {
            const std::string mode = argv[++i];
            if (mode == "nearest") args->scale = vl::ScaleMode::Nearest;
            else if (mode == "bilinear") args->scale = vl::ScaleMode::Bilinear;
            else if (mode == "bicubic") args->scale = vl::ScaleMode::Bicubic;
            else { Print("modo de escala invalido: %s\n", mode.c_str()); return false; }
        }
        else { Print("argumento desconhecido: %s\n", arg); return false; }
    }
    if (args->target.empty()) { Print("faltou --target\n"); return false; }
    return true;
}

}  // namespace

int main(int argc, char** argv) {
    Args args;
    if (!ParseArgs(argc, argv, &args)) {
        Print("uso: VLCompositor.exe --target \"<exe-ou-titulo>\" [--width N] [--height N] [--scale modo] [--seconds N]\n");
        return 2;
    }

    vl::CompositorOptions options;
    options.TargetExecutableOrTitle = args.target;
    options.OutputWidth = args.width;
    options.OutputHeight = args.height;
    options.Scale = args.scale;
    options.DrawFps = args.drawFps;

    vl::Compositor compositor;
    std::wstring error;
    if (!compositor.Start(options, &error)) {
        Print("COMPOSITOR_START=FAIL %ls\n", error.c_str());
        return 3;
    }
    const vl::CaptureTarget& target = compositor.Target();
    Print("COMPOSITOR_START=OK target_pid=%u target=%ux%u output=%ux%u\n",
          target.ProcessId, target.Width, target.Height,
          args.width != 0 ? args.width : target.Width,
          args.height != 0 ? args.height : target.Height);

    const DWORD start = GetTickCount();
    LONGLONG presented = 0;
    LONGLONG skipped = 0;
    // Retrying a dead target must be paced: a tight rebind loop floods the log and burns the CPU.
    constexpr DWORD kRebindIntervalMs = 1000;
    DWORD lastRebind = 0;
    int rebindFailures = 0;
    while (GetTickCount() - start < static_cast<DWORD>(args.seconds) * 1000) {
        MSG message;
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }

        std::wstring frameError;
        if (compositor.RenderFrame(&frameError)) {
            ++presented;
        } else {
            ++skipped;
            if (compositor.TargetLost()) {
                const DWORD now = GetTickCount();
                if (now - lastRebind >= kRebindIntervalMs) {
                    lastRebind = now;
                    std::wstring rebindError;
                    if (compositor.RebindTarget(&rebindError)) {
                        Print("COMPOSITOR_REBIND=OK\n");
                        rebindFailures = 0;
                    } else if (++rebindFailures <= 3) {
                        Print("COMPOSITOR_REBIND=FAIL %ls\n", rebindError.c_str());
                    } else if (rebindFailures == 4) {
                        Print("COMPOSITOR_REBIND=GIVINGUP (novas falhas silenciadas)\n");
                    }
                }
            } else if (!frameError.empty() && skipped < 5) {
                Print("COMPOSITOR_FRAME=NOTE %ls\n", frameError.c_str());
            }
            // Nothing to capture: idle instead of spinning at full speed.
            Sleep(10);
            continue;
        }
        Sleep(4);
    }

    Print("COMPOSITOR_ENDING presented=%lld skipped=%lld fps=%.1f\n",
          static_cast<long long>(presented), static_cast<long long>(skipped), compositor.CurrentFps());
    compositor.Stop();
    return presented > 0 ? 0 : 4;
}
