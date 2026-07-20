using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Server.State;

namespace EBICO.Suite.Services;

/// <summary>
/// The deterministic sample public-key material the Suite seeds into the server-side key stores on
/// start-up (<see cref="KeyStoreSeeder"/>), so the key/certificate view (issue #55) can display real
/// store data. It is the single source of the sample keys: <see cref="SampleEmulatorStateProvider"/>
/// builds its <see cref="KeyView"/> list from the same entries, so the seeded state and the sample
/// view can never drift.
/// </summary>
/// <remarks>
/// The keys are fixed 2048-bit RSA public keys (SubjectPublicKeyInfo, base64) so the fingerprints are
/// deterministic and cheap — mirrors how the crypto tests pin a key. Only public material is held; the
/// server never stores private keys. In a live server these entries would instead arrive through
/// INI/HIA onboarding (<see cref="IServerKeyStore"/>) and HPB (<see cref="IServerBankKeyStore"/>); the
/// Suite runs no EBICS pipeline, so it seeds them directly.
/// </remarks>
public static class KeyStoreSeedData
{
    // Subscriber onboarding keys (A00x signature, E002 encryption, X002 authentication).
    private const string SubscriberSignatureKey =
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAwNHtiFWPbSSg/VAdHKOkwJbc6qe2eR4n8dtFP3rl9R5+hBgcyuiTG9buH93Omm4EIhIjeSZf5qTLFOoPwujBr/2bCEQbrUc5vpcau1VfvZhY3A6DBCYBXdMabWp3oTr4R0hTUrTPeEowmPR50V0LW+ianUss9tt63AVeKm4yTbQcxC/Xc56vpKfHwBbmn5tE4ZIPfGAD76RLq38Heacu+2BXJS6alDQgtJQx3tCEq/plAQBsbGTlzRdECN/7O9Q/qW0/wC7FhTlCtkx1vWl78VnVDGxmsBUTQ5eyaOCE/DyZLejuNfRBoJGWBGHPECUie/ncqM6ac36K+vGPP9/UhQIDAQAB";

    private const string SubscriberEncryptionKey =
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA6IOFgzlXfDsatSlFx7vffoRVxCbyZFP1i22TehsjQ2YvbMuO1b7saI1OPxB9sn2z6e3i0PW7ux/W1t7yvfcAMtPrVF2RDgDSzEdnWUPOX8OhKZoScdhbmidnvX695Nxj8hNc+ZWREo74Cau6f7h8TH4zZ8GqWGYtSyTViYgtAVFLxPgJrypfKcJY8Y9X941ZeMKVHz4SvZYST1cnNzBSQ9czTdCxG1e70sdF5nVmfmZXY+xhC2AnZYXZlvDQqbHLv1ljo/ml9Q4MFpk6vcClWuXSyRNa6z1M/ZckC8LxWBMAsIk9uw4pTqe3/CCOsDbht6lnCdv3xwDzyLa1qAhipQIDAQAB";

    private const string SubscriberAuthenticationKey =
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAqhb+u7a7t32sDdeVxHwUaZcKJ99HRu9kPvDN69M0NzQlt4O7+Blx+ZZJG5PJNicQ08L3cXAALb/wO/TnSjd9IvrmQTaqKlS06xRfOrTGqwA2vX0y8RiukEBmabyijljQ1B2ecOfqLv6Z6dsSpHCM8FMac1Zmc29a2myhjtJTqmv+Vcyi9QDWSvzOjLlcvKX/KZVbidyYGtVBxBZKZ7G4J7A0nW26TDW2mU3SVluUAFHQgAxHQl4JqBZCR7SZS+Wf++KL3+GpkuUZEluTPg25qNvCQ+dyl8yoZ6wrr7mGRIz/uPG0SutL7ubcwhwnQvnOfYhjH7DCOOX0qPkRgrUM7QIDAQAB";

