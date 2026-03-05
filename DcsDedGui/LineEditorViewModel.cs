using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using DcsDedShared;

namespace DcsDedGui;

// ── Wrapper so WPF ComboBox can represent null (Separator) without null-binding issues ──

public record AliasOption(string? Alias, string Label);

// ── A single token chip in the line editor ────────────────────────────────────

public class TokenChipViewModel
{
    public DedLineToken Token { get; }
    public string Label { get; }
    public ICommand RemoveCommand { get; }

    public TokenChipViewModel(
        DedLineToken token,
        IReadOnlyList<FieldDefinition> fields,
        ObservableCollection<TokenChipViewModel> parent,
        Action onChange)
    {
        Token = token;
        if (token.IsSeparator)
        {
            // Show separator text with spaces replaced by middle-dots so it's visible
            var text = token.SeparatorText;
            Label = string.IsNullOrEmpty(text)
                ? "·sep·"
                : string.Concat(text.Select(c => c == ' ' ? '·' : c));
        }
        else
        {
            var fd = fields.FirstOrDefault(f =>
                string.Equals(f.Alias, token.Alias, StringComparison.OrdinalIgnoreCase));
            Label = fd?.Alias ?? token.Alias ?? "?";
        }
        RemoveCommand = new RelayCommand(() => { parent.Remove(this); onChange(); });
    }
}

// ── One DED line editor ───────────────────────────────────────────────────────

public class LineEditorViewModel : INotifyPropertyChanged
{
    public int    LineIndex   { get; }
    public string DisplayName => $"Line {LineIndex + 1}";

    public ObservableCollection<TokenChipViewModel> Chips { get; }

    private readonly IReadOnlyList<FieldDefinition> _availableFields;
    private readonly Action _onChange;

    // ── "Add token" controls ──────────────────────────────────────────────────

    /// <summary>All available field aliases, plus a Separator entry (shown first).</summary>
    public IReadOnlyList<AliasOption> AvailableAliases { get; }

    private AliasOption _selectedOption;
    public AliasOption SelectedOption
    {
        get => _selectedOption;
        set { _selectedOption = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNewSeparator)); }
    }

    public bool IsNewSeparator => _selectedOption.Alias == null;

    private string _newSeparatorText = "  ";
    public string NewSeparatorText
    {
        get => _newSeparatorText;
        set { _newSeparatorText = value; OnPropertyChanged(); }
    }

    public ICommand AddTokenCommand { get; }

    // ── Width indicator ───────────────────────────────────────────────────────

    private string _widthText = "";
    public string WidthText { get => _widthText; private set => Set(ref _widthText, value); }

    private Brush _widthBrush = Brushes.Gray;
    public Brush WidthBrush { get => _widthBrush; private set => Set(ref _widthBrush, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LineEditorViewModel(
        int lineIndex,
        List<DedLineToken> tokens,
        IReadOnlyList<FieldDefinition> availableFields,
        Action onChange)
    {
        LineIndex        = lineIndex;
        _availableFields = availableFields;
        _onChange        = onChange;

        // First entry = Separator, rest = field aliases
        var options = new List<AliasOption> { new AliasOption(null, "─ Separator ─") };
        options.AddRange(availableFields.Select(f => new AliasOption(f.Alias, f.Alias)));
        AvailableAliases = options;
        _selectedOption  = options.Count > 1 ? options[1] : options[0];

        Chips = new ObservableCollection<TokenChipViewModel>();
        foreach (var t in tokens)
            Chips.Add(new TokenChipViewModel(t, availableFields, Chips, OnChipChanged));

        Chips.CollectionChanged += (_, _) => RefreshWidth();
        AddTokenCommand = new RelayCommand(AddToken);
        RefreshWidth();
    }

    private void OnChipChanged() { RefreshWidth(); _onChange(); }

    private void AddToken()
    {
        var token = _selectedOption.Alias == null
            ? DedLineToken.MakeSeparator(_newSeparatorText)
            : new DedLineToken(_selectedOption.Alias);

        Chips.Add(new TokenChipViewModel(token, _availableFields, Chips, OnChipChanged));
        RefreshWidth();
        _onChange();
    }

    public List<DedLineToken> GetTokens() => Chips.Select(c => c.Token).ToList();

    private void RefreshWidth()
    {
        int total = Chips.Sum(c => c.Token.IsSeparator
            ? c.Token.SeparatorText.Length
            : (_availableFields.FirstOrDefault(f =>
                string.Equals(f.Alias, c.Token.Alias, StringComparison.OrdinalIgnoreCase))?.Width ?? 0));

        const int max = LineRenderer.Width;
        if (total <= max)
        {
            WidthText  = $"{total}/{max} ✓";
            WidthBrush = Brushes.LimeGreen;
        }
        else
        {
            WidthText  = $"{total}/{max} ✗";
            WidthBrush = Brushes.OrangeRed;
        }
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }
}
