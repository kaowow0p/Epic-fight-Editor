using EpicFightJsonGeneratorApp.Forms;

namespace EpicFightJsonGeneratorApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
