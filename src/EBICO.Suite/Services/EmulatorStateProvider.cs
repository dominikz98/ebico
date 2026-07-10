using EBICO.Core.Domain;
using EBICO.Server.State;

namespace EBICO.Suite.Services;

/// <summary>
/// Live <see cref="IEmulatorStateProvider"/> bridge over the server-side emulator state: banks,
/// partners and subscribers are read from the in-process <see cref="IEbicsStateStore"/>
/// (the read/write store managed by the <see cref="IMasterDataManager"/>, issue #30), so the UI
/// reflects the actual emulator state rather than static sample data.
/// </summary>
/// <remarks>
/// This realises the ADR-0009 decision to access the server state in-process via DI instead of a
/// dedicated HTTP API. Key material is not yet part of the server store (a later M3/M4 issue), so
/// <see cref="GetKeysAsync"/> still serves the deterministic sample keys from
/// <see cref="SampleEmulatorStateProvider"/> for the key/certificate view (issue #55).
/// </remarks>
public sealed class EmulatorStateProvider : IEmulatorStateProvider
{
    private readonly IEbicsStateStore _store;
    private readonly SampleEmulatorStateProvider _samples;

    /// <summary>Creates the bridge over the given <paramref name="store"/>.</summary>
    /// <param name="store">The authoritative server-side state store.</param>
    /// <param name="samples">The sample-data source used for the placeholder key catalogue.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public EmulatorStateProvider(IEbicsStateStore store, SampleEmulatorStateProvider samples)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(samples);
        _store = store;
        _samples = samples;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken cancellationToken = default)
        => _store.GetBanksAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Partner>> GetPartnersAsync(CancellationToken cancellationToken = default)
        => _store.GetPartnersAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Subscriber>> GetSubscribersAsync(CancellationToken cancellationToken = default)
        => _store.GetSubscribersAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<KeyView>> GetKeysAsync(CancellationToken cancellationToken = default)
        => _samples.GetKeysAsync(cancellationToken);
}
