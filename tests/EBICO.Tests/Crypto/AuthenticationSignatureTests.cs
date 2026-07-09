using System.Text;
using System.Xml;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Schema.H005;
using EBICO.Core.Schema.XmlDsig;
using EBICO.Core.Serialization;
using EBICO.Tests.Infrastructure;

namespace EBICO.Tests.Crypto;

/// <summary>
/// Tests for <see cref="AuthenticationSignature"/> — the EBICS authentication signature
/// (<c>AuthSignature</c>) sign + verify for X002 (RSASSA-PKCS1-v1_5 over SHA-256, inclusive
/// Canonical XML 1.0) over the <c>authenticate="true"</c> node-set (issue #20). Tier A — keys are
/// generated in-process via <see cref="TestCertificates"/>; no proprietary samples. X002 is fully
/// deterministic (PKCS1-v1.5 + inclusive C14N + SHA-256), so the whole mechanism is pinned with a
/// known-answer vector; interop against real banks is a Tier-B skip.
/// </summary>
public class AuthenticationSignatureTests
{
    private static readonly KeyVersion X002 = KeyVersion.Create("X002");

    // A representative H005 request with a single authenticated element (the header). Serialized
    // the way EbicsXmlSerializer emits it: protocol namespace as default, `ds` prefix on the root.
    private const string RequestXml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<ebicsRequest xmlns=\"urn:org:ebics:H005\" xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\" Version=\"H005\" Revision=\"1\">" +
        "<header authenticate=\"true\">" +
        "<static><HostID>EBICOHOST</HostID></static>" +
        "<mutable><TransactionPhase>Initialisation</TransactionPhase></mutable>" +
        "</header>" +
        "<body/>" +
        "</ebicsRequest>";

    // Two disjoint authenticated elements (one nested), in a non-EBICS namespace: proves the
    // node-set selector unions subtrees and is not hard-wired to the EBICS namespaces.
    private const string MultiAuthXml =
        "<root xmlns=\"urn:test\" xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\">" +
        "<a authenticate=\"true\"><x>one</x></a>" +
        "<b><c authenticate=\"true\">two</c></b>" +
        "</root>";

    // The same fixed 2048-bit key pinned in BankSignatureTests / RsaKeyImportExportTests, reused
    // here so the X002 known-answer vector stays anchored to a single, stable key.
    private const string KnownPkcs8Base64 =
        "MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQCqB3mNXiQjOc9m" +
        "vpfoUYmMbD6I9ImTgtJ9PNkmf35GbCgoBOXaPZrt16/kphXguYCsTafUz/HSNxn" +
        "nTIzM6YVzf4mS9FiJ1gXO5CEHB8H0GRc2m54EFoPdVWX9sgG0vKmkGeMM8zKZAV" +
        "x864g9JgmWzoZ6AYBgMTWhHMxCJTlmgd4ooIH8Ic6Nrp+h1/hngVLYS6EXN6chn" +
        "piTyHxJIYCiV5prnXNxr8Gbq9dzR+D5WnqVv6Mj+aEkOFv9t+gLQm309hXF+2Ln" +
        "uz0rrNoZc6TkKy90VS/kf7aczxilKmyJkaa1kd45azybd6VFaV4fHintRYgrLMw" +
        "O1Ng6mOMOe6OVAgMBAAECggEBAI+yL4VNKadnpSPFMibSOjgmBxfB6z9ykafsM+14" +
        "VGT55VolAYjXBce6wFmyD81TmS6FlrChaVLq5IZ5SImpVfKNt9wti2I6McBvRoZ" +
        "lhQJh7h9llz8HNDxrfv3QYea4h3o7sorwQjPTVbHxcLuVGQeN1VLpT/B7xkI6T1o" +
        "bkY5SFCLcQH5vBWRl6ucXGU4Inn06unho5B9SwifBHyy8CPHjPGpWlgVA/VT4og" +
        "X6M3MmA2c+TiHK0wfwXRo8+3q2WfsQjo1qZ5Uk9Lf01CzcCz+f2QbvaPa3Lg0iN" +
        "oczD51QZIPxUQCVOKd2Kh4XNqp95mPHlC67f7Db/6M5xkYVo2UCgYEAxb2oNmbk" +
        "dNODrdVWGz51/az0Rk988tCGmWVazJUmrtM2Xdqa3ZgHNbhS+jpHtBNZaIHcl9w" +
        "50IkTqkB4QaL4+nIFJE8vR9k8+AX4EkhfcRIN2AZq+WVkfKpAVPvD3IgdkfYYUiw" +
        "iv8W/UzrUW+hK7gNoFG+s2sxx5OvmzPwOuZsCgYEA3B+z/UYC3A6dhLpy85E5myEC" +
        "j3O+195ObGzVb+3VKSprJ7t6LIY4uR6PXfiOBfctb1NooMd3GO67FeLvq/3/6WA" +
        "zFzxAFN188bhddL8csnv1rBxGKfOmZrN6zahBmw4hsbvBPVIgjWDnYRW4OGijgMF" +
        "j3+LmPV0ooc5lR524Qo8CgYBWFn/JT3peskc9wwc9zS+pRUcD5U9MlyRCXDHvp2+z" +
        "5RhiO+34U1uwM5NMhVr6NwJR0VesdaBl/YemM3MngEBNKJ68dAzthtJYWKDrtL5" +
        "4h5enWQPxmAbrj2N6nDFlLY1SIoXsIHLwcrMdFRum97bHcIw7eXMTvrZHJ7zPuVz" +
        "fyQKBgBnQxgUgHtm8BRE55J1YHM9qsagtROaANeZVZTq5Q9SOGv8P56YtH53mTZ4" +
        "RtmZQtM1nlM+2VOthpCNO+BjNsyOlmphRAprv1uVqX9t/RlhQXWGP91KYNp240uA" +
        "nqXoL0DvN7z3H0fWCteAW8gH7k6FYDOSG8cWklU1UrWAWyTNVAoGAFNPqjOPy0Q1D" +
        "hF6BwjGVqJcDznwagHA6oU7e0+7TprUAek4WHmqQNvQZCXu223UUzTOBvuw/Waso" +
        "c91V5bJslNTgm05QXlBUYeR6PCGUN9XAhfbZArdcNOxLDTrf4rK5O4vbKT1yycLB" +
        "kjCZgKKmLS1H+slZ4UxClx0t13eowmc=";

