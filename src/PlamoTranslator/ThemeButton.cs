using System.Drawing.Drawing2D;

namespace PlamoTranslator;

internal sealed class ThemeButton : Button
{
    private bool keyboardPressed;

    internal ThemeButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        MouseEnter += (_, _) => Invalidate();
        MouseLeave += (_, _) => Invalidate();
        MouseDown += (_, _) => Invalidate();
        MouseUp += (_, _) => Invalidate();
        MouseCaptureChanged += (_, _) => Invalidate();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Space) { keyboardPressed = true; Invalidate(); } };
        KeyUp += (_, e) => { if (e.KeyCode == Keys.Space) { keyboardPressed = false; Invalidate(); } };
        LostFocus += (_, _) => { keyboardPressed = false; Invalidate(); };
    }

    private GraphicsPath Outline(RectangleF bounds)
    {
        var path = new GraphicsPath();
        var cornerSize = 10f * DeviceDpi / 96;
        path.AddRoundedRectangle(bounds, new SizeF(cornerSize, cornerSize));
        return path;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width <= 0 || Height <= 0) return;
        using var path = Outline(ClientRectangle);
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var hovered = Enabled && Region?.IsVisible(PointToClient(MousePosition)) == true;
        var pressed = Enabled && (keyboardPressed || (hovered && Capture && (MouseButtons & MouseButtons.Left) != 0));
        var background = pressed ? FlatAppearance.MouseDownBackColor
            : hovered ? FlatAppearance.MouseOverBackColor : BackColor;
        var foreground = !Enabled && SystemInformation.HighContrast ? SystemColors.GrayText : ForeColor;
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = Outline(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1));
        using var fill = new SolidBrush(background);
        using var border = new Pen(FlatAppearance.BorderColor, IsDefault ? 2 : 1);
        e.Graphics.FillPath(fill, outline);
        e.Graphics.DrawPath(border, outline);
        var textBounds = ClientRectangle;
        if (pressed) textBounds.Offset(1, 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            (ShowKeyboardCues ? TextFormatFlags.Default : TextFormatFlags.HidePrefix));
        if (Focused && ShowFocusCues)
        {
            using var focus = Outline(new RectangleF(3.5f, 3.5f, Width - 7, Height - 7));
            using var focusPen = new Pen(foreground) { DashStyle = DashStyle.Dot };
            e.Graphics.DrawPath(focusPen, focus);
        }
    }
}
