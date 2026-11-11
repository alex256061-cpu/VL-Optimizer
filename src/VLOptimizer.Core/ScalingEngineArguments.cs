using System.Runtime.InteropServices;

namespace VLOptimizer.Core;

/// <summary>Configuration ABI of the native presentation engine's <c>ApplySettings</c> export
/// (ordinal 2, RVA 179840), proven from its complete 402-byte, 68-instruction disassembly:
/// <list type="bullet">
/// <item>No prologue (<c>sub rsp</c>) — every operand is read from the caller's stack frame.</item>
/// <item>First read is <c>movss xmm0, [rsp+0x28]</c>; in Win-64 ABI the first stack argument sits at
/// <c>RSP+0x28</c> (8-byte return address + 0x20 shadow space), so 4 arguments arrive in
/// <c>ECX/EDX/R8D/R9D</c> and the remaining 28 on the stack at 8-byte slots.</item>
/// <item>All 28 stack operands land inside <c>RSP+0x28 .. RSP+0x100</c> with no slot overrun.</item>
/// <item>Register argument 4 (<c>R9D</c>) is consumed last, at offset 0x18A — the order parameters
/// are read is not their order in the ABI.</item>
/// </list>
/// 32 parameters total; the function writes each into a process-wide global without range
/// validation, so every value is clamped here.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ScalingEngineArguments
{
    // Registers.
    public ScalingEngineMode Mode;                       // arg1  ECX
    public ScalingFitMode FitMode;                       // arg2  EDX
    public ScalingEngineMethod Method;                   // arg3  R8D
    public int Subtype;                                  // arg4  R9D (read last, at 0x18A)

    // Stack slots, arg5..arg32 at RSP+0x28 + 8*(n-5).
    public float ScaleFactor;                            // arg5  RSP+0x28
    public byte ResizeBeforeScale;                       // arg6  RSP+0x30
    public int Sharpness;                                // arg7  RSP+0x38
    public byte Vrs;                                     // arg8  RSP+0x40
    public FrameGenerationKind FrameGeneration;          // arg9  RSP+0x48
    public int FrameGenerationSize;                      // arg10 RSP+0x50
    public FrameGenerationMode FrameGenerationMode;      // arg11 RSP+0x58
    public float FrameMultiplier;                        // arg12 RSP+0x60
    public float TargetFps;                              // arg13 RSP+0x68
    public int FlowScale;                                // arg14 RSP+0x70
    public byte ClipCursor;                              // arg15 RSP+0x78
    public byte CursorSensitivity;                       // arg16 RSP+0x80
    public byte HideCursor;                              // arg17 RSP+0x88
    public byte ScaleCursor;                             // arg18 RSP+0x90
    public SyncMode SyncMode;                            // arg19 RSP+0x98
    public int MaxFrameLatency;                          // arg20 RSP+0xa0
    public byte GsyncSupport;                            // arg21 RSP+0xa8
    public byte HdrSupport;                              // arg22 RSP+0xb0
    public CaptureApi CaptureApi;                        // arg23 RSP+0xb8
    public int QueueTarget;                              // arg24 RSP+0xc0
    public byte DrawFps;                                 // arg25 RSP+0xc8
    public int GpuId;                                    // arg26 RSP+0xd0
    public int DisplayId;                                // arg27 RSP+0xd8
    public int CaptureOffsetLeft;                        // arg28 RSP+0xe0
    public int CaptureOffsetTop;                         // arg29 RSP+0xe8
    public int CaptureOffsetRight;                       // arg30 RSP+0xf0
    public int CaptureOffsetBottom;                      // arg31 RSP+0xf8
    public byte MultiDisplayMode;                        // arg32 RSP+0x100

    public const int ParameterCount = 32;
}
