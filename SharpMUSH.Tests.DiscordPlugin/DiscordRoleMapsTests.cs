using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Roles;

namespace SharpMUSH.Tests.DiscordPlugin;

/// <summary>Mapping a game role onto a Discord role, under the rule that governs assigning roles.</summary>
public class DiscordRoleMapsTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

	private static SharpRole Role(string slug, int priority, bool administrator = false) => new()
	{
		Slug = slug,
		Name = char.ToUpperInvariant(slug[0]) + slug[1..],
		Priority = priority,
		Permissions = administrator
			? new Dictionary<string, PermissionState> { [PortalPermission.Administrator] = PermissionState.Allow }
			: [],
	};

	/// <summary>Someone whose highest role sits at <paramref name="priority"/>.</summary>
	private static PermissionContext Actor(int priority)
		=> new([Role("top", priority)], new Dictionary<string, PermissionState>(), false);

	private static DiscordRoleInfo Discord(ulong id, string name, string? problem = null)
		=> new(id, 900, name, "Guild", Everyone: false, Managed: false, Linked: false, problem);

	private sealed class Fixture
	{
		public MemoryRoleStore Store { get; } = new();
		public IDiscordRoles Discord { get; } = Substitute.For<IDiscordRoles>();
		public IRoleRegistryService Registry { get; } = Substitute.For<IRoleRegistryService>();
		public DiscordRoleMaps Maps { get; }

		public Fixture(params SharpRole[] roles)
		{
			foreach (var role in roles)
			{
				Registry.GetRoleAsync(role.Slug, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Found<SharpRole>>(role));
			}

			Registry.GetRoleAsync(Arg.Is<string>(slug => roles.All(role => role.Slug != slug)), Arg.Any<CancellationToken>())
				.Returns(Task.FromResult<Found<SharpRole>>(new NotFound()));
			var services = new ServiceCollection().AddSingleton(Registry).BuildServiceProvider();
			Maps = new DiscordRoleMaps(Store, Discord, services, new FixedTime(Now));
		}
	}

	private sealed class FixedTime(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	[Test]
	public async Task ARoleBelowYourOwn_IsMapped_AndItsServerGoneOver()
	{
		var fixture = new Fixture(Role("builder", 10));
		fixture.Discord.Find("Builders").Returns(new Result<DiscordRoleInfo>(Discord(500, "Builders")));

		var mapped = (await fixture.Maps.MapAsync(Actor(50), "#1:1", "builder", "Builders", false, CancellationToken.None))
			.Expect<DiscordRoleMappingStatus>();

		await Assert.That(mapped.Mapping).IsEqualTo(new DiscordRoleMapping(500, 900, "Builders", "builder", DiscordRoleMode.Report, Now, "#1:1"));
		await Assert.That(fixture.Store.Mapping(500)).IsEqualTo(mapped.Mapping);
		fixture.Discord.Received(1).Pass(900);
	}

	[Test]
	[Arguments("player", 0, false, "held implicitly")]
	[Arguments("admins", 10, true, "allows administrator")]
	[Arguments("council", 60, false, "not below your highest role")]
	[Arguments("wizard", 40, false, "@discord/map/confirm")]
	public async Task SomeRoles_AreRefused(string slug, int priority, bool administrator, string reason)
	{
		var fixture = new Fixture(Role(slug, priority, administrator));

		var refused = (await fixture.Maps.MapAsync(Actor(50), "#1:1", slug, "500", false, CancellationToken.None)).Expect<Error<string>>();

		await Assert.That(refused.Value).Contains(reason);
		await Assert.That(fixture.Store.Mappings()).IsEmpty();
	}

	[Test]
	public async Task AStaffRole_IsMappedOnceConfirmed()
	{
		var fixture = new Fixture(Role("wizard", 40));
		fixture.Discord.Find("500").Returns(new Result<DiscordRoleInfo>(Discord(500, "Staff")));

		await Assert.That(await fixture.Maps.MapAsync(Actor(50), "#1:1", "wizard", "500", true, CancellationToken.None))
			.IsTypeOf<Result<DiscordRoleMappingStatus>>();
		await Assert.That(fixture.Store.Mapping(500)?.Slug).IsEqualTo("wizard");
	}

	[Test]
	public async Task TheLinkedRole_EveryoneAndIntegrationRoles_AreRefused()
	{
		var fixture = new Fixture(Role("builder", 10));
		fixture.Discord.Find("linked").Returns(new Result<DiscordRoleInfo>(Discord(1, "Linked") with { Linked = true }));
		fixture.Discord.Find("everyone").Returns(new Result<DiscordRoleInfo>(Discord(2, "@everyone") with { Everyone = true }));
		fixture.Discord.Find("booster").Returns(new Result<DiscordRoleInfo>(Discord(3, "Booster") with { Managed = true }));

		foreach (var name in new[] { "linked", "everyone", "booster" })
		{
			await Assert.That(await fixture.Maps.MapAsync(Actor(50), "#1:1", "builder", name, false, CancellationToken.None))
				.Satisfies(result => result is Error<string>);
		}

		await Assert.That(fixture.Store.Mappings()).IsEmpty();
	}

	[Test]
	public async Task ARoleMappedToAnother_IsRefused()
	{
		var fixture = new Fixture(Role("builder", 10), Role("staff", 20));
		await fixture.Store.SaveAsync(new DiscordRoleMapping(500, 900, "Builders", "staff", DiscordRoleMode.Report, Now, "#1:1"));
		fixture.Discord.Find("500").Returns(new Result<DiscordRoleInfo>(Discord(500, "Builders")));

		var refused = (await fixture.Maps.MapAsync(Actor(50), "#1:1", "builder", "500", false, CancellationToken.None)).Expect<Error<string>>();

		await Assert.That(refused.Value).Contains("already mapped to staff");
	}

	[Test]
	public async Task Enforcing_PutsBackWhatWasChangedByHand_AndUnmappingKeepsMembersRoles()
	{
		var fixture = new Fixture();
		await fixture.Store.SaveAsync(new DiscordRoleMapping(500, 900, "Builders", "builder", DiscordRoleMode.Report, Now, "#1:1"));

		var enforced = (await fixture.Maps.ModeAsync("@Builders", DiscordRoleMode.Enforce, CancellationToken.None)).Expect<DiscordRoleMapping>();
		await Assert.That(enforced.Mode).IsEqualTo(DiscordRoleMode.Enforce);
		fixture.Discord.Received(1).Settle(500);

		await Assert.That((await fixture.Maps.UnmapAsync("500", CancellationToken.None)).Expect<DiscordRoleMapping>().RoleId).IsEqualTo(500UL);
		await Assert.That(fixture.Store.Mappings()).IsEmpty();
		fixture.Discord.DidNotReceive().Pass(Arg.Any<ulong>());
	}
}
