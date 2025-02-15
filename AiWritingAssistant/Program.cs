using System.Text;

namespace AiWritingAssistant;

public class Program
{
    [STAThread]
    static void Main()
    {
        Console.WriteLine($"Thread Apartment State: {Thread.CurrentThread.GetApartmentState()}");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Console.OutputEncoding = Encoding.UTF8;
        Application.Run(new BackgroundApp());
    }
}