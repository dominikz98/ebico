using EBICO.Connector;
using EBICO.Connector.Transport;

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

/// <summary>
/// A test <see cref="ITransport"/> that records the request payload and returns a canned response,
/// used to drive the onboarding handlers without a real HTTP round-trip.
/// </summary>
public sealed class FakeTransport : ITransport
{
    private readonly Func<EbicsHttpRequest, EbicsHttpResponse> _responder;

    /// <summary>Creates a transport with the given responder.</summary>
    /// <param name="responder">Produces the response for a given request.</param>
    public FakeTransport(Func<EbicsHttpRequest, EbicsHttpResponse> responder) => _responder = responder;

    /// <summary>The payload of the last request the transport was asked to send.</summary>
    public byte[]? LastRequestPayload { get; private set; }

    /// <inheritdoc />
    public Task<EbicsHttpResponse> SendAsync(EbicsHttpRequest request, CancellationToken ct = default)
    {
        LastRequestPayload = request.Payload.ToArray();
        return Task.FromResult(_responder(request));
    }
}

/// <summary>A <see cref="TimeProvider"/> that always returns a fixed instant, for deterministic tests.</summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    /// <summary>Creates a provider fixed at <paramref name="now"/>.</summary>
    /// <param name="now">The instant to return from <see cref="GetUtcNow"/>.</param>
    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>
/// A <see cref="TimeProvider"/> whose current instant can be moved forward, for testing idle-timeout and
/// expiry behavior deterministically (unlike <see cref="FixedTimeProvider"/>, which cannot advance).
/// Only the clock is emulated (no timer callbacks); expiry logic is tested by advancing and querying.
/// </summary>
public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    /// <summary>Creates a provider starting at <paramref name="start"/>.</summary>
    /// <param name="start">The initial instant.</param>
    public MutableTimeProvider(DateTimeOffset start) => _now = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Sets the current instant.</summary>
    /// <param name="now">The new instant.</param>
    public void SetUtcNow(DateTimeOffset now) => _now = now;

    /// <summary>Moves the current instant forward by <paramref name="by"/>.</summary>
    /// <param name="by">The amount to advance.</param>
    public void Advance(TimeSpan by) => _now += by;
}
