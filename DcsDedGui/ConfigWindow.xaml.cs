using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using System.Windows.Media.Imaging;
using DcsDedShared;
using DedSharp;

namespace DcsDedGui;

public partial class ConfigWindow : Window, INotifyPropertyChanged
{
    private readonly BridgeService _bridge;
    private DedConfig _config;
    private PlaneProfile? _selectedProfile;
    private LineEditorViewModel[]? _currentLineEditors;
    private readonly BmsDedDisplayProvider _previewProvider = new();
    private WriteableBitmap? _previewBitmap;
    private readonly ObservableCollection<PlaneProfile> _planes;
    private FieldDefinition? _editingField;

    public event Action<DedConfig>? ConfigSaved;
    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Runtime status properties ─────────────────────────────────────────────

    public string DedStatusText => _bridge.DedStatusText;
    public Brush DedStatusBrush => _bridge.DedConnected ? Brushes.LimeGreen : Brushes.OrangeRed;

    public string UdpStatusText => _bridge.UdpStatusText;
    public Brush UdpStatusBrush => _bridge.UdpListening ? Brushes.LimeGreen : Brushes.OrangeRed;

    public string DcsStatusText => _bridge.DcsStatusText;
    public Brush DcsStatusBrush =>
        _bridge.CurrentAircraft.Length > 0 ? Brushes.LimeGreen : Brushes.Gold;

    public bool HasSnapshot => _bridge.LastSnapshot != null;

    // ── DCS Setup properties ──────────────────────────────────────────────────

    private string? _resolvedDcsDir;

    private string _scriptStatusText = "";
    public string ScriptStatusText { get => _scriptStatusText; private set => Notify(ref _scriptStatusText, value); }

    private Brush _scriptStatusBrush = Brushes.Gray;
    public Brush ScriptStatusBrush { get => _scriptStatusBrush; private set => Notify(ref _scriptStatusBrush, value); }

    private string _exportStatusText = "";
    public string ExportStatusText { get => _exportStatusText; private set => Notify(ref _exportStatusText, value); }

    private Brush _exportStatusBrush = Brushes.Gray;
    public Brush ExportStatusBrush { get => _exportStatusBrush; private set => Notify(ref _exportStatusBrush, value); }

    private bool _canDeployScript;
    public bool CanDeployScript { get => _canDeployScript; private set => Notify(ref _canDeployScript, value); }

    private bool _canFixExport;
    public bool CanFixExport { get => _canFixExport; private set => Notify(ref _canFixExport, value); }

    // ── Constructor ───────────────────────────────────────────────────────────

