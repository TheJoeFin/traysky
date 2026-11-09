using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class WindowBoundsPolicyTests
{
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    [TestMethod]
    public void LeavesBoundsThatFitAlone() =>
        Assert.AreEqual(new PixelRect(100, 50, 800, 600), WindowBoundsPolicy.Fit(new PixelRect(100, 50, 800, 600), Work));

    [TestMethod]
    public void PullsAnOffScreenWindowBackOn()
    {
        // Saved on a second monitor that has since been unplugged.
        PixelRect fitted = WindowBoundsPolicy.Fit(new PixelRect(3000, -400, 800, 600), Work);

        Assert.AreEqual(new PixelRect(1120, 0, 800, 600), fitted);
    }

    [TestMethod]
    public void ShrinksAWindowBiggerThanTheWorkArea() =>
        Assert.AreEqual(new PixelRect(0, 0, 1920, 1040), WindowBoundsPolicy.Fit(new PixelRect(-50, -50, 5000, 4000), Work));

    [TestMethod]
    public void CentersWithinTheWorkArea() =>
        Assert.AreEqual(new PixelRect(460, 220, 1000, 600), WindowBoundsPolicy.Centered(1000, 600, Work));
}
