using EBICO.Connector;

namespace EBICO.Tests.Connector;

/// <summary>A minimal result type for dispatch tests.</summary>
/// <param name="Payload">An arbitrary payload the fake handler echoes back.</param>
public sealed record FakeResult(string Payload);

/// <summary>A minimal request used to exercise <see cref="IEbicsClient"/> dispatch.</summary>
public sealed class FakeRequest : IEbicsRequest<FakeResult>
{
    /// <summary>Arbitrary input echoed by <see cref="FakeHandler"/>.</summary>
    public string Input { get; init; } = string.Empty;
}

/// <summary>A fake handler that records its invocation and returns a success result.</summary>
public sealed class FakeHandler : IEbicsRequestHandler<FakeRequest, FakeResult>
{
    /// <summary>Whether <see cref="Handle"/> has been invoked.</summary>
    public bool WasCalled { get; private set; }

    /// <summary>The context passed to the last invocation.</summary>
    public EbicsContext? LastContext { get; private set; }

    /// <inheritdoc />
    public Task<EbicsResult<FakeResult>> Handle(FakeRequest request, EbicsContext ctx, CancellationToken ct)
    {
        WasCalled = true;
        LastContext = ctx;
        return Task.FromResult(EbicsResult<FakeResult>.Success(new FakeResult($"handled:{request.Input}")));
    }
}

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that returns a canned response (or throws) and records
/// the request it received. Wired into the connector's named client via
/// <c>ConfigurePrimaryHttpMessageHandler</c>.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    /// <summary>Creates a stub with an asynchronous responder.</summary>
    /// <param name="responder">Produces the response for a given request.</param>
    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        => _responder = responder;

    /// <summary>Creates a stub with a synchronous responder.</summary>
    /// <param name="responder">Produces the response for a given request.</param>
    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this((req, _) => Task.FromResult(responder(req)))
    {
    }

    /// <summary>The last request seen by the handler.</summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    /// <summary>The body of the last request, read as bytes.</summary>
    public byte[]? LastRequestBody { get; private set; }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        return await _responder(request, cancellationToken);
    }
}
