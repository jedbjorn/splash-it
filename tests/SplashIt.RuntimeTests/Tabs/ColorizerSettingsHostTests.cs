using SplashIt.Core.Files;
using SplashIt.Core.Settings;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Settings;
using SplashIt.UI.Tabs;

namespace SplashIt.RuntimeTests.Tabs;

/// <summary>The settings window's host over the colorizer (review m8): statuses by name, Apply, Reset.</summary>
public class ColorizerSettingsHostTests
{
    [Fact]
    public void EveryColorizerStatus_MapsToTheSettingsStatusOfTheSameName()
    {
        var colorizer = Enum.GetValues<ColorizerStatus>();
        var settings = Enum.GetValues<SettingsStatusKind>();
        Assert.Equal(settings.Length, colorizer.Length);
        foreach (var status in colorizer)
            Assert.Equal(status.ToString(), ColorizerSettingsHost.ToSettingsStatus(status).ToString());
        // Every window status is reached by exactly one colorizer status.
        Assert.Equal(settings.OrderBy(s => s), colorizer.Select(ColorizerSettingsHost.ToSettingsStatus).OrderBy(s => s));
        // The two enums order their members differently: a cast would show Off as Active.
        Assert.NotEqual((int)ColorizerStatus.Off, (int)SettingsStatusKind.Off);
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorizerSettingsHost.ToSettingsStatus((ColorizerStatus)99));
    }

    [Fact]
    public void Host_ReadsTheStatus_AppliesAndResets_ThroughTheColorizer()
    {
        Screenshots.RunSta(() =>
        {
            var strip = HostTabs.Strip(HostTabs.RevitLikeStyle(Core.Theming.UiTheme.Light), Core.Theming.UiTheme.Light, "P1 - L1");
            var surface = new FakeSurface(strip);
            var p1 = new object();
            surface.Bind(HostTabs.Tab(strip, 0), p1);
            var documents = new FakeDocuments();
            documents.Add(p1, FileKind.Project);
            var events = new FakeEvents();
            var colorizer = new DocumentTabColorizer(surface, documents, events, new TabStyleFactory());
            var host = new ColorizerSettingsHost(() => colorizer);

            Assert.Equal(new SettingsStatus(SettingsStatusKind.Off), host.Status);
            host.Apply(SplashItSettings.Default);
            Assert.Equal(new SettingsStatus(SettingsStatusKind.Active), host.Status);
            Assert.True(TabStyleFactory.IsDerived(HostTabs.Tab(strip, 0).Style));

            host.ResetAssignments(); // no throw, still active
            Assert.Equal(SettingsStatusKind.Active, host.Status.Kind);

            surface.UnavailableReason = "no pane";
            events.Layout();
            Assert.Equal(new SettingsStatus(SettingsStatusKind.Unavailable, "no pane"), host.Status);

            host.Apply(SplashItSettings.Default with { Enabled = false });
            Assert.Equal(SettingsStatusKind.Off, host.Status.Kind);
        });
    }

    [Fact]
    public void Host_WithoutAColorizer_ShowsStopped_AndSaveStillSucceeds()
    {
        var log = new ListLog();
        var host = new ColorizerSettingsHost(() => null, log);
        Assert.Equal(SettingsStatusKind.Faulted, host.Status.Kind);
        host.Apply(SplashItSettings.Default);
        host.ResetAssignments();
        Assert.Contains(log.Lines, l => l.Contains("not running"));
    }

    [Fact]
    public void TheUnselectedOpacity_ComesFromTheSettings()
    {
        var settings = SplashItSettings.Default with { ProjectFillOpacity = 80, ProjectUnselectedOpacity = 25 };
        var treatment = TabTreatment.ProjectFill(SplashItDefaults.ProjectColors[0], settings);
        Assert.Equal(80, treatment.OpacityPercent);
        Assert.Equal(25, treatment.UnselectedPercent);
        Assert.Equal(SplashItDefaults.ProjectUnselectedOpacity, TabTreatment.DefaultUnselectedPercent);
    }
}
