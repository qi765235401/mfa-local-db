using MfaLocalDb.Controls;

namespace MfaLocalDb;

public sealed class MapForm : Form
{
    private readonly WorldMapControl _map = new();

    public MapForm(IReadOnlySet<string> countryNames)
    {
        Text = "世界地图";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 620);
        Size = new Size(1180, 760);
        BackColor = Color.FromArgb(240, 243, 247);
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildUi(countryNames);
    }

    public string? SelectedCountryName { get; private set; }

    private void BuildUi(IReadOnlySet<string> countryNames)
    {
        var topBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(16, 10, 16, 10),
            BackColor = Color.FromArgb(249, 250, 252),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "滚轮缩放，右键或中键拖动平移；点击国家后关闭地图并打开国家正文。",
            ForeColor = Color.FromArgb(49, 65, 84),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };

        var resetButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "重置",
            Margin = new Padding(8, 0, 0, 0),
        };
        resetButton.Click += (_, _) => _map.ResetView();

        var closeButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "关闭",
            Margin = new Padding(8, 0, 0, 0),
        };
        closeButton.Click += (_, _) => Close();

        layout.Controls.Add(hint, 0, 0);
        layout.Controls.Add(resetButton, 1, 0);
        layout.Controls.Add(closeButton, 2, 0);
        topBar.Controls.Add(layout);

        _map.Dock = DockStyle.Fill;
        _map.Margin = new Padding(12);
        _map.SetAvailableCountries(countryNames);
        _map.CountrySelected += (_, countryName) =>
        {
            SelectedCountryName = countryName;
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(_map);
        Controls.Add(topBar);
    }
}
