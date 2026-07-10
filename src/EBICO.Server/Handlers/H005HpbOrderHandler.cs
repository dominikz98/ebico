using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H005;

namespace EBICO.Server.Handlers;

/// <summary>
/// The H005 (EBICS 3.0) HPB handler. H005 is <b>certificate-based</b>: the bank's authentication and
/// encryption keys are returned as self-signed <c>X509Data</c> certificates inside the
/// <c>HPBResponseOrderData</c>.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the certificates are freshly self-signed on each response from the bank
/// key pair (which must carry a private key — the auto-generated default does). Trust is established
/// via the public-key fingerprint, not a certificate chain (M8). The validity window is derived from
/// the injected <see cref="TimeProvider"/>.
/// </remarks>
public sealed class H005HpbOrderHandler : HpbOrderHandlerBase
{
    // A fixed, DN-safe subject: EBICS host ids may contain ',' / '=', which would break X.500 DN
    // parsing if injected. The host id travels in the order data's HostID element instead.
    private const string SubjectName = "CN=EBICO Bank";

    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    /// <param name="keyStore">The server key store.</param>
    /// <param name="bankKeyStore">The bank key store.</param>
    /// <param name="timeProvider">The time source for the certificate validity window.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public H005HpbOrderHandler(
        IMasterDataManager masterData,
        IServerKeyStore keyStore,
        IServerBankKeyStore bankKeyStore,
        TimeProvider timeProvider)
        : base(masterData, keyStore, bankKeyStore)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H005;

    /// <inheritdoc />
    protected override HpbRequestData ExtractHpbRequest(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsNoPubKeyDigestsRequest
            ?? throw new InvalidDataException("The HPB request is not an H005 ebicsNoPubKeyDigestsRequest.");
        var header = request.Header?.Static;
        return new HpbRequestData(header?.HostId, header?.PartnerId, header?.UserId);
    }

    /// <inheritdoc />
    protected override byte[] SerializeBankPubKeyOrderData(BankKeyPair bankKeys, string hostId)
    {
        var now = _timeProvider.GetUtcNow();
        using var authCert = SelfSignedCertificateFactory.Create(
            bankKeys.Authentication, KeyPurpose.Authentication, SubjectName, now.AddMinutes(-5), now.AddYears(1));
        using var encCert = SelfSignedCertificateFactory.Create(
            bankKeys.Encryption, KeyPurpose.Encryption, SubjectName, now.AddMinutes(-5), now.AddYears(1));

        return EbicsXmlSerializer.SerializeOrderData(new H.HpbResponseOrderDataType
        {
            AuthenticationPubKeyInfo = new H.AuthenticationPubKeyInfoType
            {
                AuthenticationVersion = bankKeys.AuthenticationVersion.Value,
                X509Data = ToX509Data(authCert.RawData),
            },
            EncryptionPubKeyInfo = new H.EncryptionPubKeyInfoType
            {
                EncryptionVersion = bankKeys.EncryptionVersion.Value,
                X509Data = ToX509Data(encCert.RawData),
            },
            HostId = hostId,
        });
    }
}
