// Copied from gantt-it src/GanttIt.Revit/UI/Swatches/ColorPicker.xaml.cs (gantt-it commit aa83f35, MIT, same owner).
// Changes: namespaces.

using System.Windows.Controls;

namespace SplashIt.UI.Swatches;

/// <summary>The custom color picker control (gantt-it §2.4). Bind its DataContext to a <see cref="ColorPickerViewModel"/>.</summary>
public partial class ColorPicker : UserControl
{
    public ColorPicker() => InitializeComponent();
}
