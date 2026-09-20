using System;
using System.Text.Json.Serialization;

namespace Traysky.Models;

/// <summary>
/// The minimum needed to get back into a Bluesky session on the next launch without asking
/// for a password again. The refresh token is the only secret; the access token is short lived
/// and deliberately not kept. DPoP fields are only populated for OAuth sessions.
/// </summary>
/// <remarks>
/// Serialized through <see cref="SessionJsonContext"/> and encrypted with DPAPI by
/// <c>SessionStore</c>. Never log an instance of this type.
/// </remarks>
public sealed class PersistedSession
{
    /// <summary>The PDS that issued the token, e.g. https://bsky.social or a self-hosted PDS.</summary>
    public required string Service { get; init; }

    /// <summary>The account's DID, the stable identity the token belongs to.</summary>
    public required string Did { get; init; }

    /// <summary>The handle at the time of login, kept for display before the profile loads.</summary>
    public string? Handle { get; init; }

    /// <summary>
    /// Mirrors <c>idunno.AtProto.Authentication.AuthenticationType</c>, stored by name so the
    /// file stays readable if the enum's numbering ever changes.
    /// </summary>
    public required string AuthenticationType { get; init; }

    public required string RefreshToken { get; init; }

    public string? DPoPProofKey { get; init; }

    public string? DPoPNonce { get; init; }

    public DateTimeOffset SavedAtUtc { get; init; }
}

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(PersistedSession))]
public sealed partial class SessionJsonContext : JsonSerializerContext
{
}
