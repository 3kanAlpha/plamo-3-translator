namespace PlamoTranslator;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var settings = new ThemeSettings(ThemeSettings.DefaultPath);
        Application.SetColorMode(settings.Load());
        Application.Run(new MainForm(settings));
    }
}
