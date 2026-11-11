using System.Runtime.InteropServices;

namespace VLOptimizer.Core;

/// <summary>Binding for the engine's configuration export, proven against the full disassembly.
/// The 32 parameters are declared explicitly rather than through a struct: the native entry has no
/// prologue and reads the caller's stack directly, so this is the only shape that matches it.
/// Calling convention is Win-64 (4 registers + stack).</summary>
internal static class ScalingEngineNative
{
    /// <summary>True when the export bound successfully.</summary>
    internal static bool Bound { get; private set; }
    internal static string? LoadError { get; private set; }

    /// <summary>Loads the engine and resolves <c>ApplySettings</c>. The library is resolved by full
    /// path so the caller controls exactly which binary is used.</summary>
    internal static bool TryLoad(string libraryPath, out string error)
    {
        error = "";
        try
        {
            if (NativeLibrary.TryLoad(libraryPath, out var handle) && NativeLibrary.TryGetExport(handle, "ApplySettings", out var address) && address != IntPtr.Zero)
            {
                ApplySettingsPointer = address;
                Bound = true;
                return true;
            }
            error = "Export ApplySettings não encontrado em " + Path.GetFileName(libraryPath) + ".";
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            error = "Biblioteca do motor incompatível: " + ex.Message;
            return false;
        }
    }

    private static IntPtr ApplySettingsPointer;

    /// <summary>Invokes the engine's configuration entry through a manually built stack frame.
    /// A raw <c>call ptr</c> against the resolved address is used instead of
    /// <c>DllImport</c> so the engine can live anywhere on disk, while keeping the exact
    /// 32-parameter Win-64 layout the disassembly demands.</summary>
    internal static bool Apply(ScalingEngineArguments arguments)
    {
        if (!Bound || ApplySettingsPointer == IntPtr.Zero) return false;
        return CallApply(ApplySettingsPointer, arguments);
    }

    /// <summary>Same call path as <see cref="Apply"/> but against a caller-supplied export address.
    /// Used when the engine is loaded by a session object rather than through this type's loader.</summary>
    internal static bool ApplyFromAddress(IntPtr address, ScalingEngineArguments arguments)
    {
        if (address == IntPtr.Zero) return false;
        return CallApply(address, arguments);
    }

    private static bool CallApply(IntPtr address, ScalingEngineArguments arguments)
    {
        return CallApply(address,
            (int)arguments.Mode, (int)arguments.FitMode, (int)arguments.Method, arguments.Subtype,
            arguments.ScaleFactor, arguments.ResizeBeforeScale, arguments.Sharpness, arguments.Vrs,
            (int)arguments.FrameGeneration, arguments.FrameGenerationSize, (int)arguments.FrameGenerationMode,
            arguments.FrameMultiplier, arguments.TargetFps, arguments.FlowScale,
            arguments.ClipCursor, arguments.CursorSensitivity, arguments.HideCursor, arguments.ScaleCursor,
            (int)arguments.SyncMode, arguments.MaxFrameLatency, arguments.GsyncSupport, arguments.HdrSupport,
            (int)arguments.CaptureApi, arguments.QueueTarget, arguments.DrawFps,
            arguments.GpuId, arguments.DisplayId,
            arguments.CaptureOffsetLeft, arguments.CaptureOffsetTop, arguments.CaptureOffsetRight, arguments.CaptureOffsetBottom,
            arguments.MultiDisplayMode);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ApplyRunner(int mode, int fitMode, int method, int subtype,
        float scaleFactor, byte resizeBeforeScale, int sharpness, byte vrs,
        int frameGeneration, int frameGenerationSize, int frameGenerationMode,
        float frameMultiplier, float targetFps, int flowScale,
        byte clipCursor, byte cursorSensitivity, byte hideCursor, byte scaleCursor,
        int syncMode, int maxFrameLatency, byte gsyncSupport, byte hdrSupport,
        int captureApi, int queueTarget, byte drawFps, int gpuId, int displayId,
        int captureOffsetLeft, int captureOffsetTop, int captureOffsetRight, int captureOffsetBottom,
        byte multiDisplayMode);

    private static bool CallApply(IntPtr address, int mode, int fitMode, int method, int subtype,
        float scaleFactor, byte resizeBeforeScale, int sharpness, byte vrs,
        int frameGeneration, int frameGenerationSize, int frameGenerationMode,
        float frameMultiplier, float targetFps, int flowScale,
        byte clipCursor, byte cursorSensitivity, byte hideCursor, byte scaleCursor,
        int syncMode, int maxFrameLatency, byte gsyncSupport, byte hdrSupport,
        int captureApi, int queueTarget, byte drawFps, int gpuId, int displayId,
        int captureOffsetLeft, int captureOffsetTop, int captureOffsetRight, int captureOffsetBottom,
        byte multiDisplayMode)
    {
        try
        {
            var runner = Marshal.GetDelegateForFunctionPointer<ApplyRunner>(address);
            runner(mode, fitMode, method, subtype, scaleFactor, resizeBeforeScale, sharpness, vrs,
                frameGeneration, frameGenerationSize, frameGenerationMode, frameMultiplier, targetFps, flowScale,
                clipCursor, cursorSensitivity, hideCursor, scaleCursor, syncMode, maxFrameLatency,
                gsyncSupport, hdrSupport, captureApi, queueTarget, drawFps, gpuId, displayId,
                captureOffsetLeft, captureOffsetTop, captureOffsetRight, captureOffsetBottom, multiDisplayMode);
            return true;
        }
        catch (Exception ex) when (ex is SEHException or AccessViolationException) { return false; }
    }
}

/// <summary>Projects a stored profile onto the proven 32-argument ABI. Validation upstream has
/// already constrained every string to a known value; anything unrecognised maps to the neutral
/// member rather than throwing.</summary>
public static class ScalingEngineProjection
{
    public static ScalingEngineArguments FromProfile(ScalingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new()
        {
            Mode = profile.ScalingMode == "Custom" ? ScalingEngineMode.Custom : ScalingEngineMode.Auto,
            FitMode = profile.FitMode == "Fullscreen" ? ScalingFitMode.Fullscreen : ScalingFitMode.AspectRatio,
            Method = Method(profile),
            Subtype = Subtype(profile),
            ScaleFactor = ClampFloat((float)profile.ScaleFactor, 1f, 20f),
            ResizeBeforeScale = B(profile.ResizeBeforeScale),
            Sharpness = int.Clamp(profile.Sharpness, 0, 10),
            Vrs = B(profile.Vrs),
            FrameGeneration = Generation(profile.FrameGeneration),
            FrameGenerationSize = 0,
            FrameGenerationMode = GenerationMode(profile.FrameGenerationMode),
            FrameMultiplier = ClampFloat((float)profile.FrameMultiplier, 1f, 20f),
            TargetFps = ClampFloat((float)profile.TargetFps, 10f, 1000f),
            FlowScale = int.Clamp(profile.FlowScale, 25, 100),
            ClipCursor = B(profile.ClipCursor),
            CursorSensitivity = B(profile.AdjustCursorSpeed),
            HideCursor = B(profile.HideCursor),
            ScaleCursor = B(profile.ScaleCursor),
            SyncMode = Sync(profile.SyncMode),
            MaxFrameLatency = int.Clamp(profile.MaxFrameLatency, 1, 15),
            GsyncSupport = B(profile.GsyncSupport),
            HdrSupport = B(profile.HdrSupport),
            CaptureApi = profile.CaptureApi == "WGC" ? CaptureApi.Wgc : CaptureApi.Dxgi,
            QueueTarget = int.Clamp(profile.QueueTarget, 0, 2),
            DrawFps = B(profile.DrawFps),
            GpuId = int.Clamp(profile.PreferredGpuIndex, 0, 64),
            DisplayId = int.Clamp(profile.OutputDisplayIndex, 0, 64),
            CaptureOffsetLeft = int.Clamp(profile.CropLeft, 0, 16384),
            CaptureOffsetTop = int.Clamp(profile.CropTop, 0, 16384),
            CaptureOffsetRight = int.Clamp(profile.CropRight, 0, 16384),
            CaptureOffsetBottom = int.Clamp(profile.CropBottom, 0, 16384),
            MultiDisplayMode = B(profile.MultiDisplay),
        };
    }

