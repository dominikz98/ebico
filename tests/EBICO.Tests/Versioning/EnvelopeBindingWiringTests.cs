using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Versioning;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Tests.Versioning;

/// <summary>
/// Verifies the hand-written partial declarations that wire the generated per-version
/// envelope bindings to the version-independent interfaces and expose
/// <see cref="IEbicsEnvelope.ProtocolVersion"/> (issue #14).
/// </summary>
public class EnvelopeBindingWiringTests
{
    public static TheoryData<Type, EbicsVersion> RequestEnvelopeTypes() => new()
    {
        { typeof(H003.EbicsRequest), EbicsVersion.H003 },
        { typeof(H003.EbicsUnsecuredRequest), EbicsVersion.H003 },
        { typeof(H003.EbicsUnsignedRequest), EbicsVersion.H003 },
        { typeof(H003.EbicsNoPubKeyDigestsRequest), EbicsVersion.H003 },
        { typeof(H004.EbicsRequest), EbicsVersion.H004 },
        { typeof(H004.EbicsUnsecuredRequest), EbicsVersion.H004 },
        { typeof(H004.EbicsUnsignedRequest), EbicsVersion.H004 },
        { typeof(H004.EbicsNoPubKeyDigestsRequest), EbicsVersion.H004 },
        { typeof(H005.EbicsRequest), EbicsVersion.H005 },
        { typeof(H005.EbicsUnsecuredRequest), EbicsVersion.H005 },
        { typeof(H005.EbicsUnsignedRequest), EbicsVersion.H005 },
        { typeof(H005.EbicsNoPubKeyDigestsRequest), EbicsVersion.H005 },
    };

    public static TheoryData<Type, EbicsVersion> ResponseEnvelopeTypes() => new()
    {
        { typeof(H003.EbicsResponse), EbicsVersion.H003 },
        { typeof(H003.EbicsKeyManagementResponse), EbicsVersion.H003 },
        { typeof(H004.EbicsResponse), EbicsVersion.H004 },
        { typeof(H004.EbicsKeyManagementResponse), EbicsVersion.H004 },
        { typeof(H005.EbicsResponse), EbicsVersion.H005 },
        { typeof(H005.EbicsKeyManagementResponse), EbicsVersion.H005 },
    };

    [Theory]
    [MemberData(nameof(RequestEnvelopeTypes))]
    public void RequestEnvelope_ImplementsRequestMarker_WithMatchingProtocolVersion(Type type, EbicsVersion expected)
    {
        typeof(IEbicsRequestEnvelope).IsAssignableFrom(type).Should().BeTrue();
        typeof(IEbicsResponseEnvelope).IsAssignableFrom(type)
            .Should().BeFalse("a request envelope must not also be a response envelope");

        var envelope = (IEbicsEnvelope)Activator.CreateInstance(type)!;
        envelope.ProtocolVersion.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(ResponseEnvelopeTypes))]
    public void ResponseEnvelope_ImplementsResponseMarker_WithMatchingProtocolVersion(Type type, EbicsVersion expected)
    {
        typeof(IEbicsResponseEnvelope).IsAssignableFrom(type).Should().BeTrue();
        typeof(IEbicsRequestEnvelope).IsAssignableFrom(type)
            .Should().BeFalse("a response envelope must not also be a request envelope");

        var envelope = (IEbicsEnvelope)Activator.CreateInstance(type)!;
        envelope.ProtocolVersion.Should().Be(expected);
    }

    [Fact]
    public void VersionAndRevision_RoundTripThroughInterface()
    {
        IEbicsEnvelope envelope = new H005.EbicsRequest();

        envelope.Version = "H005";
        envelope.Revision = 7;
        envelope.Version.Should().Be("H005");
        envelope.Revision.Should().Be((byte)7);

        envelope.Revision = null;
        envelope.Revision.Should().BeNull();
    }
}
