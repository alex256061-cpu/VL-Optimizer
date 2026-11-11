using System.Runtime.InteropServices;

namespace VLOptimizer.Core;

public enum ScalingEngineState { Unavailable, Running, Failed }

public sealed record ScalingEngineStart(ScalingEngineState State, string Message, nint WindowHandle = default);

/// <summary>Runs the native scaling engine inside the VL process, in the order its exports require
/// and that was verified against the real binary:
/// <list type="number">
/// <item><c>Init(callback)</c> first. The engine stores the pointer and <c>Activate</c> calls it
///   during teardown; passing null leaves a null function pointer and the engine faults at address 0
///   with 0xc0000005. The callback is therefore rooted for the whole session.</item>
/// <item>The output window and D3D11 device are created <b>after</b> Init, so the engine's DXGI hooks
///   are installed before this process creates its presentation objects.</item>
/// <item><c>Activate(hwnd)</c> installs the window-event hook and the overlay swapchain. It is a
///   toggle: calling it again disables the engine.</item>
/// <item><c>ApplySettings</c> with the 32-argument ABI, proven against the disassembly.</item>
/// </list>
/// The engine is a compositor: it captures the foreground window and presents the scaled result in
/// its own output window. It does not inject into the game, so the target keeps running untouched.</summary>
public sealed class ScalingEngineHost : IDisposable
{
    private readonly object gate = new();
    private EngineBinding? binding;
    private EngineCallback? callback;
    private nint outputWindow;
    private bool activated;
    private Thread? engineThread;
    private ManualResetEventSlim? engineStop;


    // x64 has a single calling convention, so the unmarshalable x86-only attributes do not apply.
    // The callback is invoked by native code with four pointer arguments; matching the C++ host's
    // `static void __stdcall EngineTeardownCallback(void*, void*, void*, void*)` exactly.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void EngineCallback(nint a1, nint a2, nint a3, nint a4);

    public ScalingEngineHost() => callback = OnEngineCallback;

    // The engine invokes this on teardown with zeroed arguments. Doing nothing is the correct answer:
    // the pointer exists only so the engine never calls address 0.
    private static void OnEngineCallback(nint a1, nint a2, nint a3, nint a4) { _ = a1; _ = a2; _ = a3; _ = a4; }

    public bool IsRunning { get { lock (gate) return binding is not null && activated; } }
    public nint OutputWindow { get { lock (gate) return outputWindow; } }

