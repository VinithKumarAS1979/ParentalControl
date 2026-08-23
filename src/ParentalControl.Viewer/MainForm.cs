using System.Data;
using ParentalControl.Common;

namespace ParentalControl.Viewer;

/// <summary>Lets a parent/administrator browse the daily visited-sites log, filtered by date,
/// Windows account, and free text (URL/title).</summary>
public sealed class MainForm : Form
{
    private readonly DateTimePicker _datePicker;
    private readonly TextBox _userFilterBox;
    private readonly TextBox _textFilterBox;
    private readonly Button _loadButton;
    private readonly DataGridView _grid;
    private readonly Label _statusLabel;

    public MainForm()
    {
        Text = "Parental Control - Visited Websites Viewer";
        Width = 1000;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;

        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(8),
        };

        _datePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };
        _userFilterBox = new TextBox { Width = 140, PlaceholderText = "Windows user filter" };
        _textFilterBox = new TextBox { Width = 200, PlaceholderText = "URL/title filter" };
        _loadButton = new Button { Text = "Load", AutoSize = true };
        _loadButton.Click += (_, _) => LoadLog();

        topPanel.Controls.Add(new Label { Text = "Date:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 4, 0) });
        topPanel.Controls.Add(_datePicker);
        topPanel.Controls.Add(new Label { Text = "User:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 6, 4, 0) });
        topPanel.Controls.Add(_userFilterBox);
        topPanel.Controls.Add(new Label { Text = "Filter:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 6, 4, 0) });
        topPanel.Controls.Add(_textFilterBox);
        topPanel.Controls.Add(_loadButton);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };

        _statusLabel = new Label { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(8, 4, 0, 0) };

        Controls.Add(_grid);
        Controls.Add(_statusLabel);
        Controls.Add(topPanel);

        Load += (_, _) =>
        {
            _datePicker.Value = DateTime.UtcNow.Date;
            LoadLog();
        };
    }

    private void LoadLog()
    {
        var date = _datePicker.Value.Date;
        var path = PathsConfig.LogFileForDate(date);
        var table = new DataTable();
        table.Columns.Add("Time (UTC)");
        table.Columns.Add("User");
        table.Columns.Add("Browser");
        table.Columns.Add("URL");
        table.Columns.Add("Title");

        if (!File.Exists(path))
        {
            _grid.DataSource = table;
            _statusLabel.Text = $"No log file found for {date:yyyy-MM-dd} at {path}";
            return;
        }

        var userFilter = _userFilterBox.Text.Trim();
        var textFilter = _textFilterBox.Text.Trim();
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

        _grid.DataSource = table;
        _statusLabel.Text = $"{matched} entr{(matched == 1 ? "y" : "ies")} for {date:yyyy-MM-dd}";
    }
}
