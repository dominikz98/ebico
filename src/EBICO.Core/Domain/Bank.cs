namespace EBICO.Core.Domain;

/// <summary>
/// A bank / credit institution exposed as an EBICS server endpoint, identified by its
/// <see cref="HostId"/>. This is a lightweight identity-and-metadata aggregate; keys,
/// certificates and onboarding state are added by the server layer (M3).
/// </summary>
public sealed class Bank
{
    /// <summary>Creates a bank.</summary>
    /// <param name="hostId">The bank's EBICS host identifier.</param>
    /// <param name="name">Optional human-readable name (surfaced as the <c>Institute</c> by HPD).</param>
    /// <param name="supportedVersions">
    /// The EBICS versions this host offers; defaults to all supported versions when
    /// <see langword="null"/>. Duplicates are collapsed.
    /// </param>
    /// <param name="url">Optional EBICS endpoint URL of the bank (surfaced by HPD as the access URL).</param>
    public Bank(HostId hostId, string? name = null, IEnumerable<EbicsVersion>? supportedVersions = null, string? url = null)
    {
        HostId = hostId;
        Name = name;
        SupportedVersions = (supportedVersions ?? Enum.GetValues<EbicsVersion>()).Distinct().ToArray();
        Url = url;
    }

    /// <summary>The bank's EBICS host identifier (<c>HostID</c>).</summary>
    public HostId HostId { get; }

    /// <summary>Optional human-readable name of the bank (surfaced as the <c>Institute</c> by HPD).</summary>
    public string? Name { get; }

    /// <summary>The EBICS protocol versions this host supports.</summary>
    public IReadOnlyCollection<EbicsVersion> SupportedVersions { get; }

    /// <summary>The bank's optional EBICS endpoint URL (surfaced by HPD as the access URL), or <see langword="null"/>.</summary>
    public string? Url { get; }
}
