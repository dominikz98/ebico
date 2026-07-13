using System.Security.Cryptography;
using System.Xml;
using AwesomeAssertions;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Versioning;
using EBICO.Server.Handlers;
using EBICO.Server.ReturnCodes;
using EBICO.Server.State;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for <see cref="EbicsErrorMapper"/> — the central exception → EBICS return-code mapping over
/// the catalogue from issue #36. Covers the envelope/version faults, the order-data faults, the
/// user/state faults and the internal-error fallback.
/// </summary>
public class EbicsErrorMapperTests
{
    private readonly EbicsErrorMapper _mapper = new();

    // --- Envelope / version / XML faults ---

    [Fact]
    public void Map_EnvelopeFormatException_ReturnsInvalidXml()
    {
        _mapper.Map(new EbicsEnvelopeFormatException("malformed"))
            .Should().Be(EbicsReturnCode.InvalidXml);
    }

    [Fact]
    public void Map_VersionNotSupportedException_ReturnsInvalidRequest()
    {
        _mapper.Map(new EbicsVersionNotSupportedException("unknown ns"))
            .Should().Be(EbicsReturnCode.InvalidRequest);
    }

    [Fact]
    public void Map_VersionMismatchException_ReturnsInvalidRequest()
    {
        _mapper.Map(new EbicsVersionMismatchException("mismatch"))
            .Should().Be(EbicsReturnCode.InvalidRequest);
    }

    [Fact]
    public void Map_XmlException_ReturnsInvalidXml()
    {
        _mapper.Map(new XmlException("bad xml"))
            .Should().Be(EbicsReturnCode.InvalidXml);
    }

    [Fact]
    public void Map_DeserializationXmlException_ReturnsInvalidXml()
    {
        // XmlSerializer wraps well-formedness failures in an InvalidOperationException.
        _mapper.Map(new InvalidOperationException("deserialize failed", new XmlException("mismatch")))
            .Should().Be(EbicsReturnCode.InvalidXml);
    }

    // --- Order-data faults -> InvalidOrderDataFormat ---

    public static TheoryData<Exception> OrderDataFaults() => new()
    {
        new EbicsOrderDataException("order data could not be decoded"),
        new KeyMaterialException("key material could not be reconstructed"),
        new InvalidKeyVersionException("bad key version"),
        new KeyVersionNotPermittedException("key version not permitted"),
        new InvalidDataException("undecompressable"),
        new FormatException("bad base64"),
        new CryptographicException("decryption failed"),
    };

    [Theory]
    [MemberData(nameof(OrderDataFaults))]
    public void Map_OrderDataFault_ReturnsInvalidOrderDataFormat(Exception exception)
    {
        _mapper.Map(exception).Should().Be(EbicsReturnCode.InvalidOrderDataFormat);
    }

    // --- User / state faults -> InvalidUserOrUserState ---

    public static TheoryData<Exception> UserStateFaults() => new()
    {
        new InvalidEbicsIdentifierException("bad identifier"),
        new InvalidSubscriberStateTransitionException("illegal transition"),
        new UnknownSubscriberException("unknown subscriber"),
        new UnknownPartnerException("unknown partner"),
        new UnknownBankException("unknown bank"),
        new MasterDataException("master-data lookup miss"),
    };

    [Theory]
    [MemberData(nameof(UserStateFaults))]
    public void Map_UserStateFault_ReturnsInvalidUserOrUserState(Exception exception)
    {
        _mapper.Map(exception).Should().Be(EbicsReturnCode.InvalidUserOrUserState);
    }

    // --- Fallback / guard ---

    [Fact]
    public void Map_UnknownException_ReturnsInternalError()
    {
        _mapper.Map(new InvalidOperationException("boom"))
            .Should().Be(EbicsReturnCode.InternalError);
    }

    [Fact]
    public void Map_PlainArgumentException_ReturnsInternalError()
    {
        // A general-purpose exception outside the order-data decode step denotes a server bug, not
        // invalid client order data.
        _mapper.Map(new ArgumentException("boom"))
            .Should().Be(EbicsReturnCode.InternalError);
    }

    [Fact]
    public void Map_Null_Throws()
    {
        var act = () => _mapper.Map(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
