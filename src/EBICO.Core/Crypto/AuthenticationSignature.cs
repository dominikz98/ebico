using System.Security.Cryptography;
using System.Xml;
using System.Xml.Serialization;
using EBICO.Core.Schema.XmlDsig;
using EBICO.Core.Serialization;

namespace EBICO.Core.Crypto;

/// <summary>
/// Computes and verifies the EBICS authentication signature (<c>AuthSignature</c>) over an EBICS
/// request — the XML Digital Signature (<c>ds:Signature</c>) that protects every element carrying
/// the <c>authenticate="true"</c> attribute. Signature key version <b>X002</b> (RSASSA-PKCS1-v1_5
/// over SHA-256, inclusive Canonical XML 1.0). Stateless BCL wrappers
/// (<see href="../adr/0008-krypto-bibliothek.md">ADR-0008</see>) that build on the issue #18 key
/// layer (<see cref="RsaKeyMaterial"/>, <see cref="KeyVersions"/>) and the issue #15 canonicalizer
/// (<see cref="XmlCanonicalizer"/>).
/// </summary>
/// <remarks>
/// <para>
/// The signature has two hashes. The <c>ds:Reference</c> digest is SHA-256 over the C14N of the
/// authenticated node-set (the <c>authenticate="true"</c> subtrees). The <c>ds:SignatureValue</c>
/// is the RSA signature over the C14N of the <c>ds:SignedInfo</c> element. Both canonicalizations
/// run <b>in document context</b>: the signed material inherits the enveloping request's namespace
/// declarations (default protocol namespace and the <c>ds</c> prefix), exactly as a counterparty
/// produces and expects them. The padding scheme is taken from the <see cref="KeyVersions"/>
/// registry (<see cref="KeyVersionInfo.PaddingIntent"/>), never hard-coded — X001/X002 map to
/// <see cref="RSASignaturePadding.Pkcs1"/>.
/// </para>
/// <para>
/// This layer is deliberately policy-free: it does <b>not</b> check whether a key version is
/// permitted for a given EBICS protocol version — that is <see cref="KeyVersions.EnsurePermitted"/>'s
/// job. It only rejects versions that are not a known <see cref="KeyPurpose.Authentication"/>
/// version. Like <see cref="BankSignature.Verify"/>, verification returns <see langword="false"/>
/// (rather than throwing) for any malformed or invalid signature, so a bad client signature is a
/// clean rejection on the server.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> the exact canonicalization method (inclusive vs exclusive), the
/// reference selector (the <c>#xpointer(//*[@authenticate='true'])</c> URI and its XPath
/// realization) and the SignedInfo canonicalization context are EBICS spec details not yet verified
/// against the official Annexe (the XSDs are proprietary and not in the repo — see <c>CLAUDE.md</c>
/// and <c>docs/protocol/serialization-c14n.md</c>). They are confined to the constants and the
/// <c>c14n</c> parameter; self-consistent sign &#8594; verify round-trips and the deterministic
/// known-answer vector hold regardless of that choice.
/// </para>
/// </remarks>
public static class AuthenticationSignature
{
    /// <summary>The hash algorithm used for the EBICS authentication signature (SHA-256).</summary>
    public static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// The <c>ds:SignatureMethod</c> algorithm URI: RSASSA-PKCS1-v1_5 over SHA-256
    /// (<c>http://www.w3.org/2001/04/xmldsig-more#rsa-sha256</c>).
    /// </summary>
    public const string SignatureMethodAlgorithm = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";

    /// <summary>
    /// The <c>ds:DigestMethod</c> algorithm URI: SHA-256
    /// (<c>http://www.w3.org/2001/04/xmlenc#sha256</c>).
    /// </summary>
    public const string DigestMethodAlgorithm = "http://www.w3.org/2001/04/xmlenc#sha256";

    /// <summary>
    /// The <c>ds:Reference/@URI</c> selecting the authenticated node-set
    /// (<c>#xpointer(//*[@authenticate='true'])</c>).
    /// </summary>
    public const string AuthenticatedNodesReferenceUri = "#xpointer(//*[@authenticate='true'])";

    private const string XmlDsigNamespace = "http://www.w3.org/2000/09/xmldsig#";

    // The XPath that realizes the reference URI as a C14N node-set: every node and attribute that
    // is (or is inside) an element with authenticate="true" — i.e. the full authenticated subtrees.
    // The naive "//*[@authenticate='true']" would omit the descendants and yield empty elements.
    private const string AuthenticatedNodesXPath = "(//. | //@*)[ancestor-or-self::*[@authenticate='true']]";

    // The node-set of a single element's subtree (element, descendants and their attributes),
    // used to canonicalize the assembled SignedInfo in document context.
    private const string SubtreeXPath = "descendant-or-self::node() | descendant-or-self::*/@*";

    private static readonly XmlSerializer SignedInfoSerializer = new(typeof(SignedInfoType));