    /// <summary>Brings the engine up. Returns a failed start (never throws) when the library is
    /// wrong, Init refuses, or no window can be created, so the caller shows a reason instead of
    /// crashing.
    /// <para><b>Thread requirement, measured:</b> Init faults when called on a plain .NET console
    /// main thread and succeeds on an explicit STA thread with a large stack. The engine's whole
    /// lifetime therefore runs on a dedicated STA thread created here; callers never have to know.</para></summary>
    public ScalingEngineStart Start(string libraryPath, int width = 1280, int height = 720)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        lock (gate)
        {
            if (binding is not null) return new(ScalingEngineState.Running, "O motor já está ativo.", outputWindow);
            if (!File.Exists(libraryPath)) return new(ScalingEngineState.Failed, "Biblioteca não encontrada.");

            ScalingEngineStart? start = null;
            using var ready = new ManualResetEventSlim(false);
            using var stopSignal = new ManualResetEventSlim(false);

            engineStop = stopSignal;
            engineThread = new Thread(() =>
            {
                try
                {
                    start = StartOnCurrentThread(libraryPath, width, height);
                }
                catch (Exception ex) { start = new(ScalingEngineState.Failed, ex.GetType().Name + ": " + ex.Message); }
                finally { ready.Set(); }

                if (start?.State != ScalingEngineState.Running) return;

                // The window lives on this thread. If the thread exits, the HWND dies with it and the
                // engine loses its overlay target, so keep pumping messages until Stop is called.
                while (!stopSignal.IsSet)
                {
                    WindowHost.PumpMessages();
                    Thread.Sleep(10);
                }
            }, 16 * 1024 * 1024);
            engineThread.SetApartmentState(ApartmentState.STA);
            engineThread.IsBackground = true;
            engineThread.Start();

            if (!ready.Wait(TimeSpan.FromSeconds(15)))
            {
                return new(ScalingEngineState.Failed, "O motor não respondeu a tempo na thread de inicialização.");
            }
            return start ?? new(ScalingEngineState.Failed, "A thread do motor terminou sem resultado.");
        }
    }

    private ScalingEngineStart StartOnCurrentThread(string libraryPath, int width, int height)
    {
        {
            var loaded = EngineBinding.Load(libraryPath);
            if (loaded.Error.Length > 0) return new(ScalingEngineState.Failed, loaded.Error);

            byte initialized;
            try { initialized = loaded.Init(callback!); }
            catch (Exception ex) when (ex is SEHException or AccessViolationException)
            {
                loaded.Dispose();
                return new(ScalingEngineState.Failed, "Init falhou com falha de proteção (o motor recusou o processo atual).");
            }
            if (initialized == 0)
            {
                loaded.Dispose();
                return new(ScalingEngineState.Failed, "O motor recusou Init (D3D11 indisponível ou DispatcherQueue sem suporte).");
            }

            // After Init, never before: the engine hooks DXGI during Init and needs the window to
            // exist only when Activate installs the overlay.
            outputWindow = WindowHost.Create("VL Scaling", width, height);
            if (outputWindow == 0)
            {
                loaded.Dispose();
                return new(ScalingEngineState.Failed, "Não foi possível criar a janela de saída do motor.");
            }

            if (loaded.Activate(outputWindow) == 0)
            {
                WindowHost.Destroy(outputWindow);
                outputWindow = 0;
                loaded.Dispose();
                return new(ScalingEngineState.Failed, "O motor recusou a janela (Activate devolveu falso).");
            }

            binding = loaded;
            activated = true;
            return new(ScalingEngineState.Running, "Motor ativo: captura a janela em primeiro plano e apresenta escalado.", outputWindow);
        }
    }

    /// <summary>Pushes a stored profile to the engine through the proven 32-argument ABI.</summary>
    public (bool Ok, string Message) Apply(ScalingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (gate)
        {
            if (binding is null || !activated) return (false, "Inicie o motor antes de aplicar um perfil.");
            bool sent = binding.Apply(ScalingEngineProjection.FromProfile(profile));
            return sent
                ? (true, profile.ScalingAlgorithm + " a " + profile.ScaleFactor.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x enviado ao motor.")
                : (false, "O motor não aceitou a configuração.");
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            if (binding is null && engineThread is null) return;

            // Tear the engine down on its own thread first, then release the message pump so the
            // thread can exit; destroying the window from another thread while it pumps is a race.
            if (binding is not null)
            {
                try { if (activated) binding.Deactivate(outputWindow); }
                catch (Exception ex) when (ex is SEHException or AccessViolationException) { }
                binding.Dispose();
                binding = null;
                activated = false;
            }
            if (outputWindow != 0) { WindowHost.Destroy(outputWindow); outputWindow = 0; }

            engineStop?.Set();
            engineThread?.Join(TimeSpan.FromSeconds(3));
            engineThread = null;
            engineStop = null;
        }
    }

    public void Dispose()
    {
        Stop();
        callback = null;
        GC.SuppressFinalize(this);
    }
}

/// <summary>Owns the loaded module and the function pointers. A missing or wrong library becomes an
/// error string, never an exception at type load.</summary>
internal sealed class EngineBinding
{
    private nint module;
    private InitDelegate? init;
    private ToggleDelegate? activate;
    private Action? uninit;
    private nint applyAddress;

    private delegate byte InitDelegate(nint callback);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate byte ToggleDelegate(nint hwnd);

    public string Error { get; private set; } = "";

