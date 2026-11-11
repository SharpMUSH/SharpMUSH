using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Database;

/// <summary>An object in several zones (<see cref="IZoneListStore"/>), as a RhostMUSH import leaves one.</summary>
public class MultiZoneTests : ServerTestBase
{
	private IZoneListStore Zones => WebAppFactoryArg.Services.GetRequiredService<IZoneListStore>();

	private async Task<DBRef> CreateAsync(string prefix)
		=> DBRef.Parse(await Eval($"create({TestIsolationHelpers.GenerateUniqueName(prefix)})"));

	private async Task<int[]> ZonesOfAsync(DBRef obj)
		=> [.. (await Mediator.Send(new GetZonesOfQuery(obj))).Select(zone => zone.Object().DBRef.Number)];

	[Test]
	public async Task TheListKeepsItsOrderAndItsFirstIsTheZone()
	{
		var obj = await CreateAsync("MzObj");
		var a = await CreateAsync("MzA");
		var b = await CreateAsync("MzB");
		var c = await CreateAsync("MzC");

		await Zones.SetZonesAsync(obj, [a, b, c, b]);

		await Assert.That(await ZonesOfAsync(obj)).IsEquivalentTo([a.Number, b.Number, c.Number], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(await Eval($"zone({obj})")).StartsWith($"#{a.Number}:");
	}

	[Test]
	public async Task EachZoneListsItsMembers()
	{
		var obj = await CreateAsync("MzMember");
		var a = await CreateAsync("MzMemberA");
		var b = await CreateAsync("MzMemberB");
		await Zones.SetZonesAsync(obj, [a, b]);

		foreach (var zone in new[] { a, b })
		{
			var members = await Mediator.CreateStream(new GetObjectsByZoneQuery(zone)).Select(m => m.DBRef.Number).ToListAsync();
			await Assert.That(members).Contains(obj.Number);
		}

		await Assert.That(await Eval($"lsearch(all,zone,{b})")).Contains($"#{obj.Number}");
	}

	[Test]
	public async Task DestroyingTheFirstZoneMovesTheNextUp()
	{
		var obj = await CreateAsync("MzDestroy");
		var a = await CreateAsync("MzDestroyA");
		var b = await CreateAsync("MzDestroyB");
		var c = await CreateAsync("MzDestroyC");
		await Zones.SetZonesAsync(obj, [a, b, c]);

		await Mediator.Send(new DeleteObjectCommand(a));
		await Assert.That(await ZonesOfAsync(obj)).IsEquivalentTo([b.Number, c.Number], TUnit.Assertions.Enums.CollectionOrdering.Matching);

		await Mediator.Send(new DeleteObjectCommand(c));
		await Assert.That(await ZonesOfAsync(obj)).IsEquivalentTo([b.Number]);
	}

	/// <summary>A builder with objects of their own, so what they are told is theirs alone.</summary>
	private async Task<(TestIsolationHelpers.TestPlayer Builder, DBRef[] Objects)> BuilderWithAsync(string prefix, int count)
	{
		var builder = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, prefix);
		var objects = new DBRef[count];
		for (var i = 0; i < count; i++)
		{
			objects[i] = await CreateAsync($"{prefix}{i}");
			await Cmd($"@chown {objects[i]}={builder.DbRef}");
		}

		return (builder, objects);
	}

	private async Task<List<string>> RunAsync(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.For(player.DbRef).Count;
		await CmdAs(player.DbRef, player.Handle, command);
		return [.. Notifications.For(player.DbRef).Skip(before)];
	}

	[Test]
	public async Task ChzoneLeavesOneZoneAndAddAndRemoveChangeTheList()
	{
		var (builder, objects) = await BuilderWithAsync("MzChzone", 4);
		var (obj, a, b, c) = (objects[0], objects[1], objects[2], objects[3]);

		try
		{
			await RunAsync(builder, $"@chzone {obj}={a}");
			await Assert.That(await RunAsync(builder, $"@chzone/add {obj}={b}")).Contains("Zone changed.");
			await RunAsync(builder, $"@chzone/add {obj}={c}");
			await Assert.That(await ZonesOfAsync(obj)).IsEquivalentTo([a.Number, b.Number, c.Number], TUnit.Assertions.Enums.CollectionOrdering.Matching);

			await Assert.That(await RunAsync(builder, $"@chzone/add {obj}={b}")).Contains("That object is already in that zone.");

			await RunAsync(builder, $"@chzone/remove {obj}={a}");
			await Assert.That(await ZonesOfAsync(obj)).IsEquivalentTo([b.Number, c.Number], TUnit.Assertions.Enums.CollectionOrdering.Matching);
			await Assert.That(await RunAsync(builder, $"@chzone/remove {obj}={a}")).Contains("That object is not in that zone.");

			await RunAsync(builder, $"@chzone {obj}={a}");
			await Assert.That(await ZonesOfAsync(obj)).IsEquivalentTo([a.Number]);
		}
		finally
		{
			await ConnectionService.Disconnect(builder.Handle);
		}
	}

	[Test]
	public async Task ChzoneToTheFirstZoneLeavesOnlyThatOne()
	{
		var (builder, objects) = await BuilderWithAsync("MzFirst", 3);
		var (obj, a, b) = (objects[0], objects[1], objects[2]);

		try
		{
			await Zones.SetZonesAsync(obj, [a, b]);
			await Assert.That(await RunAsync(builder, $"@chzone {obj}={a}")).Contains("Zone changed.");
			await Assert.That(await ZonesOfAsync(obj)).IsEquivalentTo([a.Number]);
		}
		finally
		{
			await ConnectionService.Disconnect(builder.Handle);
		}
	}

