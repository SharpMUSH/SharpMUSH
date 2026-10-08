using System.Collections.Immutable;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// Which roles <see cref="AdministrativeCapabilityService"/> says an actor holds: everyone, the roles
/// and overrides on the character being played, and the account's own.
/// </summary>
public class AdministrativeCapabilityTests
{
	[Test]
	public async Task EveryActiveAccountHoldsEveryone()
	{
		var (service, _, _, _) = Build();
		var context = await service.GetContextAsync(new("a"));
		await Assert.That(context.Roles.Select(r => r.Slug)).Contains(BuiltInRoles.EveryoneSlug);
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.WikiRead)).IsTrue();
	}

	[Test]
	public async Task AccountWithoutCharactersHoldsEveryoneOnly()
	{
		var (service, _, _, _) = Build();
		var context = await service.GetContextAsync(new("a"));
		await Assert.That(context.Roles.Select(r => r.Slug).ToArray()).IsEquivalentTo([BuiltInRoles.EveryoneSlug]);
		await Assert.That(BuiltInRoles.TierOf(context)).IsEqualTo(PortalRole.Guest);
	}

	[Test]
	public async Task CharacterRolesApplyWhilePlayingIt()
	{
		var (service, accounts, registry, _) = Build();
		var wizard = Player(7);
		var alt = Player(8);
		await registry.AssignRoleToObjectAsync(7, BuiltInRoles.WizardSlug);
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([wizard, alt]));
		var slugs = (await service.GetContextAsync(new("a", wizard.Object.DBRef, wizard.Object.DBRef))).Roles.Select(r => r.Slug).ToArray();
		await Assert.That(slugs).IsEquivalentTo([BuiltInRoles.EveryoneSlug, BuiltInRoles.PlayerSlug, BuiltInRoles.WizardSlug]);
		var altSlugs = (await service.GetContextAsync(new("a", alt.Object.DBRef, alt.Object.DBRef))).Roles.Select(r => r.Slug).ToArray();
		await Assert.That(altSlugs).IsEquivalentTo([BuiltInRoles.EveryoneSlug, BuiltInRoles.PlayerSlug]);
	}

	[Test]
	public async Task WithNoCharacterChosenAnAccountHoldsItsCharactersRoles()
	{
		var (service, accounts, registry, _) = Build();
		var builder = Player(7);
		await registry.AssignRoleToObjectAsync(7, BuiltInRoles.BuilderSlug);
		await registry.SetObjectOverrideAsync(7, PortalPermission.MediaUpload, PermissionState.Deny);
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([builder]));
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.DiagnosticsProfile)).IsTrue();
		// A character's override applies only while playing it.
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.MediaUpload)).IsTrue();
		await Assert.That(await service.AuthorizeAsync(new("a", builder.Object.DBRef, builder.Object.DBRef), PortalPermission.MediaUpload)).IsFalse();
	}

	[Test]
	public async Task ObjectOverrideBeatsAccountOverride()
	{
		var (service, accounts, registry, _) = Build();
		var player = Player(7);
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([player]));
		await registry.SetAccountOverrideAsync("a", PortalPermission.WikiDelete, PermissionState.Allow);
		await registry.SetObjectOverrideAsync(7, PortalPermission.WikiDelete, PermissionState.Deny);
		var actor = new CapabilityActor("a", player.Object.DBRef, player.Object.DBRef);
		await Assert.That(await service.AuthorizeAsync(actor, PortalPermission.WikiDelete)).IsFalse();
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.WikiDelete)).IsTrue();
	}

	[Test]
	public async Task PlayerOneIsTheOwner()
	{
		var (service, accounts, _, _) = Build();
		var god = Player(1);
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([god]));
		var context = await service.GetContextAsync(new("a"));
		await Assert.That(context.IsOwner).IsTrue();
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.ServerAdmin)).IsTrue();
	}

	[Test]
	public async Task AssignedRolesAndOverridesApply()
	{
		var (service, accounts, registry, _) = Build();
		var player = Player(7);
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([player]));
		await registry.AssignRoleToAccountAsync("a", "helper");
		await registry.SetAccountOverrideAsync("a", PortalPermission.MediaUpload, PermissionState.Deny);
		var actor = new CapabilityActor("a", player.Object.DBRef, player.Object.DBRef);
		await Assert.That(await service.AuthorizeAsync(actor, PortalPermission.PlayersView)).IsTrue();
		await Assert.That(await service.AuthorizeAsync(actor, PortalPermission.MediaUpload)).IsFalse();
		await Assert.That(await service.AuthorizeAsync(actor, PortalPermission.SoftcodeUse)).IsTrue();
	}

	[Test]
	public async Task ExecutionRechecksRevocationAndAccountStatus()
	{
		var (service, _, registry, account) = Build();
		await registry.AssignRoleToAccountAsync("a", "helper");
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.PlayersView)).IsTrue();
		await registry.RemoveRoleFromAccountAsync("a", "helper");
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.PlayersView)).IsFalse();
		await registry.AssignRoleToAccountAsync("a", "helper");
		account.Status = AccountStatus.Disabled;
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.PlayersView)).IsFalse();
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.WikiRead)).IsFalse();
	}

	[Test]
	public async Task ActiveCharacterMustBeLinkedAndExecuteAsItself()
	{
		var (service, accounts, _, _) = Build();
		var player = Player(7);
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([player]));
		var active = player.Object.DBRef;
		await Assert.That(await service.AuthorizeAsync(new("a", active, active), PortalPermission.SnapshotCapture)).IsTrue();
		await Assert.That(await service.AuthorizeAsync(new("a", active, new DBRef(8, 1)), PortalPermission.SnapshotCapture)).IsFalse();
		await Assert.That(await service.AuthorizeAsync(new("a", new DBRef(8, 1), new DBRef(8, 1)), PortalPermission.SnapshotCapture)).IsFalse();
		await Assert.That(await service.AuthorizeAsync(new("a", new DBRef(7), new DBRef(7)), PortalPermission.SnapshotCapture)).IsFalse();
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([]));
		await Assert.That(await service.AuthorizeAsync(new("a", active, active), PortalPermission.SnapshotCapture)).IsFalse();
	}

	[Test]
	public async Task LinkedOwnerDoesNotElevateAnotherActiveCharacter()
	{
		var (service, accounts, _, _) = Build();
		var god = Player(1);
		var player = Player(7);
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([god, player]));
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.ServerAdmin)).IsTrue();
		var actor = new CapabilityActor("a", player.Object.DBRef, player.Object.DBRef);
		await Assert.That(await service.AuthorizeAsync(actor, PortalPermission.ServerAdmin)).IsFalse();
		await Assert.That((await service.GetContextAsync(actor)).IsOwner).IsFalse();
	}

	[Test]
	public async Task GameAdapterRejectsUnlinkedDisabledAndBareReferences()
	{
		var (service, accounts, _, account) = Build();
		var executor = new DBRef(7, 1);
		await Assert.That(await service.GetGameActorAsync(executor)).IsNull();
		accounts.GetAccountForCharacterAsync(executor, Arg.Any<CancellationToken>()).Returns(account);
		await Assert.That(await service.GetGameActorAsync(executor)).IsNotNull();
		await Assert.That(await service.GetGameActorAsync(new DBRef(7))).IsNull();
		account.Status = AccountStatus.Disabled;
		await Assert.That(await service.GetGameActorAsync(executor)).IsNull();
	}

	internal static SharpPlayer Player(int number, string[]? flags = null, string[]? powers = null, ObjectGrants? grants = null) => new()
	{
		PasswordHash = "", Quota = 0, Home = null!, Location = null!,
		Object = new()
		{
			Key = number, CreationTime = 1, Name = "player" + number, Type = "PLAYER", Locks = ImmutableDictionary<string, SharpLockData>.Empty,
			Owner = null!, Attributes = null!, LazyAttributes = null!, AllAttributes = null!, LazyAllAttributes = null!,
			Flags = new(() => (flags ?? []).Select(f => new SharpObjectFlag
			{ Name = f, Symbol = f[..1], SetPermissions = [], UnsetPermissions = [], TypeRestrictions = [], System = true }).ToAsyncEnumerable()),
			Grants = new(_ => Task.FromResult(grants ?? ObjectGrants.None)),
			Powers = new(() => (powers ?? []).Select(p => new SharpPower
			{ Name = p, System = true, SetPermissions = [], UnsetPermissions = [], TypeRestrictions = [] }).ToAsyncEnumerable()),
			Parent = null!, Zone = null!, Children = null!
		}
	};

	private static (AdministrativeCapabilityService, IAccountService, InMemoryRoleRegistry, SharpAccount) Build()
	{
		var accounts = Substitute.For<IAccountService>();
		var registry = InMemoryRoleRegistry.Seeded();
		var account = new SharpAccount { Id = "a", Username = "a", PasswordHash = "" };
		accounts.GetByIdAsync("a", Arg.Any<CancellationToken>()).Returns(account);
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([]));
		return (new(accounts, registry, new PermissionResolver()), accounts, registry, account);
	}
}
