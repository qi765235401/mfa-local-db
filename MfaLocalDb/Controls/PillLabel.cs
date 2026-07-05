using System.Drawing.Drawing2D;

namespace MfaLocalDb.Controls;

public sealed class PillLabel : Control
{
    public PillLabel()
    {
        DoubleBuffered = true;
        AutoSize = false;
        Height = 30;
        Padding = new Padding(12, 7, 12, 7);
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        ForeColor = Color.FromArgb(33, 49, 71);
        FillColor = Color.FromArgb(233, 239, 248);
        BorderColor = Color.FromArgb(208, 218, 231);
    }

    public Color FillColor { get; set; }

    public Color BorderColor { get; set; }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        using var graphics = CreateGraphics();
        var size = TextRenderer.MeasureText(graphics, Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        Width = size.Width + Padding.Horizontal;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreatePath(rect, Height / 2);
        using var brush = new SolidBrush(FillColor);
        using var pen = new Pen(BorderColor, 1f);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            rect,
            ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath CreatePath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, radius * 2);
        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
