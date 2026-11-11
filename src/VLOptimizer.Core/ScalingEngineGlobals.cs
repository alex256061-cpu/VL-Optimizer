using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace VLOptimizer.Core;

/// <summary>One proven write target: a stack/register argument and the global the engine stores it in.
/// Offsets are RVAs into the engine module, derived from the <c>ApplySettings</c> disassembly.</summary>
public sealed record EngineArgumentTarget(int Argument, int Rva, bool IsByte, string Global)
{
    public int Size => IsByte ? 1 : 4;
}

/// <summary>The complete write table of <c>ApplySettings</c>, in ABI parameter order. Every entry was
/// resolved from a <c>mov [rip+disp]</c> against its source operand in the disassembly; a parameter that
/// writes two globals (the scale factor) appears twice.</summary>
public static class ScalingEngineGlobals
{
    public static readonly IReadOnlyList<EngineArgumentTarget> Writes =
    [
        new(1, 0x50570, false, "scalingMode"),
        new(2, 0x505CC, false, "scalingFitMode"),
        new(3, 0x505C0, false, "scalingType"),
        new(4, 0x5055C, false, "scalingSubtype (register R9D)"),
        new(5, 0x4D384, false, "scaleFactor.a"),
        new(5, 0x505EC, false, "scaleFactor.b"),
        new(6, 0x50525, true, "resizeBeforeScale"),
        new(7, 0x4D3C8, false, "sharpness"),
        new(8, 0x5058C, true, "VRS"),
        new(9, 0x50578, false, "frameGenerationType"),
        new(10, 0x50554, false, "frameGenerationSize"),
        new(11, 0x5059C, false, "frameGenerationMode"),
        new(12, 0x505C4, false, "frameGenerationMultiplier"),
        new(13, 0x505A8, false, "frameGenerationTarget"),
        new(14, 0x505B0, false, "frameGenerationFlowScale"),
        new(15, 0x4D301, true, "clipCursor"),
        new(16, 0x5053E, true, "cursorSensitivity"),
        new(17, 0x50527, true, "hideCursor"),
        new(18, 0x5057E, true, "scaleCursor"),
        new(19, 0x4D3C4, false, "syncMode"),
        new(20, 0x4D3CC, false, "maxFrameLatency"),
        new(21, 0x5053C, true, "gsyncSupport"),
        new(22, 0x5057C, true, "hdrSupport"),
        new(23, 0x50588, false, "captureApi"),
        new(24, 0x505D0, false, "queueTarget"),
        new(25, 0x5057D, true, "drawFps"),
        new(26, 0x505C8, false, "gpuId"),
        new(27, 0x50558, false, "displayId"),
        new(28, 0x505D4, false, "captureOffsetLeft"),
        new(29, 0x505A4, false, "captureOffsetTop"),
        new(30, 0x50550, false, "captureOffsetRight"),
        new(31, 0x50598, false, "captureOffsetBottom"),
        new(32, 0x50526, true, "multiDisplayMode"),
    ];

    /// <summary>Distinct argument identifiers whose write was observed.</summary>
    public static IEnumerable<int> Arguments => Writes.Select(x => x.Argument).Distinct();
}
