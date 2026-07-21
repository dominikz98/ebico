extern alias EbicoServer;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Serialization;
using EBICO.Server.State;
using EBICO.Tests.Infrastructure;
using EBICO.Tests.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Ds = EBICO.Core.Schema.XmlDsig;
using S002 = EBICO.Core.Schema.Signature.S002;

namespace EBICO.Tests.Conformance;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// Conformance (issue #59), <b>negative / known-gap</b> layer: documents (and guards) how the server
/// rejects two interoperability faults a real third-party client can genuinely produce. Both surface as
/// <c>090004 EBICS_INVALID_ORDER_DATA_FORMAT</c> — the codes are asserted so a future change in behaviour
/// (e.g. adding bare-key support for H005) is caught and the deviation doc kept honest.
/// </summary>
/// <remarks>
/// These are recorded in the deviations section of <c>docs/development/conformance-real-clients.md</c>:
/// <list type="bullet">
/// <item>H005 is certificate-based in EBICO — a client that submits a bare <c>RSAKeyValue</c> instead of
/// an <c>X509Data</c> certificate is rejected.</item>
/// <item>EBICS order data must be compressed — a client that skips compression is rejected.</item>
/// </list>
/// </remarks>
public class WireShapeNegativeConformanceTests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public WireShapeNegativeConformanceTests(WebApplicationFactory<ServerProgram> factory) => _factory = factory;

    [Fact]
    public async Task H005Ini_WithBareRsaKeyInsteadOfCertificate_IsRejected()
    {
        var (client, master, host, partner, user) = await SeedNewSubscriberAsync("BAREKEY");

        // A conforming-looking H005 INI whose order data carries a bare RSAKeyValue instead of the
        // X509Data certificate H005 requires — the shape a certificate-less client would emit.
        var orderData = EbicsCompression.Compress(
            BuildH005IniOrderDataWithBareRsaKey(partner.Value, user.Value, E2E.E2EKeyPool.Subscriber(KeyPurpose.Signature)));
        var xml = ServerTestHelpers.BuildUnsecuredRequestWithOrderData(
            EbicsVersion.H005, host.Value, partner.Value, user.Value, orderData, "INI");

        var body = await PostAsync(client, xml);

        body.Should().Be(EbicsReturnCode.InvalidOrderDataFormat.Code);
        // The subscriber is untouched: a rejected INI must not advance the lifecycle.
        (await master.GetSubscriberAsync(host, partner, user, _ct))!.State.Should().Be(SubscriberState.New);
    }

    [Fact]
    public async Task H005Ini_WithUncompressedOrderData_IsRejected()
    {
        var (client, master, host, partner, user) = await SeedNewSubscriberAsync("NOZIP");

        // Valid S002 order data, but submitted uncompressed — a common client-side interop bug. EBICS
        // order data is always compressed, so the server's decompression step rejects it.
        using var certificate = TestCertificates.CreateSelfSigned("CN=EBICO Conformance");
        var uncompressed = BuildH005IniOrderDataWithCertificate(partner.Value, user.Value, certificate);
        var xml = ServerTestHelpers.BuildUnsecuredRequestWithOrderData(
            EbicsVersion.H005, host.Value, partner.Value, user.Value, uncompressed, "INI");

        var body = await PostAsync(client, xml);

        body.Should().Be(EbicsReturnCode.InvalidOrderDataFormat.Code);
        (await master.GetSubscriberAsync(host, partner, user, _ct))!.State.Should().Be(SubscriberState.New);
    }

    private async Task<(HttpClient Client, IMasterDataManager Master, HostId Host, PartnerId Partner, UserId User)>
        SeedNewSubscriberAsync(string scenario)
    {
        var factory = _factory.WithWebHostBuilder(_ => { });
        var host = HostId.Create($"NEG{scenario}");
        var partner = PartnerId.Create($"P{scenario}");
        var user = UserId.Create($"U{scenario}");

        var master = factory.Services.GetRequiredService<IMasterDataManager>();
        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);
        await master.SaveSubscriberAsync(new Subscriber(host, partner, user), _ct);

        return (factory.CreateClient(), master, host, partner, user);
    }

    private async Task<string?> PostAsync(HttpClient client, string xml)
    {
        var response = await client.PostAsync("/ebics", new StringContent(xml, Encoding.UTF8, "text/xml"), _ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(await response.Content.ReadAsStringAsync(_ct));
        return ServerTestHelpers.ReadReturnCodes(envelope).BodyCode;
    }

    private static byte[] BuildH005IniOrderDataWithBareRsaKey(string partnerId, string userId, RsaKeyMaterial key)
    {
        var (modulus, exponent) = RsaKeyImportExport.ExportRsaKeyValue(key);
        XNamespace ds = "http://www.w3.org/2000/09/xmldsig#";

        var orderData = new S002.SignaturePubKeyOrderDataType
        {
            SignaturePubKeyInfo = new S002.SignaturePubKeyInfoType
            {
                SignatureVersion = "A006",
                // X509Data intentionally omitted — a bare RSA key is placed in the wildcard instead.
            },
            PartnerId = partnerId,
            UserId = userId,
        };
        orderData.SignaturePubKeyInfo.Any.Add(new XElement(
            ds + "RSAKeyValue",
            new XElement(ds + "Modulus", Convert.ToBase64String(modulus)),
            new XElement(ds + "Exponent", Convert.ToBase64String(exponent))));

        return EbicsXmlSerializer.SerializeOrderData(orderData);
    }

    private static byte[] BuildH005IniOrderDataWithCertificate(string partnerId, string userId, X509Certificate2 certificate)
    {
        var x509 = new Ds.X509DataType();
        x509.X509Certificate.Add(certificate.RawData);

        var orderData = new S002.SignaturePubKeyOrderDataType
        {
            SignaturePubKeyInfo = new S002.SignaturePubKeyInfoType
            {
                SignatureVersion = "A006",
                X509Data = x509,
            },
            PartnerId = partnerId,
            UserId = userId,
        };

        return EbicsXmlSerializer.SerializeOrderData(orderData);
    }
}
