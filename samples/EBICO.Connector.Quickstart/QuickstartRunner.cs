using System.IO.Compression;
using System.Text;
using EBICO.Connector;
using EBICO.Connector.Download;
using EBICO.Connector.Onboarding;
using EBICO.Connector.Onboarding.Keys;
using EBICO.Connector.Upload;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Server;
using EBICO.Server.Http;
using EBICO.Server.State;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EBICO.Connector.Quickstart;

/// <summary>
/// The self-contained EBICO quickstart: hosts the <c>EBICO.Server</c> emulator in-process on an
/// ephemeral loopback port, seeds the master data a flow needs, and drives the <c>EBICO.Connector</c>
/// through the full round-trip — key generation, onboarding (INI/HIA/HPB), a SEPA credit-transfer
/// upload (CCT) and a statement download (C53). Everything runs without an external server or a real
/// bank, so <c>dotnet run</c> exercises the whole library end to end.
/// </summary>
public static class QuickstartRunner
{
    // EBICS-identifier-safe values (no hyphens/underscores), mirroring the E2E harness conventions.
    private const string Host = "EBICOHOST";
    private const string Partner = "PARTNER01";
    private const string User = "USER01";
    private const EbicsVersion Version = EbicsVersion.H005;

    /// <summary>
    /// Runs the complete quickstart flow and returns a summary of each step.
    /// </summary>
    /// <param name="log">Where human-readable progress is written (e.g. <see cref="Console.Out"/>).</param>
    /// <param name="ct">A token to cancel the run.</param>
    /// <returns>The per-step outcome; <see cref="QuickstartResult.Success"/> is <see langword="true"/> only when every step succeeded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="log"/> is <see langword="null"/>.</exception>
    public static async Task<QuickstartResult> RunAsync(TextWriter log, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(log);

        // 1) EBICO.Server in-process starten (Kestrel, ephemerer Loopback-Port).
        var builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning); // Server-Logspam aus dem Demo-Output halten
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddEbicoServer();

        var app = builder.Build();
        var serverOptions = app.Services.GetRequiredService<IOptions<EbicoServerOptions>>().Value;
        app.MapEbicsEndpoint(serverOptions.EndpointPath);

        await app.StartAsync(ct);
        try
        {
            var baseUrl = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();
            var endpointUrl = baseUrl.TrimEnd('/') + serverOptions.EndpointPath;
            log.WriteLine($"EBICO.Server läuft auf {baseUrl} (EBICS-Endpoint {endpointUrl}).");

            // 2) Stammdaten + Bank-Keypair serverseitig seeden. Der Subscriber startet in 'New';
            //    das echte INI/HIA treibt ihn nach Initialized/Ready.
            var hostId = HostId.Create(Host);
            var partnerId = PartnerId.Create(Partner);
            var userId = UserId.Create(User);

            var master = app.Services.GetRequiredService<IMasterDataManager>();
            await master.SaveBankAsync(new Bank(hostId), ct);
            await master.SavePartnerAsync(new Partner(hostId, partnerId), ct);
            await master.SaveSubscriberAsync(
                new Subscriber(
                    hostId,
                    partnerId,
                    userId,
                    permissions: [new SubscriberPermission("CCT", SignatureClass.T), new SubscriberPermission("C53", SignatureClass.T)]),
                ct);

            // Bekanntes Bank-Keypair seeden, damit HPB die zurückgelieferten Fingerprints prüfen kann.
            var bankKeys = new BankKeyPair(
                RsaKeyMaterial.Generate(),
                KeyVersions.Default(KeyPurpose.Authentication, Version).Version,
                RsaKeyMaterial.Generate(),
                KeyVersions.Default(KeyPurpose.Encryption, Version).Version);
            await app.Services.GetRequiredService<IServerBankKeyStore>().SetAsync(hostId, bankKeys, ct);

            // 3) Connector-DI gegen den laufenden Server aufbauen.
            var services = new ServiceCollection();
            services.AddEbicoConnector(o =>
            {
                o.Url = endpointUrl; // absolute URL inkl. Endpoint-Pfad
                o.HostId = Host;
                o.PartnerId = Partner;
                o.UserId = User;
                o.Version = Version;
            });
            services.AddEbicoOnboarding();
            services.AddEbicoUpload();
            services.AddEbicoDownload();
            await using var provider = services.BuildServiceProvider();
            var client = provider.GetRequiredService<IEbicsClient>();

            // 4) Teilnehmerschlüssel (A00x/X002/E002) erzeugen und ablegen.
            await provider.GetRequiredService<ISubscriberKeyGenerator>().GenerateAsync(ct: ct);
            log.WriteLine("Teilnehmerschlüssel erzeugt (A00x/X002/E002).");

            // 5) Onboarding: INI -> HIA -> HPB (Bank-Fingerprints in-flow geprüft).
            var ini = await client.Send(new IniRequest { IncludeLetter = false }, ct);
            var hia = await client.Send(new HiaRequest { IncludeLetter = false }, ct);
            var hpb = await client.Send(
                new HpbRequest
                {
                    ExpectedAuthenticationKeyDigest = PublicKeyFingerprint.Compute(bankKeys.Authentication),
                    ExpectedEncryptionKeyDigest = PublicKeyFingerprint.Compute(bankKeys.Encryption),
                },
                ct);
            log.WriteLine($"Onboarding: INI {ini.ReturnCode}, HIA {hia.ReturnCode}, HPB {hpb.ReturnCode}.");

            // 6) Upload: SEPA Credit Transfer (pain.001).
            var pain = Encoding.UTF8.GetBytes(SamplePain.CreditTransfer(12.34m, 56.78m));
            var upload = await client.Send(new CctUploadRequest { Pain001 = pain }, ct);
            log.WriteLine(
                $"Upload (CCT): {upload.ReturnCode}, TxId {upload.Value?.TransactionId}, {upload.Value?.NumSegments} Segment(e).");

            // 7) Download: Kontoauszug camt.053 (C53) mit Parse-Hook (ZIP-Einträge auslesen).
            var download = await client.Send(
                new C53DownloadRequest { Parse = zip => ZipEntryNames(zip) },
                ct);
            var entries = download.Value?.ParsedAs<IReadOnlyList<string>>() ?? [];
            log.WriteLine(
                $"Download (C53): {download.ReturnCode}, {download.Value?.NumSegments} Segment(e), {download.Value?.OrderData.Length ?? 0} Byte, Einträge: {string.Join(", ", entries)}.");

            var result = new QuickstartResult(
                Success: ini.IsSuccess && hia.IsSuccess && hpb.IsSuccess && upload.IsSuccess && download.IsSuccess,
                IniReturnCode: ini.ReturnCode,
                HiaReturnCode: hia.ReturnCode,
                HpbReturnCode: hpb.ReturnCode,
                UploadReturnCode: upload.ReturnCode,
                UploadTransactionId: upload.Value?.TransactionId,
                DownloadReturnCode: download.ReturnCode,
                DownloadSegments: download.Value?.NumSegments ?? 0);

            log.WriteLine(result.Success ? "Quickstart erfolgreich abgeschlossen." : "Quickstart mit Fehlern beendet.");
            return result;
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
            await app.DisposeAsync();
        }
    }

    /// <summary>Reads the entry names from a ZIP container (the C53 download's order data).</summary>
    private static IReadOnlyList<string> ZipEntryNames(ReadOnlyMemory<byte> zip)
    {
        using var stream = new MemoryStream(zip.ToArray(), writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.Entries.Select(entry => entry.FullName).ToArray();
    }
}
