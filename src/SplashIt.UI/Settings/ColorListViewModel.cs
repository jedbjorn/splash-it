using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.UI.Dialogs;
using SplashIt.UI.Mvvm;
using SplashIt.UI.Resources;

namespace SplashIt.UI.Settings;

/// <summary>One color chip in a list: its color, its 1-based position, and whether it is the selected chip.</summary>
public sealed class ColorChipViewModel : ObservableObject
{
    private readonly Func<int, string> _nameFor;
    private Rgb _color;
    private int _number;
    private bool _isSelected;

    internal ColorChipViewModel(Rgb color, int number, Func<int, string> nameFor, string automationPrefix)
    {
        _color = color;
        _number = number;
        _nameFor = nameFor;
        AutomationPrefix = automationPrefix;
    }

    private string AutomationPrefix { get; }

    public Rgb Color
    {
        get => _color;
        set
        {
            if (!SetField(ref _color, value)) return;
            OnPropertyChanged(nameof(Hex));
            OnPropertyChanged(nameof(Brush));
            OnPropertyChanged(nameof(LabelBrush));
            OnPropertyChanged(nameof(AutomationName));
            OnPropertyChanged(nameof(ToolTip));
        }
    }

    /// <summary>The 1-based position in the list, drawn on the chip.</summary>
    public int Number
    {
        get => _number;
        internal set
        {
            if (!SetField(ref _number, value)) return;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(AutomationName));
            OnPropertyChanged(nameof(AutomationId));
            OnPropertyChanged(nameof(ToolTip));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetField(ref _isSelected, value);
    }

    public string Hex => Color.ToHex();

    /// <summary>"Project color 3".</summary>
    public string Name => _nameFor(Number);

    public string AutomationName => UiStrings.Settings_ChipName(Name, Hex);

    public string AutomationId => $"{AutomationPrefix}.Chip{Number}";

    public string ToolTip => UiStrings.Settings_ChipTip(Name, Hex);

    public Brush Brush => Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(Color.R, Color.G, Color.B)));

    /// <summary>The number's color: black or white, whichever reads better on the chip (WCAG).</summary>
    public Brush LabelBrush
    {
        get
        {
            var text = ColorMath.TextColorFor(Color);
            return Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(text.R, text.G, text.B)));
        }
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// One of the two color lists (spec #1, Settings window): 1 to 12 chips in order. Click a chip
/// to edit it in the color picker (it becomes the selected chip); add a color (disabled at 12),
/// remove the selected one (disabled at 1), move it left or right, or reset the list to its
/// defaults. Every change is unsaved until the window's Save.
/// </summary>
public sealed class ColorListViewModel : ObservableObject
{
    private readonly IReadOnlyList<Rgb> _defaults;
    private readonly IDialogService _dialogs;
    private readonly Func<int, string> _nameFor;
    private readonly string _automationPrefix;
    private ColorChipViewModel? _selected;

    /// <param name="nameFor">A chip's name from its 1-based position ("Project color 3").</param>
    /// <param name="defaults">The list's default colors, also the picker's palette.</param>
    /// <param name="automationPrefix">Prefix of the chips' AutomationIds ("Settings.ProjectColors").</param>
    public ColorListViewModel(
        IReadOnlyList<Rgb> colors, IReadOnlyList<Rgb> defaults, Func<int, string> nameFor, IDialogService dialogs, string automationPrefix)
    {
        _defaults = defaults;
        _dialogs = dialogs;
        _nameFor = nameFor;
        _automationPrefix = automationPrefix;
        Chips = [];
        Load(colors);

        EditCommand = new RelayCommand<ColorChipViewModel>(Edit);
        AddCommand = new RelayCommand(Add, () => CanAdd);
        RemoveCommand = new RelayCommand(Remove, () => CanRemove);
        MoveLeftCommand = new RelayCommand(() => Move(-1), () => CanMoveLeft);
        MoveRightCommand = new RelayCommand(() => Move(+1), () => CanMoveRight);
        ResetCommand = new RelayCommand(Reset);
    }

