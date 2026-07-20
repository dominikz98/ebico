using System.Diagnostics.CodeAnalysis;
using EBICO.Core;

namespace EBICO.Tests.Conformance;

/// <summary>Direction of a vendor-capture fixture relative to the bank server.</summary>
internal enum VendorDirection
{
    /// <summary>Client → server (request).</summary>
    Request,

    /// <summary>Server → client (response).</summary>
    Response,
}

/// <summary>
/// Resolves committed <em>vendor captures</em> — request/response XML produced by a real third-party
/// EBICS client — under <c>tests/EBICO.Tests/Conformance/Vendor/&lt;client&gt;/&lt;version&gt;/&lt;direction&gt;/</c>.
/// <para>
/// Unlike the proprietary EBICS examples resolved by <see cref="Infrastructure.SampleXml"/> (which are
/// <c>.gitignore</c>d and therefore skip-if-missing), a permissively-licensed OSS client's <em>output</em>
/// is neither EBICS-SC property nor a derivative of the client's licence, so these captures <b>are</b>
/// committed and this path is <b>not</b> ignored — the replay tests run permanently in CI. The loader
/// still degrades gracefully (returns an empty list) when no captures are present, so a fresh checkout or
/// a partial corpus keeps the suite green. See <c>docs/development/conformance-real-clients.md</c> and
/// <c>docs/adr/0026-konformitaet-gegen-reale-clients.md</c>.
/// </para>
/// </summary>
internal static class VendorCaptureCorpus
{
    /// <summary>Root directory of the vendor corpus, resolved next to the test assembly.</summary>
    public static string CorpusRoot { get; } =
        Path.Combine(AppContext.BaseDirectory, "Conformance", "Vendor");

    /// <summary>Builds the expected path for a capture without checking existence.</summary>
    /// <param name="client">The client (corpus sub-directory name).</param>
    /// <param name="version">The EBICS version.</param>
    /// <param name="direction">The message direction.</param>
    /// <param name="fileName">The capture file name.</param>
    /// <returns>The absolute path the capture would live at.</returns>
    public static string PathFor(string client, EbicsVersion version, VendorDirection direction, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Path.Combine(
            CorpusRoot, client, version.ToString(), direction.ToString().ToLowerInvariant(), fileName);
    }

    /// <summary>Attempts to load a capture; returns <c>false</c> (and <c>null</c>) when it is absent.</summary>
    /// <param name="client">The client (corpus sub-directory name).</param>
    /// <param name="version">The EBICS version.</param>
    /// <param name="direction">The message direction.</param>
    /// <param name="fileName">The capture file name.</param>
    /// <param name="xml">The loaded XML, or <see langword="null"/> when absent.</param>
    /// <returns><see langword="true"/> when the capture was loaded.</returns>
    public static bool TryLoad(
        string client,
        EbicsVersion version,
        VendorDirection direction,
        string fileName,
        [NotNullWhen(true)] out string? xml)
    {
        var path = PathFor(client, version, direction, fileName);
        if (File.Exists(path))
        {
            xml = File.ReadAllText(path);
            return true;
        }

        xml = null;
        return false;
    }
}
