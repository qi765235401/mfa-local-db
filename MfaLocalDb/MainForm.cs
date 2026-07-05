using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using MfaLocalDb.Controls;
using MfaLocalDb.Models;
using MfaLocalDb.Services;

namespace MfaLocalDb;

public sealed class MainForm : Form
{
    private static readonly Regex FactRegex = new(@"^【(?<key>[^】]{1,30})】\s*(?<value>.*)$", RegexOptions.Compiled);

    private readonly DatabaseService _database;

    private readonly TextBox _searchBox = new();
    private readonly ComboBox _kindCombo = new();
    private readonly Button _syncButton = new();
    private readonly Button _openSourceButton = new();
    private readonly Button _mapButton = new();
    private readonly Label _resultsLabel = new();
    private readonly Label _overviewTitleLabel = new();
    private readonly ListView _entryListView = new();
    private readonly Label _titleLabel = new();
    private readonly Label _metaLabel = new();
    private readonly FlowLayoutPanel _badgePanel = new();
    private readonly FlowLayoutPanel _factsPanel = new();
    private readonly Label _factsTitleLabel = new();
    private readonly WebBrowser _contentBrowser = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripStatusLabel _countLabel = new();
    private readonly SplitContainer _mainSplit = new();
    private readonly SplitContainer _leftSplit = new();
    private readonly SplitContainer _overviewSplit = new();
    private readonly SplitContainer _detailSplit = new();
    private readonly ToolStripMenuItem _leftModuleMenu = new("检索列表") { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem _factsModuleMenu = new("结构化信息") { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem _contentModuleMenu = new("正文") { Checked = true, CheckOnClick = true };
    private IReadOnlySet<string> _countryNames = new HashSet<string>(StringComparer.Ordinal);

    private EntryDetail? _currentEntry;
    private bool _isSyncing;

    public MainForm(DatabaseService database)
    {
        _database = database;

        Text = "外交部国家和组织信息库";
        MinimumSize = new Size(1320, 820);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(240, 243, 247);
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildUi();
        Load += async (_, _) => await LoadEntriesAsync();
        Shown += (_, _) =>
        {
            ApplySplitterMinimums();
            ResetSplitterBoundsIfNeeded(force: true);
        };
    }

    private void BuildUi()
    {
        var menuStrip = new MenuStrip
        {
            Dock = DockStyle.Top,
            BackColor = Color.FromArgb(249, 250, 252),
        };
        var moduleMenu = new ToolStripMenuItem("模块");
        moduleMenu.DropDownItems.AddRange([
            _leftModuleMenu,
            _factsModuleMenu,
            _contentModuleMenu,
        ]);
        menuStrip.Items.Add(moduleMenu);

        _leftModuleMenu.CheckedChanged += (_, _) => ApplyModuleVisibility();
        _factsModuleMenu.CheckedChanged += (_, _) => ApplyModuleVisibility();
        _contentModuleMenu.CheckedChanged += (_, _) => ApplyModuleVisibility();

        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(18, 8, 18, 8),
            BackColor = Color.FromArgb(249, 250, 252),
        };

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "外交部国家和组织信息库",
            Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 35, 51),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        headerPanel.Controls.Add(title);

        _mainSplit.Dock = DockStyle.Fill;
        _mainSplit.Orientation = Orientation.Vertical;
        _mainSplit.SplitterWidth = 7;
        _mainSplit.Panel1MinSize = 25;
        _mainSplit.Panel2MinSize = 25;
        _mainSplit.BackColor = BackColor;
        _mainSplit.Padding = new Padding(12, 12, 12, 10);
        _mainSplit.Panel1.Controls.Add(BuildLeftColumn());
        _mainSplit.Panel2.Controls.Add(BuildRightColumn());

        Resize += (_, _) => ResetSplitterBoundsIfNeeded();

        _statusStrip.BackColor = Color.FromArgb(248, 250, 252);
        _statusStrip.SizingGrip = false;
        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
        _statusStrip.Items.Add(_countLabel);

        Controls.Add(_mainSplit);
        Controls.Add(headerPanel);
        Controls.Add(menuStrip);
        Controls.Add(_statusStrip);

        UpdateDetail(null);
        ApplyModuleVisibility();
    }

