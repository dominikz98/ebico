using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.ReturnCodes;
using EBICO.Server.Pipeline;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for <see cref="EbicsOrderHandlerResolver"/> — the handle-stage resolver of the server
/// host skeleton (issue #25): positive match, version mismatch, ordinal case-sensitivity and the
/// empty-handler / empty-order-type cases.
/// </summary>
public class EbicsOrderHandlerResolverTests
{
    private sealed class StubHandler(EbicsVersion version, string orderType) : IEbicsOrderHandler
    {
        public EbicsVersion Version { get; } = version;

        public string OrderType { get; } = orderType;

        public Task<EbicsOrderResult> HandleAsync(EbicsRequestContext context, CancellationToken ct = default)
            => Task.FromResult(new EbicsOrderResult(EbicsReturnCode.Ok));
    }

    [Fact]
    public void Resolve_MatchingVersionAndOrderType_ReturnsHandler()
    {
        var handler = new StubHandler(EbicsVersion.H004, "HPB");
        var resolver = new EbicsOrderHandlerResolver([handler]);

        resolver.Resolve(EbicsVersion.H004, "HPB").Should().BeSameAs(handler);
    }

    [Fact]
    public void Resolve_DifferentVersion_ReturnsNull()
    {
        var resolver = new EbicsOrderHandlerResolver([new StubHandler(EbicsVersion.H004, "HPB")]);

        resolver.Resolve(EbicsVersion.H005, "HPB").Should().BeNull();
    }

    [Fact]
    public void Resolve_IsOrdinalCaseSensitive_ReturnsNull()
    {
        var resolver = new EbicsOrderHandlerResolver([new StubHandler(EbicsVersion.H004, "HPB")]);

        resolver.Resolve(EbicsVersion.H004, "hpb").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_EmptyOrderType_ReturnsNull(string? orderType)
    {
        var resolver = new EbicsOrderHandlerResolver([new StubHandler(EbicsVersion.H004, "HPB")]);

        resolver.Resolve(EbicsVersion.H004, orderType).Should().BeNull();
    }

    [Fact]
    public void Resolve_NoHandlersRegistered_ReturnsNull()
    {
        var resolver = new EbicsOrderHandlerResolver([]);

        resolver.Resolve(EbicsVersion.H004, "HPB").Should().BeNull();
    }

    [Fact]
    public void Constructor_NullHandlers_Throws()
    {
        var act = () => new EbicsOrderHandlerResolver(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