    /// <summary>
    /// Signs an EBICS request: computes the <c>ds:Reference</c> digest over the authenticated
    /// node-set, assembles the <c>ds:SignedInfo</c>, and signs its canonical form with the X002
    /// authentication key.
    /// </summary>
    /// <param name="requestXml">
    /// The serialized EBICS request. The <c>AuthSignature</c> element may be absent or empty — it is
    /// not itself authenticated and does not affect the digest.
    /// </param>
    /// <param name="key">The signer's key material; must contain a private key.</param>
    /// <param name="version">The authentication key version (must resolve to a known X00x version).</param>
    /// <param name="c14n">The canonicalization variant. Defaults to <see cref="C14nMode.Inclusive"/>.</param>
    /// <returns>The populated <see cref="SignatureType"/> to assign to the request's <c>AuthSignature</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestXml"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyMaterialException"><paramref name="key"/> has no private key.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="version"/> is not a known authentication version.</exception>
    /// <exception cref="XmlException"><paramref name="requestXml"/> is not well-formed, or contains a prohibited <c>&lt;!DOCTYPE&gt;</c>.</exception>
    public static SignatureType Sign(
        string requestXml, RsaKeyMaterial key, KeyVersion version, C14nMode c14n = C14nMode.Inclusive)
    {
        ArgumentNullException.ThrowIfNull(requestXml);
        ArgumentNullException.ThrowIfNull(key);
        if (!key.HasPrivateKey)
        {
            throw new KeyMaterialException("Cannot sign: the key material has no private key.");
        }

        var padding = ResolveSignaturePadding(version);

        var document = LoadRequest(requestXml);
        var digest = ComputeAuthenticatedDigest(document, c14n);
        var signedInfo = BuildSignedInfo(digest, c14n);
        var signedInfoC14n = CanonicalizeSignedInfoInContext(document, signedInfo, c14n);

        using var rsa = key.CreateRsa();
        var signature = rsa.SignData(signedInfoC14n, HashAlgorithm, padding);

        return new SignatureType
        {
            SignedInfo = signedInfo,
            SignatureValue = new SignatureValueType { Value = signature },
        };
    }

