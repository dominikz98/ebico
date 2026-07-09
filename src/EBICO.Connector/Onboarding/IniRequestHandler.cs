using EBICO.Connector.Onboarding.Envelopes;
using EBICO.Connector.Onboarding.Letter;
using EBICO.Core.Crypto;

namespace EBICO.Connector.Onboarding;

/// <summary>Handles <see cref="IniRequest"/>: builds and sends the INI order, then renders the letter.</summary>
internal sealed class IniRequestHandler : IEbicsRequestHandler<IniRequest, IniResult>
{
    private readonly IOnboardingEnvelopeBuilderRegistry _registry;
    private readonly IInitializationLetterRenderer _letterRenderer;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the handler.</summary>
    /// <param name="registry">The version-specific envelope builder registry.</param>
    /// <param name="letterRenderer">The initialization-letter renderer.</param>
    /// <param name="timeProvider">The time source for certificate validity and the letter date.</param>
    public IniRequestHandler(
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
    public async Task<EbicsResult<IniResult>> Handle(IniRequest request, EbicsContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);

        var version = ctx.Connection.Version;
        var builder = _registry.Get(version);
        var signatureVersion = request.SignatureVersion ?? KeyVersions.Default(KeyPurpose.Signature, version).Version;
        KeyVersions.EnsurePermitted(signatureVersion, version);

        var material = await OnboardingSupport.RequireSubscriberKeyAsync(ctx, KeyPurpose.Signature, ct).ConfigureAwait(false);
        var descriptor = OnboardingSupport.Descriptor(ctx, material, signatureVersion, KeyPurpose.Signature, _timeProvider);

        var envelope = builder.BuildIniRequest(OnboardingSupport.Header(ctx), descriptor);
        var responseXml = await OnboardingSupport.ExchangeAsync(envelope, ctx, ct).ConfigureAwait(false);
        var view = builder.ParseResponse(responseXml);
        if (view.ReturnCode != EbicsResult<IniResult>.OkReturnCode)
        {
            return EbicsResult<IniResult>.Failure(view.ReturnCode, view.ReportText);
        }

        var digest = PublicKeyFingerprint.Compute(material);
        var digestText = PublicKeyFingerprint.ToLetterFormat(digest);

        InitializationLetter? letter = null;
        if (request.IncludeLetter)
        {
            letter = _letterRenderer.Render(new InitializationLetterModel
            {
                Kind = LetterKind.Ini,
                HostId = ctx.Connection.HostId.Value,
                PartnerId = ctx.Connection.PartnerId.Value,
                UserId = ctx.Connection.UserId.Value,
                VersionCode = ctx.Version.Code,
                CreatedAt = _timeProvider.GetUtcNow(),
                Keys = [new LetterKeyEntry(KeyPurpose.Signature, signatureVersion.Value, digestText)],
            });
        }

        return EbicsResult<IniResult>.Success(new IniResult
        {
            SignatureKeyVersion = signatureVersion.Value,
            SignatureKeyDigest = digest,
            SignatureKeyDigestText = digestText,
            Letter = letter,
        });
    }
}
