using System.Text.Json;
using Mediator;
using NSubstitute;
using SharpMUSH.Implementation.Handlers.Database;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Queries.Database;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Database;

public class SurrealDefinitionMutationTests
{
	public static IEnumerable<(bool Power, bool Alias, string Operation)> Cases()
	{
		foreach (var power in new[] { false, true })
			foreach (var alias in power ? new[] { false } : new[] { false, true })
				foreach (var operation in new[] { "update", "disable", "delete" })
					yield return (power, alias, operation);
	}

	[Test]
	[MethodDataSource(nameof(Cases))]
	public async Task LegacyMutationUsesStoredIdentityAndPreservesOtherRows(bool power, bool alias, string operation)
	{
		await using var world = await DefinitionCreationContract.Open("surrealdb");
		var client = world.Client!;
		var table = power ? "power" : "object_flag";
		var edges = power ? "has_powers" : "has_flags";
		var aliasAssignment = power ? "alias = 'LegacyAlias'" : "aliases = ['LegacyAlias']";
		var otherAliasAssignment = power ? "alias = 'OtherAlias'" : "aliases = ['OtherAlias']";
		var created = await client.RawQuery($"CREATE {table}:LegacyStorage SET name = 'LegacyDefinition', {aliasAssignment}, symbol = 'Q', system = false, disabled = false, setPermissions = ['wizard'], unsetPermissions = ['god'], typeRestrictions = ['THING']; CREATE {table}:OtherStorage SET name = 'OtherDefinition', {otherAliasAssignment}, symbol = 'X', system = false; RELATE object:1->{edges}->{table}:LegacyStorage; RELATE object:1->{edges}->{table}:OtherStorage;");
		await Assert.That(created.HasErrors).IsFalse();
		var otherBefore = JsonSerializer.Serialize((await client.RawQuery($"SELECT *, <string> id AS nativeId FROM {table}:OtherStorage")).GetValue<List<DefinitionRow>>(0)!.Single());
		var key = alias ? "LEGACYALIAS" : "LEGACYDEFINITION";
		await Assert.That(await Mutate(world.Database, power, operation, key)).IsTrue();
		var rows = (await client.RawQuery($"SELECT *, <string> id AS nativeId FROM {table}:LegacyStorage")).GetValue<List<DefinitionRow>>(0)!;
		if (operation == "delete")
		{
			await Assert.That(rows).IsEmpty();
			await Assert.That((await client.RawQuery($"SELECT VALUE <string> id FROM {edges} WHERE out = {table}:LegacyStorage")).GetValue<List<string>>(0)!).IsEmpty();
			await Assert.That(await Mutate(world.Database, power, operation, key)).IsFalse();
		}
		else
		{
			var row = rows.Single();
			await Assert.That(row.name).IsEqualTo("LegacyDefinition");
			await Assert.That(row.nativeId).Contains("LegacyStorage");
			if (operation == "update")
			{
				await Assert.That((power ? row.alias : string.Join("|", row.aliases))).Contains("UpdatedAlias");
				await Assert.That(row.symbol).IsEqualTo("Z");
				await Assert.That(row.setPermissions).IsEquivalentTo(["royalty"]);
				await Assert.That(row.unsetPermissions).IsEquivalentTo(["trusted"]);
				await Assert.That(row.typeRestrictions).IsEquivalentTo(["PLAYER"]);
			}
			else
			{
				await Assert.That(row.disabled).IsTrue();
				await Assert.That(power ? await world.Database.SetPowerDisabledAsync(key, false) : await world.Database.SetObjectFlagDisabledAsync(key, false)).IsTrue();
				var enabled = (await client.RawQuery($"SELECT *, <string> id AS nativeId FROM {table}:LegacyStorage")).GetValue<List<DefinitionRow>>(0)!.Single();
				await Assert.That(enabled.disabled).IsFalse();
			}
			await Assert.That((await client.RawQuery($"SELECT VALUE <string> id FROM {edges} WHERE out = {table}:LegacyStorage")).GetValue<List<string>>(0)!).HasSingleItem();
		}
		await Assert.That(JsonSerializer.Serialize((await client.RawQuery($"SELECT *, <string> id AS nativeId FROM {table}:OtherStorage")).GetValue<List<DefinitionRow>>(0)!.Single())).IsEqualTo(otherBefore);
		await Assert.That((await client.RawQuery($"SELECT VALUE <string> id FROM {edges} WHERE out = {table}:OtherStorage")).GetValue<List<string>>(0)!).HasSingleItem();
	}

