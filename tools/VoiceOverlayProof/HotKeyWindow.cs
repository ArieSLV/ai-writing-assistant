using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VoiceOverlayProof;

internal sealed class HotKeyWindow : NativeWindow, IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int VoiceHotKeyId = 3;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VirtualKeyG = 0x47;

    private bool _registered;
    private bool _disposed;

    public HotKeyWindow()
    {
        CreateHandle(new CreateParams());
    }

    public event EventHandler? Pressed;

    public void Register()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_registered)
        {
            return;
        }

        if (!RegisterHotKey(Handle, VoiceHotKeyId, ModControl | ModShift, VirtualKeyG))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Ctrl+Shift+G could not be registered for the Voice overlay proof.");
        }

        _registered = true;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        _ = UnregisterHotKey(Handle, VoiceHotKeyId);
        _registered = false;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotKey && message.WParam.ToInt32() == VoiceHotKeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Unregister();
        DestroyHandle();
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
