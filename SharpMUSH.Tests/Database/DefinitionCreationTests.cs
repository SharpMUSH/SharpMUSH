using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;

namespace SharpMUSH.Tests.Database;

public class LightningDefinitionCreationTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public Task DuplicateCreatePreservesEntireDefinitionAndAssignments(bool power)
		=> DefinitionCreationContract.DuplicateCreatePreservesEntireDefinitionAndAssignments("lightning", power);

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public Task ConcurrentCanonicalCreatesHaveOneWholeWinner(bool power)
		=> DefinitionCreationContract.ConcurrentCanonicalCreatesHaveOneWholeWinner("lightning", power);

	[Test]
	public Task MixedCaseSeedPowerCannotBeDuplicated()
		=> DefinitionCreationContract.MixedCaseSeedPowerCannotBeDuplicated("lightning");
}

public class SurrealDefinitionCreationTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public Task DuplicateCreatePreservesEntireDefinitionAndAssignments(bool power)
		=> DefinitionCreationContract.DuplicateCreatePreservesEntireDefinitionAndAssignments("surrealdb", power);

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public Task ConcurrentCanonicalCreatesHaveOneWholeWinner(bool power)
		=> DefinitionCreationContract.ConcurrentCanonicalCreatesHaveOneWholeWinner("surrealdb", power);

	[Test]
	public Task MixedCaseSeedPowerCannotBeDuplicated()
		=> DefinitionCreationContract.MixedCaseSeedPowerCannotBeDuplicated("surrealdb");

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public Task LegacyMixedCaseRowRetainsIdentityAndAssignments(bool power)
		=> DefinitionCreationContract.SurrealLegacyMixedCaseRowRetainsIdentityAndAssignments(power);
}

