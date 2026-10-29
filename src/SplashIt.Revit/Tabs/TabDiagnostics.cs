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
    private static bool _enabled;
    private static long _checkedAt = long.MinValue / 2; // far enough back that "now - _checkedAt" cannot overflow

    /// <summary>Whether <c>diag.on</c> exists; the file is looked for at most once a second (review n5).</summary>
    public static bool Enabled
    {
        get
        {
            var now = Environment.TickCount64;
            if (now - _checkedAt < 1000) return _enabled;
            _checkedAt = now;
            _enabled = File.Exists(Path.Combine(Folder, "diag.on"));
            return _enabled;
        }
    }

    public static void Dump(IReadOnlyList<SurfaceTab> tabs, Func<IEnumerable<string>> documentTitles)
    {
        try
        {
            if (!Enabled) return;
            var shape = string.Join("|", tabs.Select(t => t.Title + ":" + t.IsSelected + ":" + t.DocumentKey + ":" + t.Tab.Style?.GetHashCode()));
            if (shape == _lastShape) return;
            _lastShape = shape;

            var sb = new StringBuilder();
            sb.AppendLine($"tabs: {tabs.Count}; documents: {string.Join(" | ", documentTitles())}");
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
                if (i < 2) CloseButton(sb, tab);
            }
            // The close button of a tab Splash-It has not recolored (a family's, or a native one): where Revit's grey comes from.
            foreach (var t in tabs)
            {
                if (SplashIt.UI.Tabs.TabCloseGlyph.FindRecolored(t.Tab) is not null || SplashIt.UI.Tabs.TabCloseGlyph.FindButton(t.Tab) is null) continue;
                sb.AppendLine("--- native close button:");
                CloseButton(sb, t.Tab);
                break;
            }
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, $"diag-{++_serial}.txt"), sb.ToString());
        }
        catch (Exception)
        {
            // Diagnostics never matter more than the add-in.
        }
    }

    /// <summary>Where the close button's Foreground comes from (review M1).</summary>
    private static void CloseButton(StringBuilder sb, TabItem tab)
    {
        var button = SplashIt.UI.Tabs.TabCloseGlyph.FindButton(tab);
        if (button is null)
        {
            sb.AppendLine("    close button: not found");
            return;
        }
        var source = DependencyPropertyHelper.GetValueSource(button, Control.ForegroundProperty);
        sb.AppendLine($"    close button {button.GetType().FullName}#{button.Name}: Foreground={Brush(button.Foreground)} source={source.BaseValueSource} expression={source.IsExpression} local={Describe(button.ReadLocalValue(Control.ForegroundProperty))} style={Describe(button.Style)}");
        if (button.Style is { } style)
            foreach (var setter in style.Setters.OfType<Setter>())
                sb.AppendLine($"      style setter {setter.Property?.Name} = {Describe(setter.Value)}{(setter.Value is DynamicResourceExtension d ? " key=" + d.ResourceKey : "")}");
        for (DependencyObject? node = button; node is not null; node = VisualTreeHelper.GetChild(node, 0))
        {
            if (node is TextBlock text)
            {
                var ts = DependencyPropertyHelper.GetValueSource(text, TextBlock.ForegroundProperty);
                sb.AppendLine($"      glyph TextBlock \"{text.Text}\" Foreground={Brush(text.Foreground)} source={ts.BaseValueSource} expression={ts.IsExpression}");
                break;
            }
            if (VisualTreeHelper.GetChildrenCount(node) == 0) break;
        }
        // Resource keys holding the button's color, from the button up to the application.
        if (button.Foreground is SolidColorBrush grey)
        {
            for (DependencyObject? node = button; node is not null; node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node))
                if (node is FrameworkElement fe && fe.Resources is { } r) Keys(sb, r, grey.Color, fe.GetType().Name, 0);
            if (Application.Current?.Resources is { } app) Keys(sb, app, grey.Color, "Application", 0);
        }
    }

    private static void Keys(StringBuilder sb, ResourceDictionary dictionary, Color color, string owner, int depth)
    {
        if (depth > 6) return;
        foreach (var key in dictionary.Keys)
        {
            object? value;
            try { value = dictionary[key]; } catch (Exception) { continue; }
            if (value is SolidColorBrush b && b.Color == color) sb.AppendLine($"      resource {owner}: key={key} ({key?.GetType().Name})");
            if (value is Color c && c == color) sb.AppendLine($"      resource {owner}: color key={key}");
        }
        foreach (var merged in dictionary.MergedDictionaries) Keys(sb, merged, color, owner + ">" + (merged.Source?.ToString() ?? "merged"), depth + 1);
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
