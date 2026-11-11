using System.Runtime.InteropServices;

namespace VLOptimizer.Core;

/// <summary>Global hotkey that works even when the game has focus.
///
/// <para>Registered with <c>RegisterHotKey</c> against a dedicated message window, so it needs no
/// accessibility hook and cannot be swallowed by a focused fullscreen window. Registration is best
/// effort: if another program already owns the combination, <see cref="TryRegister"/> returns false and
/// the caller reports it instead of pretending the key works.</para></summary>
public sealed class GlobalHotkey : IDisposable
{
    /// <summary>Ctrl+Alt+B, the same combination the reference tool uses.</summary>
    public const uint Control = 0x0002;
    public const uint Alt = 0x0001;
    public const uint KeyB = 0x42;
    private const int WmHotkey = 0x0312;

    private readonly object gate = new();
    private nint window;
    private WndProc? proc;
    private bool registered;
    private Action? action;
    private int id;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    public bool IsRegistered { get { lock (gate) return registered; } }

    /// <summary>Creates the message window and registers the hotkey. Returns false when the OS refuses
    /// — usually because another application already registered the same combination.</summary>
    public bool TryRegister(uint modifiers, uint virtualKey, Action onPressed)
    {
        lock (gate)
        {
            if (registered) return true;
            ArgumentNullException.ThrowIfNull(onPressed);
            action = onPressed;

            try
            {
                proc = WindowProc;
                // The window only exists to receive WM_HOTKEY; it is never shown.
                window = CreateWindowEx(0, "STATIC", "VLHotkey", 0, 0, 0, 0, 0, 0, 0, GetModuleHandle(null), 0);
                if (window == 0) return false;

                id = Environment.ProcessId & 0xBFFF;
                if (!RegisterHotKey(window, id, modifiers, virtualKey))
                {
                    DestroyWindow(window);
                    window = 0;
                    return false;
                }
                registered = true;
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
            {
                return false;
            }
        }
    }

    private nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WmHotkey)
        {
            // The callback must never throw into the message loop.
            try { action?.Invoke(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
            return 0;
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (!registered) return;
            try { UnregisterHotKey(window, id); } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
            if (window != 0) DestroyWindow(window);
            window = 0;
            registered = false;
            action = null;
            proc = null;
        }
    }

    [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);
}
