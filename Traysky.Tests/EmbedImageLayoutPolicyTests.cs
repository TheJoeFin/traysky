using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class EmbedImageLayoutPolicyTests
{
    [TestMethod]
    public void FitsAsManyColumnsAsTheWidthAllows()
    {
        Assert.AreEqual(2, EmbedImageLayoutPolicy.Columns(330, 140, 4, count: 4));
        Assert.AreEqual(3, EmbedImageLayoutPolicy.Columns(432, 140, 4, count: 4));
    }

    [TestMethod]
    public void LonePictureStillTakesOneColumnNotTheRow()
    {
        Assert.AreEqual(2, EmbedImageLayoutPolicy.Columns(330, 140, 4, count: 1));
    }

    [TestMethod]
    public void NarrowerThanOneColumnIsStillOneColumn()
    {
        Assert.AreEqual(1, EmbedImageLayoutPolicy.Columns(100, 140, 4, count: 3));
    }

    [TestMethod]
    public void UnboundedWidthPutsEveryPictureInOneRow()
    {
        Assert.AreEqual(3, EmbedImageLayoutPolicy.Columns(double.PositiveInfinity, 140, 4, count: 3));
        Assert.AreEqual(140, EmbedImageLayoutPolicy.CellWidth(double.PositiveInfinity, 3, 140, 4));
    }

    [TestMethod]
    public void ColumnsShareTheLeftoverWidthInWholePixels()
    {
        // 330 - (2 * 144 - 4) = 46 left over, 23 each.
        Assert.AreEqual(163, EmbedImageLayoutPolicy.CellWidth(330, 2, 140, 4));
        Assert.AreEqual(162, EmbedImageLayoutPolicy.CellWidth(329, 2, 140, 4));
    }

    [TestMethod]
    public void OneNarrowColumnTakesTheWholeWidth()
    {
        Assert.AreEqual(100, EmbedImageLayoutPolicy.CellWidth(100, 1, 140, 4));
    }

    [TestMethod]
    public void WidePictureIsDrawnAtTheCellWidth()
    {
        Assert.AreEqual(163, EmbedImageLayoutPolicy.DrawnWidth(163, 92, 16.0 / 9.0));
    }

    [TestMethod]
    public void TallPictureIsDrawnAtItsLetterboxedWidth()
    {
        // A 3:4 picture capped at 220 tall is drawn 165 wide in a 330-wide cell.
        Assert.AreEqual(165, EmbedImageLayoutPolicy.DrawnWidth(330, 220, 0.75));
    }

    [TestMethod]
    public void UnknownAspectRatioAssumesTheCellWidth()
    {
        Assert.AreEqual(163, EmbedImageLayoutPolicy.DrawnWidth(163, 92, 0));
    }

    [TestMethod]
    public void DrawnWidthRoundsUp()
    {
        Assert.AreEqual(163, EmbedImageLayoutPolicy.DrawnWidth(162.2, 300, 2));
    }

    [TestMethod]
    public void FirstDecodeIsExactlyTheDrawnWidth()
    {
        Assert.AreEqual(163, EmbedImageLayoutPolicy.DecodeWidth(163, decodedWidth: 0));
    }

    [TestMethod]
    public void GrowingDecodesAgainWithHeadroom()
    {
        Assert.AreEqual(213, EmbedImageLayoutPolicy.DecodeWidth(170, decodedWidth: 163));
    }

    [TestMethod]
    public void ShrinkingOrStayingPutKeepsTheDecode()
    {
        Assert.AreEqual(163, EmbedImageLayoutPolicy.DecodeWidth(163, decodedWidth: 163));
        Assert.AreEqual(213, EmbedImageLayoutPolicy.DecodeWidth(150, decodedWidth: 213));
    }
}