internal static class DefinitionCreationContract
{
	private sealed record World(ISharpDatabase Database, Func<ValueTask> Cleanup, ISurrealDbClient? Client = null) : IAsyncDisposable
	{
		public ValueTask DisposeAsync() => Cleanup();
	}
	private static async Task<World> Open(string provider)
	{
		if (provider == "lightning")
		{
			var path = Path.Combine(Path.GetTempPath(), "definitions-" + Guid.NewGuid().ToString("N"));
			var db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
				new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
			async ValueTask Cleanup()
			{
				await db.DisposeAsync();
				if (Directory.Exists(path)) Directory.Delete(path, true);
			}
			try { await db.Migrate(); return new World(db, Cleanup); }
			catch { await Cleanup(); throw; }
		}
		var services = new ServiceCollection();
		services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning).AddSimpleConsole());
		services.AddSurreal($"Endpoint=mem://;Namespace=definitions;Database=d{Guid.NewGuid():N}").AddInMemoryProvider();
		var container = services.BuildServiceProvider();
		try
		{
			var client = container.GetRequiredService<ISurrealDbClient>();
			await client.Connect();
			var db = new SurrealDatabase(container.GetRequiredService<ILogger<SurrealDatabase>>(), client, Substitute.For<IPasswordService>(), Substitute.For<IObjectRelationLoader>());
			await db.Migrate();
			return new World(db, container.DisposeAsync, client);
		}
		catch { await container.DisposeAsync(); throw; }
	}

	public static async Task DuplicateCreatePreservesEntireDefinitionAndAssignments(string provider, bool power)
	{
		await using var world = await Open(provider);
		var db = world.Database;
		var god = (await db.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>();
		const string name = "DISPOSABLE_DEFINITION";
		if (power)
		{
			var original = await db.CreatePowerAsync(name, "ORIGINAL", "Q", false, ["FLAG^ROYALTY"], ["FLAG^WIZARD"], ["THING"]);
			await Assert.That(original).IsNotNull();
			await db.SetObjectPowerAsync(god, original!);
			await db.SetPowerDisabledAsync(name, true);
			var before = JsonSerializer.Serialize(await db.GetPowerAsync(name));
			foreach (var key in new[] { name, name.ToLowerInvariant() })
				await Assert.That(await db.CreatePowerAsync(key, "REPLACED", "X", true, [], [], ["PLAYER"])).IsNull();
			await Assert.That(JsonSerializer.Serialize(await db.GetPowerAsync(name))).IsEqualTo(before);
			await Assert.That(await (await db.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>().Object().Powers.Value.AnyAsync(item => item.Name == name)).IsTrue();
		}
		else
		{
			var original = await db.CreateObjectFlagAsync(name, ["ORIGINAL"], "Q", false, ["FLAG^ROYALTY"], ["FLAG^WIZARD"], ["THING"]);
			await Assert.That(original).IsNotNull();
			await db.SetObjectFlagAsync(god, original!);
			await db.SetObjectFlagDisabledAsync(name, true);
			var before = JsonSerializer.Serialize(await db.GetObjectFlagAsync(name));
			foreach (var key in new[] { name, name.ToLowerInvariant() })
				await Assert.That(await db.CreateObjectFlagAsync(key, ["REPLACED"], "X", true, [], [], ["PLAYER"])).IsNull();
			await Assert.That(JsonSerializer.Serialize(await db.GetObjectFlagAsync(name))).IsEqualTo(before);
			await Assert.That(await (await db.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>().Object().Flags.Value.AnyAsync(item => item.Name == name)).IsTrue();
		}
	}

	public static async Task ConcurrentCanonicalCreatesHaveOneWholeWinner(string provider, bool power)
	{
		await using var world = await Open(provider);
		var db = world.Database;
		const string name = "RACE_DEFINITION";
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		if (power)
		{
			async Task<SharpPower?> Create(string key, string alias)
			{
				await gate.Task;
				return await db.CreatePowerAsync(key, alias, "Q", false, [alias], [alias], [alias]);
			}
			var attempts = new[] { Create(name, "FIRST"), Create(name.ToLowerInvariant(), "SECOND") };
			gate.SetResult();
			var results = await Task.WhenAll(attempts);
			var winners = results.OfType<SharpPower>().ToArray();
			await Assert.That(winners).HasSingleItem();
			await Assert.That(winners[0].Name).IsEqualTo(name);
			await Assert.That(JsonSerializer.Serialize(await db.GetPowerAsync(name))).IsEqualTo(JsonSerializer.Serialize(winners[0]));
			var other = await db.CreatePowerAsync("other", "OTHER", "Q", false, [], [], []);
			await Assert.That(other!.Name).IsEqualTo("OTHER");
			await Assert.That((await db.GetPowerAsync("other"))!.Name).IsEqualTo("OTHER");
		}
		else
		{
			async Task<SharpObjectFlag?> Create(string key, string alias)
			{
				await gate.Task;
				return await db.CreateObjectFlagAsync(key, [alias], "Q", false, [alias], [alias], [alias]);
			}
			var attempts = new[] { Create(name, "FIRST"), Create(name.ToLowerInvariant(), "SECOND") };
			gate.SetResult();
			var results = await Task.WhenAll(attempts);
			var winners = results.OfType<SharpObjectFlag>().ToArray();
			await Assert.That(winners).HasSingleItem();
			await Assert.That(winners[0].Name).IsEqualTo(name);
			await Assert.That(JsonSerializer.Serialize(await db.GetObjectFlagAsync(name))).IsEqualTo(JsonSerializer.Serialize(winners[0]));
			var other = await db.CreateObjectFlagAsync("other", [], "Q", false, [], [], []);
			await Assert.That(other!.Name).IsEqualTo("OTHER");
			await Assert.That((await db.GetObjectFlagAsync("other"))!.Name).IsEqualTo("OTHER");
		}
	}

	public static async Task MixedCaseSeedPowerCannotBeDuplicated(string provider)
	{
		await using var world = await Open(provider);
		var db = world.Database;
		var original = await db.GetPowerAsync("Can_Spoof");
		await Assert.That(original).IsNotNull();
		var god = (await db.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>();
		await db.SetObjectPowerAsync(god, original!);
		await Assert.That(await db.CreatePowerAsync("CAN_SPOOF", "REPLACED", "Q", false, [], [], [])).IsNull();
		await Assert.That(JsonSerializer.Serialize(await db.GetPowerAsync("can_spoof"))).IsEqualTo(JsonSerializer.Serialize(original));
		await Assert.That((await db.GetPowerAsync("CAN_SPOOF"))!.Id).IsEqualTo(original!.Id);
		await Assert.That(await (await db.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>().Object().Powers.Value.AnyAsync(item => item.Id == original.Id)).IsTrue();
	}

	public static async Task SurrealLegacyMixedCaseRowRetainsIdentityAndAssignments(bool power)
	{
		await using var world = await Open("surrealdb");
		var table = power ? "power" : "object_flag";
		var alias = power ? "alias = 'LegacyAlias'" : "aliases = ['LegacyAlias']";
		var response = await world.Client!.RawQuery($"CREATE {table}:LegacyDefinition SET name = 'LegacyDefinition', {alias}, symbol = 'Q', system = true, disabled = true, setPermissions = ['FLAG^ROYALTY'], unsetPermissions = ['FLAG^WIZARD'], typeRestrictions = ['THING']");
		await Assert.That(response.HasErrors).IsFalse();
		var db = world.Database;
		var god = (await db.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>();
		if (power)
		{
			var original = await db.GetPowerAsync("legacydefinition");
			await Assert.That(original).IsNotNull();
			await db.SetObjectPowerAsync(god, original!);
			await Assert.That(await db.CreatePowerAsync("LEGACYDEFINITION", "OTHER", "X", false, [], [], [])).IsNull();
			var after = await db.GetPowerAsync("LEGACYDEFINITION");
			await Assert.That(JsonSerializer.Serialize(after)).IsEqualTo(JsonSerializer.Serialize(original));
			await Assert.That(after!.Id).IsEqualTo(original!.Id);
			await Assert.That(await (await db.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>().Object().Powers.Value.AnyAsync(item => item.Id == original.Id)).IsTrue();
		}
		else
		{
			var original = await db.GetObjectFlagAsync("legacydefinition");
			await Assert.That(original).IsNotNull();
			await db.SetObjectFlagAsync(god, original!);
			await Assert.That(await db.CreateObjectFlagAsync("LEGACYDEFINITION", ["OTHER"], "X", false, [], [], [])).IsNull();
			var after = await db.GetObjectFlagAsync("LEGACYDEFINITION");
			await Assert.That(JsonSerializer.Serialize(after)).IsEqualTo(JsonSerializer.Serialize(original));
			await Assert.That(after!.Id).IsEqualTo(original!.Id);
			await Assert.That(await (await db.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>().Object().Flags.Value.AnyAsync(item => item.Id == original.Id)).IsTrue();
		}
	}
}
