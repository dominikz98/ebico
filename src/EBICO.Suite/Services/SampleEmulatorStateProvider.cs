using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;

namespace EBICO.Suite.Services;

/// <summary>
/// In-memory placeholder implementation of <see cref="IEmulatorStateProvider"/> that
/// serves a small, deterministic set of sample banks, partners and subscribers built
/// from the <see cref="EBICO.Core.Domain"/> aggregates.
/// </summary>
/// <remarks>
/// Placeholder until the real emulator store exists (server layer, M3/M4). It lets the
/// UI grundgerüst (#52) demonstrate the state binding end-to-end; once the server store
/// is available this implementation is swapped for one reading the live state.
/// </remarks>
public sealed class SampleEmulatorStateProvider : IEmulatorStateProvider
{
    private static readonly IReadOnlyList<Bank> Banks =
    [
        new Bank(HostId.Create("EBICOHOST"), "EBICO Test-Bank"),
        new Bank(HostId.Create("BANKB"), "Zweitbank", [EbicsVersion.H004, EbicsVersion.H005]),
    ];

    private static readonly IReadOnlyList<Partner> Partners =
    [
        new Partner(PartnerId.Create("PARTNER01"), "Muster GmbH"),
        new Partner(PartnerId.Create("PARTNER02"), "Beispiel AG"),
    ];

    private static readonly IReadOnlyList<Subscriber> Subscribers =
    [
        new Subscriber(
            HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), UserId.Create("USER0001"),
            state: SubscriberState.Ready,
            permissions:
            [
                new SubscriberPermission("CCT", SignatureClass.E),
                new SubscriberPermission("STA", SignatureClass.T),
            ]),
        new Subscriber(
            HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), UserId.Create("USER0002"),
            state: SubscriberState.Initialized,
            permissions: [new SubscriberPermission("CCT", SignatureClass.A)]),
        new Subscriber(
            HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER02"), UserId.Create("USER0003"),
            systemId: SystemId.Create("SYS01"),
            state: SubscriberState.New),
        new Subscriber(
            HostId.Create("BANKB"), PartnerId.Create("PARTNER02"), UserId.Create("USER0004"),
            state: SubscriberState.Suspended),
    ];

    /// <inheritdoc />
    public Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Banks);

    /// <inheritdoc />
    public Task<IReadOnlyList<Partner>> GetPartnersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Partners);

    /// <inheritdoc />
    public Task<IReadOnlyList<Subscriber>> GetSubscribersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Subscribers);

    /// <inheritdoc />
    public Task<IReadOnlyList<KeyView>> GetKeysAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Keys);

    // Fixed 2048-bit RSA public keys (SubjectPublicKeyInfo, base64) so the sample fingerprints are
    // deterministic and cheap — mirrors how PublicKeyFingerprintTests pins a key. Placeholder sample
    // data until the real server store exists (M3/M4).
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

    private static readonly IReadOnlyList<KeyView> Keys =
    [
        BuildKey("Teilnehmer PARTNER01 / USER0001", KeyPurpose.Signature, "A006", SubscriberSignatureKey),
        BuildKey("Teilnehmer PARTNER01 / USER0001", KeyPurpose.Encryption, "E002", SubscriberEncryptionKey),
        BuildKey("Teilnehmer PARTNER01 / USER0001", KeyPurpose.Authentication, "X002", SubscriberAuthenticationKey),
        BuildKey("Bank EBICOHOST", KeyPurpose.Encryption, "E002", BankEncryptionKey),
        BuildKey("Bank EBICOHOST", KeyPurpose.Authentication, "X002", BankAuthenticationKey),
    ];

    private static KeyView BuildKey(string ownerLabel, KeyPurpose purpose, string keyVersion, string spkiBase64)
    {
        var material = RsaKeyImportExport.ImportSubjectPublicKeyInfo(Convert.FromBase64String(spkiBase64));
        var digest = PublicKeyFingerprint.Compute(material);
        return new KeyView
        {
            OwnerLabel = ownerLabel,
            Purpose = purpose,
            KeyVersion = keyVersion,
            PublicKey = material,
            FingerprintText = PublicKeyFingerprint.ToLetterFormat(digest),
        };
    }
}
