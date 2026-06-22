using System.Diagnostics.CodeAnalysis;

namespace EBICO.Core.Crypto;

/// <summary>
/// The single source of truth for the known EBICS key versions (A004/A005/A006,
/// E001/E002, X001/X002), their metadata, and the rule of which version is permitted with
/// which EBICS protocol version. Mirrors the <c>EbicsVersions</c> registry style.
/// </summary>
/// <remarks>
/// The per-protocol-version permission table reflects the common reading (legacy A004/E001/X001
/// retired in EBICS 3.0/H005, the PSS signature version A006 introduced with H005). Per the
/// project's conventions this is to be verified against the official EBICS XSDs/Annexe once
/// available, and adjusted here in one place if needed.
/// </remarks>
public static class KeyVersions
{
    private static readonly EbicsVersion[] H003H004 = [EbicsVersion.H003, EbicsVersion.H004];
    private static readonly EbicsVersion[] AllProtocolVersions = [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];
    private static readonly EbicsVersion[] H005Only = [EbicsVersion.H005];

    private static readonly KeyVersionInfo A004Info = new(
        KeyVersion.Create("A004"), KeyPurpose.Signature, isLegacy: true, RsaPaddingScheme.Pkcs1V15, H003H004);

    private static readonly KeyVersionInfo A005Info = new(
        KeyVersion.Create("A005"), KeyPurpose.Signature, isLegacy: false, RsaPaddingScheme.Pkcs1V15, AllProtocolVersions);

    private static readonly KeyVersionInfo A006Info = new(
        KeyVersion.Create("A006"), KeyPurpose.Signature, isLegacy: false, RsaPaddingScheme.Pss, H005Only);

    private static readonly KeyVersionInfo E001Info = new(
        KeyVersion.Create("E001"), KeyPurpose.Encryption, isLegacy: true, RsaPaddingScheme.Pkcs1V15Encryption, H003H004);

    private static readonly KeyVersionInfo E002Info = new(
        KeyVersion.Create("E002"), KeyPurpose.Encryption, isLegacy: false, RsaPaddingScheme.Oaep, AllProtocolVersions);

    private static readonly KeyVersionInfo X001Info = new(
        KeyVersion.Create("X001"), KeyPurpose.Authentication, isLegacy: true, RsaPaddingScheme.Pkcs1V15, H003H004);

    private static readonly KeyVersionInfo X002Info = new(
        KeyVersion.Create("X002"), KeyPurpose.Authentication, isLegacy: false, RsaPaddingScheme.Pkcs1V15, AllProtocolVersions);

    private static readonly KeyVersionInfo[] AllInfos =
        [A004Info, A005Info, A006Info, E001Info, E002Info, X001Info, X002Info];

    /// <summary>All known key versions, grouped by purpose (A*, then E*, then X*).</summary>
    public static IReadOnlyList<KeyVersionInfo> All => AllInfos;

    /// <summary>Returns the metadata for a known key version.</summary>
    /// <param name="version">The key version.</param>
    /// <returns>The matching <see cref="KeyVersionInfo"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="version"/> is well-formed but not a known version (or is <c>default</c>).
    /// </exception>
    public static KeyVersionInfo Get(KeyVersion version) => version.Value switch
    {
        "A004" => A004Info,
        "A005" => A005Info,
        "A006" => A006Info,
        "E001" => E001Info,
        "E002" => E002Info,
        "X001" => X001Info,
        "X002" => X002Info,
        _ => throw new ArgumentOutOfRangeException(nameof(version), version.Value, "Unknown EBICS key version."),
    };

    /// <summary>Tries to resolve a key version to its metadata without throwing.</summary>
    /// <param name="version">The key version.</param>
    /// <param name="info">The metadata when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="version"/> is a known version.</returns>
    public static bool TryGet(KeyVersion version, [NotNullWhen(true)] out KeyVersionInfo? info)
        => TryFromCode(version.Value, out info);

