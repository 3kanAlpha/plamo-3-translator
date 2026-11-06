namespace PlamoTranslator;

internal sealed class ThemeSettings(string path)
{
    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlamoTranslator", "theme.txt");

    internal SystemColorMode Load()
    {
        try
        {
            return File.ReadAllText(path).Trim() switch
            {
                "Classic" => SystemColorMode.Classic,
                "Dark" => SystemColorMode.Dark,
                _ => SystemColorMode.System
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return SystemColorMode.System;
        }
    }

    internal bool Save(SystemColorMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, mode.ToString());
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
