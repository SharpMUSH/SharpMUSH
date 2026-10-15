using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A change to who may do what takes effect on the next check, though the grants, owner and location
/// behind each check are kept per object between writes (<c>ObjectRelationMemo</c>). Each test asks once
/// before the change, so the answer is kept, then changes it each way it can change and asks again.
/// </summary>
public class PermissionChangeTests : ServerTestBase
{
	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();

	private IRoleManagementService Roles => WebAppFactoryArg.Services.GetRequiredService<IRoleManagementService>();

	private async Task<(TestIsolationHelpers.TestPlayer Player, string Thing)> MortalAndHiddenThingAsync(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, prefix);
		var thing = $"#{(await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, prefix + "Box")).Number}";
		await Cmd($"&SECRET {thing}=hidden");
		return (player, thing);
	}

	private Task<string> Secret(TestIsolationHelpers.TestPlayer player, string thing)
		=> EvalAs(player.DbRef, $"get({thing}/SECRET)");

	[Test]
	public async Task RoleOnTheObjectTakesEffectAtOnce()
	{
		var (player, thing) = await MortalAndHiddenThingAsync("PcFlag");
		await Assert.That(await Secret(player, thing)).IsNotEqualTo("hidden");

		await Cmd($"@set #{player.DbRef.Number}=ROYALTY");
		await Assert.That(await Secret(player, thing)).IsEqualTo("hidden");

		await Cmd($"@set #{player.DbRef.Number}=!ROYALTY");
		await Assert.That(await Secret(player, thing)).IsNotEqualTo("hidden");
	}

	[Test]
	public async Task RolePermissionChangeTakesEffectAtOnce()
	{
		var (player, thing) = await MortalAndHiddenThingAsync("PcRole");
		var slug = "r" + Guid.NewGuid().ToString("N")[..12];
		var created = await Cmd($"@role/create {slug}=Staff");
		await Cmd($"@role/assign #{player.DbRef.Number}={slug}");
		await Assert.That(await Secret(player, thing)).IsNotEqualTo("hidden");

		// Only the role changes: nothing is written to the player.
		var allowed = await Cmd($"@role/allow {slug}=game.see_all");
		await Assert.That(await EvalAs(player.DbRef, "haspower(me,See_All)")).IsEqualTo("1").Because(created + " / " + allowed);
		await Assert.That(await Secret(player, thing)).IsEqualTo("hidden");

		await Cmd($"@role/delete {slug}");
		await Assert.That(await Secret(player, thing)).IsNotEqualTo("hidden");
	}

	[Test]
	public async Task AccountRoleTakesEffectAtOnce()
	{
		var (player, thing) = await MortalAndHiddenThingAsync("PcAcct");
		var account = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("PcAcctA"), null,
			"account-pass-1")).Expect<SharpAccount>();
		await Accounts.LinkCharacterAsync(account.Id!, player.DbRef);
		await Assert.That(await Secret(player, thing)).IsNotEqualTo("hidden");

		// The role goes on the account, so only the account's claims are invalidated, not the character.
		RoleActor god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		(await Roles.AssignAsync(god, account.Id!, BuiltInRoles.RoyaltySlug)).Expect<Success>();
		await Assert.That(await Secret(player, thing)).IsEqualTo("hidden");

		(await Roles.UnassignAsync(god, account.Id!, BuiltInRoles.RoyaltySlug)).Expect<Success>();
		await Assert.That(await Secret(player, thing)).IsNotEqualTo("hidden");
	}

	[Test]
	public async Task NewOwnerControlsAtOnce()
	{
		var (player, thing) = await MortalAndHiddenThingAsync("PcOwner");
		await Assert.That(await EvalAs(player.DbRef, $"controls(me,{thing})")).IsEqualTo("0");

		await Cmd($"@chown {thing}=#{player.DbRef.Number}");
		await Assert.That(await EvalAs(player.DbRef, $"controls(me,{thing})")).IsEqualTo("1");
		await Assert.That(await Secret(player, thing)).IsEqualTo("hidden");

		await Cmd($"@chown {thing}=#1");
		await Assert.That(await EvalAs(player.DbRef, $"controls(me,{thing})")).IsEqualTo("0");
		await Assert.That(await Secret(player, thing)).IsNotEqualTo("hidden");
	}

	[Test]
	public async Task MovedObjectIsFoundWhereItWent()
	{
		var thing = $"#{(await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "PcMoved")).Number}";
		var first = $"#{(await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "PcFirst")).Number}";
		var second = $"#{(await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "PcSecond")).Number}";

		await Cmd($"@tel {thing}={first}");
		await Assert.That(await Eval($"num(loc({thing}))")).IsEqualTo(first);

		await Cmd($"@tel {thing}={second}");
		await Assert.That(await Eval($"num(loc({thing}))")).IsEqualTo(second);
	}
}