    private void ApplyModuleVisibility()
    {
        if (!_factsModuleMenu.Checked && !_contentModuleMenu.Checked)
        {
            _contentModuleMenu.Checked = true;
            return;
        }

        _mainSplit.Panel1Collapsed = !_leftModuleMenu.Checked;
        _detailSplit.Panel1Collapsed = !_factsModuleMenu.Checked;
        _detailSplit.Panel2Collapsed = !_contentModuleMenu.Checked;
        ResetSplitterBoundsIfNeeded();
    }

    private void ResetSplitterBoundsIfNeeded(bool force = false)
    {
        ClampSplitter(_mainSplit, 400, force);
        ClampSplitter(_leftSplit, 165, force);
        ClampSplitter(_overviewSplit, 132, force);
        ClampSplitter(_detailSplit, 164, force);
    }

    private void ApplySplitterMinimums()
    {
        TrySetSplitterMinimums(_overviewSplit, 104, 180);
        TrySetSplitterMinimums(_detailSplit, 96, 180);
    }

    private static void TrySetSplitterMinimums(SplitContainer split, int panel1MinSize, int panel2MinSize)
    {
        var length = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        if (length <= panel1MinSize + panel2MinSize + split.SplitterWidth)
        {
            return;
        }

        split.Panel1MinSize = panel1MinSize;
        split.Panel2MinSize = panel2MinSize;
    }

    private static void ClampSplitter(SplitContainer split, int preferredDistance, bool force)
    {
        if (split.Panel1Collapsed || split.Panel2Collapsed || split.Width <= 0 || split.Height <= 0)
        {
            return;
        }

        var length = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        var min = split.Panel1MinSize;
        var max = length - split.Panel2MinSize - split.SplitterWidth;
        if (max < min)
        {
            return;
        }

        if (force || split.SplitterDistance < min || split.SplitterDistance > max)
        {
            split.SplitterDistance = Math.Clamp(preferredDistance, min, max);
        }
    }

    private Control BuildLeftColumn()
    {
        _leftSplit.Dock = DockStyle.Fill;
        _leftSplit.Orientation = Orientation.Horizontal;
        _leftSplit.SplitterWidth = 7;
        _leftSplit.Panel1MinSize = 25;
        _leftSplit.Panel2MinSize = 25;
        _leftSplit.BackColor = BackColor;

        var searchCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.White,
        };

