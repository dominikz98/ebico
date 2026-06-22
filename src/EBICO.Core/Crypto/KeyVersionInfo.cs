namespace EBICO.Core.Crypto;

/// <summary>
/// Immutable metadata describing one known EBICS key version: its typed
/// <see cref="KeyVersion"/>, the <see cref="KeyPurpose"/> it serves, whether it is a legacy
/// version, the RSA scheme it implies (as inert metadata), and the set of EBICS protocol
/// versions that permit it.
/// </summary>
/// <remarks>
/// Instances are created by and obtained from <see cref="KeyVersions"/>, the single source
/// of truth — mirroring how <c>EbicsVersionInfo</c> is owned by <c>EbicsVersions</c>.
/// </remarks>
public sealed class KeyVersionInfo
{
    internal KeyVersionInfo(
        KeyVersion version,
        KeyPurpose purpose,
        bool isLegacy,
        RsaPaddingScheme paddingIntent,
        IReadOnlyList<EbicsVersion> permittedIn)
    {
        Version = version;
        Purpose = purpose;
        IsLegacy = isLegacy;
        PaddingIntent = paddingIntent;
        PermittedIn = permittedIn;
    }

    /// <summary>The typed key-version code this metadata describes.</summary>
    public KeyVersion Version { get; }

    /// <summary>The purpose (signature/encryption/authentication) of this key version.</summary>
    public KeyPurpose Purpose { get; }

    /// <summary>The four-character code, e.g. <c>"A005"</c> (shorthand for <c>Version.Value</c>).</summary>
    public string Code => Version.Value;

    /// <summary>
    /// Whether this is a legacy version (A004/E001/X001) superseded by a current one. Legacy
    /// versions are retained for older protocol versions only.
    /// </summary>
    public bool IsLegacy { get; }

    /// <summary>
    /// The RSA scheme this version implies. Descriptive metadata only — no cryptographic
    /// operation is performed in the key layer.
    /// </summary>
    public RsaPaddingScheme PaddingIntent { get; }

    /// <summary>The EBICS protocol versions that permit this key version.</summary>
    public IReadOnlyList<EbicsVersion> PermittedIn { get; }

    /// <summary>
    /// Indicates whether this key version may be used with the given protocol version.
    /// </summary>
    /// <param name="ebicsVersion">The protocol version to test.</param>
    /// <returns><see langword="true"/> when <paramref name="ebicsVersion"/> is in <see cref="PermittedIn"/>.</returns>
    public bool IsPermittedIn(EbicsVersion ebicsVersion)
    {
        for (var i = 0; i < PermittedIn.Count; i++)
        {
            if (PermittedIn[i] == ebicsVersion)
            {
                return true;
            }
        }

        return false;
    }
}
