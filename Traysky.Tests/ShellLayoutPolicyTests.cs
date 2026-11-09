using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ShellLayoutPolicyTests
{
    [TestMethod]
    [DataRow(0, ShellLayoutKind.Compact)]
    [DataRow(420, ShellLayoutKind.Compact)]
    [DataRow(639.9, ShellLayoutKind.Compact)]
    [DataRow(640, ShellLayoutKind.Medium)]
    [DataRow(959.9, ShellLayoutKind.Medium)]
    [DataRow(960, ShellLayoutKind.Wide)]
    [DataRow(1279.9, ShellLayoutKind.Wide)]
    [DataRow(1280, ShellLayoutKind.VeryWide)]
    [DataRow(3000, ShellLayoutKind.VeryWide)]
    public void PicksLayoutByWidth(double width, ShellLayoutKind expected) =>
        Assert.AreEqual(expected, ShellLayoutPolicy.ForWidth(width));

    [TestMethod]
    [DataRow(639, false, false)]
    [DataRow(640, false, true)]
    [DataRow(1000, false, true)]
    [DataRow(639, true, true)]   // already a window: stays one a little below the threshold
    [DataRow(600, true, true)]
    [DataRow(599, true, false)]  // ...but goes back to a flyout well below it
    public void WindowSwitchHasHysteresis(double width, bool isWindow, bool expected) =>
        Assert.AreEqual(expected, ShellLayoutPolicy.ShouldBeWindow(width, isWindow));

    [TestMethod]
    public void ChromeGrowsWithWidth()
    {
        Assert.IsFalse(ShellLayoutPolicy.ShowsRail(ShellLayoutKind.Compact));
        Assert.IsTrue(ShellLayoutPolicy.ShowsRail(ShellLayoutKind.Medium));

        Assert.IsFalse(ShellLayoutPolicy.ShowsSidebar(ShellLayoutKind.Medium));
        Assert.IsTrue(ShellLayoutPolicy.ShowsSidebar(ShellLayoutKind.Wide));
        Assert.IsTrue(ShellLayoutPolicy.ShowsSidebar(ShellLayoutKind.VeryWide));

        Assert.IsFalse(ShellLayoutPolicy.RailShowsLabels(ShellLayoutKind.Wide));
        Assert.IsTrue(ShellLayoutPolicy.RailShowsLabels(ShellLayoutKind.VeryWide));
    }

    [TestMethod]
    public void CompactContentIsUnbounded_OthersCapped()
    {
        Assert.IsTrue(double.IsPositiveInfinity(ShellLayoutPolicy.MaxContentWidth(ShellLayoutKind.Compact)));
        Assert.AreEqual(ShellLayoutPolicy.ContentMaxWidth, ShellLayoutPolicy.MaxContentWidth(ShellLayoutKind.Medium));
        Assert.AreEqual(ShellLayoutPolicy.ContentMaxWidth, ShellLayoutPolicy.MaxContentWidth(ShellLayoutKind.VeryWide));
    }
}