    /// <summary>
    /// Verifies an EBICS authentication signature against the request it was produced over. Bad
    /// signatures (wrong key, tampered request/signature, malformed structure, unsupported
    /// algorithm) yield <see langword="false"/> rather than an exception.
    /// </summary>
    /// <param name="requestXml">The serialized EBICS request that was signed.</param>
    /// <param name="authSignature">The <c>AuthSignature</c> to verify.</param>
    /// <param name="key">The signer's public key material (a private key is not required).</param>
    /// <param name="version">The authentication key version (must resolve to a known X00x version).</param>
    /// <returns><see langword="true"/> when the signature is valid; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="version"/> is not a known authentication version.</exception>
    public static bool Verify(
        string requestXml, SignatureType authSignature, RsaKeyMaterial key, KeyVersion version)
    {
        ArgumentNullException.ThrowIfNull(requestXml);
        ArgumentNullException.ThrowIfNull(authSignature);
        ArgumentNullException.ThrowIfNull(key);

        var padding = ResolveSignaturePadding(version);

        var signedInfo = authSignature.SignedInfo;
        if (signedInfo?.CanonicalizationMethod?.Algorithm is null
            || signedInfo.SignatureMethod?.Algorithm is null
            || signedInfo.Reference.Count != 1)
        {
            return false;
        }

        var reference = signedInfo.Reference[0];
        if (reference.DigestValue is null
            || reference.DigestMethod?.Algorithm is null
            || reference.Transforms.Count != 1
            || reference.Transforms[0].Algorithm is null
            || authSignature.SignatureValue?.Value is null)
        {
            return false;
        }

        // We only implement rsa-sha256 over sha256 — reject anything else as an unverifiable signature.
        if (!string.Equals(signedInfo.SignatureMethod.Algorithm, SignatureMethodAlgorithm, StringComparison.Ordinal)
            || !string.Equals(reference.DigestMethod.Algorithm, DigestMethodAlgorithm, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var referenceMode = C14nAlgorithms.FromAlgorithmUri(reference.Transforms[0].Algorithm!);
            var signedInfoMode = C14nAlgorithms.FromAlgorithmUri(signedInfo.CanonicalizationMethod.Algorithm);
            var document = LoadRequest(requestXml);

            var computedDigest = ComputeAuthenticatedDigest(document, referenceMode);
            if (!CryptographicOperations.FixedTimeEquals(computedDigest, reference.DigestValue))
            {
                return false;
            }

            var signedInfoC14n = CanonicalizeSignedInfoInContext(document, signedInfo, signedInfoMode);
            using var rsa = key.CreateRsa();
            return rsa.VerifyData(signedInfoC14n, authSignature.SignatureValue.Value, HashAlgorithm, padding);
        }
        catch (Exception ex) when (ex is XmlException or FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Computes the <c>ds:Reference</c> digest: SHA-256 over the canonical form of the authenticated
    /// node-set (the <c>authenticate="true"</c> subtrees), canonicalized in document context.
    /// </summary>
    private static byte[] ComputeAuthenticatedDigest(XmlDocument document, C14nMode mode)
    {
        var nodes = document.SelectNodes(AuthenticatedNodesXPath)!;
        var canonical = XmlCanonicalizer.Canonicalize(nodes, mode);
        return SHA256.HashData(canonical);
    }

    /// <summary>Builds the <c>ds:SignedInfo</c> for the given reference digest and C14N variant.</summary>
    private static SignedInfoType BuildSignedInfo(byte[] digest, C14nMode c14n)
    {
        var algorithmUri = C14nAlgorithms.ToAlgorithmUri(c14n);

        var reference = new ReferenceType
        {
            Uri = AuthenticatedNodesReferenceUri,
            DigestMethod = new DigestMethodType { Algorithm = DigestMethodAlgorithm },
            DigestValue = digest,
        };
        reference.Transforms.Add(new TransformType { Algorithm = algorithmUri });

        var signedInfo = new SignedInfoType
        {
            CanonicalizationMethod = new CanonicalizationMethodType { Algorithm = algorithmUri },
            SignatureMethod = new SignatureMethodType { Algorithm = SignatureMethodAlgorithm },
        };
        signedInfo.Reference.Add(reference);
        return signedInfo;
    }

    /// <summary>
    /// Canonicalizes <paramref name="signedInfo"/> in the request's namespace context: it is
    /// serialized with only the <c>ds</c> prefix, imported into a clone of the request document
    /// (under the root, after removing any existing <c>AuthSignature</c>), and its subtree is
    /// canonicalized so it inherits the request's in-scope namespaces — the octets a counterparty
    /// signs and verifies. Sign and verify share this seam, so round-trips stay symmetric.
    /// </summary>
    private static byte[] CanonicalizeSignedInfoInContext(
        XmlDocument requestDocument, SignedInfoType signedInfo, C14nMode mode)
    {
        var working = (XmlDocument)requestDocument.CloneNode(deep: true);
        working.PreserveWhitespace = true;
        RemoveAuthSignature(working);

        var fragmentDocument = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using (var reader = XmlReader.Create(
            new StringReader(SerializeSignedInfoFragment(signedInfo)), CreateReaderSettings()))
        {
            fragmentDocument.Load(reader);
        }

        var imported = (XmlElement)working.ImportNode(fragmentDocument.DocumentElement!, deep: true);
        working.DocumentElement!.AppendChild(imported);

        var subtree = imported.SelectNodes(SubtreeXPath)!;
        return XmlCanonicalizer.Canonicalize(subtree, mode);
    }

    /// <summary>Serializes a <see cref="SignedInfoType"/> to a fragment declaring only the <c>ds</c> prefix.</summary>
    private static string SerializeSignedInfoFragment(SignedInfoType signedInfo)
    {
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("ds", XmlDsigNamespace);

        using var buffer = new StringWriter();
        using (var writer = XmlWriter.Create(
            buffer, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false }))
        {
            SignedInfoSerializer.Serialize(writer, signedInfo, namespaces);
        }

        return buffer.ToString();
    }

    /// <summary>Removes any <c>AuthSignature</c> child of the root (regardless of namespace).</summary>
    private static void RemoveAuthSignature(XmlDocument document)
    {
        var root = document.DocumentElement;
        var authSignatures = root?.SelectNodes("*[local-name()='AuthSignature']");
        if (authSignatures is null)
        {
            return;
        }

        foreach (XmlNode node in authSignatures)
        {
            root!.RemoveChild(node);
        }
    }

    private static XmlDocument LoadRequest(string requestXml)
    {
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(requestXml), CreateReaderSettings());
        document.Load(reader);
        return document;
    }

    private static XmlReaderSettings CreateReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>
    /// Maps an authentication key version to its BCL RSA padding by consulting the
    /// <see cref="KeyVersions"/> registry — X001/X002 (<see cref="RsaPaddingScheme.Pkcs1V15"/>) →
    /// <see cref="RSASignaturePadding.Pkcs1"/>.
    /// </summary>
    private static RSASignaturePadding ResolveSignaturePadding(KeyVersion version)
    {
        if (!KeyVersions.TryGet(version, out var info) || info.Purpose != KeyPurpose.Authentication)
        {
            throw new InvalidOperationException(
                $"Key version '{version.Value}' is not a known EBICS authentication version (expected X001 or X002).");
        }

        return info.PaddingIntent switch
        {
            RsaPaddingScheme.Pkcs1V15 => RSASignaturePadding.Pkcs1,
            _ => throw new InvalidOperationException(
                $"Authentication version '{version.Value}' implies a non-signature padding scheme ({info.PaddingIntent})."),
        };
    }
}
