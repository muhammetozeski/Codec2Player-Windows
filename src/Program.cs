namespace Codec2Player;

/// <summary>Application entry point.</summary>
static class Program
{
    /// <summary>Initializes WinForms and runs the main window message loop.</summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
