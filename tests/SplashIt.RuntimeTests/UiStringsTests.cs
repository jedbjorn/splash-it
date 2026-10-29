using System.Reflection;
using SplashIt.UI.Resources;

namespace SplashIt.RuntimeTests;

/// <summary>Every UiStrings accessor resolves to a real UiStrings.resx entry (none falls back to its key).</summary>
public class UiStringsTests
{
    [Fact]
    public void EveryAccessor_ResolvesToAResxEntry()
    {
        var checkedCount = 0;
        foreach (var p in typeof(UiStrings).GetProperties(BindingFlags.Static | BindingFlags.Public))
        {
            var value = (string)p.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), p.Name);
            Assert.NotEqual(p.Name, value);
            checkedCount++;
        }
        foreach (var m in typeof(UiStrings).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                     .Where(m => m.ReturnType == typeof(string) && m.Name.Contains('_')))
        {
            var args = m.GetParameters().Select(_ => (object?)"X").ToArray();
            var value = (string)m.Invoke(null, args)!;
            Assert.False(value.StartsWith(m.Name, StringComparison.Ordinal), m.Name);
            Assert.DoesNotContain("{", value);
            checkedCount++;
        }
        Assert.True(checkedCount >= 80, $"only {checkedCount} accessors found");
    }

    [Fact]
    public void TheImportNotice_IsTheSpecsWording() =>
        Assert.Equal("Imported from team.json; Save to apply.", UiStrings.Settings_Imported("team.json"));
}
