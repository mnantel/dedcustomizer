using System.Windows;
using System.Windows.Input;

namespace DcsDedGui;

public partial class FieldBrowserWindow : Window
{
    private readonly IReadOnlyDictionary<string, string> _snapshot;

    public string? SelectedKey   { get; private set; }
    public string? SelectedValue { get; private set; }

    public FieldBrowserWindow(IReadOnlyDictionary<string, string> snapshot)
    {
        _snapshot = snapshot;
        InitializeComponent();
        ApplyFilter("");
    }

    private void ApplyFilter(string filter)
    {
        var items = _snapshot.Select(kv => new FieldEntry(kv.Key, kv.Value));

        if (!string.IsNullOrWhiteSpace(filter))
            items = items.Where(x => x.Key.Contains(filter, StringComparison.OrdinalIgnoreCase));

        FieldsDataGrid.ItemsSource = items.OrderBy(x => x.Key).ToList();
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => ApplyFilter(FilterBox.Text);

    private void FieldsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => SelectCurrentRow();

    private void AddAsField_Click(object sender, RoutedEventArgs e)
        => SelectCurrentRow();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SelectCurrentRow()
    {
        if (FieldsDataGrid.SelectedItem is not FieldEntry entry) return;
        SelectedKey   = entry.Key;
        SelectedValue = entry.Value;
        DialogResult  = true;
        Close();
    }

    private record FieldEntry(string Key, string Value);
}
