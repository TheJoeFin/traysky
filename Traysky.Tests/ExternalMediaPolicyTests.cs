using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ExternalMediaPolicyTests
{
    [TestMethod]
    public void KlipyGifFromThePickerIsMedia()
    {
        const string url = "https://static.klipy.com/ii/d7aec6f6f171607374b2065c836f92f4/28/c5/Arhx1lFy.gif?hh=118&ww=200&mp4=E96jbeQxm38FR5L&webm=N3bET6znhZSkZuk";

        Assert.IsTrue(ExternalMediaPolicy.TryGetMedia(url, "Monty Python Holy Grail: Message for You!", "ALT: Monty Python Holy Grail: Message for You!", out ExternalMedia? media));
        Assert.AreEqual(url, media.Uri.OriginalString);
        Assert.AreEqual("Monty Python Holy Grail: Message for You!", media.AltText);
        Assert.AreEqual(200.0 / 118, media.AspectRatio!.Value, 1e-9);
    }

    [TestMethod]
    public void TenorGifIsMedia()
    {
        Assert.IsTrue(ExternalMediaPolicy.TryGetMedia("https://media.tenor.com/abc123AAAAC/cat.gif?hh=240&ww=320", "Cat", "Alt: a cat", out ExternalMedia? media));
        Assert.AreEqual("a cat", media.AltText);
        Assert.AreEqual(320.0 / 240, media.AspectRatio!.Value, 1e-9);
    }

    [TestMethod]
    [DataRow("https://example.com/photo.PNG")]
    [DataRow("https://example.com/photo.jpg")]
    [DataRow("https://example.com/photo.jpeg?v=2")]
    [DataRow("https://example.com/photo.webp")]
    public void DirectImageLinksAreMedia(string url)
    {
        Assert.IsTrue(ExternalMediaPolicy.TryGetMedia(url, "A photo", null, out ExternalMedia? media));
        Assert.AreEqual("A photo", media.AltText);
        Assert.IsNull(media.AspectRatio);
    }

    [TestMethod]
    public void GiphyPageMapsToTheGif()
    {
        Assert.IsTrue(ExternalMediaPolicy.TryGetMedia("https://giphy.com/gifs/cat-dance-3o7TKSjRrfIPjeiVyM", "Cat dance", null, out ExternalMedia? media));
        Assert.AreEqual("https://i.giphy.com/media/3o7TKSjRrfIPjeiVyM/giphy.gif", media.Uri.ToString());
    }

    [TestMethod]
    [DataRow("https://giphy.com/search/cats")]
    [DataRow("https://example.com/article")]
    [DataRow("https://example.com/gif-roundup.html")]
    [DataRow("file:///C:/pics/a.gif")]
    [DataRow("not a url")]
    [DataRow(null)]
    public void OtherLinksStayCards(string? url)
    {
        Assert.IsFalse(ExternalMediaPolicy.TryGetMedia(url, "Title", "Description", out _));
    }

    [TestMethod]
    public void AltTextFallsBackToDescriptionWithoutTitle()
    {
        Assert.IsTrue(ExternalMediaPolicy.TryGetMedia("https://example.com/a.gif", "  ", "A description", out ExternalMedia? media));
        Assert.AreEqual("A description", media.AltText);
    }
}
