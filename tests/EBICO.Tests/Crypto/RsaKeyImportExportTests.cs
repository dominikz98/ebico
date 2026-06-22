using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using EBICO.Core.Crypto;
using EBICO.Tests.Infrastructure;

namespace EBICO.Tests.Crypto;

/// <summary>
/// Tests for <see cref="RsaKeyImportExport"/> — PKCS#8 / SubjectPublicKeyInfo / PEM / X.509 /
/// RSAKeyValue import and export, round-trip fidelity, and failure handling (issue #18). Tier A —
/// uses in-process keys (<see cref="TestCertificates"/>) plus a fixed, externally generated
/// known-answer vector to lock the canonical byte form. No proprietary samples.
/// </summary>
public class RsaKeyImportExportTests
{
    // A fixed 2048-bit RSA key generated once outside the test (not regenerated per run), used to
    // prove interop with externally produced key blobs and to pin the canonical modulus/exponent.
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

    private const string KnownModulusBase64 =
        "qgd5jV4kIznPZr6X6FGJjGw+iPSJk4LSfTzZJn9+RmwoKATl2j2a7dev5KYV4LmA" +
        "rE2n1M/x0jcZ50yMzOmFc3+JkvRYidYFzuQhBwfB9BkXNpueBBaD3VVl/bIBtLy" +
        "ppBnjDPMymQFcfOuIPSYJls6GegGAYDE1oRzMQiU5ZoHeKKCB/CHOja6fodf4Z4" +
        "FS2EuhFzenIZ6Yk8h8SSGAoleaa51zca/Bm6vXc0fg+Vp6lb+jI/mhJDhb/bfoC" +
        "0Jt9PYVxfti57s9K6zaGXOk5CsvdFUv5H+2nM8YpSpsiZGmtZHeOWs8m3elRWle" +
        "Hx4p7UWIKyzMDtTYOpjjDnujlQ==";

    private const string KnownExponentBase64 = "AQAB";

    [Fact]
    public void Pkcs8_RoundTrip_IsByteStable()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var pkcs8 = rsa.ExportPkcs8PrivateKey();

        var material = RsaKeyImportExport.ImportPkcs8(pkcs8);
        material.HasPrivateKey.Should().BeTrue();

