using System.Xml;
using System.Xml.Schema;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Tests.E2E;
using EBICO.Tests.Infrastructure;
using EBICO.Tests.Server;

namespace EBICO.Tests.Conformance;

/// <summary>
/// Conformance (issue #59), <b>Tier B</b> schema-validation layer: validates the request XML EBICO
/// <em>emits</em> against the official EBICS XSDs. The schemas are proprietary and <c>.gitignore</c>d, so
/// this test is <b>skip-if-missing</b> — it runs only on a checkout that fetched the schemas locally
/// (<c>scripts/fetch-schemas.sh</c>) and skips (keeping CI green) otherwise.
/// </summary>
/// <remarks>
/// It is a local diagnostic, not a CI gate. A schema set that fails to <em>compile</em> is treated as a
/// local setup problem and skips; only an actual validation error against a compiled schema fails the
/// test — that would be a genuine, reportable conformance deviation.
/// </remarks>
public class SchemaValidationConformanceTests
{
    /// <summary>The EBICS versions covered by the schema-validation matrix.</summary>
    public static TheoryData<EbicsVersion> Versions => [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(Versions))]
    public void GeneratedUnsecuredRequest_ValidatesAgainstOfficialXsd(EbicsVersion version)
    {
        if (!TryLoadSchemaSet(version, out var schemas, out var skipReason))
        {
            Assert.Skip(skipReason);
        }

        var xml = BuildRepresentativeIniRequest(version);
        var errors = Validate(xml, schemas);

        errors.Should().BeEmpty(
            $"EBICO's generated {version} ebicsUnsecuredRequest should validate against the official XSDs");
    }

    private static string BuildRepresentativeIniRequest(EbicsVersion version)
    {
        if (version == EbicsVersion.H005)
        {
            using var certificate = TestCertificates.CreateSelfSigned("CN=EBICO Conformance");
            return ServerTestHelpers.BuildUnsecuredIniRequest(
                version, "SCHEMAHOST", "SCHEMAPART", "SCHEMAUSER", certificate: certificate);
        }

        return ServerTestHelpers.BuildUnsecuredIniRequest(
            version, "SCHEMAHOST", "SCHEMAPART", "SCHEMAUSER",
            rsaKey: E2EKeyPool.Subscriber(KeyPurpose.Signature));
    }

    private static IReadOnlyList<string> Validate(string xml, XmlSchemaSet schemas)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas };
        settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
        settings.ValidationEventHandler += (_, e) =>
        {
            if (e.Severity == XmlSeverityType.Error)
            {
                errors.Add(e.Message);
            }
        };

        using var reader = XmlReader.Create(new StringReader(xml), settings);
        while (reader.Read())
        {
            // Drive the reader to completion so every node is validated.
        }

        return errors;
    }

    // Locates schemas/<version>/ by walking up from the test assembly, and compiles every .xsd there into
    // one set. Returns false (with a skip reason) when the schemas are absent or the set does not compile —
    // both are local-setup conditions, not conformance failures.
    private static bool TryLoadSchemaSet(EbicsVersion version, out XmlSchemaSet schemas, out string skipReason)
    {
        schemas = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };

        var dir = LocateSchemaDirectory(version);
        if (dir is null)
        {
            skipReason = $"No local EBICS XSDs found under schemas/{version}/ — see scripts/fetch-schemas.sh.";
            return false;
        }

        var xsds = Directory.EnumerateFiles(dir, "*.xsd").ToList();
        if (xsds.Count == 0)
        {
            skipReason = $"schemas/{version}/ exists but contains no .xsd files.";
            return false;
        }

        try
        {
            foreach (var xsd in xsds)
            {
                schemas.Add(targetNamespace: null, xsd);
            }

            schemas.Compile();
        }
        catch (XmlSchemaException ex)
        {
            skipReason = $"Local schema set for {version} did not compile ({ex.Message}); treated as a setup issue.";
            return false;
        }

        skipReason = string.Empty;
        return true;
    }

    private static string? LocateSchemaDirectory(EbicsVersion version)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "schemas", version.ToString());
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
