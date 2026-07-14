using AwesomeAssertions;
using EBICO.Core.Domain;

namespace EBICO.Tests.Domain;

/// <summary>
/// Tests for the <see cref="Subscriber"/> aggregate: lifecycle transitions, identity
/// preservation, system-ID handling and permission evaluation (issue #16). Tier A.
/// </summary>
public class SubscriberTests
{
    private static Subscriber NewSubscriber(
        SubscriberState state = SubscriberState.New,
        IEnumerable<SubscriberPermission>? permissions = null,
        SystemId? systemId = null)
        => new(
            HostId.Create("BANKDE01"),
            PartnerId.Create("PARTNER01"),
            UserId.Create("USER0001"),
            systemId,
            state,
            permissions);

    [Fact]
    public void New_DefaultsToNewStateWithoutPermissions()
    {
        var subscriber = new Subscriber(HostId.Create("BANKDE01"), PartnerId.Create("P1"), UserId.Create("U1"));

        subscriber.State.Should().Be(SubscriberState.New);
        subscriber.Permissions.Should().BeEmpty();
        subscriber.SystemId.Should().BeNull();
        subscriber.IsTechnicalSubscriber.Should().BeFalse();
    }

    [Fact]
    public void SystemId_WhenPresent_MarksTechnicalSubscriber()
    {
        var subscriber = NewSubscriber(systemId: SystemId.Create("SYS00001"));

        subscriber.IsTechnicalSubscriber.Should().BeTrue();
        subscriber.SystemId.Should().Be(SystemId.Create("SYS00001"));
    }

    [Theory]
    [InlineData(SubscriberState.New, SubscriberState.Initialized)]
    [InlineData(SubscriberState.Initialized, SubscriberState.Ready)]
    [InlineData(SubscriberState.New, SubscriberState.Suspended)]
    [InlineData(SubscriberState.Initialized, SubscriberState.Suspended)]
    [InlineData(SubscriberState.Ready, SubscriberState.Suspended)]
    [InlineData(SubscriberState.Suspended, SubscriberState.Ready)]
    public void Transition_AllowedTransition_ReturnsNewInstanceInTargetState(SubscriberState from, SubscriberState to)
    {
        var subscriber = NewSubscriber(from);

        var moved = subscriber.Transition(to);

        moved.State.Should().Be(to);
        moved.Should().NotBeSameAs(subscriber);
        subscriber.State.Should().Be(from, "the aggregate is immutable");
    }

    [Theory]
    [InlineData(SubscriberState.New, SubscriberState.Ready)]
    [InlineData(SubscriberState.New, SubscriberState.New)]
    [InlineData(SubscriberState.Ready, SubscriberState.Initialized)]
    [InlineData(SubscriberState.Suspended, SubscriberState.New)]
    public void Transition_IllegalTransition_Throws(SubscriberState from, SubscriberState to)
    {
        var act = () => NewSubscriber(from).Transition(to);

        act.Should().Throw<InvalidSubscriberStateTransitionException>();
    }

    [Fact]
    public void Transition_PreservesIdentityAndPermissions()
    {
        var permissions = new[] { new SubscriberPermission("CCT", SignatureClass.E) };
        var subscriber = NewSubscriber(SubscriberState.New, permissions, SystemId.Create("SYS00001"));

        var moved = subscriber.Transition(SubscriberState.Initialized);

        moved.HostId.Should().Be(subscriber.HostId);
        moved.PartnerId.Should().Be(subscriber.PartnerId);
        moved.UserId.Should().Be(subscriber.UserId);
        moved.SystemId.Should().Be(subscriber.SystemId);
        moved.Permissions.Should().BeEquivalentTo(permissions);
    }

    [Fact]
    public void CanAuthorize_TrueOnlyForBankTechnicalPermission()
    {
        var permissions = new[]
        {
            new SubscriberPermission("CCT", SignatureClass.E),
            new SubscriberPermission("STA", SignatureClass.T),
        };
        var subscriber = NewSubscriber(permissions: permissions);

        subscriber.CanAuthorize("CCT").Should().BeTrue();
        subscriber.CanAuthorize("STA").Should().BeFalse("STA is transport-only");
        subscriber.CanAuthorize("ZZZ").Should().BeFalse("there is no permission for ZZZ");
    }

