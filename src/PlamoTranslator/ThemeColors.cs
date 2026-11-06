namespace PlamoTranslator;

internal sealed record ThemeColors(Color Background, Color Surface, Color Text, Color Muted,
    Color Border, Color Accent, Color AccentText, Color Error)
{
    internal static ThemeColors Create(bool dark, bool highContrast) => highContrast
        ? new(SystemColors.Control, SystemColors.Window, SystemColors.WindowText, SystemColors.GrayText,
            SystemColors.WindowText, SystemColors.Highlight, SystemColors.HighlightText, SystemColors.ControlText)
        : dark
            ? new(Color.FromArgb(30, 31, 36), Color.FromArgb(40, 42, 49), Color.FromArgb(235, 237, 245),
                Color.FromArgb(162, 166, 184), Color.FromArgb(79, 83, 102), Color.FromArgb(165, 180, 252),
                Color.FromArgb(30, 27, 75), Color.FromArgb(252, 165, 165))
            : new(Color.FromArgb(245, 246, 250), Color.White, Color.FromArgb(30, 34, 48),
                Color.FromArgb(101, 108, 128), Color.FromArgb(177, 183, 201), Color.FromArgb(79, 70, 229),
                Color.White, Color.FromArgb(180, 35, 45));
}
