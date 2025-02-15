public static class ClipboardHelper
{
    private static Thread _staThread;
    private static AutoResetEvent _requestEvent = new AutoResetEvent(false);
    private static AutoResetEvent _responseEvent = new AutoResetEvent(false);
    private static Func<object> _clipboardAction;
    private static object _clipboardResult;

    static ClipboardHelper()
    {
        _staThread = new Thread(ClipboardThread);
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.IsBackground = true;
        _staThread.Start();
    }

    private static void ClipboardThread()
    {
        Application.Run(new ApplicationContext());
    }

    public static string GetClipboardText() => (string)ExecuteInSTAThread(() => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty);

    public static void SetClipboardText(string text)
    {
        ExecuteInSTAThread(() =>
        {
            Clipboard.SetText(text);
            return null;
        });
    }

    private static object ExecuteInSTAThread(Func<object> action)
    {
        _clipboardAction = action;
        _requestEvent.Set();
        _responseEvent.WaitOne();
        return _clipboardResult;
    }
}