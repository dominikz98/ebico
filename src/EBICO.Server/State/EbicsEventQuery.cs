using EBICO.Core.Domain;

namespace EBICO.Server.State;

/// <summary>
/// A filter over the <see cref="IEventLog"/> (issue #69). Every field is optional; a <see langword="null"/>
/// (or unset) field means "do not filter on this dimension". The filters combine with logical AND. Results
/// are returned ordered by <see cref="EbicsEvent.Sequence"/> ascending.
/// </summary>
/// <remarks>
/// The two projections drive their reads through this query: HAC (M5) uses
/// <c>{ PartnerId = …, Visibility = CustomerVisible }</c> to get one customer's visible events; the Suite
/// inspector (M7) uses the raw filters (customer/time/type/severity) without a visibility constraint so it
/// sees internal events too.
/// </remarks>
public sealed record EbicsEventQuery
{
    /// <summary>When set, only events for this host are returned.</summary>
    public HostId? HostId { get; init; }

    /// <summary>When set, only events for this customer (Partner) are returned.</summary>
    public PartnerId? PartnerId { get; init; }

    /// <summary>When set, only events for this subscriber (User) are returned.</summary>
    public UserId? UserId { get; init; }

    /// <summary>When set, only events of this type are returned.</summary>
    public EbicsEventType? Type { get; init; }

    /// <summary>When set, only events with this visibility are returned (HAC uses <see cref="EbicsEventVisibility.CustomerVisible"/>).</summary>
    public EbicsEventVisibility? Visibility { get; init; }

    /// <summary>When set, only events at or after this instant are returned (inclusive lower bound).</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>When set, only events strictly before this instant are returned (exclusive upper bound).</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>
    /// When set to a positive value, at most this many events are returned (the earliest matching ones by
    /// <see cref="EbicsEvent.Sequence"/>). <see langword="null"/> or a non-positive value means no limit.
    /// </summary>
    public int? Limit { get; init; }
}
