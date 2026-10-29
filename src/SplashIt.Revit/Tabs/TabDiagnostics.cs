#if DEBUG
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SplashIt.UI.Tabs;

namespace SplashIt.Revit.Tabs;

/// <summary>
/// Debug builds only, and only when <c>%LOCALAPPDATA%\SplashIt\diag.on</c> exists: writes what
/// the tab surface sees (tab titles, the types and members behind each tab, and each tab's
/// visual tree with its brushes) to <c>%LOCALAPPDATA%\SplashIt\diag-&lt;n&gt;.txt</c>. It is
/// how the AC11 seat inspects Revit's real tab tree. It includes titles, so it never runs in
/// a Release build and never writes to the log.
/// </summary>
internal static class TabDiagnostics
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SplashIt");

    private static string _lastShape = "";
    private static int _serial;

    public static bool Enabled => File.Exists(Path.Combine(Folder, "diag.on"));

    public static void Dump(IReadOnlyList<SurfaceTab> tabs, IEnumerable<string> documentTitles)
    {
        try
        {
            if (!Enabled) return;
            var shape = string.Join("|", tabs.Select(t => t.Title + ":" + t.IsSelected + ":" + t.DocumentKey + ":" + t.Tab.Style?.GetHashCode()));
            if (shape == _lastShape) return;
            _lastShape = shape;

            var sb = new StringBuilder();
            sb.AppendLine($"tabs: {tabs.Count}; documents: {string.Join(" | ", documentTitles)}");
            for (var i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                var tab = t.Tab;
                sb.AppendLine($"--- tab {i}: title=\"{t.Title}\" selected={t.IsSelected} key={t.DocumentKey}");
                sb.AppendLine($"    TabItem type={tab.GetType().FullName}; Header={Describe(tab.Header)}; DataContext={Describe(tab.DataContext)}; Content={Describe(tab.Content)}; ToolTip={Describe(tab.ToolTip)}");
                sb.AppendLine($"    Style local={Describe(tab.ReadLocalValue(FrameworkElement.StyleProperty))} effective={Describe(tab.Style)} basedOn={Describe(tab.Style?.BasedOn)} target={tab.Style?.TargetType.Name}");
                sb.AppendLine($"    Background={Brush(tab.Background)} Foreground={Brush(tab.Foreground)} BorderBrush={Brush(tab.BorderBrush)} Template={Describe(tab.Template)}");
                if (i == 0 && tab.DataContext is { } model) Members(sb, model, "    model", 0);
                if (i < 2) Tree(sb, tab, 1);
            }
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, $"diag-{++_serial}.txt"), sb.ToString());
        }
        catch (Exception)
        {
            // Diagnostics never matter more than the add-in.
        }
    }

    private static void Members(StringBuilder sb, object value, string prefix, int depth)
    {
        if (depth > 1) return;
        for (var type = value.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                object? v;
                try { v = p.GetValue(value); } catch (Exception ex) { v = "!" + ex.GetType().Name; }
                sb.AppendLine($"{prefix}.{p.Name} ({type.Name}, {p.PropertyType.Name}) = {Describe(v)}");
                if (depth == 0 && p.Name == "Content" && v is not null and not string) Members(sb, v, prefix + ".Content", depth + 1);
            }
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                object? v;
                try { v = f.GetValue(value); } catch (Exception ex) { v = "!" + ex.GetType().Name; }
                sb.AppendLine($"{prefix}.{f.Name} [field] ({type.Name}, {f.FieldType.Name}) = {Describe(v)}");
            }
        }
    }

    private static void Tree(StringBuilder sb, DependencyObject node, int depth)
    {
        if (depth > 14) return;
        var pad = new string(' ', depth * 2);
        var extra = node switch
        {
            Border b => $" Background={Brush(b.Background)} BorderBrush={Brush(b.BorderBrush)} Thickness={b.BorderThickness}",
            Panel p => $" Background={Brush(p.Background)}",
            Control c => $" Background={Brush(c.Background)} Foreground={Brush(c.Foreground)}",
            TextBlock tb => $" Text=\"{tb.Text}\" Foreground={Brush(tb.Foreground)}",
            System.Windows.Shapes.Shape s => $" Fill={Brush(s.Fill)} Stroke={Brush(s.Stroke)}",
            _ => "",
        };
        var name = (node as FrameworkElement)?.Name;
        var size = node is UIElement u ? $" {u.RenderSize.Width:0}x{u.RenderSize.Height:0} vis={u.Visibility} op={u.Opacity:0.##}" : "";
        sb.AppendLine($"{pad}{node.GetType().Name}{(string.IsNullOrEmpty(name) ? "" : "#" + name)}{size}{extra}");
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++) Tree(sb, VisualTreeHelper.GetChild(node, i), depth + 1);
    }

    private static string Brush(Brush? brush) => brush switch
    {
        null => "null",
        SolidColorBrush s => $"{s.Color}@{s.Opacity:0.##}",
        _ => brush.GetType().Name,
    };

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        _ when value == DependencyProperty.UnsetValue => "Unset",
        _ => value.GetType().FullName + (value is ValueType ? "(" + value + ")" : ""),
    };
}
#endif
