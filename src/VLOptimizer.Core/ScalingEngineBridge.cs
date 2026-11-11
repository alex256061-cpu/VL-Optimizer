using System.Runtime.InteropServices;

namespace VLOptimizer.Core;

/// <summary>What a connected presentation engine can report. Every member here comes from an
/// export whose calling convention is a proven tail call into a documented Win32 function:
/// <c>GetAdapterNames</c>/<c>GetDisplayNames</c> end in <c>jmp qword [imp OLEAUT32.SysAllocStringLen]</c>
/// with <c>(LPCSTR, DWORD)</c>, and <c>GetDwmRefreshRate</c> returns an <c>int</c>. Nothing here
/// mutates engine state.</summary>
public sealed record ScalingEngineInfo(bool Available, string? LibraryPath, string Message, IReadOnlyList<string> Adapters, IReadOnlyList<string> Displays, int DwmRefreshRate);

/// <summary>Read-only host for the presentation engine DLL. Loads it with
/// <see cref="NativeLibrary"/> so a missing or mismatched library is reported instead of
/// killing the process, then exposes only the information exports.
/// <para><c>ApplySettings</c> is deliberately not bound. The disassembly shows it is a
/// 33-parameter function (one float at <c>[rsp+0x28]</c>, four in registers, the rest on the
/// stack) that writes process-wide globals without range checking. A wrong parameter binding
/// corrupts the host process, so it stays unbound until its ABI is proven at runtime.</para></summary>
public sealed class ScalingEngineBridge : IDisposable
{
    private const string EngineFileName = "Lossless.dll";

    /// <summary>File name of the library this bridge hosts. Exposed so tests can assert no static
    /// P/Invoke targets it.</summary>
    public const string FileName = EngineFileName;

    private readonly SemaphoreSlim gate = new(1, 1);
    private IntPtr handle;
    private NamesDelegate? getAdapters;
    private NamesDelegate? getDisplays;
    private DwmRateDelegate? getRefreshRate;
    private string? libraryPath;
    private bool bound;

    /// <summary>Looks for the engine beside the app and in an <c>engine</c> subfolder. Never downloads it.</summary>
    public static string? ResolveEnginePath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        foreach (var candidate in new[] { Path.Combine(baseDirectory, EngineFileName), Path.Combine(baseDirectory, "engine", EngineFileName) })
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    public async Task<ScalingEngineInfo> ReadInfoAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { return ReadInfoCore(); }
        finally { gate.Release(); }
    }

    private ScalingEngineInfo ReadInfoCore()
    {
        if (!Bind(out var failure)) return new(false, libraryPath, failure, [], [], 0);

        var adapters = ReadNames(getAdapters!);
        var displays = ReadNames(getDisplays!);
        int refresh = 0;
        try { if (getRefreshRate != null) refresh = getRefreshRate(); }
        catch (Exception ex) when (ex is SEHException or AccessViolationException) { refresh = 0; }

        string message = adapters.Count == 0 && displays.Count == 0
            ? "Biblioteca carregada, mas não reportou adaptadores nem monitores."
            : $"Biblioteca carregada: {adapters.Count} adaptador(es), {displays.Count} monitor(es).";
        return new(true, libraryPath, message, adapters, displays, refresh);
    }

    private bool Bind(out string failure)
    {
        failure = "";
        if (bound) return true;
        if (handle == IntPtr.Zero)
        {
            libraryPath = ResolveEnginePath();
            if (libraryPath == null) { failure = "Motor de apresentação ausente: coloque " + EngineFileName + " na pasta do VL ou em engine\\."; return false; }
            if (!NativeLibrary.TryLoad(libraryPath, out handle)) { failure = "Falha ao carregar " + EngineFileName + " (arquitetura incompatível com este processo)."; return false; }
        }

        var missing = new List<string>();
        getAdapters = Bind<NamesDelegate>("GetAdapterNames", missing);
        getDisplays = Bind<NamesDelegate>("GetDisplayNames", missing);
        getRefreshRate = Bind<DwmRateDelegate>("GetDwmRefreshRate", missing);

        if (missing.Count > 0)
        {
            failure = "Exports de leitura ausentes: " + string.Join(", ", missing) + ". Motor não conectado.";
            return false;
        }
        bound = true;
        return true;
    }

    private T? Bind<T>(string name, List<string> missing) where T : Delegate
    {
        if (NativeLibrary.TryGetExport(handle, name, out var address) && address != IntPtr.Zero)
        {
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }
        missing.Add(name);
        return null;
    }

    /// <summary>Both name exports are <c>BSTR</c> producers: they tail-call
    /// <c>SysAllocStringLen(LPCSTR, DWORD)</c>, so the returned pointer is a BSTR whose
    /// length prefix gives the byte count of the wide string. Entries are newline separated.</summary>
    private static IReadOnlyList<string> ReadNames(NamesDelegate export)
    {
        IntPtr pointer;
        try { pointer = export(); }
        catch (Exception ex) when (ex is SEHException or AccessViolationException) { return []; }
        if (pointer == IntPtr.Zero) return [];
        try
        {
            // A BSTR's length prefix is the byte count of the wide string that follows it.
            int byteLength = Marshal.ReadInt32(pointer, -4);
            if (byteLength <= 0 || byteLength > 1024 * 1024) return [];
            string value = Marshal.PtrToStringUni(pointer, byteLength / 2) ?? "";
            return value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        finally { Marshal.FreeBSTR(pointer); }
    }

    public void Dispose()
    {
        bound = false;
        getAdapters = null; getDisplays = null; getRefreshRate = null;
        if (handle != IntPtr.Zero) { NativeLibrary.Free(handle); handle = IntPtr.Zero; }
        gate.Dispose();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr NamesDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DwmRateDelegate();
}
