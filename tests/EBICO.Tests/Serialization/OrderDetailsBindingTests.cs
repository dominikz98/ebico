using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Tests.Server;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Tests.Serialization;

/// <summary>
/// Guard tests for the <c>OrderDetails</c> binding fixup (issue #117, ADR-0029). The generated
/// bindings type <c>OrderDetails</c> in the unsecured / no-pub-key-digests static header as the
/// <em>abstract</em> <c>OrderDetailsType</c>, because xscgen does not translate the XSD restriction
/// that re-types the element concretely. An abstract binding makes the <c>XmlSerializer</c> demand an
/// <c>xsi:type</c> discriminator — which EBICO's own connector emitted but real third-party clients
/// (node-ebics-client) do not, so every one of their onboarding requests was rejected.
/// </summary>
/// <remarks>
/// <c>scripts/generate-bindings.sh</c> strips the <c>abstract</c> keyword after each generation run.
/// That step lives in a shell script and would otherwise vanish silently on the next schema update —
/// these tests are what makes it loud. They cover both directions of the contract: EBICO accepts
/// <c>&lt;OrderDetails&gt;</c> with <b>and</b> without the discriminator, and emits it without.
/// </remarks>
public class OrderDetailsBindingTests
{
    /// <summary>Every supported version with its protocol namespace and order-type element name.</summary>
    public static TheoryData<EbicsVersion, string, string> VersionCases() => new()
    {
        { EbicsVersion.H003, "http://www.ebics.org/H003", "OrderType" },
        { EbicsVersion.H004, "urn:org:ebics:H004", "OrderType" },
        { EbicsVersion.H005, "urn:org:ebics:H005", "AdminOrderType" },
    };

    /// <summary>
    /// The fixup itself: a regenerated-but-unpatched binding would make this fail before any of the
    /// wire-level assertions below get a chance to.
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="namespaceUri">Unused here; part of the shared case set.</param>
    /// <param name="orderTypeElement">Unused here; part of the shared case set.</param>
    [Theory]
    [MemberData(nameof(VersionCases))]
    public void OrderDetailsType_IsNotAbstract(EbicsVersion version, string namespaceUri, string orderTypeElement)
    {
        _ = namespaceUri;
        _ = orderTypeElement;

        OrderDetailsTypeOf(version).IsAbstract.Should()
            .BeFalse("scripts/generate-bindings.sh must strip `abstract` from OrderDetailsType (ADR-0029)");
    }

    /// <summary>
    /// The headline case: a real client's <c>&lt;OrderDetails&gt;</c> carries no <c>xsi:type</c>.
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="namespaceUri">That version's protocol namespace.</param>
    /// <param name="orderTypeElement">The version's order-type element name.</param>
    [Theory]
    [MemberData(nameof(VersionCases))]
    public void DeserializeEnvelope_UnsecuredRequestWithoutXsiType_ReadsOrderDetails(
        EbicsVersion version, string namespaceUri, string orderTypeElement)
    {
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(
            UnsecuredRequestXml(version, namespaceUri, orderTypeElement, xsiType: null));

        ReadOrderType(envelope).Should().Be("INI");
    }

    /// <summary>
    /// Backwards compatibility: the discriminator EBICO used to emit is still accepted, so an existing
    /// counterpart (or a client that models the abstract base) does not regress.
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="namespaceUri">That version's protocol namespace.</param>
    /// <param name="orderTypeElement">The version's order-type element name.</param>
    [Theory]
    [MemberData(nameof(VersionCases))]
    public void DeserializeEnvelope_UnsecuredRequestWithXsiType_StillReadsOrderDetails(
        EbicsVersion version, string namespaceUri, string orderTypeElement)
    {
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(
            UnsecuredRequestXml(version, namespaceUri, orderTypeElement, xsiType: "UnsecuredReqOrderDetailsType"));

        ReadOrderType(envelope).Should().Be("INI");
    }

