using idunno.AtProto.Authentication;
using idunno.AtProto.Events;
using idunno.Bluesky;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Traysky.Services;

/// <summary>
/// <see cref="BlueskyAgent"/> with a fix for how idunno.AtProto 7.0.0 handles DPoP nonce
/// changes. Every request snapshots the agent's credentials when it starts; when the response
/// carries a new nonce, the base callback writes that snapshot back as the agent's credentials
/// and bumps their generation. If a token refresh lands while the request is in flight, that
/// puts the spent refresh token back on the agent (and on disk). If the nonce update lands
/// while the refresh is in flight, the generation bump makes the refresh throw away the new
/// tokens after the server has already rotated the old one. Either way every later refresh
/// fails straight away with "already exchanged", and with a one-minute poll racing an hourly
/// refresh it happens within a day or so.
/// </summary>
public sealed class TrayskyAgent(BlueskyAgentOptions? options = null) : BlueskyAgent(options)
{
    protected override Task InternalOnCredentialsUpdatedCallBack(AtProtoCredential credentials, CancellationToken cancellationToken = default)
    {
        AccessCredentials? current = Credentials;

        // Tokens the agent no longer holds belong to a request that started before a refresh
        // or a logout. Its nonce is not worth a stale session; the next request picks up a new
        // one from the server's use_dpop_nonce retry.
        if (credentials is not AccessCredentials updated
            || current?.Did is null
            || !string.Equals(updated.RefreshToken, current.RefreshToken, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        // Requests normally carry the agent's own instance, whose nonce the HTTP client has
        // already changed in place. Copy it across when they don't. Either way the credentials
        // are not reassigned, so a refresh in flight still gets to publish its tokens.
        if (!ReferenceEquals(updated, current)
            && updated is IDPoPBoundCredential updatedDPoP
            && current is IDPoPBoundCredential currentDPoP
            && !string.IsNullOrWhiteSpace(updatedDPoP.DPoPNonce))
        {
            currentDPoP.DPoPNonce = updatedDPoP.DPoPNonce;
        }

        return OnCredentialsUpdatedAsync(new CredentialsUpdatedEventArgs(current.Did, current.Service, current), cancellationToken);
    }
}
