using System.Reflection;

namespace SplashIt.Core.Tests.Resources;

/// <summary>Every CoreStrings accessor resolves to a real .resx entry (none falls back to its key).</summary>
public class CoreStringsTests
{
    private static readonly Type Strings =
        typeof(SplashIt.Core.Settings.SplashItSettings).Assembly.GetType("SplashIt.Core.Resources.CoreStrings", throwOnError: true)!;

    [Fact]
    public void EveryAccessor_ResolvesToAResxEntry()
    {
        var checkedCount = 0;
        foreach (var p in Strings.GetProperties(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            var value = (string)p.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), p.Name);
            Assert.NotEqual(p.Name, value);
            checkedCount++;
        }
        foreach (var m in Strings.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                     .Where(m => m.ReturnType == typeof(string) && m.Name.Contains('_')))
        {
            var args = m.GetParameters().Select(_ => (object?)"X").ToArray();
            var value = (string)m.Invoke(null, args)!;
            Assert.False(value.StartsWith(m.Name, StringComparison.Ordinal), m.Name);
            Assert.DoesNotContain("{", value);
            checkedCount++;
        }
        Assert.True(checkedCount >= 20, $"only {checkedCount} accessors found");
    }
}
