using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

public class DefinitionCreationTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public Task DuplicateCreatePreservesEntireDefinitionAndAssignments(bool power)
		=> DefinitionCreationContract.DuplicateCreatePreservesEntireDefinitionAndAssignments(power);

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public Task ConcurrentCanonicalCreatesHaveOneWholeWinner(bool power)
		=> DefinitionCreationContract.ConcurrentCanonicalCreatesHaveOneWholeWinner(power);

	[Test]
	public Task MixedCaseSeedPowerCannotBeDuplicated()
		=> DefinitionCreationContract.MixedCaseSeedPowerCannotBeDuplicated();
}

internal static class DefinitionCreationContract
{
	internal sealed record World(ISharpDatabase Database, Func<ValueTask> Cleanup) : IAsyncDisposable
	{
		public ValueTask DisposeAsync() => Cleanup();
	}
	internal static async Task<World> Open()
	{
		var path = Path.Combine(Path.GetTempPath(), "definitions-" + Guid.NewGuid().ToString("N"));
		var db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		async ValueTask Cleanup()
		{
			await db.DisposeAsync();
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}
		try { await db.Migrate(); return new World(db, Cleanup); }
		catch { await Cleanup(); throw; }
	}

	public static async Task DuplicateCreatePreservesEntireDefinitionAndAssignments(bool power)
	{
		await using var world = await Open();
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

	public static async Task ConcurrentCanonicalCreatesHaveOneWholeWinner(bool power)
	{
		await using var world = await Open();
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
			var attempts = Enumerable.Range(0, 8)
				.Select(index => Create(index % 2 == 0 ? name : name.ToLowerInvariant(), $"CANDIDATE{index}")).ToArray();
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
			var attempts = Enumerable.Range(0, 8)
				.Select(index => Create(index % 2 == 0 ? name : name.ToLowerInvariant(), $"CANDIDATE{index}")).ToArray();
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

	public static async Task MixedCaseSeedPowerCannotBeDuplicated()
	{
		await using var world = await Open();
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
}
