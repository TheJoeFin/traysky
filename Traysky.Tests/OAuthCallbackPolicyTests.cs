using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class OAuthCallbackPolicyTests
{
    [TestMethod]
    public void RedirectUriFollowsTheReverseDomainRule()
    {
        // atproto: custom scheme = client_id hostname reversed, then ":/" and a path.
        string host = new Uri(OAuthCallbackPolicy.ClientId).Host;
        string reversed = string.Join('.', host.Split('.').Reverse());

        Assert.AreEqual(reversed, OAuthCallbackPolicy.Scheme);
        StringAssert.StartsWith(OAuthCallbackPolicy.RedirectUri, OAuthCallbackPolicy.Scheme + ":/");
        Assert.IsFalse(OAuthCallbackPolicy.RedirectUri.StartsWith(OAuthCallbackPolicy.Scheme + "://", StringComparison.Ordinal));

        // Windows protocol names: lowercase, at most 39 characters.
        Assert.AreEqual(OAuthCallbackPolicy.Scheme.ToLowerInvariant(), OAuthCallbackPolicy.Scheme);
        Assert.IsTrue(OAuthCallbackPolicy.Scheme.Length <= 39);
    }

    [TestMethod]
    public void RecognisesTheCallback()
    {
        Assert.IsTrue(OAuthCallbackPolicy.IsCallback(new Uri(OAuthCallbackPolicy.RedirectUri)));
        Assert.IsTrue(OAuthCallbackPolicy.IsCallback(new Uri(OAuthCallbackPolicy.RedirectUri + "?code=abc&state=xyz&iss=https%3A%2F%2Fbsky.social")));
        Assert.IsTrue(OAuthCallbackPolicy.IsCallback(new Uri(OAuthCallbackPolicy.RedirectUri + "/?code=abc")));

        Assert.IsFalse(OAuthCallbackPolicy.IsCallback(null));
        Assert.IsFalse(OAuthCallbackPolicy.IsCallback(new Uri(OAuthCallbackPolicy.Scheme + ":/elsewhere?code=abc")));
        Assert.IsFalse(OAuthCallbackPolicy.IsCallback(new Uri("https://thejoefin.github.io/traysky/callback?code=abc")));
    }

    [TestMethod]
    public void ReadsQueryValues()
    {
        Uri uri = new(OAuthCallbackPolicy.RedirectUri + "?state=a%2Bb&iss=https%3A%2F%2Fbsky.social&flag&desc=two+words");

        Assert.AreEqual("a+b", OAuthCallbackPolicy.GetQueryValue(uri, "state"));
        Assert.AreEqual("https://bsky.social", OAuthCallbackPolicy.GetQueryValue(uri, "iss"));
        Assert.AreEqual(string.Empty, OAuthCallbackPolicy.GetQueryValue(uri, "flag"));
        Assert.AreEqual("two words", OAuthCallbackPolicy.GetQueryValue(uri, "desc"));
        Assert.IsNull(OAuthCallbackPolicy.GetQueryValue(uri, "code"));
        Assert.IsNull(OAuthCallbackPolicy.GetQueryValue(new Uri(OAuthCallbackPolicy.RedirectUri), "state"));
    }

    [TestMethod]
    public void ErrorMessages()
    {
        Uri ok = new(OAuthCallbackPolicy.RedirectUri + "?code=abc&state=xyz");
        Uri denied = new(OAuthCallbackPolicy.RedirectUri + "?error=access_denied&state=xyz");
        Uri described = new(OAuthCallbackPolicy.RedirectUri + "?error=server_error&error_description=Try%20later");
        Uri bare = new(OAuthCallbackPolicy.RedirectUri + "?error=server_error");

        Assert.IsNull(OAuthCallbackPolicy.ErrorMessage(ok));
        Assert.AreEqual("Sign in was cancelled in the browser.", OAuthCallbackPolicy.ErrorMessage(denied));
        Assert.AreEqual("Bluesky couldn't sign you in: Try later", OAuthCallbackPolicy.ErrorMessage(described));
        Assert.AreEqual("Bluesky couldn't sign you in (server_error).", OAuthCallbackPolicy.ErrorMessage(bare));
    }

    [TestMethod]
    public void PublishedMetadataMatchesTheApp()
    {
        string root = RepoRoot();
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "oauth", "client-metadata.json")));
        JsonElement meta = doc.RootElement;

        Assert.AreEqual(OAuthCallbackPolicy.ClientId, meta.GetProperty("client_id").GetString());
        Assert.AreEqual("native", meta.GetProperty("application_type").GetString());
        Assert.AreEqual("none", meta.GetProperty("token_endpoint_auth_method").GetString());
        Assert.IsTrue(meta.GetProperty("dpop_bound_access_tokens").GetBoolean());
        Assert.AreEqual(string.Join(' ', OAuthCallbackPolicy.Scopes), meta.GetProperty("scope").GetString());
        CollectionAssert.Contains(
            meta.GetProperty("redirect_uris").EnumerateArray().Select(e => e.GetString()).ToList(),
            OAuthCallbackPolicy.RedirectUri);
    }

    [TestMethod]
    public void ManifestDeclaresTheScheme()
    {
        XDocument manifest = XDocument.Load(Path.Combine(RepoRoot(), "Traysky", "Package.appxmanifest"));
        string[] protocols = manifest.Descendants()
            .Where(e => e.Name.LocalName == "Protocol")
            .Select(e => (string?)e.Attribute("Name") ?? string.Empty)
            .ToArray();

        CollectionAssert.Contains(protocols, OAuthCallbackPolicy.Scheme);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Traysky.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Traysky.slnx not found above the test output folder.");
    }
}
