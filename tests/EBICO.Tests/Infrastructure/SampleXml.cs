using System.Diagnostics.CodeAnalysis;
using EBICO.Core;

namespace EBICO.Tests.Infrastructure;

/// <summary>Direction of an EBICS sample-XML fixture relative to the bank server.</summary>
public enum SampleDirection
{
    /// <summary>Client → server (request).</summary>
    Request,

    /// <summary>Server → client (response).</summary>
    Response,
}

/// <summary>
/// Resolves EBICS sample-XML fixtures under
/// <c>tests/EBICO.Tests/Fixtures/Xml/&lt;version&gt;/&lt;direction&gt;/</c>.
/// <para>
/// The official EBICS examples (ebics.org) are proprietary and are <b>not</b>
/// committed (see the fixture README and <c>.gitignore</c>). Tests that need a
/// real example should use <see cref="TryLoad"/> and skip gracefully when the
/// file is absent, so the suite stays green in environments without the
/// examples (e.g. CI).
/// </para>
/// </summary>
public static class SampleXml
{
    /// <summary>Root directory of the XML fixtures, resolved next to the test assembly.</summary>
    public static string XmlFixturesRoot { get; } =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Xml");

    /// <summary>Builds the expected path for a sample without checking existence.</summary>
    public static string PathFor(EbicsVersion version, SampleDirection direction, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Path.Combine(
            XmlFixturesRoot,
            version.ToString(),
            direction.ToString().ToLowerInvariant(),
            fileName);
    }

    /// <summary>
    /// Attempts to load a sample. Returns <c>false</c> (and <c>null</c>) when the
    /// fixture is not present locally.
    /// </summary>
    public static bool TryLoad(
        EbicsVersion version,
        SampleDirection direction,
        string fileName,
        [NotNullWhen(true)] out string? xml)
    {
        var path = PathFor(version, direction, fileName);
        if (File.Exists(path))
        {
            xml = File.ReadAllText(path);
            return true;
        }

        xml = null;
        return false;
    }

    /// <summary>Loads a sample or throws if it is not present.</summary>
    /// <exception cref="FileNotFoundException">The fixture is not present locally.</exception>
    public static string Load(EbicsVersion version, SampleDirection direction, string fileName)
    {
        if (TryLoad(version, direction, fileName, out var xml))
        {
            return xml;
        }

        throw new FileNotFoundException(
            $"Sample-XML fixture not found. The official EBICS examples are proprietary and " +
            $"are not committed — see {Path.Combine("Fixtures", "Xml", "README.md")}.",
            PathFor(version, direction, fileName));
    }
}
