namespace EBICO.Core.Domain;

/// <summary>
/// The lifecycle state of an EBICS subscriber (Teilnehmer) at a bank. The states advance
/// through onboarding (<see cref="New"/> → <see cref="Initialized"/> → <see cref="Ready"/>);
/// a subscriber can be <see cref="Suspended"/> from any active state and reactivated.
/// </summary>
/// <remarks>Allowed transitions are enforced by <see cref="Subscriber.Transition(SubscriberState)"/>.</remarks>
public enum SubscriberState
{
    /// <summary>Created on the server, but no keys have been submitted yet (no INI/HIA).</summary>
    New,

    /// <summary>The bank-technical signature key has been submitted (INI); not yet usable for transactions.</summary>
    Initialized,

    /// <summary>Fully onboarded and activated — ready to send and receive orders.</summary>
    Ready,

    /// <summary>Temporarily blocked; cannot transact until reactivated.</summary>
    Suspended,
}
