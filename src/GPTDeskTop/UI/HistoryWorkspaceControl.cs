using System.Text;
using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class HistoryWorkspaceControl : UserControl
{
    private const int CollapsedHeight = 56;
    private const int ExpandedHeight = 330;

    private readonly LocalDatabase _database;
    private readonly TextBox _searchBox = new()
    {
        Width = 260,
        PlaceholderText = "Search history…"
    };
    private readonly ComboBox _flowFilter = new()
    {
        Width = 125,
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly ComboBox _statusFilter = new()
    {
        Width = 125,
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly Button _clearFiltersButton = new() { Text = "Clear Filters", AutoSize = true };
    private readonly Button _refreshButton = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _copyButton = new() { Text = "Copy Selected", AutoSize = true };
    private readonly Button _exportButton = new() { Text = "Export Visible CSV", AutoSize = true };
    private readonly Button _toggleButton = new() { Text = "History", AutoSize = true };
    private readonly Label _summaryLabel = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = FluentTheme.Muted,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        AccessibleRole = AccessibleRole.StatusBar
    };
    private readonly Label _emptyState = new()
    {
        Dock = DockStyle.Fill,
        BackColor = FluentTheme.Surface,
        ForeColor = FluentTheme.Muted,
        Font = new Font("Segoe UI Variable Text", 10F),
        TextAlign = ContentAlignment.MiddleCenter,
        Padding = new Padding(20)
    };
    private readonly DataGridView _grid = new();
    private readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface };
    private readonly Font _statusFont = new("Segoe UI Variable Text", 9F, FontStyle.Bold);
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    private List<MessageLog> _allLogs = new();
    private List<MessageLog> _visibleLogs = new();
    private bool _expanded;
    private bool _loading;

    public event EventHandler? ExpandedChanged;

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            _expanded = value;
            ApplyExpandedState();
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public HistoryWorkspaceControl(LocalDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        Dock = DockStyle.Bottom;
        Height = CollapsedHeight;
        MinimumSize = new Size(0, CollapsedHeight);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = FluentTheme.Background;
        Padding = new Padding(12, 4, 12, 8);
        AccessibleName = "Stored history explorer";
        AccessibleDescription = "Search, filter, copy and export persisted GPTDeskTop history without changing monitor runtime state.";

        BuildUi();
        ConfigureAccessibility();
        WireEvents();
        ApplyExpandedState();
        Load += async (_, _) => await RefreshAsync();
    }

    private void BuildUi()
    {
        ConfigureGrid();

        var frame = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10, 5, 10, 8)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = FluentTheme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = FluentTheme.Surface,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        header.Controls.Add(new Label
        {
            Text = "Stored History Explorer",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 11F, FontStyle.Bold),
            ForeColor = FluentTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        header.Controls.Add(_summaryLabel, 1, 0);
        header.Controls.Add(_toggleButton, 2, 0);

        var bodyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = FluentTheme.Surface,
            Padding = new Padding(0, 5, 0, 0)
        };
        bodyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        bodyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            BackColor = FluentTheme.Surface,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 2)
        };
        filters.Controls.Add(new Label { Text = "Search", AutoSize = true, Margin = new Padding(0, 9, 4, 0), ForeColor = FluentTheme.Muted });
        filters.Controls.Add(_searchBox);
        filters.Controls.Add(new Label { Text = "Flow", AutoSize = true, Margin = new Padding(12, 9, 4, 0), ForeColor = FluentTheme.Muted });
        filters.Controls.Add(_flowFilter);
        filters.Controls.Add(new Label { Text = "Status", AutoSize = true, Margin = new Padding(12, 9, 4, 0), ForeColor = FluentTheme.Muted });
        filters.Controls.Add(_statusFilter);
        filters.Controls.Add(_clearFiltersButton);
        filters.Controls.Add(_refreshButton);
        filters.Controls.Add(_copyButton);
        filters.Controls.Add(_exportButton);

        var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface };
        gridHost.Controls.Add(_grid);
        gridHost.Controls.Add(_emptyState);
        _emptyState.BringToFront();

        bodyLayout.Controls.Add(filters, 0, 0);
        bodyLayout.Controls.Add(gridHost, 0, 1);
        _body.Controls.Add(bodyLayout);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_body, 0, 1);
        frame.Controls.Add(root);
        Controls.Add(frame);

        FluentTheme.Apply(FindForm() ?? new Form());
        FluentTheme.StyleButton(_toggleButton);
        FluentTheme.StyleButton(_clearFiltersButton);
        FluentTheme.StyleButton(_refreshButton);
        FluentTheme.StyleButton(_copyButton);
        FluentTheme.StyleButton(_exportButton, primary: true);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.ShowCellToolTips = true;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Timestamp), HeaderText = "Time", Width = 145, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm:ss" } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.MonitorId), HeaderText = "Monitor", Width = 62 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.TabTitle), HeaderText = "Chat", Width = 155 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Direction), HeaderText = "Flow", Width = 78 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Prompt), HeaderText = "Prompt", Width = 145 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Response), HeaderText = "Response", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Status), HeaderText = "Status", Width = 140 });
        _grid.CellFormatting += FormatStatusCell;
        FluentTheme.StyleGrid(_grid);
    }

    private void ConfigureAccessibility()
    {
        _searchBox.AccessibleName = "Search stored history";
        _searchBox.AccessibleDescription = "Search chat title, flow, prompt, response, status, monitor ID, tab ID and timestamp.";
        _flowFilter.AccessibleName = "History flow filter";
        _flowFilter.AccessibleDescription = "Show all history or only one persisted flow value.";
        _statusFilter.AccessibleName = "History status category filter";
        _statusFilter.AccessibleDescription = "Filter by Issues, Success, Deferred or Other status categories.";
        _clearFiltersButton.AccessibleName = "Clear history filters";
        _refreshButton.AccessibleName = "Refresh stored history";
        _copyButton.AccessibleName = "Copy selected history entry";
        _exportButton.AccessibleName = "Export visible history as CSV";
        _toggleButton.AccessibleName = "Expand or collapse stored history explorer";
        _summaryLabel.AccessibleName = "Stored history result summary";
        _grid.AccessibleName = "Filtered stored history results";
    }

    private void WireEvents()
    {
        _searchBox.TextChanged += (_, _) => ApplyFilters();
        _flowFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        _statusFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        _clearFiltersButton.Click += (_, _) => ClearFilters();
        _refreshButton.Click += async (_, _) => await RefreshAsync();
        _copyButton.Click += (_, _) => CopySelected();
        _exportButton.Click += async (_, _) => await ExportVisibleAsync();
        _toggleButton.Click += (_, _) => IsExpanded = !IsExpanded;
        _grid.SelectionChanged += (_, _) => UpdateActionState();
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) CopySelected();
        };
    }

    private async Task RefreshAsync()
    {
        if (_loading || IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested) return;
        _loading = true;
        _refreshButton.Enabled = false;
        _summaryLabel.Text = "Loading history…";
        _summaryLabel.ForeColor = FluentTheme.Accent;
        try
        {
            _allLogs = await _database.GetRecentLogsAsync(500, _lifetimeCancellation.Token);
            if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested) return;
            RebuildFlowOptions();
            ApplyFilters();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Normal control teardown: do not surface an error or touch disposed controls.
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "HistoryWorkspaceControl.Refresh");
            if (IsDisposed || Disposing) return;
            _summaryLabel.Text = $"History load failed: {ex.Message}";
            _summaryLabel.ForeColor = FluentTheme.Danger;
        }
        finally
        {
            _loading = false;
            if (!IsDisposed && !Disposing && !_lifetimeCancellation.IsCancellationRequested)
            {
                _refreshButton.Enabled = true;
                UpdateActionState();
            }
        }
    }

    private void RebuildFlowOptions()
    {
        var selected = _flowFilter.SelectedItem?.ToString() ?? HistoryWorkspaceLogic.All;
        var flows = _allLogs
            .Select(log => log.Direction)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _flowFilter.BeginUpdate();
        try
        {
            _flowFilter.Items.Clear();
            _flowFilter.Items.Add(HistoryWorkspaceLogic.All);
            foreach (var flow in flows) _flowFilter.Items.Add(flow);
            _flowFilter.SelectedItem = _flowFilter.Items.Cast<object>()
                .FirstOrDefault(item => string.Equals(item.ToString(), selected, StringComparison.OrdinalIgnoreCase))
                ?? HistoryWorkspaceLogic.All;
        }
        finally
        {
            _flowFilter.EndUpdate();
        }

        if (_statusFilter.Items.Count == 0)
        {
            _statusFilter.Items.AddRange(new object[]
            {
                HistoryWorkspaceLogic.All,
                HistoryWorkspaceLogic.Issues,
                HistoryWorkspaceLogic.Success,
                HistoryWorkspaceLogic.Deferred,
                HistoryWorkspaceLogic.Other
            });
            _statusFilter.SelectedIndex = 0;
        }
    }

    private void ApplyFilters()
    {
        if (_flowFilter.Items.Count == 0 || _statusFilter.Items.Count == 0) return;
        var selectedId = _grid.CurrentRow?.DataBoundItem is MessageLog selected ? selected.Id : (long?)null;
        _visibleLogs = HistoryWorkspaceLogic.Filter(
                _allLogs,
                _searchBox.Text,
                _flowFilter.SelectedItem?.ToString(),
                _statusFilter.SelectedItem?.ToString())
            .ToList();

        _grid.DataSource = null;
        _grid.DataSource = _visibleLogs;
        RestoreSelection(selectedId);
        UpdateEmptyState();
        UpdateSummary();
        UpdateActionState();
    }

    private void RestoreSelection(long? id)
    {
        if (_grid.Rows.Count == 0) return;
        if (id.HasValue)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.DataBoundItem is not MessageLog log || log.Id != id.Value) continue;
                _grid.ClearSelection();
                row.Selected = true;
                _grid.CurrentCell = row.Cells[0];
                return;
            }
        }

        _grid.ClearSelection();
        _grid.Rows[0].Selected = true;
        _grid.CurrentCell = _grid.Rows[0].Cells[0];
    }

    private void ClearFilters()
    {
        _searchBox.Clear();
        if (_flowFilter.Items.Count > 0) _flowFilter.SelectedIndex = 0;
        if (_statusFilter.Items.Count > 0) _statusFilter.SelectedIndex = 0;
        ApplyFilters();
        _searchBox.Focus();
    }

    private void UpdateSummary()
    {
        var filtered = _visibleLogs.Count != _allLogs.Count ||
                       !string.IsNullOrWhiteSpace(_searchBox.Text) ||
                       !string.Equals(_flowFilter.SelectedItem?.ToString(), HistoryWorkspaceLogic.All, StringComparison.OrdinalIgnoreCase) ||
                       !string.Equals(_statusFilter.SelectedItem?.ToString(), HistoryWorkspaceLogic.All, StringComparison.OrdinalIgnoreCase);
        _summaryLabel.Text = filtered
            ? $"{_visibleLogs.Count} visible / {_allLogs.Count} loaded"
            : $"{_allLogs.Count} recent entr{(_allLogs.Count == 1 ? "y" : "ies")} loaded";
        _summaryLabel.ForeColor = _visibleLogs.Count == 0 && _allLogs.Count > 0 ? FluentTheme.Warning : FluentTheme.Muted;
    }

    private void UpdateEmptyState()
    {
        _emptyState.Text = _allLogs.Count == 0
            ? "No stored history yet.\nInbound, outbound, recovery and diagnostic receipts will appear here."
            : "No history matches the current filters.\nClear or adjust Search, Flow or Status.";
        _emptyState.Visible = _visibleLogs.Count == 0;
        if (_emptyState.Visible) _emptyState.BringToFront(); else _grid.BringToFront();
    }

    private void UpdateActionState()
    {
        var hasSelection = _grid.CurrentRow?.DataBoundItem is MessageLog;
        _copyButton.Enabled = hasSelection;
        _exportButton.Enabled = _visibleLogs.Count > 0;
        _clearFiltersButton.Enabled = !string.IsNullOrWhiteSpace(_searchBox.Text) ||
                                      (_flowFilter.SelectedIndex > 0) ||
                                      (_statusFilter.SelectedIndex > 0);
    }

    private void CopySelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not MessageLog log) return;
        try
        {
            Clipboard.SetText(HistoryWorkspaceLogic.ToClipboardText(log));
            _summaryLabel.Text = $"Copied history entry #{log.Id}.";
            _summaryLabel.ForeColor = FluentTheme.Success;
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "HistoryWorkspaceControl.CopySelected");
            MessageBox.Show(FindForm(), ex.Message, "Copy History", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ExportVisibleAsync()
    {
        if (_visibleLogs.Count == 0) return;
        using var dialog = new SaveFileDialog
        {
            Title = "Export Visible Stored History",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = "csv",
            AddExtension = true,
            FileName = $"GPTDeskTop-History-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var csv = HistoryWorkspaceLogic.ToCsv(_visibleLogs);
            await File.WriteAllTextAsync(dialog.FileName, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            _summaryLabel.Text = $"Exported {_visibleLogs.Count} visible entr{(_visibleLogs.Count == 1 ? "y" : "ies")}.";
            _summaryLabel.ForeColor = FluentTheme.Success;
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "HistoryWorkspaceControl.ExportVisible");
            MessageBox.Show(FindForm(), ex.Message, "Export History", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyExpandedState()
    {
        _body.Visible = _expanded;
        _toggleButton.Text = _expanded ? "Collapse" : "History";
        Height = _expanded ? ExpandedHeight : CollapsedHeight;
        _toggleButton.AccessibleDescription = _expanded
            ? "Collapse the stored history explorer."
            : "Expand the stored history explorer.";
        if (_expanded && _allLogs.Count == 0 && !_loading && !_lifetimeCancellation.IsCancellationRequested) _ = RefreshAsync();
    }

    private void FormatStatusCell(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.CellStyle is not { } style) return;
        if (_grid.Columns[e.ColumnIndex].DataPropertyName != nameof(MessageLog.Status)) return;
        var category = HistoryWorkspaceLogic.GetStatusCategory(Convert.ToString(e.Value));
        style.ForeColor = category switch
        {
            HistoryWorkspaceLogic.Issues => FluentTheme.Danger,
            HistoryWorkspaceLogic.Success => FluentTheme.Success,
            HistoryWorkspaceLogic.Deferred => FluentTheme.Warning,
            _ => FluentTheme.Text
        };
        style.Font = _statusFont;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F))
        {
            if (!IsExpanded) IsExpanded = true;
            _searchBox.Focus();
            _searchBox.SelectAll();
            return true;
        }
        if (keyData == Keys.F5)
        {
            _ = RefreshAsync();
            return true;
        }
        if (keyData == (Keys.Control | Keys.C) && _grid.ContainsFocus)
        {
            CopySelected();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetimeCancellation.Cancel();
            _grid.CellFormatting -= FormatStatusCell;
            _statusFont.Dispose();
            _lifetimeCancellation.Dispose();
        }
        base.Dispose(disposing);
    }
}