    [Fact]
    public void HasPermissionFor_TrueForAnySignatureClass()
    {
        var permissions = new[]
        {
            new SubscriberPermission("CCT", SignatureClass.E),
            new SubscriberPermission("STA", SignatureClass.T),
        };
        var subscriber = NewSubscriber(permissions: permissions);

        subscriber.HasPermissionFor("CCT").Should().BeTrue("a bank-technical permission exists");
        subscriber.HasPermissionFor("STA").Should().BeTrue("a transport permission also grants the gate");
        subscriber.HasPermissionFor("ZZZ").Should().BeFalse("there is no permission for ZZZ");
    }

    [Fact]
    public void IsTransportOnlyFor_TrueWhenAllMatchingPermissionsAreTransport()
    {
        var permissions = new[]
        {
            new SubscriberPermission("STA", SignatureClass.T),
            new SubscriberPermission("CCT", SignatureClass.T),
            new SubscriberPermission("CCT", SignatureClass.B),
        };
        var subscriber = NewSubscriber(permissions: permissions);

        subscriber.IsTransportOnlyFor("STA").Should().BeTrue();
        subscriber.IsTransportOnlyFor("CCT").Should().BeFalse("CCT also carries a bank-technical B permission");
        subscriber.IsTransportOnlyFor("ZZZ").Should().BeFalse("there is no permission for ZZZ");
    }

    [Fact]
    public void WithPermission_AddsPermission_ReturningNewInstance()
    {
        var subscriber = NewSubscriber();

        var updated = subscriber.WithPermission(new SubscriberPermission("CCT", SignatureClass.E));

        updated.Should().NotBeSameAs(subscriber);
        subscriber.Permissions.Should().BeEmpty("the aggregate is immutable");
        updated.Permissions.Should().ContainSingle()
            .Which.Should().Be(new SubscriberPermission("CCT", SignatureClass.E));
    }

    [Fact]
    public void WithPermission_DuplicatePair_IsNotAddedTwice()
    {
        var subscriber = NewSubscriber(permissions: [new SubscriberPermission("CCT", SignatureClass.E)]);

        var updated = subscriber.WithPermission(new SubscriberPermission("CCT", SignatureClass.E));

        updated.Permissions.Should().ContainSingle();
    }

    [Fact]
    public void WithPermission_SameOrderTypeDifferentClass_IsAdded()
    {
        var subscriber = NewSubscriber(permissions: [new SubscriberPermission("CCT", SignatureClass.T)]);

        var updated = subscriber.WithPermission(new SubscriberPermission("CCT", SignatureClass.E));

        updated.Permissions.Should().HaveCount(2);
    }

    [Fact]
    public void WithoutPermissionsFor_RemovesEveryPermissionOfTheOrderType()
    {
        var subscriber = NewSubscriber(permissions:
        [
            new SubscriberPermission("CCT", SignatureClass.T),
            new SubscriberPermission("CCT", SignatureClass.E),
            new SubscriberPermission("STA", SignatureClass.T),
        ]);

        var updated = subscriber.WithoutPermissionsFor("CCT");

        updated.Permissions.Should().ContainSingle()
            .Which.OrderType.Should().Be("STA");
    }

    [Fact]
    public void WithoutPermissionsFor_UnknownOrderType_ReturnsSameInstance()
    {
        var subscriber = NewSubscriber(permissions: [new SubscriberPermission("STA", SignatureClass.T)]);

        subscriber.WithoutPermissionsFor("CCT").Should().BeSameAs(subscriber);
    }

    [Fact]
    public void WithPermissions_ReplacesSet_AndCollapsesDuplicates()
    {
        var subscriber = NewSubscriber(permissions: [new SubscriberPermission("OLD", SignatureClass.T)]);

        var updated = subscriber.WithPermissions(
        [
            new SubscriberPermission("CCT", SignatureClass.E),
            new SubscriberPermission("CCT", SignatureClass.E),
            new SubscriberPermission("STA", SignatureClass.T),
        ]);

        updated.Permissions.Should().HaveCount(2);
        updated.Permissions.Select(p => p.OrderType).Should().NotContain("OLD");
    }

    [Fact]
    public void WithPermissions_Null_Throws()
    {
        var act = () => NewSubscriber().WithPermissions(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