    private const string BankEncryptionKey =
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAnNLFcgSoN4Il2BCh3ePdqKgYv8H5bCq5RWjz1oFVFBgDubT8tyW9SNVCylKARmByOSJSNq+puFuoFeprb2VQGYlUdrwwLOi8YzvwA7RWsyNfMuZKHxustAeTlysSgQrFf09xAxlt6VeqAazof2TBU8s6MKQRvxlhBurn/FHyEf4LytfDwNDPMp4I362gncFcswNx9t3PdulXSfagkUoOJ+wly/hWE/+V4CyhMqURrQM7j3a8DzbUHHfUmrnr26RvaovQYOUDwqc6fqIQejplMFVmcte/A7AhN8ltHPO7SBm8iGXIx5nTcpsYQIJx2OZ1FE8ihu6rRz2yaXpCIWDbUQIDAQAB";

    private const string BankAuthenticationKey =
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAw4Tw1gS6fBT0O4T00Yf83P2/Yf+QuzxJFB06iZ9P7KBCFVlSn9inaKr8aOUUYrQZgyfu7+8tkkn42nAKIIH4Lx81miw3adblbkyYKi0q7W6mG9sgieBQBZBVyKINv0V6xSl4xgXo5lSUAxnhPfEKYCuuk+eoZNmtnYNK9o5GU6GrT+If8jrLvqfAMTSfs8HXUL6h2zdDhHB0cR2CwMldvaq0isDI8DKRVwIN3H6DBfPEJ+rlT7lMkjzBXPTp4HzcFVd+KN1Jm0wTXSnSLWYRjStvGEwzwB7KX1WKFLXkZp66z7WXBcBTPCUI9CkJZbFY36jcQ9/GWqDPERWuQNvfeQIDAQAB";

    /// <summary>
    /// The subscriber whose onboarding keys are seeded — matches the sample subscriber
    /// <c>USER0001</c> under partner <c>PARTNER01</c> at host <c>EBICOHOST</c> in
    /// <see cref="SampleEmulatorStateProvider"/>.
    /// </summary>
    public static SubscriberKeyRef SampleSubscriber { get; } =
        new(HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), UserId.Create("USER0001"));

    /// <summary>The host whose bank key pair (X002 authentication + E002 encryption) is seeded and displayed.</summary>
    public static HostId SampleBankHost { get; } = HostId.Create("EBICOHOST");

    /// <summary>The subscriber public keys to seed into the <see cref="IServerKeyStore"/>.</summary>
    public static IReadOnlyList<(SubscriberKeyRef Subscriber, StoredPublicKey Key)> SubscriberKeys { get; } =
    [
        (SampleSubscriber, new StoredPublicKey(Import(SubscriberSignatureKey), KeyVersion.Create("A006"))),
        (SampleSubscriber, new StoredPublicKey(Import(SubscriberEncryptionKey), KeyVersion.Create("E002"))),
        (SampleSubscriber, new StoredPublicKey(Import(SubscriberAuthenticationKey), KeyVersion.Create("X002"))),
    ];

    /// <summary>The bank key pairs to seed into the <see cref="IServerBankKeyStore"/>.</summary>
    public static IReadOnlyList<(HostId Host, BankKeyPair Pair)> BankKeys { get; } =
    [
        (SampleBankHost, new BankKeyPair(
            Import(BankAuthenticationKey), KeyVersion.Create("X002"),
            Import(BankEncryptionKey), KeyVersion.Create("E002"))),
    ];

    /// <summary>
    /// The hosts whose bank key pair is surfaced in the key view. Reading only these seeded hosts keeps
    /// the view deterministic: <see cref="IServerBankKeyStore.GetOrCreateAsync"/> always hits the cached
    /// seeded pair and never generates a fresh (non-reproducible) pair as a side effect of rendering.
    /// </summary>
    public static IReadOnlyList<HostId> BankHosts { get; } = [SampleBankHost];

    private static RsaKeyMaterial Import(string spkiBase64) =>
        RsaKeyImportExport.ImportSubjectPublicKeyInfo(Convert.FromBase64String(spkiBase64));
}
