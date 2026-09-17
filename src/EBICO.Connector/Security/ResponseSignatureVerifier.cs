using EBICO.Connector.Keys;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Security;

/// <summary>
/// Verifies the bank's EBICS authentication signature (<c>AuthSignature</c>, key version X002) on an
/// inbound transaction <c>ebicsResponse</c> against the bank key stored during HPB. The client-side
/// mirror image of the server's <c>X002EbicsRequestVerifier</c>, and the reason a return code or a
/// downloaded statement can be trusted: without it, anything on the wire could have written the
/// answer.
/// </summary>
/// <remarks>
/// <para>
/// Applied to the transaction <c>ebicsResponse</c> only. The key-management responses answering
/// INI/HIA/HPB carry no <c>AuthSignature</c> element — they precede or bootstrap the very key exchange
/// the signature would be checked against — so the onboarding flows never pass through here. That is
/// also how a real bank behaves.
/// </para>
/// <para>
/// A response that is unsigned, signed with the wrong key or altered in transit raises
/// <see cref="EbicsResponseSignatureException"/>; the caller does not get to act on unauthenticated
/// content. Verification can be switched off per connection
/// (<see cref="Configuration.EbicsConnectionOptions.VerifyResponseSignature"/>) for a server that does
/// not sign.
/// </para>
/// </remarks>
internal static class ResponseSignatureVerifier
{
    /// <summary>
    /// Verifies the authentication signature of <paramref name="responseXml"/> when the connection asks
    /// for it and the envelope is a signable <c>ebicsResponse</c>; otherwise returns without checking.
    /// </summary>
    /// <param name="responseXml">The raw response XML as received from the server.</param>
    /// <param name="ctx">The execution context (connection settings and key store).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <exception cref="EbicsConfigurationException">
    /// Verification is enabled but no bank authentication key is stored (HPB has not run).
    /// </exception>
    /// <exception cref="EbicsResponseSignatureException">
    /// The response carries no signature, or the signature does not verify against the bank key.
    /// </exception>
    public static async Task VerifyAsync(string responseXml, EbicsContext ctx, CancellationToken ct)
    {
        if (!ctx.Connection.VerifyResponseSignature)
        {
            return;
        }

        // Only the transaction ebicsResponse is signable. A key-management response, or a body the
        // caller will reject as malformed anyway, is left to the caller's own parsing.
        IEbicsEnvelope envelope;
        try
        {
            envelope = EbicsXmlSerializer.DeserializeEnvelope(responseXml);
        }
        catch (Exception ex) when (ex is EbicsEnvelopeFormatException or EbicsVersionNotSupportedException)
        {
            return;
        }

        if (envelope is not IAuthSignedResponseEnvelope response)
        {
            return;
        }

        // The stored bank key carries no version tag (the client key store holds raw RSA material), so the
        // version comes from the registry's default for the connection's protocol version — X002 throughout
        // the supported range. The signature's own algorithm URIs are checked by AuthenticationSignature.
        var bankAuthKey = await ctx.Keys.GetAsync(KeyOwner.Bank, KeyPurpose.Authentication, ct).ConfigureAwait(false)
            ?? throw new EbicsConfigurationException(
                "No bank authentication key is present, so the server's response signature cannot be "
                + "verified. Run the HPB onboarding flow to fetch and store the bank keys, or set "
                + "VerifyResponseSignature to false to talk to a server that does not sign its responses.");

        if (response.AuthSignature is null)
        {
            throw new EbicsResponseSignatureException(
                "The server returned an unsigned ebicsResponse. A bank signs its responses; an unsigned "
                + "one is unauthenticated and is not acted upon. Set VerifyResponseSignature to false to "
                + "accept it anyway.");
        }

        var version = KeyVersions.Default(KeyPurpose.Authentication, ctx.Connection.Version).Version;
        if (!AuthenticationSignature.Verify(responseXml, response.AuthSignature, bankAuthKey, version))
        {
            throw new EbicsResponseSignatureException(
                "The authentication signature of the server's ebicsResponse does not verify against the "
                + "stored bank key. The response was altered in transit, or the bank keys are stale — "
                + "re-run HPB.");
        }
    }
}
