using System;

namespace Traysky.Services;

/// <summary>
/// Traysky's atproto OAuth client identity and the rules for recognising the browser's
/// redirect back into the app. No WinRT dependency, so it is linked into the tests.
/// </summary>
/// <remarks>
/// The atproto spec requires a native client's custom-scheme redirect to be the
/// <see cref="ClientId"/> hostname in reverse-domain order, followed by <c>:/</c> and a path.
/// Changing the host that serves the client metadata therefore changes the scheme too, and
/// the scheme is also declared as a protocol in Package.appxmanifest and in
/// docs/oauth/client-metadata.json. All three must agree.
/// </remarks>
public static class OAuthCallbackPolicy
{
    /// <summary>The URL of the published client metadata document, which doubles as the OAuth client id.</summary>
    public const string ClientId = "https://thejoefin.github.io/traysky/oauth/client-metadata.json";

    /// <summary>Reverse-domain of thejoefin.github.io.</summary>
    public const string Scheme = "io.github.thejoefin";

    public const string CallbackPath = "/traysky/callback";

    public const string RedirectUri = Scheme + ":" + CallbackPath;

    /// <summary><c>atproto</c> is mandatory; <c>transition:generic</c> is everything but DMs, which Traysky does not use.</summary>
    public static readonly string[] Scopes = ["atproto", "transition:generic"];

    /// <summary>True when <paramref name="uri"/> is the OAuth redirect (with or without a query), not some other link into the app.</summary>
    public static bool IsCallback(Uri? uri) =>
        uri is not null
        && uri.IsAbsoluteUri
        && string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.AbsolutePath.TrimEnd('/'), CallbackPath, StringComparison.Ordinal);

    /// <summary>Returns the unescaped value of a query parameter, or null when it is absent.</summary>
    public static string? GetQueryValue(Uri uri, string name)
    {
        string query = uri.Query;
        if (query.Length <= 1)
            return null;

        foreach (string pair in query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
            if (!string.Equals(key, name, StringComparison.Ordinal))
                continue;

            return eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
        }

        return null;
    }

    /// <summary>
    /// The message to show when the authorization server redirected back with an error
    /// instead of a code, or null when the callback carries no error.
    /// </summary>
    public static string? ErrorMessage(Uri callback)
    {
        string? error = GetQueryValue(callback, "error");
        if (string.IsNullOrEmpty(error))
            return null;

        return error switch
        {
            "access_denied" => "Sign in was cancelled in the browser.",
            _ => GetQueryValue(callback, "error_description") is { Length: > 0 } description
                ? $"Bluesky couldn't sign you in: {description}"
                : $"Bluesky couldn't sign you in ({error})."
        };
    }
}
