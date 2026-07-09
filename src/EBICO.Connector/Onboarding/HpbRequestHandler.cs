using System.Security.Cryptography.X509Certificates;
using EBICO.Connector.Keys;
using EBICO.Connector.Onboarding.Envelopes;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;

namespace EBICO.Connector.Onboarding;

/// <summary>
/// Handles <see cref="HpbRequest"/>: sends the X002-signed HPB request, decrypts and decompresses the
/// response, verifies the bank key fingerprints against the bank letter, optionally verifies the
/// X.509 chain (H005) and stores the bank keys.
/// </summary>
internal sealed class HpbRequestHandler : IEbicsRequestHandler<HpbRequest, HpbResult>
{
    private readonly IOnboardingEnvelopeBuilderRegistry _registry;

    /// <summary>Initializes the handler.</summary>
    /// <param name="registry">The version-specific envelope builder registry.</param>
    public HpbRequestHandler(IOnboardingEnvelopeBuilderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <inheritdoc />
    public async Task<EbicsResult<HpbResult>> Handle(HpbRequest request, EbicsContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);

        var version = ctx.Connection.Version;
        var builder = _registry.Get(version);
        var authVersion = KeyVersions.Default(KeyPurpose.Authentication, version).Version;
        var encVersion = KeyVersions.Default(KeyPurpose.Encryption, version).Version;

        // Sign the request with the subscriber's X002 key.
        var authKey = await OnboardingSupport.RequireSubscriberKeyAsync(ctx, KeyPurpose.Authentication, ct).ConfigureAwait(false);
        var envelope = builder.BuildHpbRequest(OnboardingSupport.Header(ctx));
        var unsignedXml = EbicsXmlSerializer.SerializeToString(envelope);
        envelope.AuthSignature = AuthenticationSignature.Sign(unsignedXml, authKey, authVersion);

        var responseXml = await OnboardingSupport.ExchangeAsync(envelope, ctx, ct).ConfigureAwait(false);
        var view = builder.ParseResponse(responseXml);
        if (view.ReturnCode != EbicsResult<HpbResult>.OkReturnCode)
        {
            return EbicsResult<HpbResult>.Failure(view.ReturnCode, view.ReportText);
        }

        if (view.EncryptedTransactionKey is null || view.EncryptedOrderData is null)
        {
            throw new EbicsOnboardingException("The HPB response carries no encrypted order data.");
        }

        // Decrypt (subscriber E002 private key), decompress and parse the bank keys.
        var encKey = await OnboardingSupport.RequireSubscriberKeyAsync(ctx, KeyPurpose.Encryption, ct).ConfigureAwait(false);
        var encrypted = new EncryptedOrderData(view.EncryptedTransactionKey, view.EncryptedOrderData);
        var compressed = EncryptionE002.Decrypt(encrypted, encKey, encVersion);
        var orderDataXml = EbicsCompression.Decompress(compressed);
        var bankKeys = builder.ParseHpbOrderData(orderDataXml);

        // Verify the fingerprints against the bank letter (a mismatch is a security failure → throw,
        // before anything is stored).
        var fingerprintsVerified = false;
        if (request.ExpectedAuthenticationKeyDigest is { } expectedAuth)
        {
            if (!PublicKeyFingerprint.Verify(bankKeys.Authentication, expectedAuth.Span))
            {
                throw new EbicsOnboardingException(
                    "The bank authentication key fingerprint does not match the expected value from the bank letter.");
            }

            fingerprintsVerified = true;
        }

        if (request.ExpectedEncryptionKeyDigest is { } expectedEnc)
        {
            if (!PublicKeyFingerprint.Verify(bankKeys.Encryption, expectedEnc.Span))
            {
                throw new EbicsOnboardingException(
                    "The bank encryption key fingerprint does not match the expected value from the bank letter.");
            }

            fingerprintsVerified = true;
        }

        // Optional X.509 chain verification (H005).
        if (request.TrustAnchors is { } trustAnchors)
        {
            VerifyCertificate(bankKeys.AuthenticationCertificate, trustAnchors, KeyPurpose.Authentication);
            VerifyCertificate(bankKeys.EncryptionCertificate, trustAnchors, KeyPurpose.Encryption);
        }

        if (request.StoreBankKeys)
        {
            await ctx.Keys.StoreAsync(KeyOwner.Bank, KeyPurpose.Authentication, bankKeys.Authentication, ct).ConfigureAwait(false);
            await ctx.Keys.StoreAsync(KeyOwner.Bank, KeyPurpose.Encryption, bankKeys.Encryption, ct).ConfigureAwait(false);
        }

        var authDigest = PublicKeyFingerprint.Compute(bankKeys.Authentication);
        var encDigest = PublicKeyFingerprint.Compute(bankKeys.Encryption);
        return EbicsResult<HpbResult>.Success(new HpbResult
        {
            BankKeys = bankKeys,
            AuthenticationKeyDigest = authDigest,
            EncryptionKeyDigest = encDigest,
            AuthenticationKeyDigestText = PublicKeyFingerprint.ToLetterFormat(authDigest),
            EncryptionKeyDigestText = PublicKeyFingerprint.ToLetterFormat(encDigest),
            FingerprintsVerified = fingerprintsVerified,
        });
    }

    private static void VerifyCertificate(
        X509Certificate2? certificate, X509Certificate2Collection trustAnchors, KeyPurpose purpose)
    {
        if (certificate is null)
        {
            // Pure-key procedures (H003/H004) carry no certificate; nothing to chain-verify.
            return;
        }

        var result = X509CertificateVerifier.Verify(certificate, trustAnchors, purpose);
        if (!result.IsValid)
        {
            throw new EbicsOnboardingException(
                $"The bank {purpose} certificate failed X.509 verification: {string.Join("; ", result.Diagnostics)}");
        }
    }
}
