using System.Data;
using ParentalControl.Common;

namespace ParentalControl.Viewer;

/// <summary>Lets a parent/administrator browse browser history and DNS topology snapshots by date.</summary>
public sealed class MainForm : Form
{
    private readonly DateTimePicker _datePicker;
    private readonly TextBox _userFilterBox;
    private readonly TextBox _textFilterBox;
    private readonly Button _loadButton;
    private readonly DataGridView _historyGrid;
    private readonly DataGridView _dnsGrid;
    private readonly Label _statusLabel;
    private readonly TabPage _historyTab;
    private readonly TabPage _dnsTab;

    public MainForm()
    {
        Text = "Parental Control - Viewer";
        Width = 1200;
        Height = 750;
        StartPosition = FormStartPosition.CenterScreen;

        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(8),
            WrapContents = false,
            AutoSize = false,
        };

        _datePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };
        _userFilterBox = new TextBox { Width = 160, PlaceholderText = "Windows user filter" };
        _textFilterBox = new TextBox { Width = 240, PlaceholderText = "URL/title filter" };
        _loadButton = new Button { Text = "Load", AutoSize = true };
        _loadButton.Click += (_, _) => LoadLogs();

        topPanel.Controls.Add(new Label { Text = "Date:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 7, 4, 0) });
        topPanel.Controls.Add(_datePicker);
        topPanel.Controls.Add(new Label { Text = "User:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 7, 4, 0) });
        topPanel.Controls.Add(_userFilterBox);
        topPanel.Controls.Add(new Label { Text = "Filter:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 7, 4, 0) });
        topPanel.Controls.Add(_textFilterBox);
        topPanel.Controls.Add(_loadButton);

        _historyGrid = CreateGrid();
        _dnsGrid = CreateGrid();

        var tabs = new TabControl { Dock = DockStyle.Fill };
        _historyTab = new TabPage("Browser History");
        _dnsTab = new TabPage("DNS Topology");
        _historyTab.Controls.Add(_historyGrid);
        _dnsTab.Controls.Add(_dnsGrid);
        tabs.TabPages.Add(_historyTab);
        tabs.TabPages.Add(_dnsTab);

        _statusLabel = new Label { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(8, 4, 0, 0) };

        Controls.Add(tabs);
        Controls.Add(_statusLabel);
        Controls.Add(topPanel);

        Load += (_, _) =>
        {
            _datePicker.Value = DateTime.UtcNow.Date;
            LoadLogs();
        };
    }

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
    };

    private void LoadLogs()
    {
        var date = _datePicker.Value.Date;
        var userFilter = _userFilterBox.Text.Trim();
        var textFilter = _textFilterBox.Text.Trim();

        var historyResult = LoadHistoryLog(date, userFilter, textFilter);
        var dnsResult = LoadDnsTopologyLog(date, textFilter);

        _historyGrid.DataSource = historyResult.Table;
        _dnsGrid.DataSource = dnsResult.Table;

        _historyTab.Text = $"Browser History ({historyResult.MatchedCount})";
        _dnsTab.Text = $"DNS Topology ({dnsResult.MatchedCount})";

        _statusLabel.Text =
            $"{historyResult.MatchedCount} browser entr{(historyResult.MatchedCount == 1 ? "y" : "ies")}, " +
            $"{dnsResult.MatchedCount} DNS entr{(dnsResult.MatchedCount == 1 ? "y" : "ies")} for {date:yyyy-MM-dd}";
    }

    private static (DataTable Table, int MatchedCount) LoadHistoryLog(DateTime date, string userFilter, string textFilter)
    {
        var table = new DataTable();
        table.Columns.Add("Time (UTC)");
        table.Columns.Add("User");
        table.Columns.Add("Browser");
        table.Columns.Add("URL");
        table.Columns.Add("Title");

        var path = PathsConfig.LogFileForDate(date);
        if (!File.Exists(path))
        {
            return (table, 0);
        }

        var matched = 0;
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split('\t');
            if (parts.Length < 5) continue;

            var (time, windowsUser, browser, url, title) = (parts[0], parts[1], parts[2], parts[3], parts[4]);

            if (!string.IsNullOrEmpty(userFilter) && !windowsUser.Equals(userFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(textFilter) &&
                !url.Contains(textFilter, StringComparison.OrdinalIgnoreCase) &&
                !title.Contains(textFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            table.Rows.Add(time, windowsUser, browser, url, title);
            matched++;
        }

        return (table, matched);
    }

    private static (DataTable Table, int MatchedCount) LoadDnsTopologyLog(DateTime date, string textFilter)
    {
        var table = new DataTable();
        table.Columns.Add("Time (UTC)");
        table.Columns.Add("Adapter");
        table.Columns.Add("Source");
        table.Columns.Add("DHCP");
        table.Columns.Add("DNS Domain");
        table.Columns.Add("DNS Servers");

        var path = PathsConfig.DnsTopologyLogFileForDate(date);
        if (!File.Exists(path))
        {
            return (table, 0);
        }

        var matched = 0;
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split('\t');
            if (parts.Length < 6) continue;

            var (time, adapter, source, dhcp, dnsDomain, dnsServers) = (parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]);

            if (!string.IsNullOrEmpty(textFilter) &&
                !adapter.Contains(textFilter, StringComparison.OrdinalIgnoreCase) &&
                !source.Contains(textFilter, StringComparison.OrdinalIgnoreCase) &&
                !dnsDomain.Contains(textFilter, StringComparison.OrdinalIgnoreCase) &&
                !dnsServers.Contains(textFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            table.Rows.Add(time, adapter, source, dhcp, dnsDomain, dnsServers);
            matched++;
        }

        return (table, matched);
    }
}
