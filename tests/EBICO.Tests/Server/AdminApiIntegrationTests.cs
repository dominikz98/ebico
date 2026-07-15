extern alias EbicoServer;
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using EBICO.Server.Http.Admin;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EBICO.Tests.Server;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// End-to-end tests for the master-data admin API (issue #30), driven through
/// <see cref="WebApplicationFactory{TEntryPoint}"/>: CRUD round-trips, referential integrity,
/// cascading deletes, permissions/lifecycle and status-code mapping. Each test uses a fresh
/// factory so the in-memory store is isolated.
/// </summary>
public class AdminApiIntegrationTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static WebApplicationFactory<ServerProgram> NewFactory() => new();

    [Fact]
    public async Task PutBank_ThenGet_RoundTrips()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();

        var put = await client.PutAsJsonAsync("/admin/banks/EBICOHOST", new BankUpsertDto("EBICO", ["H004", "H005"]), _ct);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var bank = await client.GetFromJsonAsync<BankDto>("/admin/banks/EBICOHOST", _ct);
        bank!.HostId.Should().Be("EBICOHOST");
        bank.Name.Should().Be("EBICO");
        bank.SupportedVersions.Should().BeEquivalentTo(["H004", "H005"]);
    }

    [Fact]
    public async Task PutBank_WithUrl_RoundTrips()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();

        await client.PutAsJsonAsync("/admin/banks/EBICOHOST",
            new BankUpsertDto("EBICO", ["H005"], "https://ebico.example/ebics"), _ct);

        var bank = await client.GetFromJsonAsync<BankDto>("/admin/banks/EBICOHOST", _ct);
        bank!.Url.Should().Be("https://ebico.example/ebics");
    }

    [Fact]
    public async Task PutPartner_WithAddressAndAccounts_RoundTrips()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        await client.PutAsJsonAsync("/admin/banks/EBICOHOST", new BankUpsertDto(null, null), _ct);

        await client.PutAsJsonAsync("/admin/banks/EBICOHOST/partners/PARTNER01",
            new PartnerUpsertDto(
                "Acme GmbH",
                new AddressDto("Acme GmbH", City: "Berlin"),
                [new AccountDto("DE89370400440532013000", "COBADEFFXXX", Id: "ACC1")]),
            _ct);

        var partner = await client.GetFromJsonAsync<PartnerDto>("/admin/banks/EBICOHOST/partners/PARTNER01", _ct);
        partner!.Address!.City.Should().Be("Berlin");
        partner.Accounts.Should().ContainSingle().Which.Iban.Should().Be("DE89370400440532013000");
    }

    [Fact]
    public async Task PutSubscriber_WithName_RoundTrips()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        await client.PutAsJsonAsync("/admin/banks/EBICOHOST", new BankUpsertDto(null, null), _ct);
        await client.PutAsJsonAsync("/admin/banks/EBICOHOST/partners/PARTNER01", new PartnerUpsertDto(null), _ct);

        await client.PutAsJsonAsync(
            "/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01",
            new SubscriberUpsertDto(null, null, null, "Alice"), _ct);

        var subscriber = await client.GetFromJsonAsync<SubscriberDto>(
            "/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01", _ct);
        subscriber!.Name.Should().Be("Alice");
    }

    [Fact]
    public async Task GetBank_Unknown_Returns404()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/admin/banks/NOPE", _ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutPartner_WithoutBank_Returns409()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/admin/banks/EBICOHOST/partners/PARTNER01", new PartnerUpsertDto("Kunde"), _ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PutSubscriber_WithBankButNoPartner_Returns409()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        await client.PutAsJsonAsync("/admin/banks/EBICOHOST", new BankUpsertDto(null, null), _ct);

        var response = await client.PutAsJsonAsync(
            "/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01",
            new SubscriberUpsertDto(null, null, null), _ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task FullHierarchy_CanBeCreatedAndRead()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();

        await client.PutAsJsonAsync("/admin/banks/EBICOHOST", new BankUpsertDto("EBICO", null), _ct);
        await client.PutAsJsonAsync("/admin/banks/EBICOHOST/partners/PARTNER01", new PartnerUpsertDto("Kunde"), _ct);
        var put = await client.PutAsJsonAsync(
            "/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01",
            new SubscriberUpsertDto(null, "New", [new SubscriberPermissionDto("CCT", "E")]), _ct);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var subscriber = await client.GetFromJsonAsync<SubscriberDto>(
            "/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01", _ct);
        subscriber!.UserId.Should().Be("USER01");
        subscriber.Permissions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SubscriberPermissionDto("CCT", "E"));
    }

    [Fact]
    public async Task DeleteBank_CascadesOverHttp()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        await client.PutAsJsonAsync("/admin/banks/EBICOHOST", new BankUpsertDto(null, null), _ct);
        await client.PutAsJsonAsync("/admin/banks/EBICOHOST/partners/PARTNER01", new PartnerUpsertDto(null), _ct);
        await client.PutAsJsonAsync(
            "/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01",
            new SubscriberUpsertDto(null, null, null), _ct);

        var delete = await client.DeleteAsync("/admin/banks/EBICOHOST", _ct);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetAsync("/admin/banks/EBICOHOST", _ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/admin/banks/EBICOHOST/partners/PARTNER01", _ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01", _ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutPermissions_ReplacesSet()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        await SeedSubscriberAsync(client);

        await client.PutAsJsonAsync(
            "/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01/permissions",
            new[] { new SubscriberPermissionDto("CCT", "E"), new SubscriberPermissionDto("STA", "T") }, _ct);

        var subscriber = await client.GetFromJsonAsync<SubscriberDto>(
            "/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01", _ct);
        subscriber!.Permissions.Select(p => p.OrderType).Should().BeEquivalentTo(["CCT", "STA"]);
    }

    [Fact]
    public async Task PostState_ValidTransition_Returns200_InvalidReturns409()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        await SeedSubscriberAsync(client);

        var ok = await client.PostAsJsonAsync(
            "/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01/state",
            new StateTransitionDto("Initialized"), _ct);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        // New already advanced to Initialized; jumping straight to a fresh New→Ready is illegal.
        var invalid = await client.PostAsJsonAsync(
            "/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01/state",
            new StateTransitionDto("New"), _ct);
        invalid.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PostState_UnknownSubscriber_Returns404()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01/state",
            new StateTransitionDto("Initialized"), _ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InvalidHostId_Returns400()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/admin/banks/{new string('X', 40)}", _ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvalidSignatureClass_Returns400()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        await SeedSubscriberAsync(client);

        var response = await client.PutAsJsonAsync(
            "/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01/permissions",
            new[] { new SubscriberPermissionDto("CCT", "Z") }, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task SeedSubscriberAsync(HttpClient client)
    {
        await client.PutAsJsonAsync("/admin/banks/EBICOHOST", new BankUpsertDto(null, null), _ct);
        await client.PutAsJsonAsync("/admin/banks/EBICOHOST/partners/PARTNER01", new PartnerUpsertDto(null), _ct);
        await client.PutAsJsonAsync(
            "/admin/banks/EBICOHOST/partners/PARTNER01/subscribers/USER01",
            new SubscriberUpsertDto(null, null, null), _ct);
    }
}
