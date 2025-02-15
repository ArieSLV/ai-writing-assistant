using System.Runtime.InteropServices;

public class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private IntPtr _hookID = IntPtr.Zero;
    private LowLevelKeyboardProc _proc;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    public event Action HotKeyPressed;

    public KeyboardHook()
    {
        _proc = HookCallback;
        _hookID = SetHook(_proc);
    }

    private IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
        using (var curModule = curProcess.MainModule)
        {
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam);

            bool ctrl = (GetAsyncKeyState(Keys.ControlKey) < 0);
            bool shift = (GetAsyncKeyState(Keys.ShiftKey) < 0);
            bool xPressed = ((Keys)vkCode == Keys.X);

            if (ctrl && shift && xPressed)
            {
                Task.Run(() =>
                {
                    Thread.Sleep(50); // Даем ОС обработать событие
                    HotKeyPressed?.Invoke();
                });

                return (IntPtr)1; // Блокируем дальнейшую обработку
            }
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(Keys vKey);

    public void Dispose()
    {
        UnhookWindowsHookEx(_hookID);
    }
}