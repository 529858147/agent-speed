using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using AgentSpeed.Settings;

namespace AgentSpeed.Windows.Tests;

public sealed class SettingsPageHostTests
{
    [Fact]
    public void OnlyVisitedPagesAreCreatedAndSwitchingBackPreservesTheirControls() => OnSta(() =>
    {
        var created = new List<string>();
        var host = new SettingsPageHost(key => { created.Add(key); return new TextBox(); });
        Assert.Empty(host.Children);
        var sources = Assert.IsType<TextBox>(host.ShowPage("sources"));
        sources.Text = "unfinished edit";
        Assert.Equal(["sources"], created);
        var orb = host.ShowPage("orb");
        Assert.Equal(Visibility.Collapsed, sources.Visibility);
        Assert.Equal(Visibility.Visible, orb.Visibility);
        Assert.Same(sources, host.ShowPage("sources"));
        Assert.Equal("unfinished edit", sources.Text);
        Assert.Equal(Visibility.Collapsed, orb.Visibility);
        Assert.Equal(["sources", "orb"], created);
        Assert.Equal(2, host.Children.Count);
    });

    [Fact]
    public void CachedAndNewPagesInheritTheCurrentSettingsContext() => OnSta(() =>
    {
        var first = new object();
        var second = new object();
        var host = new SettingsPageHost(_ => new Border()) { DataContext = first };
        var sources = host.ShowPage("sources");
        Assert.Same(first, sources.DataContext);
        host.DataContext = second;
        Assert.Same(second, sources.DataContext);
        Assert.Same(second, host.ShowPage("orb").DataContext);
    });

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
