namespace EBICO.Core.Administrative;

/// <summary>
/// A single, version-neutral entry of the customer protocol (HAC/PTK, issue #41). It is the Core input the
/// <see cref="HacProtocolBuilder"/> and <see cref="PtkProtocolBuilder"/> render; the server maps its
/// event-log records (<c>EbicsEvent</c>) onto this shape so Core stays independent of the server layer.
/// </summary>
/// <param name="Sequence">The monotonic sequence number of the underlying event (a stable, 1-based order).</param>
/// <param name="Timestamp">The instant the event occurred.</param>
/// <param name="OrderType">The order type the entry relates to (e.g. <c>"CCT"</c>), or <see langword="null"/>.</param>
/// <param name="ReturnCode">The EBICS return code (e.g. <c>"000000"</c>), or <see langword="null"/>.</param>
/// <param name="SymbolicName">The symbolic name of the return code (e.g. <c>"EBICS_OK"</c>), or <see langword="null"/>.</param>
/// <param name="Severity">The severity label (e.g. <c>"Info"</c>, <c>"Warning"</c>, <c>"Error"</c>).</param>
/// <param name="Message">A short human-readable description of the entry.</param>
public sealed record CustomerProtocolEntry(
    long Sequence,
    DateTimeOffset Timestamp,
    string? OrderType,
    string? ReturnCode,
    string? SymbolicName,
    string Severity,
    string Message);
