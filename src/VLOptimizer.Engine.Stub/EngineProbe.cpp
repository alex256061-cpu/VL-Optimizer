// VL engine verification stub — loaded inside a target process by the VL injector.
//
// Purpose: prove the ApplySettings calling convention end to end. The stub runs on its own thread
// inside the target, exercises Init/Activate/ApplySettings through signatures derived from the
// disassembly, writes a machine-readable report next to the engine, and exits.
//
// It never touches a game: point the injector at a throwaway process in a VM.
//
// Signatures proven from disassembly:
//   Init            bool(void*)                 mov al, 1 on success
//   Activate        bool(HWND)                  sub rsp,0x58; rcx = HWND
//   ApplySettings   void(int,int,int,int,
//                        float,byte,int,byte,int,int,int,float,float,int,
//                        byte,byte,byte,byte,int,int,byte,byte,int,int,byte,
//                        int,int,int,int,int,int,byte)
//                   No prologue: reads the caller's stack from RSP+0x28 upward.

#include <windows.h>
#include <stdarg.h>
#include <stdio.h>
#include <wchar.h>

#define ENGINE_NAME L"Lossless.dll"
#define REPORT_NAME L"vl-engine-abi-report.txt"
#define STUB_THREAD_NAME L"vl-engine-stub"

typedef BOOL (__stdcall* PFN_Init)(void* reserved);
typedef BOOL (__stdcall* PFN_Activate)(HWND hwnd);

typedef void (__stdcall* PFN_ApplySettings)(
    int mode, int fitMode, int method, int subtype,
    float scaleFactor, unsigned char resizeBeforeScale, int sharpness, unsigned char vrs,
    int frameGeneration, int frameGenerationSize, int frameGenerationMode,
    float frameMultiplier, float targetFps, int flowScale,
    unsigned char clipCursor, unsigned char cursorSensitivity, unsigned char hideCursor, unsigned char scaleCursor,
    int syncMode, int maxFrameLatency, unsigned char gsyncSupport, unsigned char hdrSupport,
    int captureApi, int queueTarget, unsigned char drawFps, int gpuId, int displayId,
    int captureOffsetLeft, int captureOffsetTop, int captureOffsetRight, int captureOffsetBottom,
    unsigned char multiDisplayMode);

// Sentinel values: distinct per argument so a swapped pair is visible in the report.
#define SENT_SUBTYPE        0x0AAAA001
#define SENT_SHARPNESS      0x0AAAA002
#define SENT_FG_SIZE        0x0AAAA003
#define SENT_MAXLAT         0x0AAAA004
#define SENT_QUEUE          0x0AAAA005
#define SENT_GPU            0x0AAAA006
#define SENT_DISPLAY        0x0AAAA007
#define SENT_CROP_L         0x0AAAA008
#define SENT_CROP_T         0x0AAAA009
#define SENT_CROP_R         0x0AAAA00A
#define SENT_CROP_B         0x0AAAA00B
#define SENT_SCALE_FACTOR   3.5f
#define SENT_FG_MULT        4.5f
#define SENT_TARGET_FPS     5.5f
#define SENT_FLOW           6.5f

static void WriteReport(const wchar_t* path, const char* text) {
    HANDLE file = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return;
    DWORD written = 0;
    WriteFile(file, text, (DWORD)lstrlenA(text), &written, NULL);
    CloseHandle(file);
}