    public ConfigWindow(BridgeService bridge, DedConfig config)
    {
        _bridge = bridge;
        _config = config;

        _bridge.PropertyChanged += (_, e) =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DedStatusText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DedStatusBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UdpStatusText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UdpStatusBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DcsStatusText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DcsStatusBrush)));
            if (e.PropertyName == nameof(BridgeService.LastSnapshot))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSnapshot)));
        };

        _planes = new ObservableCollection<PlaneProfile>(_config.Planes);

        InitializeComponent();

        AircraftList.ItemsSource = _planes;
        ConfigPathLabel.Text = ConfigStore.ConfigPath;
        NewAircraftCombo.ItemsSource = KnownAircraft.Names;

        FieldsGrid.ItemsSource = _config.Fields;

        SyncDefaultModeCombo();
        ClearEditor();
        InitPreviewBitmap();
        RefreshDcsStatus();
    }

    // ── Window close → hide (keep alive) ─────────────────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    // ── Preview bitmap ────────────────────────────────────────────────────────

    private void InitPreviewBitmap()
    {
        _previewBitmap = new WriteableBitmap(200, 65, 96, 96, PixelFormats.Bgra32, null);
        PreviewImage.Source = _previewBitmap;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_previewBitmap == null) return;

        string[] lines;
        if (_selectedProfile == null)
        {
            lines = LineRenderer.RenderDefault(LineRenderer.SampleParams);
        }
        else
        {
            var mode = _selectedProfile.Mode;
            if (mode == PlaneMode.Custom && _currentLineEditors != null)
            {
                var tokenLines = _currentLineEditors.Select(e => e.GetTokens()).ToArray();
                lines = LineRenderer.RenderLines(tokenLines, LineRenderer.SampleParams, _config.Fields);
            }
            else if (mode == PlaneMode.Skip)
            {
                lines = Enumerable.Repeat(new string(' ', LineRenderer.Width), 5).ToArray();
            }
            else
            {
                lines = LineRenderer.RenderDefault(LineRenderer.SampleParams);
            }
        }

        var invertedLines = Enumerable.Repeat(new string(' ', LineRenderer.Width), 5).ToArray();
        _previewProvider.UpdateDedLines(lines, invertedLines);

        var pixels = new int[200 * 65];
        for (int row = 0; row < 65; row++)
        for (int col = 0; col < 200; col++)
        {
            bool on = _previewProvider.IsPixelOn(row, col);
            pixels[row * 200 + col] = on
                ? unchecked((int)0xFF00FF00)   // green
                : unchecked((int)0xFF000000);  // black
        }
        _previewBitmap.WritePixels(new System.Windows.Int32Rect(0, 0, 200, 65), pixels, 200 * 4, 0);
    }

    // ── Aircraft list ─────────────────────────────────────────────────────────

    private void AircraftList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selectedProfile = AircraftList.SelectedItem as PlaneProfile;
        LoadEditor(_selectedProfile);
    }

    // ── Add aircraft ──────────────────────────────────────────────────────────

    private void ShowAddPanel_Click(object sender, RoutedEventArgs e)
    {
        AddPanel.Visibility = Visibility.Visible;
        NewAircraftCombo.Focus();
    }

    private void ConfirmAdd_Click(object sender, RoutedEventArgs e)
    {
        var name = (NewAircraftCombo.Text ?? "").Trim();
        if (string.IsNullOrEmpty(name)) return;

        if (_planes.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"A profile for '{name}' already exists.", "Duplicate",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var profile = new PlaneProfile { Name = name, Mode = PlaneMode.Default };
        profile.Lines = LineRenderer.DefaultTokenLines();
        _planes.Add(profile);
        _config.Planes = _planes.ToList();

        AddPanel.Visibility = Visibility.Collapsed;
        NewAircraftCombo.Text = "";
        AircraftList.SelectedItem = profile;
    }

    private void CancelAdd_Click(object sender, RoutedEventArgs e)
    {
        AddPanel.Visibility = Visibility.Collapsed;
        NewAircraftCombo.Text = "";
    }

    // ── Remove aircraft ───────────────────────────────────────────────────────

    private void RemoveAircraft_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile == null) return;

        var r = MessageBox.Show($"Remove profile for '{_selectedProfile.Name}'?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        _planes.Remove(_selectedProfile);
        _config.Planes = _planes.ToList();
        ClearEditor();
    }

    // ── Mode radio buttons ────────────────────────────────────────────────────

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile == null) return;

        if (ModeSkip.IsChecked == true)          _selectedProfile.Mode = PlaneMode.Skip;
        else if (ModeDefault.IsChecked == true)  _selectedProfile.Mode = PlaneMode.Default;
        else if (ModeCustom.IsChecked == true)   _selectedProfile.Mode = PlaneMode.Custom;

        CustomGroup.Visibility = ModeCustom.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RefreshPreview();
    }

    // ── Line editor ───────────────────────────────────────────────────────────

    private void LoadEditor(PlaneProfile? profile)
    {
        if (profile == null) { ClearEditor(); return; }

        EmptyState.Visibility = Visibility.Collapsed;
        ProfileEditor.Visibility = Visibility.Visible;

        ProfileNameLabel.Text = profile.Name;

        ModeSkip.IsChecked    = profile.Mode == PlaneMode.Skip;
        ModeDefault.IsChecked = profile.Mode == PlaneMode.Default;
        ModeCustom.IsChecked  = profile.Mode == PlaneMode.Custom;

        CustomGroup.Visibility = profile.Mode == PlaneMode.Custom
            ? Visibility.Visible : Visibility.Collapsed;

        // Build line editor view models
        _currentLineEditors = Enumerable.Range(0, 5)
            .Select(i => new LineEditorViewModel(i, profile.Lines[i], _config.Fields, OnTokenChanged))
            .ToArray();

        LineEditors.ItemsSource = _currentLineEditors;
        RefreshPreview();
    }

    private void ClearEditor()
    {
        _selectedProfile = null;
        _currentLineEditors = null;
        EmptyState.Visibility = Visibility.Visible;
        ProfileEditor.Visibility = Visibility.Collapsed;
        AircraftList.SelectedItem = null;
        RefreshPreview();
    }

    private void OnTokenChanged()
    {
        // Sync token changes back to the model immediately
        if (_selectedProfile != null && _currentLineEditors != null)
            for (int i = 0; i < 5; i++)
                _selectedProfile.Lines[i] = _currentLineEditors[i].GetTokens();

        RefreshPreview();
    }

    // ── Fields Catalog ────────────────────────────────────────────────────────

    private void RefreshFieldsGrid()
    {
        FieldsGrid.ItemsSource = null;
        FieldsGrid.ItemsSource = _config.Fields;
    }

    private void AddField_Click(object sender, RoutedEventArgs e)
    {
        _editingField = null;
        EditAlias.Text = "";
        EditKey.Text = "";
        EditFormat.Text = "%X%";
        EditSample.Text = "";
        EditWidthPreview.Text = "1";
        FieldEditPanel.Visibility = Visibility.Visible;
        EditAlias.Focus();
    }

    private void EditFieldRow_Click(object sender, RoutedEventArgs e)
    {
        if (((System.Windows.Controls.Button)sender).Tag is not FieldDefinition fd) return;
        _editingField = fd;
        EditAlias.Text = fd.Alias;
        EditKey.Text = fd.Key;
        EditFormat.Text = fd.Format;
        EditSample.Text = fd.Sample;
        EditWidthPreview.Text = fd.Width.ToString();
        FieldEditPanel.Visibility = Visibility.Visible;
        EditAlias.Focus();
    }

    private void DeleteFieldRow_Click(object sender, RoutedEventArgs e)
    {
        if (((System.Windows.Controls.Button)sender).Tag is not FieldDefinition fd) return;

        bool inUse = _config.Planes.Any(p =>
            p.Lines.Any(line => line.Any(t =>
                !t.IsSeparator &&
                string.Equals(t.Alias, fd.Alias, StringComparison.OrdinalIgnoreCase))));

        if (inUse)
        {
            MessageBox.Show(
                $"Field '{fd.Alias}' is in use by one or more aircraft profiles.\nRemove it from all lines first.",
                "In Use", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _config.Fields.Remove(fd);
        RefreshFieldsGrid();
        if (_selectedProfile != null) LoadEditor(_selectedProfile);
    }

    private void SaveField_Click(object sender, RoutedEventArgs e)
    {
        var alias  = EditAlias.Text.Trim().ToUpperInvariant();
        var key    = EditKey.Text.Trim();
        var format = EditFormat.Text.Trim();

        if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(format))
        {
            MessageBox.Show("Alias, DCS Key, and Format are required.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dup = _config.Fields.FirstOrDefault(f =>
            string.Equals(f.Alias, alias, StringComparison.OrdinalIgnoreCase) && f != _editingField);
        if (dup != null)
        {
            MessageBox.Show($"An alias '{alias}' already exists.", "Duplicate",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sample = EditSample.Text.Trim();

        if (_editingField != null)
        {
            _editingField.Alias  = alias;
            _editingField.Key    = key;
            _editingField.Format = format;
            _editingField.Sample = sample;
        }
        else
        {
            _config.Fields.Add(new FieldDefinition { Alias = alias, Key = key, Format = format, Sample = sample });
        }

        FieldEditPanel.Visibility = Visibility.Collapsed;
        _editingField = null;
        RefreshFieldsGrid();
        if (_selectedProfile != null) LoadEditor(_selectedProfile);
    }

    private void CancelField_Click(object sender, RoutedEventArgs e)
    {
        FieldEditPanel.Visibility = Visibility.Collapsed;
        _editingField = null;
    }

    private void BrowseFields_Click(object sender, RoutedEventArgs e)
    {
        var snap = _bridge.LastSnapshot;
        if (snap == null) return;

        var dlg = new FieldBrowserWindow(snap) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.SelectedKey != null)
        {
            _editingField = null;
            EditAlias.Text = "";
            EditKey.Text = dlg.SelectedKey;
            EditFormat.Text = "%X%";
            EditSample.Text = dlg.SelectedValue ?? "";
            EditWidthPreview.Text = "1";
            FieldEditPanel.Visibility = Visibility.Visible;
            EditAlias.Focus();
        }
    }

    private void EditFormat_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (EditWidthPreview != null)
            EditWidthPreview.Text = LineRenderer.ComputeFormatWidth(EditFormat.Text).ToString();
    }

    // ── Footer ────────────────────────────────────────────────────────────────

    private void DefaultModeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DefaultModeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            _config.DefaultMode = item.Tag?.ToString() == "Skip" ? PlaneMode.Skip : PlaneMode.Default;
        }
    }

    private void SyncDefaultModeCombo()
    {
        DefaultModeCombo.SelectedIndex = _config.DefaultMode == PlaneMode.Skip ? 1 : 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Flush any pending line edits
        if (_selectedProfile != null && _currentLineEditors != null)
            for (int i = 0; i < 5; i++)
                _selectedProfile.Lines[i] = _currentLineEditors[i].GetTokens();

        _config.Planes = _planes.ToList();
        ConfigStore.Save(_config);
        ConfigSaved?.Invoke(_config);

        // Brief visual feedback
        var origContent = ((System.Windows.Controls.Button)sender).Content;
        ((System.Windows.Controls.Button)sender).Content = "Saved ✓";
        Task.Delay(1500).ContinueWith(_ =>
            Dispatcher.BeginInvoke(() =>
                ((System.Windows.Controls.Button)sender).Content = origContent));
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        _config = ConfigStore.Load();
        _planes.Clear();
        foreach (var p in _config.Planes)
            _planes.Add(p);

        SyncDefaultModeCombo();
        ClearEditor();
        RefreshFieldsGrid();
        ConfigSaved?.Invoke(_config);
        RefreshDcsStatus();
    }

    // ── DCS Setup ────────────────────────────────────────────────────────────

    private void RefreshDcsStatus()
    {
        _resolvedDcsDir = DcsSetup.ResolveDir(_config.DcsSavedGamesDir);
        DcsFolderBox.Text = _resolvedDcsDir ?? "(not detected)";

        if (_resolvedDcsDir == null)
        {
            ScriptStatusText  = "Folder not found";
            ScriptStatusBrush = Brushes.OrangeRed;
            ExportStatusText  = "Folder not found";
            ExportStatusBrush = Brushes.OrangeRed;
            CanDeployScript   = false;
            CanFixExport      = false;
            return;
        }

        var scriptStatus = DcsSetup.CheckScript(_resolvedDcsDir);
        switch (scriptStatus)
        {
            case ScriptStatus.Current:
                ScriptStatusText  = "Deployed ✓";
                ScriptStatusBrush = Brushes.LimeGreen;
                break;
            case ScriptStatus.OutOfDate:
                ScriptStatusText  = "Update available";
                ScriptStatusBrush = Brushes.Gold;
                break;
            default:
                ScriptStatusText  = "Not deployed";
                ScriptStatusBrush = Brushes.OrangeRed;
                break;
        }
        CanDeployScript = true; // always allow deploy / redeploy / update

        var exportStatus = DcsSetup.CheckExport(_resolvedDcsDir);
        switch (exportStatus)
        {
            case ExportLuaStatus.Configured:
                ExportStatusText  = "Configured ✓";
                ExportStatusBrush = Brushes.LimeGreen;
                CanFixExport      = false;
                break;
            case ExportLuaStatus.NotConfigured:
                ExportStatusText  = "Hook missing";
                ExportStatusBrush = Brushes.OrangeRed;
                CanFixExport      = true;
                break;
            case ExportLuaStatus.Missing:
                ExportStatusText  = "File missing";
                ExportStatusBrush = Brushes.Gold;
                CanFixExport      = true;
                break;
        }
    }

    private void BrowseDcsFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description       = "Select your DCS Saved Games folder (e.g. DCS.openbeta)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton    = false,
        };
        if (_resolvedDcsDir != null && System.IO.Directory.Exists(_resolvedDcsDir))
            dlg.InitialDirectory = _resolvedDcsDir;

        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        _config.DcsSavedGamesDir = dlg.SelectedPath;
        ConfigStore.Save(_config);
        RefreshDcsStatus();
    }

    private void DeployScript_Click(object sender, RoutedEventArgs e)
    {
        if (_resolvedDcsDir == null) return;
        try
        {
            DcsSetup.DeployScript(_resolvedDcsDir);
            RefreshDcsStatus();
            MessageBox.Show("ded_bridge.lua deployed successfully.", "Deploy",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Deploy failed:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FixExport_Click(object sender, RoutedEventArgs e)
    {
        if (_resolvedDcsDir == null) return;
        try
        {
            DcsSetup.ConfigureExport(_resolvedDcsDir);
            RefreshDcsStatus();
            MessageBox.Show("Export.lua updated successfully.", "Fix Export.lua",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to update Export.lua:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── INotifyPropertyChanged helper ─────────────────────────────────────────

    private void Notify<T>(ref T field, T value,
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