    /// <summary>The same, for the HPB-shaped <c>ebicsNoPubKeyDigestsRequest</c>.</summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="namespaceUri">That version's protocol namespace.</param>
    /// <param name="orderTypeElement">The version's order-type element name.</param>
    [Theory]
    [MemberData(nameof(VersionCases))]
    public void DeserializeEnvelope_NoPubKeyDigestsRequestWithoutXsiType_ReadsOrderDetails(
        EbicsVersion version, string namespaceUri, string orderTypeElement)
    {
        var xml = $"""
                   <ebicsNoPubKeyDigestsRequest xmlns="{namespaceUri}" Version="{version}" Revision="1">
                     <header authenticate="true"><static>
                       <HostID>EBICOHOST</HostID>
                       <PartnerID>PARTNER1</PartnerID>
                       <UserID>USER1</UserID>
                       <OrderDetails><{orderTypeElement}>HPB</{orderTypeElement}>{OrderAttribute(version, "DZHNN")}</OrderDetails>
                       <SecurityMedium>0000</SecurityMedium>
                     </static><mutable/></header><body/>
                   </ebicsNoPubKeyDigestsRequest>
                   """;

        ReadOrderType(EbicsXmlSerializer.DeserializeEnvelope(xml)).Should().Be("HPB");
    }

    /// <summary>
    /// The emission half: EBICO's own onboarding requests must carry no discriminator either, so a
    /// strict foreign parser sees the same shape a real client would send.
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="namespaceUri">Unused here; part of the shared case set.</param>
    /// <param name="orderTypeElement">Unused here; part of the shared case set.</param>
    [Theory]
    [MemberData(nameof(VersionCases))]
    public void Serialize_UnsecuredRequest_EmitsNoXsiTypeDiscriminator(
        EbicsVersion version, string namespaceUri, string orderTypeElement)
    {
        _ = namespaceUri;
        _ = orderTypeElement;

        var xml = ServerTestHelpers.BuildUnsecuredRequestWithOrderData(
            version, "EBICOHOST", "PARTNER1", "USER1", [1, 2, 3, 4]);

        xml.Should().NotContain("xsi:type");
        xml.Should().NotContain("xmlns:xsi");
        xml.Should().Contain("<OrderDetails>");
    }

    private static Type OrderDetailsTypeOf(EbicsVersion version) => version switch
    {
        EbicsVersion.H003 => typeof(H003.OrderDetailsType),
        EbicsVersion.H004 => typeof(H004.OrderDetailsType),
        EbicsVersion.H005 => typeof(H005.OrderDetailsType),
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, null),
    };

    // H005 dropped OrderAttribute from OrderDetailsType; H003/H004 still require it.
    private static string OrderAttribute(EbicsVersion version, string value)
        => version == EbicsVersion.H005 ? string.Empty : $"<OrderAttribute>{value}</OrderAttribute>";

    private static string UnsecuredRequestXml(
        EbicsVersion version, string namespaceUri, string orderTypeElement, string? xsiType)
    {
        var discriminator = xsiType is null
            ? string.Empty
            : $" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:type=\"{xsiType}\"";

        return $"""
                <ebicsUnsecuredRequest xmlns="{namespaceUri}" Version="{version}" Revision="1">
                  <header authenticate="true"><static>
                    <HostID>EBICOHOST</HostID>
                    <PartnerID>PARTNER1</PartnerID>
                    <UserID>USER1</UserID>
                    <OrderDetails{discriminator}><{orderTypeElement}>INI</{orderTypeElement}>{OrderAttribute(version, "DZNNN")}</OrderDetails>
                    <SecurityMedium>0000</SecurityMedium>
                  </static><mutable/></header>
                  <body><DataTransfer><OrderData>eJw=</OrderData></DataTransfer></body>
                </ebicsUnsecuredRequest>
                """;
    }

    private static string? ReadOrderType(IEbicsEnvelope envelope) => envelope switch
    {
        H003.EbicsUnsecuredRequest r => r.Header?.Static?.OrderDetails?.OrderType,
        H004.EbicsUnsecuredRequest r => r.Header?.Static?.OrderDetails?.OrderType,
        H005.EbicsUnsecuredRequest r => r.Header?.Static?.OrderDetails?.AdminOrderType,
        H003.EbicsNoPubKeyDigestsRequest r => r.Header?.Static?.OrderDetails?.OrderType,
        H004.EbicsNoPubKeyDigestsRequest r => r.Header?.Static?.OrderDetails?.OrderType,
        H005.EbicsNoPubKeyDigestsRequest r => r.Header?.Static?.OrderDetails?.AdminOrderType,
        _ => null,
    };
}