    // The expected X002 reference digest and signature over RequestXml with the pinned key.
    // X002 is deterministic (inclusive C14N + SHA-256 + RSASSA-PKCS1-v1.5), so both byte sequences
    // must reproduce exactly — this pins the C14N, the SignedInfo assembly and the padding.
    private const string ExpectedX002DigestBase64 = "RGw2j83upe07aVx3vHbJVkPPfTLe+mWFh/TyKYgMYCA=";

    private const string ExpectedX002SignatureBase64 =
        "BRXKN0Ga9NKvNcRBpEqO8Cjy3W9bJ0qWkz/OVwjwoc4a+RBfC0ZaClfIbTn8Dv5cbBnWKNW3U0UPevSMtDrRMP0" +
        "7a1oDq0BRKNh6l0GKRX8jjJu78ANfy6UBsdVa1TSjoJ82WN3RceS9EexqxXprJBYlcxnRdtBCZHE3fi68/gIYjd" +
        "oRUUYaIAeqvCq2+oV8rBkmLYsrP3JxzSQXYJtfj6Lu4JeilFGJHfS6CTP8AXKU4jgOKICRibdE9BczCRJimxGfp" +
        "MeHhA3m9aYWB/PlFLMJhyQwjCaD7jQfUX/+lZvPY8lteEQ4ch7uXUppH31XFK4n9I2MiVCOQ5l1LHvbDg==";