        RsaKeyImportExport.ExportPkcs8(material).Should().Equal(pkcs8);
    }

    [Fact]
    public void SubjectPublicKeyInfo_RoundTrip_IsByteStable()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var spki = rsa.ExportSubjectPublicKeyInfo();

        var material = RsaKeyImportExport.ImportSubjectPublicKeyInfo(spki);
        material.HasPrivateKey.Should().BeFalse();

        RsaKeyImportExport.ExportSubjectPublicKeyInfo(material).Should().Equal(spki);
    }

    [Fact]
    public void Pem_PrivateRoundTrip()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();

        var material = RsaKeyImportExport.ImportFromPem(pem);

        material.HasPrivateKey.Should().BeTrue();
        RsaKeyImportExport.ExportPkcs8(material).Should().Equal(rsa.ExportPkcs8PrivateKey());
    }

    [Fact]
    public void Pem_PublicRoundTrip()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var pem = rsa.ExportSubjectPublicKeyInfoPem();

        var material = RsaKeyImportExport.ImportFromPem(pem);

        material.HasPrivateKey.Should().BeFalse();
        RsaKeyImportExport.ExportPublicKeyPem(material).Should().Be(pem);
    }

    [Fact]
    public void RsaKeyValue_RoundTrip()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var material = RsaKeyMaterial.FromPublicKey(rsa);

        var (modulus, exponent) = RsaKeyImportExport.ExportRsaKeyValue(material);
        var rebuilt = RsaKeyImportExport.ImportRsaKeyValue(modulus, exponent);

        rebuilt.Modulus.ToArray().Should().Equal(modulus);
        rebuilt.Exponent.ToArray().Should().Equal(exponent);
    }

    [Fact]
    public void CrossFormat_Pkcs8AndSpki_YieldSamePublicKey()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var fromPkcs8 = RsaKeyImportExport.ImportPkcs8(rsa.ExportPkcs8PrivateKey());
        var fromSpki = RsaKeyImportExport.ImportSubjectPublicKeyInfo(rsa.ExportSubjectPublicKeyInfo());

        RsaKeyImportExport.ExportSubjectPublicKeyInfo(fromPkcs8)
            .Should().Equal(RsaKeyImportExport.ExportSubjectPublicKeyInfo(fromSpki));
    }

    [Fact]
    public void ImportPublicKeyFromCertificate_ExtractsPublicOnly()
    {
        using var cert = TestCertificates.CreateSelfSigned();

        var material = RsaKeyImportExport.ImportPublicKeyFromCertificate(cert);

        material.HasPrivateKey.Should().BeFalse();
        using var certKey = cert.GetRSAPublicKey()!;
        material.Modulus.ToArray().Should().Equal(certKey.ExportParameters(includePrivateParameters: false).Modulus);
    }

    [Fact]
    public void KnownVector_Pkcs8_ImportsToExpectedModulusAndExponent()
    {
        var pkcs8 = Convert.FromBase64String(KnownPkcs8Base64);

        var material = RsaKeyImportExport.ImportPkcs8(pkcs8);

        material.HasPrivateKey.Should().BeTrue();
        material.KeySizeBits.Should().Be(2048);
        material.Modulus.ToArray().Should().Equal(Convert.FromBase64String(KnownModulusBase64));
        material.Exponent.ToArray().Should().Equal(Convert.FromBase64String(KnownExponentBase64));
    }

    [Fact]
    public void KnownVector_Pkcs8_ReExportsToSameBytes()
    {
        var pkcs8 = Convert.FromBase64String(KnownPkcs8Base64);

        var material = RsaKeyImportExport.ImportPkcs8(pkcs8);

        RsaKeyImportExport.ExportPkcs8(material).Should().Equal(pkcs8);
    }

    [Fact]
    public void ExportPkcs8_PublicOnly_Throws()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var material = RsaKeyMaterial.FromPublicKey(rsa);

        var act = () => RsaKeyImportExport.ExportPkcs8(material);

        act.Should().Throw<KeyMaterialException>();
    }

    [Fact]
    public void ExportPkcs8Pem_PublicOnly_Throws()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var material = RsaKeyMaterial.FromPublicKey(rsa);

        var act = () => RsaKeyImportExport.ExportPkcs8Pem(material);

        act.Should().Throw<KeyMaterialException>();
    }

    [Fact]
    public void ImportPkcs8_MalformedData_Throws()
    {
        var act = () => RsaKeyImportExport.ImportPkcs8(new byte[] { 1, 2, 3, 4 });

        act.Should().Throw<KeyMaterialException>();
    }

    [Fact]
    public void ImportSubjectPublicKeyInfo_MalformedData_Throws()
    {
        var act = () => RsaKeyImportExport.ImportSubjectPublicKeyInfo(new byte[] { 9, 9, 9 });

        act.Should().Throw<KeyMaterialException>();
    }

    [Fact]
    public void ImportFromPem_Garbage_Throws()
    {
        var act = () => RsaKeyImportExport.ImportFromPem("not a pem");

        act.Should().Throw<KeyMaterialException>();
    }

    [Fact]
    public void ImportPublicKeyFromCertificate_NonRsaCertificate_Throws()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=EBICO EC Test", ecdsa, HashAlgorithmName.SHA256);
        var now = DateTimeOffset.UtcNow;
        using var cert = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(1));

        var act = () => RsaKeyImportExport.ImportPublicKeyFromCertificate(cert);

        act.Should().Throw<KeyMaterialException>();
    }

    [Fact]
    public void ImportPkcs8_UndersizedKey_Throws()
    {
        using var rsa = TestCertificates.CreateRsaKey(1024);
        var pkcs8 = rsa.ExportPkcs8PrivateKey();

        var act = () => RsaKeyImportExport.ImportPkcs8(pkcs8);

        act.Should().Throw<KeyMaterialException>();
    }
}
