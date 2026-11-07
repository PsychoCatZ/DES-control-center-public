using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DesControlCenter.App.Tray;

/// <summary>
/// A system-wide keyboard shortcut (works while DES sits in the tray). It owns a hidden message-only window that
/// receives WM_HOTKEY, so it needs neither a visible window nor focus. Registration can fail if another program already
/// holds the same combination; <see cref="IsRegistered"/> tells.
/// </summary>
public sealed class GlobalHotKey : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private readonly int _id;
    private readonly Action _onPressed;

    public bool IsRegistered { get; }

    /// <summary>Registers Ctrl+Alt+&lt;virtual key&gt;.</summary>
    public GlobalHotKey(uint virtualKey, Action onPressed, int id = 0x4443)
    {
        _id = id;
        _onPressed = onPressed;
        _source = new HwndSource(new HwndSourceParameters("DesHotKeyWindow") { ParentWindow = HwndMessage, Width = 0, Height = 0 });
        _source.AddHook(WndProc);
        IsRegistered = RegisterHotKey(_source.Handle, _id, ModControl | ModAlt | ModNoRepeat, virtualKey);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == _id)
        {
            handled = true;
            _onPressed();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (IsRegistered)
        {
            UnregisterHotKey(_source.Handle, _id);
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