        var searchLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 2,
        };
        searchLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        searchLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        searchLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        searchLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var searchTitle = new Label
        {
            Text = "离线检索",
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(26, 37, 52),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var searchHint = new Label
        {
            Text = "离线检索：名称优先匹配，正文命中排在后面。",
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Regular),
            ForeColor = Color.FromArgb(106, 121, 140),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _kindCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _kindCombo.Items.AddRange(["全部", "国家", "组织"]);
        _kindCombo.SelectedIndex = 0;
        _kindCombo.Dock = DockStyle.Fill;
        _kindCombo.Margin = new Padding(0, 3, 10, 3);
        _kindCombo.SelectedIndexChanged += async (_, _) => await LoadEntriesAsync();

        _searchBox.PlaceholderText = "搜索名称、标题或正文关键词";
        _searchBox.Dock = DockStyle.Fill;
        _searchBox.Margin = new Padding(0, 3, 0, 3);
        _searchBox.TextChanged += async (_, _) => await LoadEntriesAsync();

        _syncButton.Text = "同步官网";
        _syncButton.Size = new Size(92, 32);
        _syncButton.FlatStyle = FlatStyle.Flat;
        _syncButton.FlatAppearance.BorderColor = Color.FromArgb(211, 220, 232);
        _syncButton.BackColor = Color.FromArgb(248, 250, 253);
        _syncButton.ForeColor = Color.FromArgb(39, 52, 72);
        _syncButton.Click += async (_, _) => await SyncEntriesAsync();

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 4, 0, 0),
        };
        buttonRow.Controls.Add(_syncButton);

        searchLayout.Controls.Add(searchTitle, 0, 0);
        searchLayout.SetColumnSpan(searchTitle, 2);
        searchLayout.Controls.Add(searchHint, 0, 1);
        searchLayout.SetColumnSpan(searchHint, 2);
        searchLayout.Controls.Add(_kindCombo, 0, 2);
        searchLayout.Controls.Add(_searchBox, 1, 2);
        searchLayout.Controls.Add(buttonRow, 0, 3);
        searchLayout.SetColumnSpan(buttonRow, 2);
        searchCard.Controls.Add(searchLayout);

        var resultsCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 12, 14, 14),
            Margin = new Padding(0),
            BackColor = Color.White,
        };

        var resultsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
        };
        resultsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        resultsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        resultsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        resultsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _resultsLabel.Text = "索引列表";
        _resultsLabel.Dock = DockStyle.Fill;
        _resultsLabel.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _resultsLabel.ForeColor = Color.FromArgb(26, 37, 52);
        _resultsLabel.TextAlign = ContentAlignment.MiddleLeft;

        var resultsHint = new Label
        {
            Text = "选择条目查看摘要、来源和正文。",
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 8.8f),
            ForeColor = Color.FromArgb(106, 121, 140),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _entryListView.Dock = DockStyle.Fill;
        _entryListView.Margin = new Padding(0, 8, 0, 0);
        _entryListView.BorderStyle = BorderStyle.FixedSingle;
        _entryListView.View = View.Details;
        _entryListView.FullRowSelect = true;
        _entryListView.MultiSelect = false;
        _entryListView.HideSelection = false;
        _entryListView.GridLines = false;
        _entryListView.BackColor = Color.White;
        _entryListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _entryListView.Columns.Add("名称", 100);
        _entryListView.Columns.Add("类别", 48);
        _entryListView.Columns.Add("地区", 76);
        _entryListView.Columns.Add("标题", 140);
        _entryListView.SelectedIndexChanged += (_, _) => ShowSelectedEntry();
        _entryListView.SizeChanged += (_, _) => ResizeEntryColumns();

        resultsLayout.Controls.Add(_resultsLabel, 0, 0);
        resultsLayout.Controls.Add(resultsHint, 0, 1);
        resultsLayout.Controls.Add(_entryListView, 0, 2);
        resultsCard.Controls.Add(resultsLayout);

        _leftSplit.Panel1.Controls.Add(searchCard);
        _leftSplit.Panel2.Controls.Add(resultsCard);
        return _leftSplit;
    }

    private Control BuildRightColumn()
    {
        var shell = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 0, 0, 0),
            BackColor = Color.Transparent,
        };

        _overviewSplit.Dock = DockStyle.Fill;
        _overviewSplit.Orientation = Orientation.Horizontal;
        _overviewSplit.SplitterWidth = 9;
        _overviewSplit.Panel1MinSize = 25;
        _overviewSplit.Panel2MinSize = 25;
        _overviewSplit.BackColor = Color.FromArgb(197, 207, 221);

        var headerCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 12, 18, 12),
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.White,
        };

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 4,
        };
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64f));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124f));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124f));

        _overviewTitleLabel.Text = "概况";
        _overviewTitleLabel.AutoSize = false;
        _overviewTitleLabel.Dock = DockStyle.Fill;
        _overviewTitleLabel.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
        _overviewTitleLabel.ForeColor = Color.FromArgb(26, 37, 52);
        _overviewTitleLabel.TextAlign = ContentAlignment.MiddleLeft;

        _titleLabel.AutoSize = false;
        _titleLabel.Dock = DockStyle.Fill;
        _titleLabel.Font = new Font("Microsoft YaHei UI", 13.5f, FontStyle.Bold);
        _titleLabel.ForeColor = Color.FromArgb(20, 30, 48);
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        _titleLabel.AutoEllipsis = true;

        _metaLabel.AutoSize = false;
        _metaLabel.Dock = DockStyle.Fill;
        _metaLabel.Font = new Font("Microsoft YaHei UI", 9.3f, FontStyle.Regular);
        _metaLabel.ForeColor = Color.FromArgb(102, 117, 136);
        _metaLabel.TextAlign = ContentAlignment.MiddleLeft;
        _metaLabel.AutoEllipsis = true;

        _badgePanel.Dock = DockStyle.Fill;
        _badgePanel.AutoSize = false;
        _badgePanel.WrapContents = false;
        _badgePanel.AutoScroll = false;
        _badgePanel.BackColor = Color.Transparent;
        _badgePanel.Margin = new Padding(0, 3, 8, 3);

        _openSourceButton.Text = "打开原文";
        _openSourceButton.Dock = DockStyle.Top;
        _openSourceButton.Height = 34;
        _openSourceButton.Margin = new Padding(8, 2, 0, 0);
        _openSourceButton.FlatStyle = FlatStyle.Flat;
        _openSourceButton.FlatAppearance.BorderColor = Color.FromArgb(207, 217, 230);
        _openSourceButton.BackColor = Color.FromArgb(248, 250, 253);
        _openSourceButton.ForeColor = Color.FromArgb(39, 52, 72);
        _openSourceButton.Enabled = false;
        _openSourceButton.Click += (_, _) => OpenCurrentSource();

        _mapButton.Text = "世界地图";
        _mapButton.Dock = DockStyle.Top;
        _mapButton.Height = 34;
        _mapButton.Margin = new Padding(8, 2, 0, 0);
        _mapButton.FlatStyle = FlatStyle.Flat;
        _mapButton.FlatAppearance.BorderColor = Color.FromArgb(207, 217, 230);
        _mapButton.BackColor = Color.FromArgb(32, 87, 153);
        _mapButton.ForeColor = Color.White;
        _mapButton.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        _mapButton.Click += (_, _) => ShowMapWindow();

        headerLayout.Controls.Add(_overviewTitleLabel, 0, 0);
        headerLayout.Controls.Add(_badgePanel, 1, 0);
        headerLayout.Controls.Add(_openSourceButton, 2, 0);
        headerLayout.Controls.Add(_mapButton, 3, 0);
        headerLayout.Controls.Add(_titleLabel, 0, 1);
        headerLayout.SetColumnSpan(_titleLabel, 4);
        headerLayout.Controls.Add(_metaLabel, 0, 2);
        headerLayout.SetColumnSpan(_metaLabel, 4);
        headerCard.Controls.Add(headerLayout);

        _detailSplit.Dock = DockStyle.Fill;
        _detailSplit.Orientation = Orientation.Horizontal;
        _detailSplit.SplitterWidth = 9;
        _detailSplit.Panel1MinSize = 25;
        _detailSplit.Panel2MinSize = 25;
        _detailSplit.BackColor = Color.FromArgb(197, 207, 221);

        var factsCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.White,
        };

        var factsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
        };
        factsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        factsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        factsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _factsTitleLabel.Text = "结构化信息";
        _factsTitleLabel.Dock = DockStyle.Fill;
        _factsTitleLabel.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _factsTitleLabel.ForeColor = Color.FromArgb(26, 37, 52);
        _factsTitleLabel.TextAlign = ContentAlignment.MiddleLeft;

        _factsPanel.Dock = DockStyle.Fill;
        _factsPanel.AutoScroll = true;
        _factsPanel.WrapContents = true;
        _factsPanel.BackColor = Color.Transparent;

        factsLayout.Controls.Add(_factsTitleLabel, 0, 0);
        factsLayout.Controls.Add(_factsPanel, 0, 1);
        factsCard.Controls.Add(factsLayout);

        var contentCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0),
            BackColor = Color.White,
        };

        var contentHeader = new Label
        {
            Text = "正文",
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(16, 12, 16, 0),
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(26, 37, 52),
        };

        _contentBrowser.Dock = DockStyle.Fill;
        _contentBrowser.ScriptErrorsSuppressed = true;
        _contentBrowser.AllowWebBrowserDrop = false;
        _contentBrowser.IsWebBrowserContextMenuEnabled = true;
        _contentBrowser.WebBrowserShortcutsEnabled = true;

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 16, 16),
            BackColor = Color.White,
        };
        contentHost.Controls.Add(_contentBrowser);

        contentCard.Controls.Add(contentHost);
        contentCard.Controls.Add(contentHeader);

        _detailSplit.Panel1.Controls.Add(factsCard);
        _detailSplit.Panel2.Controls.Add(contentCard);

        _overviewSplit.Panel1.Controls.Add(headerCard);
        _overviewSplit.Panel2.Controls.Add(_detailSplit);
        shell.Controls.Add(_overviewSplit);
        return shell;
    }

    private async Task LoadEntriesAsync()
    {
        var previousId = _currentEntry?.Id;
        var kind = _kindCombo.SelectedItem?.ToString() switch
        {
            "国家" => "国家",
            "组织" => "组织",
            _ => string.Empty,
        };

        var keyword = _searchBox.Text.Trim();
        var (entries, totalCount, latestSyncedAt, countryNames) = await Task.Run(() => (
            _database.SearchEntries(kind, keyword),
            _database.GetEntryCount(),
            _database.GetLatestSyncedAt(),
            _database.GetCountryNames()));

        _countryNames = countryNames;

        _entryListView.BeginUpdate();
        try
        {
            _entryListView.Items.Clear();
            foreach (var entry in entries)
            {
                var item = new ListViewItem(entry.Name)
                {
                    Tag = entry,
                };
                item.SubItems.Add(entry.Kind);
                item.SubItems.Add(entry.Region);
                item.SubItems.Add(entry.Title);
                _entryListView.Items.Add(item);
            }
        }
        finally
        {
            _entryListView.EndUpdate();
        }

        ResizeEntryColumns();

        _resultsLabel.Text = string.IsNullOrWhiteSpace(keyword)
            ? $"索引列表  {entries.Count}"
            : $"搜索结果  {entries.Count}";
        _countLabel.Text = $"当前 {entries.Count} 条 / 本地库 {totalCount} 条";
        _statusLabel.Text = totalCount == 0
            ? "本地离线数据库为空，请点击同步官网建立本地库。"
            : string.IsNullOrWhiteSpace(latestSyncedAt)
                ? $"已加载离线数据库：{_database.DatabasePath}"
                : $"已加载离线数据库 {totalCount} 条，数据时间：{latestSyncedAt}";

        if (previousId.HasValue)
        {
            for (var i = 0; i < _entryListView.Items.Count; i++)
            {
                if (_entryListView.Items[i].Tag is EntryListItem item && item.Id == previousId.Value)
                {
                    _entryListView.Items[i].Selected = true;
                    _entryListView.Items[i].Focused = true;
                    _entryListView.EnsureVisible(i);
                    return;
                }
            }
        }

        if (_entryListView.Items.Count > 0)
        {
            _entryListView.Items[0].Selected = true;
            _entryListView.Items[0].Focused = true;
        }
        else
        {
            UpdateDetail(null);
        }
    }

    private void ShowSelectedEntry()
    {
        if (_entryListView.SelectedItems.Count == 0 ||
            _entryListView.SelectedItems[0].Tag is not EntryListItem item)
        {
            UpdateDetail(null);
            return;
        }

        var entry = _database.GetEntry(item.Id);
        UpdateDetail(entry);
    }

    private void ShowMapWindow()
    {
        using var form = new MapForm(_countryNames);
        if (form.ShowDialog(this) == DialogResult.OK &&
            !string.IsNullOrWhiteSpace(form.SelectedCountryName))
        {
            _ = SelectCountryAsync(form.SelectedCountryName);
        }
    }

    private async Task SelectCountryAsync(string countryName)
    {
        _kindCombo.SelectedItem = "国家";
        _searchBox.Text = countryName;
        await LoadEntriesAsync();

        for (var i = 0; i < _entryListView.Items.Count; i++)
        {
            if (_entryListView.Items[i].Tag is EntryListItem item &&
                string.Equals(item.Name, countryName, StringComparison.Ordinal))
            {
                _entryListView.Items[i].Selected = true;
                _entryListView.Items[i].Focused = true;
                _entryListView.EnsureVisible(i);
                break;
            }
        }
    }

    private void ResizeEntryColumns()
    {
        if (_entryListView.Columns.Count < 4 || _entryListView.ClientSize.Width <= 0)
        {
            return;
        }

        var width = _entryListView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8;
        _entryListView.Columns[0].Width = Math.Max(82, width * 26 / 100);
        _entryListView.Columns[1].Width = 52;
        _entryListView.Columns[2].Width = Math.Max(70, width * 22 / 100);
        _entryListView.Columns[3].Width = Math.Max(120, width - _entryListView.Columns[0].Width - _entryListView.Columns[1].Width - _entryListView.Columns[2].Width);
    }

    private void UpdateDetail(EntryDetail? entry)
    {
        _currentEntry = entry;
        _openSourceButton.Enabled = entry is not null;

        _badgePanel.Controls.Clear();
        _factsPanel.Controls.Clear();

        if (entry is null)
        {
            _titleLabel.Text = "请选择左侧条目";
            _metaLabel.Text = "首次启动如无数据，请点击左侧同步官网，地图资源会自动释放到本机缓存。";
            _contentBrowser.DocumentText = BuildContentDocument(null);
            return;
        }

        _titleLabel.Text = entry.Title;
        _metaLabel.Text = $"名称：{entry.Name}    数据时间：{entry.SyncedAt}";
        _contentBrowser.DocumentText = BuildContentDocument(entry);

        _badgePanel.Controls.Add(CreateBadge(entry.Kind, Color.FromArgb(229, 238, 255), Color.FromArgb(193, 211, 246), Color.FromArgb(25, 78, 176)));
        _badgePanel.Controls.Add(CreateBadge(entry.Region, Color.FromArgb(236, 243, 237), Color.FromArgb(205, 221, 207), Color.FromArgb(54, 103, 70)));
        _badgePanel.Controls.Add(CreateBadge("本地数据库", Color.FromArgb(246, 240, 228), Color.FromArgb(233, 219, 190), Color.FromArgb(133, 96, 25)));

        foreach (var fact in ExtractFacts(entry.ContentText).Take(10))
        {
            _factsPanel.Controls.Add(CreateFactCard(fact.Key, fact.Value));
        }

        if (_factsPanel.Controls.Count == 0)
        {
            _factsPanel.Controls.Add(CreateFactCard("结构化提示", "该页面正文较自由，没有抽取到明显的字段块，地图阶段可改用独立地理表补充。"));
        }
    }

    private static string BuildContentDocument(EntryDetail? entry)
    {
        if (entry is null)
        {
            return """
                <!doctype html>
                <html><head><meta charset="utf-8"></head>
                <body style="font-family:'Microsoft YaHei UI',sans-serif;color:#5f6f82;padding:14px;">请选择左侧条目。</body></html>
                """;
        }

        var baseHref = WebUtility.HtmlEncode(entry.SourceUrl);
        return $$"""
            <!doctype html>
            <html>
            <head>
              <meta charset="utf-8">
              <base href="{{baseHref}}">
              <style>
                body { font-family: "Microsoft YaHei UI", "Microsoft YaHei", sans-serif; color: #202b3a; font-size: 14px; line-height: 1.8; margin: 0; padding: 10px 2px 18px 2px; background: #fff; }
                p { margin: 0 0 12px 0; }
                table { border-collapse: collapse; width: 100%; margin: 12px 0; table-layout: auto; }
                th, td { border: 1px solid #cfd8e3; padding: 8px 10px; vertical-align: top; word-break: break-word; }
                th { background: #f2f5f9; font-weight: 700; }
                img { max-width: 100%; height: auto; }
                a { color: #205799; }
              </style>
            </head>
            <body>
            {{entry.ContentHtml}}
            </body>
            </html>
            """;
    }

    private async Task SyncEntriesAsync()
    {
        if (_isSyncing)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            "这会从外交部官网公开页面抓取数据并写入本地数据库，首次同步可能需要几分钟。是否继续？",
            "同步官网",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);
        if (result != DialogResult.OK)
        {
            return;
        }

        SetSyncUiState(true);
        try
        {
            using var cts = new CancellationTokenSource();
            var progress = new Progress<string>(message => _statusLabel.Text = message);
            var scraper = new ScraperService();
            var scrapeResult = await scraper.ScrapeAllAsync(progress, cts.Token);
            await Task.Run(() => _database.UpsertEntries(scrapeResult.Entries), cts.Token);
            await LoadEntriesAsync();

            var summary = scrapeResult.Failures.Count == 0
                ? $"同步完成，共写入 {scrapeResult.Entries.Count} 条。"
                : $"同步完成，共写入 {scrapeResult.Entries.Count} 条，失败 {scrapeResult.Failures.Count} 条。";
            _statusLabel.Text = summary;
            MessageBox.Show(this, summary, "同步官网", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"同步失败：{ex.Message}";
            MessageBox.Show(this, $"同步失败：{ex.Message}", "同步官网", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetSyncUiState(false);
        }
    }

    private void SetSyncUiState(bool syncing)
    {
        _isSyncing = syncing;
        _syncButton.Enabled = !syncing;
        _syncButton.Text = syncing ? "同步中..." : "同步官网";
        _searchBox.Enabled = !syncing;
        _kindCombo.Enabled = !syncing;
        _entryListView.Enabled = !syncing;
        _mapButton.Enabled = !syncing;
        _openSourceButton.Enabled = !syncing && _currentEntry is not null;
        UseWaitCursor = syncing;
    }

    private void OpenCurrentSource()
    {
        if (_currentEntry is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _currentEntry.SourceUrl,
            UseShellExecute = true,
        });
    }

    private static Control CreateFactCard(string key, string value)
    {
        var panel = new CardPanel
        {
            Size = new Size(232, 104),
            Margin = new Padding(0, 0, 10, 10),
            Padding = new Padding(12, 10, 12, 10),
            BackColor = Color.FromArgb(251, 252, 254),
            BorderColor = Color.FromArgb(226, 232, 240),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var keyLabel = new Label
        {
            Text = key,
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(92, 108, 126),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };

        var valueLabel = new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(28, 38, 53),
            AutoEllipsis = true,
        };

        layout.Controls.Add(keyLabel, 0, 0);
        layout.Controls.Add(valueLabel, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private static PillLabel CreateBadge(string text, Color fill, Color border, Color foreground)
    {
        return new PillLabel
        {
            Text = text,
            FillColor = fill,
            BorderColor = border,
            ForeColor = foreground,
            Margin = new Padding(0, 0, 8, 0),
        };
    }

    private static IEnumerable<(string Key, string Value)> ExtractFacts(string contentText)
    {
        var facts = new List<(string Key, string Value)>();
        string? currentKey = null;
        var currentValue = "";

        foreach (var rawLine in contentText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var match = FactRegex.Match(line);
            if (match.Success)
            {
                if (!string.IsNullOrWhiteSpace(currentKey))
                {
                    facts.Add((currentKey, currentValue.Trim()));
                }

                currentKey = match.Groups["key"].Value.Trim();
                currentValue = match.Groups["value"].Value.Trim();
                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentKey) && currentValue.Length < 160)
            {
                currentValue = string.IsNullOrWhiteSpace(currentValue) ? line : $"{currentValue} {line}";
            }
            else if (facts.Count > 0)
            {
                break;
            }
        }

        if (!string.IsNullOrWhiteSpace(currentKey))
        {
            facts.Add((currentKey, currentValue.Trim()));
        }

        return facts.Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value));
    }
}




