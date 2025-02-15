using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public class ClipboardManager
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    private const int KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_C = 0x43;

    public static string GetSelectedText()
    {
        string selectedText = null;
        var backup = BackupClipboard();

        try
        {
            Clipboard.Clear();
            ReleaseModifierKeys();
            SimulateCtrlC();
            Thread.Sleep(200); // Даем время на копирование

            // Читаем текст в отдельном STA-потоке
            selectedText = ReadClipboardTextInSTAThread();

            if (string.IsNullOrWhiteSpace(selectedText))
            {
                for (int i = 0; i < 5; i++) // До 5 попыток
                {
                    Thread.Sleep(200);
                    selectedText = ReadClipboardTextInSTAThread();
                    if (!string.IsNullOrWhiteSpace(selectedText))
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при получении текста: {ex.Message}");
        }
        finally
        {
            RestoreClipboard(backup);
        }

        return selectedText;
    }

    private static void SimulateCtrlC()
    {
        keybd_event(VK_CONTROL, 0, 0, 0);
        keybd_event(VK_C, 0, 0, 0);
        Thread.Sleep(100); // Увеличиваем задержку
        keybd_event(VK_C, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
    }


    private static void ReleaseModifierKeys()
    {
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, 0);
        Thread.Sleep(50);
    }

    private static string ReadClipboardTextInSTAThread() => ClipboardHelper.GetClipboardText();


    public static IDataObject BackupClipboard()
    {
        IDataObject backup = null;
        Thread staThread = new Thread(() =>
        {
            try
            {
                backup = Clipboard.GetDataObject();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка резервного копирования буфера: {ex.Message}");
            }
        });

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();
        return backup;
    }

    public static void RestoreClipboard(IDataObject backup)
    {
        if (backup == null) return;

        Thread staThread = new Thread(() =>
        {
            try
            {
                ClipboardHelper.SetClipboardText((string)backup.GetData(DataFormats.Text));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка восстановления буфера обмена: {ex.Message}");
            }
        });

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();
    }

}