	[Test]
	public async Task ACloneAndANewObjectTakeEveryZone()
	{
		var (builder, objects) = await BuilderWithAsync("MzInherit", 3);
		var (obj, a, b) = (objects[0], objects[1], objects[2]);

		try
		{
			await Zones.SetZonesAsync(obj, [a, b]);
			var clone = DBRef.Parse(await Eval($"clone({obj})"));
			await Assert.That(await ZonesOfAsync(clone)).IsEquivalentTo([a.Number, b.Number],
				TUnit.Assertions.Enums.CollectionOrdering.Matching);

			await Zones.SetZonesAsync(builder.DbRef, [b, a]);
			var created = string.Join("\n", await RunAsync(builder,
				$"@create {TestIsolationHelpers.GenerateUniqueName("MzInheritNew")}"));
			var number = System.Text.RegularExpressions.Regex.Match(created, @"Created: Object #(\d+)\.");
			await Assert.That(number.Success).IsTrue();
			await Assert.That(await ZonesOfAsync(new DBRef(int.Parse(number.Groups[1].Value))))
				.IsEquivalentTo([b.Number, a.Number], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
		finally
		{
			await ConnectionService.Disconnect(builder.Handle);
		}
	}

	[Test]
	public async Task AnAddedZoneThatWouldLoopIsRefused()
	{
		var (builder, objects) = await BuilderWithAsync("MzLoop", 3);
		var (obj, a, b) = (objects[0], objects[1], objects[2]);

		try
		{
			await RunAsync(builder, $"@chzone {obj}={a}");
			await RunAsync(builder, $"@chzone {b}={obj}");
			await Assert.That(await RunAsync(builder, $"@chzone/add {obj}={b}")).Contains("You can't make circular zones!");
			await Assert.That(await ZonesOfAsync(obj)).IsEquivalentTo([a.Number]);
		}
		finally
		{
			await ConnectionService.Disconnect(builder.Handle);
		}
	}

	/// <summary>
	/// Whoever passes the zone lock of any of an object's zones controls it, where zones hand out control
	/// (<c>zone_control_zmp_only</c> off; the test world keeps it on, so the service is built with it off).
	/// </summary>
	[Test]
	public async Task PassingAnyZoneLockGivesControl()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "MzOwner");
		var builder = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "MzBuilder");
		var obj = await CreateAsync("MzControlled");
		var a = await CreateAsync("MzControlA");
		var b = await CreateAsync("MzControlB");
		await Cmd($"@chown {obj}={owner}");
		await Cmd($"@lock/zone {a}=#{owner.Number}");
		await Cmd($"@lock/zone {b}=#{builder.Number}");

		var services = WebAppFactoryArg.Services;
		var current = services.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>().CurrentValue;
		var options = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		options.CurrentValue.Returns(current with { Database = current.Database with { ZoneControlZmpOnly = false } });
		var permissions = new PermissionService(services.GetRequiredService<ILockService>(), options,
			services.GetRequiredService<IRealityPolicy>(), ConnectionService,
			new Lazy<IAttributeService>(services.GetRequiredService<IAttributeService>),
			new Lazy<Mediator.IMediator>(() => Mediator));

		var who = (await Mediator.Send(new GetObjectNodeQuery(builder))).Expect<AnySharpObject>();
		await Zones.SetZonesAsync(obj, [a]);
		var target = (await Mediator.Send(new GetObjectNodeQuery(obj))).Expect<AnySharpObject>();
		await Assert.That(await permissions.Controls(who, target)).IsFalse();

		await Mediator.Send(new SetObjectZonesCommand(target, [
			(await Mediator.Send(new GetObjectNodeQuery(a))).Expect<AnySharpObject>(),
			(await Mediator.Send(new GetObjectNodeQuery(b))).Expect<AnySharpObject>()]));
		await Assert.That(await permissions.Controls(who, target)).IsTrue();
	}

	/// <summary>A retired object restored from the recover list comes back in each zone still standing.</summary>
	[Test]
	public async Task RecoveringKeepsTheZones()
	{
		var obj = await CreateAsync("MzRecover");
		var a = await CreateAsync("MzRecoverA");
		var b = await CreateAsync("MzRecoverB");
		await Zones.SetZonesAsync(obj, [a, b]);

		var objid = DBRef.Parse(await Eval($"objid({obj})"));
		await Assert.That(await Mediator.Send(new RetireObjectCommand(objid, null))).IsTrue();
		await Cmd($"@recover {obj}");

		await Assert.That(await ZonesOfAsync(obj)).IsEquivalentTo([a.Number, b.Number], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ExamineListsEveryZone()
	{
		var (builder, objects) = await BuilderWithAsync("MzExamine", 3);
		try
		{
			await Zones.SetZonesAsync(objects[0], [objects[1], objects[2]]);

			var output = string.Join("\n", await RunAsync(builder, $"examine {objects[0]}"));
			await Assert.That(output).Contains($"Zone: {await Eval($"name({objects[1]})")}(#{objects[1].Number}")
				.And.Contains($", {await Eval($"name({objects[2]})")}(#{objects[2].Number}");
		}
		finally
		{
			await ConnectionService.Disconnect(builder.Handle);
		}
	}
}
