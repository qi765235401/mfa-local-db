namespace MfaLocalDb.Controls;

public sealed class CardPanel : Panel
{
    public CardPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.White;
        BorderColor = Color.FromArgb(222, 228, 235);
        CornerRadius = 0;
    }

    public Color BorderColor { get; set; }

    public int CornerRadius { get; set; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var brush = new SolidBrush(BackColor);
        using var pen = new Pen(BorderColor, 1f);
        e.Graphics.FillRectangle(brush, bounds);
        e.Graphics.DrawRectangle(pen, bounds);
    }
}
