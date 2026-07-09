using EBICO.Connector.Onboarding.Envelopes;
using EBICO.Connector.Onboarding.Letter;
using EBICO.Core.Crypto;

namespace EBICO.Connector.Onboarding;

/// <summary>Handles <see cref="HiaRequest"/>: builds and sends the HIA order, then renders the letter.</summary>
internal sealed class HiaRequestHandler : IEbicsRequestHandler<HiaRequest, HiaResult>
{
    private readonly IOnboardingEnvelopeBuilderRegistry _registry;
    private readonly IInitializationLetterRenderer _letterRenderer;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the handler.</summary>
    /// <param name="registry">The version-specific envelope builder registry.</param>
    /// <param name="letterRenderer">The initialization-letter renderer.</param>
    /// <param name="timeProvider">The time source for certificate validity and the letter date.</param>
    public HiaRequestHandler(
        IOnboardingEnvelopeBuilderRegistry registry,
        IInitializationLetterRenderer letterRenderer,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(letterRenderer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _registry = registry;
        _letterRenderer = letterRenderer;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<EbicsResult<HiaResult>> Handle(HiaRequest request, EbicsContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);

        var version = ctx.Connection.Version;
        var builder = _registry.Get(version);
        var authVersion = request.AuthenticationVersion ?? KeyVersions.Default(KeyPurpose.Authentication, version).Version;
        var encVersion = request.EncryptionVersion ?? KeyVersions.Default(KeyPurpose.Encryption, version).Version;
        KeyVersions.EnsurePermitted(authVersion, version);
        KeyVersions.EnsurePermitted(encVersion, version);

        var authMaterial = await OnboardingSupport.RequireSubscriberKeyAsync(ctx, KeyPurpose.Authentication, ct).ConfigureAwait(false);
        var encMaterial = await OnboardingSupport.RequireSubscriberKeyAsync(ctx, KeyPurpose.Encryption, ct).ConfigureAwait(false);
        var authDescriptor = OnboardingSupport.Descriptor(ctx, authMaterial, authVersion, KeyPurpose.Authentication, _timeProvider);
        var encDescriptor = OnboardingSupport.Descriptor(ctx, encMaterial, encVersion, KeyPurpose.Encryption, _timeProvider);

        var envelope = builder.BuildHiaRequest(OnboardingSupport.Header(ctx), authDescriptor, encDescriptor);
        var responseXml = await OnboardingSupport.ExchangeAsync(envelope, ctx, ct).ConfigureAwait(false);
        var view = builder.ParseResponse(responseXml);
        if (view.ReturnCode != EbicsResult<HiaResult>.OkReturnCode)
        {
            return EbicsResult<HiaResult>.Failure(view.ReturnCode, view.ReportText);
        }

        var authDigest = PublicKeyFingerprint.Compute(authMaterial);
        var encDigest = PublicKeyFingerprint.Compute(encMaterial);
        var authDigestText = PublicKeyFingerprint.ToLetterFormat(authDigest);
        var encDigestText = PublicKeyFingerprint.ToLetterFormat(encDigest);

        InitializationLetter? letter = null;
        if (request.IncludeLetter)
        {
            letter = _letterRenderer.Render(new InitializationLetterModel
            {
                Kind = LetterKind.Hia,
                HostId = ctx.Connection.HostId.Value,
                PartnerId = ctx.Connection.PartnerId.Value,
                UserId = ctx.Connection.UserId.Value,
                VersionCode = ctx.Version.Code,
                CreatedAt = _timeProvider.GetUtcNow(),
                Keys =
                [
                    new LetterKeyEntry(KeyPurpose.Authentication, authVersion.Value, authDigestText),
                    new LetterKeyEntry(KeyPurpose.Encryption, encVersion.Value, encDigestText),
                ],
            });
        }

        return EbicsResult<HiaResult>.Success(new HiaResult
        {
            AuthenticationKeyDigest = authDigest,
            EncryptionKeyDigest = encDigest,
            AuthenticationKeyDigestText = authDigestText,
            EncryptionKeyDigestText = encDigestText,
            Letter = letter,
        });
    }
}