    public static EngineBinding Load(string path)
    {
        var binding = new EngineBinding();
        if (!File.Exists(path)) { binding.Error = "Biblioteca não encontrada."; return binding; }
        if (!NativeLibrary.TryLoad(path, out binding.module)) { binding.Error = "Falha ao carregar " + Path.GetFileName(path) + "."; return binding; }

        if (!NativeLibrary.TryGetExport(binding.module, "Init", out var initAddress) ||
            !NativeLibrary.TryGetExport(binding.module, "Activate", out var activateAddress) ||
            !NativeLibrary.TryGetExport(binding.module, "UnInit", out var uninitAddress) ||
            !NativeLibrary.TryGetExport(binding.module, "ApplySettings", out binding.applyAddress))
        {
            binding.Error = "Exports obrigatórios ausentes no motor.";
            NativeLibrary.Free(binding.module);
            binding.module = 0;
            return binding;
        }
        binding.init = Marshal.GetDelegateForFunctionPointer<InitDelegate>(initAddress);
        binding.activate = Marshal.GetDelegateForFunctionPointer<ToggleDelegate>(activateAddress);
        binding.uninit = Marshal.GetDelegateForFunctionPointer<Action>(uninitAddress);
        return binding;
    }

    public byte Init(Delegate callback)
    {
        // A managed delegate passed by marshal must stay alive for the engine's whole lifetime; the
        // session keeps a field reference, and Marshal.GetFunctionPointerForDelegate pins it.
        nint pointer = Marshal.GetFunctionPointerForDelegate(callback);
        GC.KeepAlive(callback);
        return init!(pointer);
    }
    public byte Activate(nint hwnd) => activate!(hwnd);
    public byte Deactivate(nint hwnd) => activate!(hwnd);  // Activate is a toggle
    public bool Apply(ScalingEngineArguments arguments) => applyAddress != 0 && ScalingEngineNative.ApplyFromAddress(applyAddress, arguments);

    public void Dispose()
    {
        if (module == 0) return;
        try { uninit?.Invoke(); } catch (Exception ex) when (ex is SEHException or AccessViolationException) { }
        NativeLibrary.Free(module);
        module = 0;
    }
}

/// <summary>Minimal top-level window for the engine to present into, plus a throwaway D3D11 device and
/// swapchain on that window. The swapchain is not rendered; it exists so the process holds a real
/// <c>IDXGISwapChain</c> whose vtable is the shared DXGI one, which is what the present counter patches.
/// The engine and this swapchain therefore share one vtable, so a single patch counts both.</summary>
internal static class WindowHost
{
    private const string ClassName = "VLScalingOutput";
    private static WndProcDelegate? proc;  // rooted for the class lifetime

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProcDelegate(nint hwnd, uint message, nint wParam, nint lParam);

    public static nint Create(string title, int width, int height)
    {
        proc ??= DefaultProc;
        var instance = GetModuleHandle(null);

        var windowClass = new WindowClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
            // CS_OWNDC | CS_VREDRAW | CS_HREDRAW
            style = 0x0020 | 0x0001 | 0x0002,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = instance,
            hCursor = LoadCursor(0, 32512 /* IDC_ARROW */),
            lpszClassName = ClassName,
        };
        RegisterClassEx(ref windowClass);  // already-registered is fine, the class is identical

        const uint style = 0x00CF0000 /* WS_OVERLAPPEDWINDOW */ | 0x04000000 /* WS_CLIPCHILDREN */;
        nint hwnd = CreateWindowEx(0, ClassName, title, style, 100, 100, width, height, 0, 0, instance, 0);
        // A return value is not proof: an invalid HWND breaks Activate silently. Verify before use.
        if (hwnd == 0 || !IsWindow(hwnd)) return 0;
        ShowWindow(hwnd, 5 /* SW_SHOW */);
        UpdateWindow(hwnd);
        return hwnd;
    }

    public static void Destroy(nint hwnd)
    {
        if (hwnd != 0) DestroyWindow(hwnd);
    }



    /// <summary>Pumps pending window messages; the caller drives this on its render tick.</summary>
    public static void PumpMessages()
    {
        while (PeekMessage(out var message, 0, 0, 0, 1 /* PM_REMOVE */))
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    private static nint DefaultProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        // WM_CLOSE hides instead of destroying so the engine keeps a valid HWND while it runs.
        if (message == 0x10) { ShowWindow(hwnd, 0); return 0; }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateWindow(nint hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
    private static extern nint LoadCursor(nint instance, nint cursor);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Msg message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg message);
    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Msg message);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);
}