static void ReportPathFromEngine(wchar_t* buffer, DWORD size) {
    // Prefer the engine's folder; fall back to %TEMP% when that folder is read-only (Program Files).
    HMODULE engine = GetModuleHandleW(ENGINE_NAME);
    if (engine != NULL && GetModuleFileNameW(engine, buffer, size) > 0) {
        wchar_t* slash = wcsrchr(buffer, L'\\');
        if (slash != NULL) { *(slash + 1) = L'\0'; }
        lstrcatW(buffer, REPORT_NAME);
        HANDLE probe = CreateFileW(buffer, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        if (probe != INVALID_HANDLE_VALUE) { CloseHandle(probe); return; }
    }
    GetTempPathW(size, buffer);
    lstrcatW(buffer, REPORT_NAME);
}

static void AppendLine(char* report, size_t capacity, const char* format, ...) {
    size_t used = lstrlenA(report);
    if (used >= capacity - 1) return;
    va_list args;
    va_start(args, format);
    _vsnprintf_s(report + used, capacity - used, _TRUNCATE, format, args);
    va_end(args);
}

// Bit pattern of a float as the unsigned int the engine stores. A union keeps this well-defined
// where pointer casting would be UB under /W4.
static unsigned int FloatBits(float value) {
    union { float f; unsigned int u; } bits;
    bits.f = value;
    return bits.u;
}

static DWORD WINAPI StubThread(LPVOID parameter) {
    (void)parameter;
    static char report[16384];
    report[0] = '\0';
    wchar_t path[MAX_PATH];
    ReportPathFromEngine(path, MAX_PATH);

    HMODULE engine = GetModuleHandleW(ENGINE_NAME);
    if (engine == NULL) { AppendLine(report, sizeof(report), "RESULT=FAIL\nREASON=engine module not loaded\n"); WriteReport(path, report); return 0; }

    PFN_Init init = (PFN_Init)GetProcAddress(engine, "Init");
    PFN_ApplySettings apply = (PFN_ApplySettings)GetProcAddress(engine, "ApplySettings");
    PFN_Activate activate = (PFN_Activate)GetProcAddress(engine, "Activate");

    AppendLine(report, sizeof(report), "MODULE=%p\n", (void*)engine);
    AppendLine(report, sizeof(report), "INIT_EXPORT=%p\n", (void*)init);
    AppendLine(report, sizeof(report), "APPLY_EXPORT=%p\n", (void*)apply);
    AppendLine(report, sizeof(report), "ACTIVATE_EXPORT=%p\n", (void*)activate);

    if (apply == NULL) {
        AppendLine(report, sizeof(report), "RESULT=FAIL\nREASON=ApplySettings export missing\n");
        WriteReport(path, report);
        return 0;
    }

    // The host is expected to have run Init(callback) and Activate(window) already, so the engine's
    // pipeline objects exist and ApplySettings can write into them. Init is never called from here:
    // re-initialising would replace the host's teardown callback with a null one and crash the process.
    HWND foreground = GetForegroundWindow();
    AppendLine(report, sizeof(report), "FOREGROUND_HWND=%p\n", (void*)foreground);

    // Send the sentinel configuration and read the globals back from our own module image.
    apply(1, 1, 3, SENT_SUBTYPE,
          SENT_SCALE_FACTOR, 1, SENT_SHARPNESS, 1,
          2, SENT_FG_SIZE, 2,
          SENT_FG_MULT, SENT_TARGET_FPS, (int)SENT_FLOW,
          1, 1, 1, 1,
          3, SENT_MAXLAT, 1, 1,
          1, SENT_QUEUE, 1, SENT_GPU, SENT_DISPLAY,
          SENT_CROP_L, SENT_CROP_T, SENT_CROP_R, SENT_CROP_B,
          1);
    OutputDebugStringA("vl-engine-stub: ApplySettings returned\n");

    // Globals are RVAs into the engine image; each expected value is the sentinel that fed it.
    struct { int rva; int size; const char* name; unsigned int expected; } globals[] = {
        { 0x50570, 4, "scalingMode",            1       },
        { 0x505CC, 4, "scalingFitMode",         1       },
        { 0x505C0, 4, "scalingType",            3       },
        { 0x5055C, 4, "scalingSubtype",         SENT_SUBTYPE },
        { 0x4D384, 4, "scaleFactor.a",          FloatBits(SENT_SCALE_FACTOR) },
        { 0x505EC, 4, "scaleFactor.b",          FloatBits(SENT_SCALE_FACTOR) },
        { 0x50525, 1, "resizeBeforeScale",      1       },
        { 0x4D3C8, 4, "sharpness",              SENT_SHARPNESS },
        { 0x5058C, 1, "VRS",                    1       },
        { 0x50578, 4, "frameGenerationType",    2       },
        { 0x50554, 4, "frameGenerationSize",    SENT_FG_SIZE },
        { 0x5059C, 4, "frameGenerationMode",    2       },
        { 0x505C4, 4, "frameGenerationMultiplier", FloatBits(SENT_FG_MULT) },
        { 0x505A8, 4, "frameGenerationTarget",  FloatBits(SENT_TARGET_FPS) },
        { 0x505B0, 4, "frameGenerationFlowScale", (unsigned int)SENT_FLOW },
        { 0x4D301, 1, "clipCursor",             1       },
        { 0x5053E, 1, "cursorSensitivity",      1       },
        { 0x50527, 1, "hideCursor",             1       },
        { 0x5057E, 1, "scaleCursor",            1       },
        { 0x4D3C4, 4, "syncMode",               3       },
        { 0x4D3CC, 4, "maxFrameLatency",        SENT_MAXLAT },
        { 0x5053C, 1, "gsyncSupport",           1       },
        { 0x5057C, 1, "hdrSupport",             1       },
        { 0x50588, 4, "captureApi",             1       },
        { 0x505D0, 4, "queueTarget",            SENT_QUEUE },
        { 0x5057D, 1, "drawFps",                1       },
        { 0x505C8, 4, "gpuId",                  SENT_GPU },
        { 0x50558, 4, "displayId",              SENT_DISPLAY },
        { 0x505D4, 4, "captureOffsetLeft",      SENT_CROP_L },
        { 0x505A4, 4, "captureOffsetTop",       SENT_CROP_T },
        { 0x50550, 4, "captureOffsetRight",     SENT_CROP_R },
        { 0x50598, 4, "captureOffsetBottom",    SENT_CROP_B },
        { 0x50526, 1, "multiDisplayMode",       1       },
    };

    int mismatch = 0;
    for (int i = 0; i < (int)(sizeof(globals) / sizeof(globals[0])); i++) {
        const unsigned char* address = (const unsigned char*)engine + globals[i].rva;
        unsigned int actual = (globals[i].size == 1) ? *(const unsigned char*)address : *(const unsigned int*)address;
        const char* verdict = (actual == globals[i].expected) ? "OK" : "MISMATCH";
        if (actual != globals[i].expected) mismatch++;
        AppendLine(report, sizeof(report), "GLOBAL %-26s rva=0x%05X expected=0x%08X actual=0x%08X %s\n",
                   globals[i].name, globals[i].rva, globals[i].expected, actual, verdict);
    }

    AppendLine(report, sizeof(report), "MISMATCHES=%d\n", mismatch);
    AppendLine(report, sizeof(report), "RESULT=%s\n", mismatch == 0 ? "PASS" : "FAIL");
    WriteReport(path, report);
    return 0;
}

// Entry point executed by the injector's remote LoadLibrary. Starting the verification on a
// separate thread keeps DllMain free of loader-lock hazards.
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved) {
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(instance);
        // Mark the load before anything can fail, so a missing report always has a cause.
        wchar_t marker[MAX_PATH];
        GetTempPathW(MAX_PATH, marker);
        lstrcatW(marker, L"vl-engine-stub-loaded.flag");
        HANDLE flag = CreateFileW(marker, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        if (flag != INVALID_HANDLE_VALUE) {
            const char* text = "DllMain entered\n";
            DWORD written = 0;
            WriteFile(flag, text, (DWORD)lstrlenA(text), &written, NULL);
            CloseHandle(flag);
        }
        HANDLE thread = CreateThread(NULL, 0, StubThread, NULL, 0, NULL);
        if (thread != NULL) {
            SetThreadDescription(thread, STUB_THREAD_NAME);
            CloseHandle(thread);
        }
    }
    return TRUE;
}
