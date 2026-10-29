using System.Reflection;

namespace SplashIt.RuntimeTests;

/// <summary>
/// SplashIt.UI (spec #1, Project split, decision #16) loads in a plain WPF test host
/// and references no Revit assembly, so AC4–AC7 can run here without Revit.
/// </summary>
public class UiProjectTests
{
    private static Assembly Ui() => Assembly.Load("SplashIt.UI");

    [Fact]
    public void SplashItUi_Loads_WithoutRevit()
    {
        var ui = Ui();
        Assert.Equal("SplashIt.UI", ui.GetName().Name);
        Assert.NotEmpty(ui.GetTypes());
    }

    [Fact]
    public void SplashItUi_ReferencesNoRevitAssembly()
    {
        var references = Ui().GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();
        Assert.DoesNotContain(references, n =>
            n.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("AdWindows", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("SplashIt.Revit", StringComparison.OrdinalIgnoreCase));
    }
}
