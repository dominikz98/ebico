using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.State;

namespace EBICO.Server.Pipeline;

/// <summary>
/// The production response signer: attaches the bank's EBICS authentication signature
/// (<c>AuthSignature</c>, key version X002) to every transaction <c>ebicsResponse</c>, signed with the
/// bank's own authentication key from <see cref="IServerBankKeyStore"/>. The outbound counterpart of
/// <see cref="X002EbicsRequestVerifier"/>, and the default registration.
/// </summary>
/// <remarks>
/// <para>
/// A real bank signs its responses, and real clients verify them: the signature is what lets a
/// subscriber trust that a return code, a segment count or a downloaded statement actually came from
/// the bank and was not altered in transit. Leaving it off made every order after onboarding fail on
/// clients that check (the emulator was usable for INI/HIA/HPB only).
/// </para>
/// <para>
/// Only the <c>ebicsResponse</c> is signed. The <c>ebicsKeyManagementResponse</c> answering
/// INI/HIA/HPB has no <c>AuthSignature</c> element in its schema — those responses precede or
/// bootstrap the key exchange — so it is serialized unsigned, and
/// <see cref="IAuthSignedResponseEnvelope"/> is not attached to it.
/// </para>
/// <para>
/// The bank key pair is per <see cref="HostId"/>, so the host must be known to sign. A response is
/// therefore serialized <b>unsigned</b> when the request carried no resolvable host (malformed XML
/// answered with a fallback-version error response) or when the host is not in the master data. The
/// latter mirrors <c>HpbOrderHandlerBase</c>: the key pair is only materialized for a host that
/// exists, so an unknown <c>HostID</c> cannot make the emulator generate RSA key pairs.
/// </para>
/// <para>
/// <b>⚠️ Spec caveat:</b> which nodes of the response are authenticated follows the generated
/// bindings' <c>authenticate="true"</c> defaults (the response header, and the return code /
/// timestamp / encryption-info elements), and the canonicalisation details are the ones documented for
/// <see cref="AuthenticationSignature"/> — neither is verified against the official annexes.
/// </para>
/// </remarks>
public sealed class X002EbicsResponseSigner : IEbicsResponseSigner
{
    private readonly IServerBankKeyStore _bankKeyStore;
    private readonly IMasterDataManager _masterData;

    /// <summary>Initializes the signer with the stores it resolves the bank's signing key from.</summary>
    /// <param name="bankKeyStore">The store holding the bank's own key pair per host.</param>
    /// <param name="masterData">The master data, consulted so an unknown host does not materialize a key pair.</param>
    public X002EbicsResponseSigner(IServerBankKeyStore bankKeyStore, IMasterDataManager masterData)
    {
        ArgumentNullException.ThrowIfNull(bankKeyStore);
        ArgumentNullException.ThrowIfNull(masterData);

        _bankKeyStore = bankKeyStore;
        _masterData = masterData;
    }

    /// <inheritdoc />
    public async Task<byte[]> SerializeAsync(
        IEbicsResponseEnvelope response, HostId? host, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Only the ebicsResponse carries an AuthSignature element; the key-management response does not.
        if (response is not IAuthSignedResponseEnvelope signable || host is not { } hostId)
        {
            return EbicsXmlSerializer.SerializeToUtf8Bytes(response);
        }

        var bank = await _masterData.GetBankAsync(hostId, ct).ConfigureAwait(false);
        if (bank is null)
        {
            // An unknown host: we hold no identity to sign as, and asking the key store would mint a
            // key pair for a host that does not exist. The error response goes out unsigned.
            return EbicsXmlSerializer.SerializeToUtf8Bytes(response);
        }

        var keys = await _bankKeyStore.GetOrCreateAsync(hostId, ct).ConfigureAwait(false);
        if (!keys.Authentication.HasPrivateKey)
        {
            // A seeded public-only pair (IServerBankKeyStore.SetAsync accepts one, and the public part
            // alone is enough for HPB) cannot sign. Failing loudly beats answering unsigned: an unsigned
            // response is precisely the defect this signer exists to remove, and silently reintroducing
            // it would be invisible until a client rejected it.
            throw new InvalidOperationException(
                $"The bank key pair seeded for host '{hostId.Value}' has no private authentication key, so "
                + "the response cannot be signed. Seed a full key pair via IServerBankKeyStore.SetAsync, let "
                + "InMemoryServerBankKeyStore generate one, or register UnsignedEbicsResponseSigner to "
                + "answer unsigned on purpose.");
        }

        // Serialise → sign → write back → re-serialise. The signature is computed over the canonical
        // form of the unsigned envelope, exactly as the client recomputes it after stripping the
        // AuthSignature (which is not itself authenticated).
        var unsignedXml = EbicsXmlSerializer.SerializeToString(response);
        signable.AuthSignature = AuthenticationSignature.Sign(
            unsignedXml, keys.Authentication, keys.AuthenticationVersion);

        return EbicsXmlSerializer.SerializeToUtf8Bytes(response);
    }
}

/// <summary>
/// A response signer that never signs: every envelope is serialized as built. Useful for tests that
/// assert on raw response shapes, and as an opt-out for a client that cannot cope with a signed
/// response. Substitute it via <c>TryAddSingleton&lt;IEbicsResponseSigner, UnsignedEbicsResponseSigner&gt;()</c>
/// before <c>AddEbicoServer</c>.
/// </summary>
public sealed class UnsignedEbicsResponseSigner : IEbicsResponseSigner
{
    /// <inheritdoc />
    public Task<byte[]> SerializeAsync(
        IEbicsResponseEnvelope response, HostId? host, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        return Task.FromResult(EbicsXmlSerializer.SerializeToUtf8Bytes(response));
    }
}