	[Test]
	[MethodDataSource(nameof(Cases))]
	public async Task MissingSystemAndCancelledMutationsAreRefusedButAbsentSystemFieldRemainsEditable(bool power, bool alias, string operation)
	{
		await using var world = await DefinitionCreationContract.Open("surrealdb");
		var client = world.Client!;
		var table = power ? "power" : "object_flag";
		var aliasAssignment = power ? "alias = 'LegacyAlias'" : "aliases = ['LegacyAlias']";
		var key = alias ? "LEGACYALIAS" : "LEGACYDEFINITION";
		await Assert.That(await Mutate(world.Database, power, operation, key)).IsFalse();
		var created = await client.RawQuery($"CREATE {table}:LegacyStorage SET name = 'LegacyDefinition', {aliasAssignment}, symbol = 'Q', system = true;");
		await Assert.That(created.HasErrors).IsFalse();
		await Assert.That(await Mutate(world.Database, power, operation, key)).IsFalse();
		var before = (await client.RawQuery($"SELECT *, <string> id AS nativeId FROM {table}:LegacyStorage")).GetValue<List<DefinitionRow>>(0)!.Single();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.That(async () => await Mutate(world.Database, power, operation, key, cancellation.Token)).Throws<OperationCanceledException>();
		await Assert.That(JsonSerializer.Serialize((await client.RawQuery($"SELECT *, <string> id AS nativeId FROM {table}:LegacyStorage")).GetValue<List<DefinitionRow>>(0)!.Single())).IsEqualTo(JsonSerializer.Serialize(before));
		await Assert.That((await client.RawQuery($"UPDATE {table}:LegacyStorage UNSET system;")).HasErrors).IsFalse();
		await Assert.That(await Mutate(world.Database, power, operation, key)).IsTrue();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ProductionHandlersAndCacheBehaviorsInvalidateWarmLegacyLookupsAndLists(bool power)
	{
		await using var world = await DefinitionCreationContract.Open("surrealdb");
		var table = power ? "power" : "object_flag";
		var aliases = power ? "alias = 'LegacyAlias'" : "aliases = ['LegacyAlias']";
		await Assert.That((await world.Client!.RawQuery($"CREATE {table}:LegacyStorage SET name = 'LegacyDefinition', {aliases}, symbol = 'Q', system = false, disabled = false;")).HasErrors).IsFalse();
		using var cache = new FusionCache(new FusionCacheOptions { DefaultEntryOptions = CacheEntryProfiles.Tagged });
		var versions = new ObjectVersions();
		// Definition results are not object-shaped; these behaviors never call the mediator seam.
		var mediator = Substitute.For<IMediator>();
		var key = power ? "LEGACYDEFINITION" : "LEGACYALIAS";
		async Task<string> Read(string name) => power
			? JsonSerializer.Serialize(await new QueryCachingBehavior<GetPowerQuery, SharpPower?>(cache, versions, mediator)
				.Handle(new GetPowerQuery(name), new GetPowerQueryHandler(world.Database).Handle, CancellationToken.None))
			: JsonSerializer.Serialize(await new QueryCachingBehavior<GetObjectFlagQuery, SharpObjectFlag?>(cache, versions, mediator)
				.Handle(new GetObjectFlagQuery(name), new GetObjectFlagQueryHandler(world.Database).Handle, CancellationToken.None));
		async Task<string> List() => power
			? JsonSerializer.Serialize(await new StreamQueryCachingBehavior<GetPowersQuery, SharpPower>(cache, mediator)
				.Handle(new GetPowersQuery(), new PowerQueryHandler(world.Database).Handle, CancellationToken.None).ToArrayAsync())
			: JsonSerializer.Serialize(await new StreamQueryCachingBehavior<GetAllObjectFlagsQuery, SharpObjectFlag>(cache, mediator)
				.Handle(new GetAllObjectFlagsQuery(), new GetAllObjectFlagsQueryHandler(world.Database).Handle, CancellationToken.None).ToArrayAsync());
		ValueTask<bool> Write<T>(T command, ICommandHandler<T, bool> handler) where T : ICommand<bool>, ICacheInvalidating
			=> new CacheInvalidationBehavior<T, bool>(cache, versions).Handle(command, handler.Handle, CancellationToken.None);
		var before = await Read(key);
		await Assert.That(before).Contains("LegacyDefinition");
		await Assert.That(await List()).Contains("LegacyDefinition");
		await Assert.That(power
			? await Write(new UpdatePowerCommand(key, "UpdatedAlias", "Z", ["royalty"], ["trusted"], ["PLAYER"]), new UpdatePowerCommandHandler(world.Database))
			: await Write(new UpdateObjectFlagCommand(key, ["UpdatedAlias"], "Z", ["royalty"], ["trusted"], ["PLAYER"]), new UpdateObjectFlagCommandHandler(world.Database))).IsTrue();
		await Assert.That(await Read("LEGACYDEFINITION")).Contains("UpdatedAlias");
		await Assert.That(await List()).Contains("UpdatedAlias");
		if (!power) await Assert.That(await Read(key)).IsEqualTo("null");
		key = "LEGACYDEFINITION";
		await Assert.That(power
			? await Write(new SetPowerDisabledCommand(key, true), new SetPowerDisabledCommandHandler(world.Database))
			: await Write(new SetObjectFlagDisabledCommand(key, true), new SetObjectFlagDisabledCommandHandler(world.Database))).IsTrue();
		await Assert.That(await Read(key)).Contains("\"Disabled\":true");
		await Assert.That(await List()).Contains("\"Disabled\":true");
		await Assert.That(power
			? await Write(new DeletePowerCommand(key), new DeletePowerCommandHandler(world.Database))
			: await Write(new DeleteObjectFlagCommand(key), new DeleteObjectFlagCommandHandler(world.Database))).IsTrue();
		await Assert.That(await Read(key)).IsEqualTo("null");
		await Assert.That((await List()).Contains("LegacyDefinition", StringComparison.Ordinal)).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FailedDeleteTransactionPreservesDefinitionAndAssignments(bool power)
	{
		await using var world = await DefinitionCreationContract.Open("surrealdb");
		var table = power ? "power" : "object_flag";
		var edges = power ? "has_powers" : "has_flags";
		var setup = await world.Client!.RawQuery($"CREATE {table}:LegacyStorage SET name = 'LegacyDefinition', system = false; RELATE object:1->{edges}->{table}:LegacyStorage; DEFINE EVENT reject_delete ON TABLE {table} WHEN $event = 'DELETE' THEN {{ THROW 'fixture refuses deletion'; }};");
		await Assert.That(setup.HasErrors).IsFalse();
		await Assert.That(await Mutate(world.Database, power, "delete", "LEGACYDEFINITION")).IsFalse();
		await Assert.That((await world.Client.RawQuery($"SELECT *, <string> id AS nativeId FROM {table}:LegacyStorage")).GetValue<List<DefinitionRow>>(0)!).HasSingleItem();
		await Assert.That((await world.Client.RawQuery($"SELECT VALUE <string> id FROM {edges} WHERE out = {table}:LegacyStorage")).GetValue<List<string>>(0)!).HasSingleItem();
	}

	private static ValueTask<bool> Mutate(ISharpDatabase db, bool power, string operation, string key, CancellationToken cancellationToken = default)
		=> (power, operation) switch
		{
			(true, "update") => db.UpdatePowerAsync(key, "UpdatedAlias", "Z", ["royalty"], ["trusted"], ["PLAYER"], cancellationToken),
			(false, "update") => db.UpdateObjectFlagAsync(key, ["UpdatedAlias"], "Z", ["royalty"], ["trusted"], ["PLAYER"], cancellationToken),
			(true, "disable") => db.SetPowerDisabledAsync(key, true, cancellationToken),
			(false, "disable") => db.SetObjectFlagDisabledAsync(key, true, cancellationToken),
			(true, "delete") => db.DeletePowerAsync(key, cancellationToken),
			(false, "delete") => db.DeleteObjectFlagAsync(key, cancellationToken),
			_ => throw new ArgumentOutOfRangeException(nameof(operation))
		};

	public sealed class DefinitionRow
	{
		public string nativeId { get; set; } = "";
		public string name { get; set; } = "";
		public string symbol { get; set; } = "";
		public string alias { get; set; } = "";
		public string[] aliases { get; set; } = [];
		public string[] setPermissions { get; set; } = [];
		public string[] unsetPermissions { get; set; } = [];
		public string[] typeRestrictions { get; set; } = [];
		public bool disabled { get; set; }
	}
}
