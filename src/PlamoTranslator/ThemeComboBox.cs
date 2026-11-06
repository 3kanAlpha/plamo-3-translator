namespace PlamoTranslator;

internal sealed class ThemeComboBox : ComboBox
{
    internal ThemeColors Colors = ThemeColors.Create(false, false);

    internal ThemeComboBox()
    {
        // Draw the content ourselves so a runtime switch cannot leave a native light background.
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        AutoSize = true;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        // The native owner-drawn combo can be taller than WinForms' preferred height.
        return Size;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var background = selected ? Colors.Accent : BackColor;
        var foreground = !Enabled ? Colors.Muted : selected ? Colors.AccentText : ForeColor;
        using var brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, e.Bounds);
        var bounds = e.Bounds;
        bounds.X += 2;
        bounds.Width -= 2;
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), e.Font, bounds, foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }
}
