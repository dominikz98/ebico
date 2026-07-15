namespace EBICO.Core;

/// <summary>
/// A closed reporting period <c>[Start, End]</c> for a download order (the EBICS <c>DateRange</c> in
/// <c>FDLOrderParams</c>/<c>StandardOrderParams</c> for H003/H004 and <c>BTDOrderParams</c> for H005).
/// Either bound may be absent: a <see langword="null"/> <see cref="Start"/> or <see cref="End"/> means the
/// requester left that side open and the server applies its own default window.
/// </summary>
/// <param name="Start">The inclusive first day of the period, or <see langword="null"/> when open.</param>
/// <param name="End">The inclusive last day of the period, or <see langword="null"/> when open.</param>
public readonly record struct DateRange(DateOnly? Start, DateOnly? End);