    private static byte B(bool value) => value ? (byte)1 : (byte)0;
    private static float ClampFloat(float value, float min, float max) => float.IsFinite(value) ? float.Clamp(value, min, max) : min;

    private static ScalingEngineMethod Method(ScalingProfile profile) => profile.ScalingAlgorithm switch
    {
        "LS1" => profile.Ls1Performance ? ScalingEngineMethod.Ls1Performance : ScalingEngineMethod.Ls1,
        "FSR" => ScalingEngineMethod.Fsr,
        "NIS" => ScalingEngineMethod.Nis,
        "SGSR" => ScalingEngineMethod.Sgsr,
        "BicubicCAS" => ScalingEngineMethod.BicubicCas,
        "Anime4K" => ScalingEngineMethod.Anime4K,
        "XBR" => ScalingEngineMethod.Xbr,
        "SharpBilinear" => ScalingEngineMethod.SharpBilinear,
        "Integer" => ScalingEngineMethod.Integer,
        "Nearest" => ScalingEngineMethod.Nearest,
        _ => ScalingEngineMethod.Off,
    };

    private static int Subtype(ScalingProfile profile) => profile.ScalingAlgorithm switch
    {
        "LS1" when profile.Ls1Sharpness > 0 => int.Clamp(profile.Ls1Sharpness, 0, 4),
        "FSR" when profile.FsrOptimized => 1,
        "Anime4K" => profile.Anime4KQuality switch { "M" => 1, "L" => 2, "VL" => 3, "UL" => 4, _ => 0 },
        _ => 0,
    };

    private static FrameGenerationKind Generation(string value) => value switch
    {
        "LSFG1" => FrameGenerationKind.Lsfg1,
        "LSFG2" => FrameGenerationKind.Lsfg2,
        "LSFG3" => FrameGenerationKind.Lsfg3,
        _ => FrameGenerationKind.Off,
    };

    private static FrameGenerationMode GenerationMode(string value) => value switch
    {
        "Adaptive" => FrameGenerationMode.Adaptive,
        "X2" or "X3" or "X4" => FrameGenerationMode.Multiplier,
        _ => FrameGenerationMode.Fixed,
    };

    private static SyncMode Sync(string value) => value switch
    {
        "Off" => SyncMode.Off,
        "VSync" => SyncMode.VSync,
        "Half" => SyncMode.Half,
        "Third" => SyncMode.Third,
        _ => SyncMode.Default,
    };
}

public enum ScalingEngineMode : int { Auto = 0, Custom = 1 }
public enum ScalingFitMode : int { AspectRatio = 0, Fullscreen = 1 }
public enum ScalingEngineMethod : int { Off = 0, Ls1, Ls1Performance, Fsr, Nis, Sgsr, BicubicCas, Anime4K, Xbr, SharpBilinear, Integer, Nearest }
public enum FrameGenerationKind : int { Off = 0, Lsfg1, Lsfg2, Lsfg3 }
public enum FrameGenerationMode : int { Fixed = 0, Adaptive, Multiplier }
public enum SyncMode : int { Default = 0, Off, VSync, Half, Third }
public enum CaptureApi : int { Dxgi = 0, Wgc = 1 }