    /// <summary>Raised after every edit (color, add, remove, move, reset, load).</summary>
    public event Action? Changed;

    public ObservableCollection<ColorChipViewModel> Chips { get; }

    /// <summary>The chip that Remove and Move act on: the last one clicked, else the first.</summary>
    public ColorChipViewModel? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            if (_selected is not null) _selected.IsSelected = false;
            _selected = value;
            if (_selected is not null) _selected.IsSelected = true;
            OnPropertyChanged();
            RaiseCanExecute();
        }
    }

    public IReadOnlyList<Rgb> Colors => Chips.Select(c => c.Color).ToArray();

    public int Count => Chips.Count;

    /// <summary>"5 of 12".</summary>
    public string CountText => UiStrings.Settings_ColorCount(Count, SplashItDefaults.MaxColors);

    public bool CanAdd => Count < SplashItDefaults.MaxColors;

    public bool CanRemove => Count > SplashItDefaults.MinColors && Selected is not null;

    public bool CanMoveLeft => Selected is not null && Chips.IndexOf(Selected) > 0;

    public bool CanMoveRight => Selected is not null && Chips.IndexOf(Selected) < Count - 1;

    public ICommand EditCommand { get; }

    public ICommand AddCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand MoveLeftCommand { get; }

    public ICommand MoveRightCommand { get; }

    public ICommand ResetCommand { get; }

    /// <summary>Replaces every chip (import, reset) and selects the first.</summary>
    public void Load(IReadOnlyList<Rgb> colors)
    {
        Chips.Clear();
        foreach (var color in colors)
            Chips.Add(new ColorChipViewModel(color, Chips.Count + 1, _nameFor, _automationPrefix));
        _selected = null;
        Selected = Chips.FirstOrDefault();
        ListChanged();
    }

    /// <summary>Selects <paramref name="chip"/> and edits it in the color picker; a cancelled picker changes nothing.</summary>
    public void Edit(ColorChipViewModel chip)
    {
        if (!Chips.Contains(chip)) return;
        Selected = chip;
        if (_dialogs.PickColor(chip.Color, _defaults, chip.Name) is not { } color || color == chip.Color) return;
        chip.Color = color;
        Changed?.Invoke();
    }

    /// <summary>Appends the first default color not in the list (else the next default in turn) and selects it.</summary>
    public void Add()
    {
        if (!CanAdd) return;
        var colors = Colors;
        var next = _defaults.Cast<Rgb?>().FirstOrDefault(d => !colors.Contains(d!.Value)) ?? _defaults[Count % _defaults.Count];
        var chip = new ColorChipViewModel(next, Count + 1, _nameFor, _automationPrefix);
        Chips.Add(chip);
        Selected = chip;
        ListChanged();
    }

    /// <summary>Removes the selected chip and selects its neighbor.</summary>
    public void Remove()
    {
        if (!CanRemove || Selected is not { } chip) return;
        var index = Chips.IndexOf(chip);
        Chips.RemoveAt(index);
        Renumber();
        _selected = null;
        Selected = Chips[Math.Min(index, Count - 1)];
        ListChanged();
    }

    /// <summary>Restores the list's default colors.</summary>
    public void Reset() => Load(_defaults);

    private void Move(int by)
    {
        if (Selected is not { } chip) return;
        var from = Chips.IndexOf(chip);
        var to = from + by;
        if (to < 0 || to >= Count) return;
        Chips.Move(from, to);
        Renumber();
        ListChanged();
    }

    private void Renumber()
    {
        for (var i = 0; i < Chips.Count; i++) Chips[i].Number = i + 1;
    }

    private void ListChanged()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CountText));
        RaiseCanExecute();
        Changed?.Invoke();
    }

    private void RaiseCanExecute()
    {
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanMoveLeft));
        OnPropertyChanged(nameof(CanMoveRight));
        RelayCommand.Refresh();
    }
}
