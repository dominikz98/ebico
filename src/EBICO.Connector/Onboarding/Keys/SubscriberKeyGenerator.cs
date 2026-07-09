using EBICO.Connector.Configuration;
using EBICO.Connector.Keys;
using EBICO.Core.Crypto;

namespace EBICO.Connector.Onboarding.Keys;

/// <summary>
/// The default <see cref="ISubscriberKeyGenerator"/>: generates one RSA pair per purpose using the
/// key version that is the default for the configured EBICS version, and stores each under
/// <see cref="KeyOwner.Subscriber"/>.
/// </summary>
internal sealed class SubscriberKeyGenerator : ISubscriberKeyGenerator
{
    private static readonly KeyPurpose[] Purposes =
        [KeyPurpose.Signature, KeyPurpose.Authentication, KeyPurpose.Encryption];

    private readonly EbicsConnection _connection;
    private readonly IKeyStore _keys;

    /// <summary>Initializes the generator.</summary>
    /// <param name="connection">The resolved connection (its version selects the key versions).</param>
    /// <param name="keys">The key store the generated pairs are written to.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public SubscriberKeyGenerator(EbicsConnection connection, IKeyStore keys)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(keys);
        _connection = connection;
        _keys = keys;
    }

    /// <inheritdoc />
    public async Task<SubscriberKeySet> GenerateAsync(
        SubscriberKeyGenerationOptions? options = null, CancellationToken ct = default)
    {
        options ??= new SubscriberKeyGenerationOptions();

        if (!options.Overwrite)
        {
            foreach (var purpose in Purposes)
            {
                if (await _keys.ContainsAsync(KeyOwner.Subscriber, purpose, ct).ConfigureAwait(false))
                {
                    throw new EbicsConfigurationException(
                        $"A subscriber {purpose} key already exists. Set {nameof(SubscriberKeyGenerationOptions.Overwrite)} " +
                        "to regenerate, but note this invalidates any in-progress onboarding.");
                }
            }
        }

        var digests = new Dictionary<KeyPurpose, byte[]>(Purposes.Length);
        var versions = new Dictionary<KeyPurpose, string>(Purposes.Length);

        foreach (var purpose in Purposes)
        {
            var material = RsaKeyMaterial.Generate(options.KeySizeBits);
            await _keys.StoreAsync(KeyOwner.Subscriber, purpose, material, ct).ConfigureAwait(false);
            digests[purpose] = PublicKeyFingerprint.Compute(material);
            versions[purpose] = KeyVersions.Default(purpose, _connection.Version).Version.Value;
        }

        return new SubscriberKeySet
        {
            SignatureKeyVersion = versions[KeyPurpose.Signature],
            AuthenticationKeyVersion = versions[KeyPurpose.Authentication],
            EncryptionKeyVersion = versions[KeyPurpose.Encryption],
            SignatureKeyDigest = digests[KeyPurpose.Signature],
            AuthenticationKeyDigest = digests[KeyPurpose.Authentication],
            EncryptionKeyDigest = digests[KeyPurpose.Encryption],
        };
    }
}
