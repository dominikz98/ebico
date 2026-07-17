using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Versioning;
using EBICO.Server.State;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Server.Pipeline;

/// <summary>
/// The production request verifier (issue #58): checks the EBICS authentication signature
/// (<c>AuthSignature</c>, key version X002) of every signed transaction <c>ebicsRequest</c> against the
/// subscriber's stored authentication key, replacing the <see cref="NoOpEbicsRequestVerifier"/> default.
/// A tampered request (or one signed with the wrong key) is rejected with
/// <see cref="EbicsReturnCode.AuthenticationFailed"/> (061001).
/// </summary>
/// <remarks>
/// <para>
/// Only the signed <c>ebicsRequest</c> (upload/download initialisation, every transfer segment, the
/// download receipt, and the single-phase HCA/HCS/SPR orders) carries a verifiable X002 signature. The
/// unsecured onboarding requests (INI/HIA/HSA, <c>ebicsUnsecuredRequest</c>) and the
/// <c>ebicsNoPubKeyDigestsRequest</c> (HPB) are skipped — they precede or bootstrap the key exchange and
/// are validated by their own flows. The pipeline invokes the verifier for <em>every</em> request
/// (<see cref="EbicsRequestPipeline"/>), so that selection lives here.
/// </para>
/// <para>
/// Verification runs only once the subscriber has an authentication key on file (i.e. after HIA):
/// before key exchange there is nothing to verify against, and a premature order is rejected downstream
/// by the subscriber-state check (091002). This mirrors how EBICS bootstraps trust and keeps the check
/// from firing on requests that legitimately cannot be signed yet.
/// </para>
/// <para>
/// Initialisation and single-phase requests carry the full subscriber triple in the static header;
/// transfer/receipt requests carry only the <c>HostID</c> (the subscriber is bound to the transaction),
/// so those are resolved back to a subscriber via the transaction stores.
/// </para>
/// </remarks>
public sealed class X002EbicsRequestVerifier : IEbicsRequestVerifier
{
    private readonly IServerKeyStore _keyStore;
    private readonly IUploadTransactionStore _uploadTransactions;
    private readonly IDownloadTransactionStore _downloadTransactions;

    /// <summary>Initializes the verifier with the key and transaction stores it resolves against.</summary>
    /// <param name="keyStore">The store holding subscriber public keys (the X002 authentication key is read here).</param>
    /// <param name="uploadTransactions">The upload transaction store, used to bind a transfer request to its subscriber.</param>
    /// <param name="downloadTransactions">The download transaction store, used to bind a transfer/receipt request to its subscriber.</param>
    public X002EbicsRequestVerifier(
        IServerKeyStore keyStore,
        IUploadTransactionStore uploadTransactions,
        IDownloadTransactionStore downloadTransactions)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(uploadTransactions);
        ArgumentNullException.ThrowIfNull(downloadTransactions);

        _keyStore = keyStore;
        _uploadTransactions = uploadTransactions;
        _downloadTransactions = downloadTransactions;
    }

    /// <inheritdoc />
    public async Task<EbicsVerificationResult> VerifyAsync(EbicsRequestContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Only the signed ebicsRequest carries an X002 signature we can verify. Everything else
        // (unsecured INI/HIA/HSA, no-pub-key-digests HPB) is skipped.
        if (context.Envelope is not (H003.EbicsRequest or H004.EbicsRequest or H005.EbicsRequest))
        {
            return EbicsVerificationResult.Success;
        }

        var subscriber = ResolveSubscriber(context);
        if (subscriber is not { } keyRef)
        {
            // The request cannot be bound to a subscriber (e.g. a transfer for an unknown transaction id).
            // Nothing to verify against; the transaction engine rejects the unknown id (091101) downstream.
            return EbicsVerificationResult.Success;
        }

        var authKey = await _keyStore.GetAsync(keyRef, KeyPurpose.Authentication, ct).ConfigureAwait(false);
        if (authKey is null)
        {
            // No authentication key on file yet: the subscriber has not completed HIA, so the signature
            // cannot be verified. The subscriber-state check rejects premature orders (091002).
            return EbicsVerificationResult.Success;
        }

        // A signed request from a subscriber whose authentication key we hold must carry a valid signature.
        if (context.Envelope is not IAuthSignedRequestEnvelope signed || signed.AuthSignature is null)
        {
            return EbicsVerificationResult.Fail(EbicsReturnCode.AuthenticationFailed);
        }

        var verified = AuthenticationSignature.Verify(
            context.RequestXml, signed.AuthSignature, authKey.Key, authKey.Version);
        return verified
            ? EbicsVerificationResult.Success
            : EbicsVerificationResult.Fail(EbicsReturnCode.AuthenticationFailed);
    }

    // Resolves the subscriber the request's signature must verify against: from the static-header triple
    // for initialisation/single-phase requests, or from the transaction stores (by transaction id) for
    // transfer/receipt requests, whose header carries only the HostID.
    private SubscriberKeyRef? ResolveSubscriber(EbicsRequestContext context)
    {
        var (host, partner, user) = ExtractHeaderIds(context.Envelope);
        if (HostId.TryCreate(host, out var h)
            && PartnerId.TryCreate(partner, out var p)
            && UserId.TryCreate(user, out var u))
        {
            return new SubscriberKeyRef(h, p, u);
        }

        if (context.TransactionId is { } transactionId)
        {
            var hex = Convert.ToHexString(transactionId);
            if (_uploadTransactions.TryGet(hex, out var upload) && upload is not null)
            {
                return upload.Subscriber;
            }

            if (_downloadTransactions.TryGet(hex, out var download) && download is not null)
            {
                return download.Subscriber;
            }
        }

        return null;
    }

    private static (string? Host, string? Partner, string? User) ExtractHeaderIds(IEbicsRequestEnvelope envelope) => envelope switch
    {
        H003.EbicsRequest r => (r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
        H004.EbicsRequest r => (r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
        H005.EbicsRequest r => (r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
        _ => (null, null, null),
    };
}
