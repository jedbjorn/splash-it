using System.Reflection;
using System.Reflection.Emit;
using System.Windows;
using System.Windows.Controls;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Tabs;

namespace SplashIt.RuntimeTests.Tabs;

/// <summary>The layout pulse (the hot-path trigger) and the pyRevit guard.</summary>
public class PulseAndGuardTests
{
    [Fact]
    public void LayoutPulse_FiresForAnyLayoutPass_AndStopsCleanly()
    {
        Screenshots.RunSta(() =>
        {
            var pulse = new LayoutPulse();
            var fired = 0;
            pulse.Start(() => fired++);

            // A layout pass of an element the pulse knows nothing about.
            HostTabs.Layout(new Border { Child = new TextBlock { Text = "elsewhere" } });
            Assert.True(fired > 0);
            Assert.True(pulse.IsRunning);

            pulse.Stop();
            var after = fired;
            HostTabs.Layout(new Border { Child = new TextBlock { Text = "again" } });
            Assert.Equal(after, fired);
            Assert.False(pulse.IsRunning);
            pulse.Stop(); // twice is fine
        });
    }

    /// <summary>A dynamic assembly named like pyRevit's runtime, with a tab type and a static state.</summary>
    private static Assembly FakePyRevit(string assemblyName, string typeName, string member, bool value, bool asField = false)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule(assemblyName);
        var type = module.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);
        if (asField)
        {
            var field = type.DefineField(member, typeof(bool), FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal);
            field.SetConstant(value);
        }
        else
        {
            var getter = type.DefineMethod("get_" + member,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                typeof(bool), Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            var property = type.DefineProperty(member, PropertyAttributes.None, typeof(bool), null);
            property.SetGetMethod(getter);
        }
        type.CreateType();
        return assembly;
    }

    [Fact]
    public void PyRevitGuard_NoPyRevit_IsNotRunning()
    {
        var log = new ListLog();
        var guard = new PyRevitGuard(() => [typeof(object).Assembly, typeof(PyRevitGuard).Assembly], log);
        Assert.False(guard.IsRunning(out var detail));
        Assert.Null(detail);
        Assert.Contains(log.Lines, l => l.Contains("pyRevit is not loaded"));
    }

    [Fact]
    public void PyRevitGuard_TabColorizerOn_IsRunning()
    {
        var pyRevit = FakePyRevit("pyRevitLabs.PyRevit.Runtime.Test1", "DocumentTabEventUtils", "IsUpdatingDocumentTabs", true);
        var guard = new PyRevitGuard(() => [typeof(object).Assembly, pyRevit]);
        Assert.True(guard.IsRunning(out var detail));
        Assert.Contains("DocumentTabEventUtils.IsUpdatingDocumentTabs", detail);
    }

    [Fact]
    public void PyRevitGuard_ReadsStaticFieldsToo()
    {
        var pyRevit = FakePyRevit("pyRevitLoader.Test2", "DocumentTabColors", "Active", true, asField: true);
        Assert.True(new PyRevitGuard(() => [pyRevit]).IsRunning(out _));
    }

    [Fact]
    public void PyRevitGuard_LoadedButNotColoring_IsNotRunning()
    {
        var off = FakePyRevit("pyRevitLabs.PyRevit.Runtime.Test3", "DocumentTabEventUtils", "IsUpdatingDocumentTabs", false);
        var unrelated = FakePyRevit("pyRevitLabs.Common.Test4", "Configs", "IsActive", true); // not a tab type
        var log = new ListLog();
        var guard = new PyRevitGuard(() => [off, unrelated], log);
        Assert.False(guard.IsRunning(out _));
        Assert.Contains(log.Lines, l => l.Contains("pyRevit is loaded; 1 tab types found, none reports a running colorizer"));
    }

    [Fact]
    public void PyRevitGuard_NeverThrows()
    {
        var guard = new PyRevitGuard(() => throw new InvalidOperationException("no assemblies"));
        Assert.False(guard.IsRunning(out _));
    }
}