    /// <summary>Tries to resolve a raw key-version code (e.g. <c>"A005"</c>) to its metadata.</summary>
    /// <param name="code">The code to resolve.</param>
    /// <param name="info">The metadata when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="code"/> is a known version code.</returns>
    public static bool TryFromCode(string? code, [NotNullWhen(true)] out KeyVersionInfo? info)
    {
        foreach (var candidate in AllInfos)
        {
            if (string.Equals(candidate.Code, code, StringComparison.Ordinal))
            {
                info = candidate;
                return true;
            }
        }

        info = null;
        return false;
    }

    /// <summary>Returns all known versions for a purpose, e.g. signature → A004/A005/A006.</summary>
    /// <param name="purpose">The key purpose.</param>
    /// <returns>The matching versions, oldest first.</returns>
    public static IReadOnlyList<KeyVersionInfo> ForPurpose(KeyPurpose purpose)
    {
        var result = new List<KeyVersionInfo>(AllInfos.Length);
        foreach (var info in AllInfos)
        {
            if (info.Purpose == purpose)
            {
                result.Add(info);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the key version EBICO uses by default for the given purpose and protocol version
    /// (A005 / E002 / X002; the PSS signature version A006 is opt-in, not the default).
    /// </summary>
    /// <param name="purpose">The key purpose.</param>
    /// <param name="ebicsVersion">The target protocol version.</param>
    /// <returns>The default <see cref="KeyVersionInfo"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="purpose"/> is not a defined value.</exception>
    /// <exception cref="KeyVersionNotPermittedException">
    /// The default version for <paramref name="purpose"/> is not permitted for <paramref name="ebicsVersion"/>.
    /// </exception>
    public static KeyVersionInfo Default(KeyPurpose purpose, EbicsVersion ebicsVersion)
    {
        var info = purpose switch
        {
            KeyPurpose.Signature => A005Info,
            KeyPurpose.Encryption => E002Info,
            KeyPurpose.Authentication => X002Info,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown key purpose."),
        };

        if (!info.IsPermittedIn(ebicsVersion))
        {
            throw new KeyVersionNotPermittedException(
                $"Default {purpose} key version {info.Code} is not permitted for EBICS version {ebicsVersion}.");
        }

        return info;
    }

    /// <summary>Indicates whether a key version may be used with a protocol version.</summary>
    /// <param name="keyVersion">The key version.</param>
    /// <param name="ebicsVersion">The protocol version.</param>
    /// <returns><see langword="true"/> when known and permitted.</returns>
    public static bool IsPermitted(KeyVersion keyVersion, EbicsVersion ebicsVersion)
        => TryGet(keyVersion, out var info) && info.IsPermittedIn(ebicsVersion);

    /// <summary>Resolves a key version and asserts it is permitted for a protocol version.</summary>
    /// <param name="keyVersion">The key version.</param>
    /// <param name="ebicsVersion">The protocol version.</param>
    /// <returns>The resolved metadata.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="keyVersion"/> is not a known version.</exception>
    /// <exception cref="KeyVersionNotPermittedException"><paramref name="keyVersion"/> is not permitted for <paramref name="ebicsVersion"/>.</exception>
    public static KeyVersionInfo EnsurePermitted(KeyVersion keyVersion, EbicsVersion ebicsVersion)
    {
        var info = Get(keyVersion);
        if (!info.IsPermittedIn(ebicsVersion))
        {
            throw new KeyVersionNotPermittedException(
                $"Key version {info.Code} is not permitted for EBICS version {ebicsVersion}.");
        }

        return info;
    }

    /// <summary>Returns the key versions of a given purpose that are permitted for a protocol version.</summary>
    /// <param name="purpose">The key purpose.</param>
    /// <param name="ebicsVersion">The protocol version.</param>
    /// <returns>The permitted versions, oldest first.</returns>
    public static IReadOnlyList<KeyVersionInfo> PermittedFor(KeyPurpose purpose, EbicsVersion ebicsVersion)
    {
        var result = new List<KeyVersionInfo>(AllInfos.Length);
        foreach (var info in AllInfos)
        {
            if (info.Purpose == purpose && info.IsPermittedIn(ebicsVersion))
            {
                result.Add(info);
            }
        }

        return result;
    }
}
