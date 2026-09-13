using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database;

public class SurrealDefinitionAssignmentTests
{
	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	public async Task RealAssignmentReloadAndRemovalUseLegacyNativeIdentity(bool power, bool alias)
	{
		await using var world = await DefinitionCreationContract.Open("surrealdb");
		var table = power ? "power" : "object_flag";
		var edges = power ? "has_powers" : "has_flags";
		var aliases = power ? "alias = 'LegacyAlias'" : "aliases = ['LegacyAlias']";
		await Assert.That((await world.Client!.RawQuery($"CREATE {table}:LegacyStorage SET name = 'LegacyDefinition', {aliases}, system = false; CREATE {table}:OtherDefinition SET name = 'OtherDefinition', system = false;")).HasErrors).IsFalse();
		var god = (await world.Database.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>();
		var name = alias ? "LEGACYALIAS" : "LEGACYDEFINITION";
		await Assert.That(await Assign(world.Database, god, power, "OTHERDEFINITION", true)).IsTrue();
		await Assert.That(await Assign(world.Database, god, power, name, true)).IsTrue();
		await Assert.That(await Membership(world.Database, power)).Contains("LegacyDefinition");
		await Assert.That((await world.Client.RawQuery($"SELECT VALUE <string> id FROM {edges} WHERE in = object:1 AND out = {table}:LegacyStorage")).GetValue<List<string>>(0)!).HasSingleItem();
		await Assert.That(await Assign(world.Database, god, power, name, true)).IsFalse();
		await Assert.That(await Assign(world.Database, god, power, name, false)).IsTrue();
		await Assert.That((await Membership(world.Database, power)).Contains("LegacyDefinition")).IsFalse();
		await Assert.That(await Assign(world.Database, god, power, name, false)).IsFalse();
		await Assert.That(await Membership(world.Database, power)).Contains("OtherDefinition");
		await Assert.That((await world.Client.RawQuery($"SELECT VALUE <string> id FROM {edges} WHERE out = {table}:LegacyStorage")).GetValue<List<string>>(0)!).IsEmpty();
		await Assert.That((await world.Client.RawQuery($"SELECT VALUE name FROM {table}:LegacyStorage")).GetValue<List<string>>(0)!).IsEquivalentTo(["LegacyDefinition"]);
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task MissingAssignmentEndpointDoesNotCreateADanglingRelationship(bool power, bool missingDefinition)
	{
		await using var world = await DefinitionCreationContract.Open("surrealdb");
		var obj = missingDefinition ? (await world.Database.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>()
			: new Services.TestObjectFactory().CreateThing(999999, "missing");
		var name = missingDefinition ? "MISSING_DEFINITION" : power ? "Can_Spoof" : "AUDIBLE";
		await Assert.That(await Assign(world.Database, obj, power, name, true)).IsFalse();
		var edges = power ? "has_powers" : "has_flags";
		var table = power ? "power" : "object_flag";
		await Assert.That((await world.Client!.RawQuery($"SELECT VALUE <string> id FROM {edges} WHERE in = object:999999 OR out = {table}:MISSING_DEFINITION")).GetValue<List<string>>(0)!).IsEmpty();
		await Assert.That(await Assign(world.Database, obj, power, name, false)).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SeededSystemAssignmentsRemainAllowedAndConcurrentCreationIsUnique(bool power)
	{
		await using var world = await DefinitionCreationContract.Open("surrealdb");
		var god = (await world.Database.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>();
		var name = power ? "Can_Spoof" : "AUDIBLE";
		await Assert.That(power ? (await world.Database.GetPowerAsync(name))!.System
			: (await world.Database.GetObjectFlagAsync(name))!.System).IsTrue();
		var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Assign(world.Database, god, power, name, true).AsTask()));
		var edges = power ? "has_powers" : "has_flags";
		var destination = power ? "power:Can_Spoof" : "object_flag:AUDIBLE";
		var duplicate = await world.Client!.RawQuery($"RELATE object:1->{edges}->{destination}");
		await Assert.That(duplicate.HasErrors).IsTrue();
		await Assert.That(results.Count(success => success)).IsEqualTo(1);
		await Assert.That((await Membership(world.Database, power)).Count(value => value.Equals(name, StringComparison.OrdinalIgnoreCase))).IsEqualTo(1);
		await Assert.That(await Assign(world.Database, god, power, name, false)).IsTrue();
		await Assert.That(await Assign(world.Database, god, power, name, false)).IsFalse();
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task CancelledAssignmentDoesNotMutate(bool power, bool set)
	{
		await using var world = await DefinitionCreationContract.Open("surrealdb");
		var god = (await world.Database.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>();
		var name = power ? "Can_Spoof" : "AUDIBLE";
		if (!set) await Assert.That(await Assign(world.Database, god, power, name, true)).IsTrue();
		var before = await Membership(world.Database, power);
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();
		await Assert.That(async () => await Assign(world.Database, god, power, name, set, cancellation.Token)).Throws<OperationCanceledException>();
		await Assert.That(await Membership(world.Database, power)).IsEquivalentTo(before);
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task RejectedAssignmentWriteReturnsFailureAndRetainsMembership(bool power, bool set)
	{
		await using var world = await DefinitionCreationContract.Open("surrealdb");
		var god = (await world.Database.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>();
		var name = power ? "Can_Spoof" : "AUDIBLE";
		if (!set) await Assert.That(await Assign(world.Database, god, power, name, true)).IsTrue();
		var before = await Membership(world.Database, power);
		var edges = power ? "has_powers" : "has_flags";
		var operation = set ? "CREATE" : "DELETE";
		await Assert.That((await world.Client!.RawQuery($"DEFINE EVENT reject_write ON TABLE {edges} WHEN $event = '{operation}' THEN {{ THROW 'fixture refuses assignment'; }};")).HasErrors).IsFalse();
		await Assert.That(await Assign(world.Database, god, power, name, set)).IsFalse();
		await Assert.That(await Membership(world.Database, power)).IsEquivalentTo(before);
	}

	private static async Task<string[]> Membership(ISharpDatabase database, bool power)
	{
		var god = (await database.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>();
		return power ? await god.Object().Powers.Value.Select(item => item.Name).ToArrayAsync()
			: await god.Object().Flags.Value.Select(item => item.Name).ToArrayAsync();
	}

	private static async ValueTask<bool> Assign(ISharpDatabase database, AnySharpObject obj, bool power, string name, bool set,
		CancellationToken cancellationToken = default)
	{
		if (power)
		{
			var definition = await database.GetPowerAsync(name) ?? new SharpPower
			{
				Name = name, Alias = "", System = false, SetPermissions = [], UnsetPermissions = [], TypeRestrictions = []
			};
			return set ? await database.SetObjectPowerAsync(obj, definition, cancellationToken)
				: await database.UnsetObjectPowerAsync(obj, definition, cancellationToken);
		}
		var flag = await database.GetObjectFlagAsync(name) ?? new SharpObjectFlag
		{
			Name = name, Symbol = "", System = false, SetPermissions = [], UnsetPermissions = [], TypeRestrictions = []
		};
		return set ? await database.SetObjectFlagAsync(obj, flag, cancellationToken)
			: await database.UnsetObjectFlagAsync(obj, flag, cancellationToken);
	}
}
