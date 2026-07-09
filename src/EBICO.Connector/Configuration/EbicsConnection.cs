using EBICO.Core;
using EBICO.Core.Domain;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Configuration;

/// <summary>
/// The resolved, validated and immutable form of <see cref="EbicsConnectionOptions"/>. Raw
/// option strings have been parsed into the strongly typed <c>EBICO.Core</c> identifiers and the
/// target version has been bound to its <see cref="EbicsVersionInfo"/> metadata.
/// </summary>
public sealed class EbicsConnection
{
    private EbicsConnection(
        Uri url,
        HostId hostId,
        PartnerId partnerId,
        UserId userId,
        EbicsVersion version,
        EbicsVersionInfo versionInfo)
    {
        Url = url;
        HostId = hostId;
        PartnerId = partnerId;
        UserId = userId;
        Version = version;
        VersionInfo = versionInfo;
    }

    /// <summary>The absolute EBICS server endpoint URL.</summary>
    public Uri Url { get; }

    /// <summary>The validated EBICS host identifier.</summary>
    public HostId HostId { get; }

    /// <summary>The validated EBICS partner identifier.</summary>
    public PartnerId PartnerId { get; }

    /// <summary>The validated EBICS user identifier.</summary>
    public UserId UserId { get; }

    /// <summary>The target EBICS protocol version.</summary>
    public EbicsVersion Version { get; }

    /// <summary>The metadata (code, namespace, envelope types) of the target <see cref="Version"/>.</summary>
    public EbicsVersionInfo VersionInfo { get; }

    /// <summary>
    /// Validates <paramref name="options"/> and builds the immutable connection. This is the
    /// single authoritative construction path; <see cref="EbicsConnectionOptionsValidator"/>
    /// reports the same problems ahead of time via the options pipeline.
    /// </summary>
    /// <param name="options">The raw connection options.</param>
    /// <returns>The resolved connection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="EbicsConfigurationException">One or more options are missing or invalid.</exception>
    public static EbicsConnection FromOptions(EbicsConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = Validate(options);
        if (errors.Count > 0)
        {
            throw new EbicsConfigurationException(
                "Invalid EBICS connection configuration: " + string.Join("; ", errors) + ".");
        }

        var url = new Uri(options.Url!, UriKind.Absolute);
        var hostId = HostId.Create(options.HostId!);
        var partnerId = PartnerId.Create(options.PartnerId!);
        var userId = UserId.Create(options.UserId!);
        var versionInfo = EbicsVersions.Get(options.Version);

        return new EbicsConnection(url, hostId, partnerId, userId, options.Version, versionInfo);
    }

    /// <summary>
    /// Collects all validation problems in <paramref name="options"/>. Returns an empty list when
    /// the options are valid.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    /// <returns>The list of human-readable validation errors.</returns>
    internal static IReadOnlyList<string> Validate(EbicsConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Url))
        {
            errors.Add($"{nameof(EbicsConnectionOptions.Url)} is required");
        }
        else if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"{nameof(EbicsConnectionOptions.Url)} must be an absolute http/https URL");
        }

        if (!HostId.TryCreate(options.HostId, out _))
        {
            errors.Add($"{nameof(EbicsConnectionOptions.HostId)} is missing or not a valid EBICS identifier");
        }

        if (!PartnerId.TryCreate(options.PartnerId, out _))
        {
            errors.Add($"{nameof(EbicsConnectionOptions.PartnerId)} is missing or not a valid EBICS identifier");
        }

        if (!UserId.TryCreate(options.UserId, out _))
        {
            errors.Add($"{nameof(EbicsConnectionOptions.UserId)} is missing or not a valid EBICS identifier");
        }

        if (!Enum.IsDefined(options.Version))
        {
            errors.Add($"{nameof(EbicsConnectionOptions.Version)} '{options.Version}' is not a supported EBICS version");
        }

        return errors;
    }
}
