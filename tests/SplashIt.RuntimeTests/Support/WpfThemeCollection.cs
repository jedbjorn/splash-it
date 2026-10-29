namespace SplashIt.RuntimeTests.Support;

/// <summary>
/// Tests that open windows under a theme. <c>SplashItTheme.Current</c> is process-wide (one
/// Revit UI thread in production), so these run one at a time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfThemeCollection
{
    public const string Name = "WPF theme";
}