    private static RsaKeyMaterial NewKeyPair()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        return RsaKeyMaterial.FromKeyPair(rsa);
    }

    [Fact]
    public void Sign_Then_Verify_Succeeds()
    {
        var key = NewKeyPair();

        var signature = AuthenticationSignature.Sign(RequestXml, key, X002);

        AuthenticationSignature.Verify(RequestXml, signature, key, X002).Should().BeTrue();
    }

    [Fact]
    public void Verify_WithPublicOnlyKey_Succeeds()
    {
        var key = NewKeyPair();

        var signature = AuthenticationSignature.Sign(RequestXml, key, X002);

        AuthenticationSignature.Verify(RequestXml, signature, key.ToPublicOnly(), X002).Should().BeTrue();
    }

    [Fact]
    public void Sign_PopulatesSignedInfoWithExpectedAlgorithms()
    {
        var key = NewKeyPair();

        var signature = AuthenticationSignature.Sign(RequestXml, key, X002);

        signature.SignedInfo.SignatureMethod.Algorithm
            .Should().Be(AuthenticationSignature.SignatureMethodAlgorithm);
        signature.SignedInfo.Reference.Should().ContainSingle();
        signature.SignedInfo.Reference[0].DigestMethod.Algorithm
            .Should().Be(AuthenticationSignature.DigestMethodAlgorithm);
        signature.SignedInfo.Reference[0].Uri
            .Should().Be(AuthenticationSignature.AuthenticatedNodesReferenceUri);
        signature.SignedInfo.Reference[0].DigestValue.Should().HaveCount(32);
        signature.SignatureValue.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void MultipleAuthenticatedElements_RoundTrips()
    {
        var key = NewKeyPair();

        var signature = AuthenticationSignature.Sign(MultiAuthXml, key, X002);

        AuthenticationSignature.Verify(MultiAuthXml, signature, key, X002).Should().BeTrue();
    }

    [Fact]
    public void Verify_TamperedNestedAuthenticatedElement_ReturnsFalse()
    {
        var key = NewKeyPair();
        var signature = AuthenticationSignature.Sign(MultiAuthXml, key, X002);

        var tampered = MultiAuthXml.Replace("two", "TWO");

        AuthenticationSignature.Verify(tampered, signature, key, X002).Should().BeFalse();
    }

    [Fact]
    public void Verify_TamperedAuthenticatedElement_ReturnsFalse()
    {
        var key = NewKeyPair();
        var signature = AuthenticationSignature.Sign(RequestXml, key, X002);

        var tampered = RequestXml.Replace("EBICOHOST", "HACKEDXXX");

        AuthenticationSignature.Verify(tampered, signature, key, X002).Should().BeFalse();
    }

    [Fact]
    public void Verify_TamperedSignatureValue_ReturnsFalse()
    {
        var key = NewKeyPair();
        var signature = AuthenticationSignature.Sign(RequestXml, key, X002);
        signature.SignatureValue.Value[0] ^= 0xFF;

        AuthenticationSignature.Verify(RequestXml, signature, key, X002).Should().BeFalse();
    }

    [Fact]
    public void Verify_TamperedDigestValue_ReturnsFalse()
    {
        var key = NewKeyPair();
        var signature = AuthenticationSignature.Sign(RequestXml, key, X002);
        signature.SignedInfo.Reference[0].DigestValue[0] ^= 0xFF;

        AuthenticationSignature.Verify(RequestXml, signature, key, X002).Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongKey_ReturnsFalse()
    {
        var signer = NewKeyPair();
        var other = NewKeyPair();
        var signature = AuthenticationSignature.Sign(RequestXml, signer, X002);

        AuthenticationSignature.Verify(RequestXml, signature, other, X002).Should().BeFalse();
    }

    [Fact]
    public void Verify_UnknownCanonicalizationAlgorithm_ReturnsFalse()
    {
        var key = NewKeyPair();
        var signature = AuthenticationSignature.Sign(RequestXml, key, X002);
        signature.SignedInfo.CanonicalizationMethod.Algorithm = "urn:unsupported:c14n";

        AuthenticationSignature.Verify(RequestXml, signature, key, X002).Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongSignatureMethod_ReturnsFalse()
    {
        var key = NewKeyPair();
        var signature = AuthenticationSignature.Sign(RequestXml, key, X002);
        signature.SignedInfo.SignatureMethod.Algorithm = "http://www.w3.org/2000/09/xmldsig#rsa-sha1";

        AuthenticationSignature.Verify(RequestXml, signature, key, X002).Should().BeFalse();
    }

    [Fact]
    public void Verify_MissingSignedInfo_ReturnsFalse()
    {
        var key = NewKeyPair();
        var signature = new SignatureType { SignatureValue = new SignatureValueType { Value = [1, 2, 3] } };

        AuthenticationSignature.Verify(RequestXml, signature, key, X002).Should().BeFalse();
    }

    [Fact]
    public void Verify_MissingSignatureValue_ReturnsFalse()
    {
        var key = NewKeyPair();
        var signature = AuthenticationSignature.Sign(RequestXml, key, X002);
        signature.SignatureValue = null!;

        AuthenticationSignature.Verify(RequestXml, signature, key, X002).Should().BeFalse();
    }

    [Fact]
    public void Sign_NullRequestXml_Throws()
    {
        var key = NewKeyPair();

        var act = () => AuthenticationSignature.Sign(null!, key, X002);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Sign_NullKey_Throws()
    {
        var act = () => AuthenticationSignature.Sign(RequestXml, null!, X002);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Sign_PublicOnlyKey_Throws()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var publicOnly = RsaKeyMaterial.FromPublicKey(rsa);

        var act = () => AuthenticationSignature.Sign(RequestXml, publicOnly, X002);

        act.Should().Throw<KeyMaterialException>();
    }

    [Theory]
    [InlineData("A005")]
    [InlineData("E002")]
    [InlineData("X999")]
    public void Sign_NonAuthenticationOrUnknownVersion_Throws(string code)
    {
        var key = NewKeyPair();

        var act = () => AuthenticationSignature.Sign(RequestXml, key, KeyVersion.Create(code));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Sign_DefaultVersion_Throws()
    {
        var key = NewKeyPair();

        var act = () => AuthenticationSignature.Sign(RequestXml, key, default);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Verify_NonAuthenticationVersion_Throws()
    {
        var key = NewKeyPair();
        var signature = AuthenticationSignature.Sign(RequestXml, key, X002);

        var act = () => AuthenticationSignature.Verify(RequestXml, signature, key, KeyVersion.Create("A005"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Verify_NullArguments_Throw()
    {
        var key = NewKeyPair();
        var signature = AuthenticationSignature.Sign(RequestXml, key, X002);

        ((Action)(() => AuthenticationSignature.Verify(null!, signature, key, X002)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => AuthenticationSignature.Verify(RequestXml, null!, key, X002)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => AuthenticationSignature.Verify(RequestXml, signature, null!, X002)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RealEbicsRequest_SignAttachSerializeDeserialize_Verifies()
    {
        var key = NewKeyPair();
        var request = new EbicsRequest { Version = "H005", Header = new EbicsRequestHeader() };

        var unsignedXml = EbicsXmlSerializer.SerializeToString(request, EbicsVersion.H005);
        request.AuthSignature = AuthenticationSignature.Sign(unsignedXml, key, X002);
        var signedXml = EbicsXmlSerializer.SerializeToString(request, EbicsVersion.H005);

        var roundTripped = (EbicsRequest)EbicsXmlSerializer.DeserializeEnvelope(signedXml);

        AuthenticationSignature.Verify(signedXml, roundTripped.AuthSignature, key.ToPublicOnly(), X002)
            .Should().BeTrue();
    }

    [Fact]
    public void RealBankSample_VerifiesOrSkips()
    {
        if (!SampleXml.TryLoad(EbicsVersion.H005, SampleDirection.Request, "ebicsRequest.signed.xml", out var xml)
            || !SampleXml.TryLoad(EbicsVersion.H005, SampleDirection.Request, "bank-x002-pub.pem", out var pem))
        {
            Assert.Skip("Proprietary signed-request sample and/or X002 public key not present.");
            return;
        }

        var request = (EbicsRequest)EbicsXmlSerializer.DeserializeEnvelope(xml);
        var bankKey = RsaKeyImportExport.ImportFromPem(pem);

        AuthenticationSignature.Verify(xml, request.AuthSignature, bankKey, X002).Should().BeTrue();
    }

    [Fact]
    public void KnownVector_X002_ProducesPinnedDigestAndSignature()
    {
        var key = RsaKeyImportExport.ImportPkcs8(Convert.FromBase64String(KnownPkcs8Base64));

        var signature = AuthenticationSignature.Sign(RequestXml, key, X002);

        signature.SignedInfo.Reference[0].DigestValue
            .Should().Equal(Convert.FromBase64String(ExpectedX002DigestBase64));
        signature.SignatureValue.Value
            .Should().Equal(Convert.FromBase64String(ExpectedX002SignatureBase64));
        AuthenticationSignature.Verify(RequestXml, signature, key.ToPublicOnly(), X002).Should().BeTrue();
    }

    [Fact]
    public void AuthenticatedDigest_IsCanonicalizedInDocumentContext()
    {
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        document.LoadXml(RequestXml);
        var nodes = document.SelectNodes("(//. | //@*)[ancestor-or-self::*[@authenticate='true']]")!;

        var canonical = Encoding.UTF8.GetString(XmlCanonicalizer.Canonicalize(nodes));

        // Inclusive C14N renders the namespaces inherited from the request root at the apex, so the
        // header's signed form carries the default protocol namespace — exactly as a bank produces it.
        canonical.Should().Contain("xmlns=\"urn:org:ebics:H005\"");
    }
}
