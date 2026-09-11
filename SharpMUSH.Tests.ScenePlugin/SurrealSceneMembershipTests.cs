using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Plugins.Scene.Storage;
using SurrealDb.Net;
using SurrealDb.Net.Models.Response;

namespace SharpMUSH.Tests.ScenePlugin;

public class SurrealSceneMembershipTests
{
	private sealed class World(ServiceProvider services, ISurrealStorageAccessor accessor) : IAsyncDisposable
	{
		public ISurrealStorageAccessor Accessor { get; } = accessor;
		public SurrealSceneStorage Storage { get; } = new(accessor);

		public async Task Query(string query)
		{
			var response = await Accessor.ExecuteAsync(query);
			await Assert.That(response.HasErrors).IsFalse()
				.Because(string.Join("; ", response.Errors.OfType<SurrealDbErrorResult>().Select(e => e.Details)));
		}

		public async Task Migrate()
		{
			foreach (var statement in new Plugins.Scene.ScenePlugin().SurrealStatements)
				await Query(statement);
		}

		public ValueTask DisposeAsync() => services.DisposeAsync();
	}

	private static async Task<World> CreateWorld()
	{
		var services = new ServiceCollection();
		services.AddSurreal($"Endpoint=mem://;Namespace=scene_members;Database=d{Guid.NewGuid():N}")
			.AddInMemoryProvider();
		var provider = services.BuildServiceProvider();
		var client = provider.GetRequiredService<ISurrealDbClient>();
		await client.Connect();
		var database = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client,
			Substitute.For<IPasswordService>(), Substitute.For<IObjectRelationLoader>());
		var world = new World(provider, database);
		await world.Migrate();
		await world.Query("CREATE object:1 SET key = 1, name = 'Player'");
		await world.Storage.CreateSceneAsync("#1", "#1");
		return world;
	}

	[Test]
	public async Task ConcurrentAdds_CreateOneMembership()
	{
		await using var world = await CreateWorld();
		var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var writes = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
		{
			await start.Task;
			return await new SurrealSceneStorage(world.Accessor).AddMemberAsync("1", "#1", "participant");
		})).ToArray();
		start.SetResult();
		var results = await Task.WhenAll(writes);
		await Assert.That(results.All(r => r is SceneMember { Role: "participant" })).IsTrue();
		var members = Expect(await world.Storage.GetMembersAsync("1"));
		await Assert.That(members.Count).IsEqualTo(1);
	}

	[Test]
	public async Task RoleChange_PreservesFocusPersonaAndGrantTime()
	{
		await using var world = await CreateWorld();
		var original = Expect(await world.Storage.AddMemberAsync("1", "#1", "owner"));
		await world.Storage.SetFocusAsync("#1", "1");
		await world.Storage.SetShowAsAsync("1", "#1", "A persona");
		var changed = Expect(await world.Storage.AddMemberAsync("1", "#1", "participant"));
		await Assert.That(changed.Role).IsEqualTo("participant");
		await Assert.That(changed.IsCurrent).IsTrue();
		await Assert.That(changed.ShowAs).IsEqualTo("A persona");
		await Assert.That(changed.GrantedAt).IsEqualTo(original.GrantedAt);
	}
	[Test]
	public async Task Migration_ConsolidatesLegacyDuplicatesAndSurvivesRestart()
	{
		await using var world = await CreateWorld();
		await world.Query("DELETE migration:scene_member_ids_v1; DELETE migration:scene_focus_pointer_v1");
		await world.Query("""
			RELATE object:1->scene_member:old->scene:⟨1⟩ SET
				role = 'owner', showAs = '', isCurrent = false, grantedAt = 10, memberName = 'Player';
			RELATE object:1->scene_member:new->scene:⟨1⟩ SET
				role = '', showAs = 'Persona', isCurrent = true, grantedAt = 20, memberName = 'Player';
			""");
		await world.Migrate();
		await world.Migrate();
		var members = Expect(await world.Storage.GetMembersAsync("1"));
		await Assert.That(members.Count).IsEqualTo(1);
		await Assert.That(members[0].Role).IsEqualTo("owner");
		await Assert.That(members[0].ShowAs).IsEqualTo("Persona");
		await Assert.That(members[0].IsCurrent).IsTrue();
		await Assert.That(members[0].GrantedAt).IsEqualTo(10L);
		await world.Query("IF array::len((SELECT * FROM scene_member_duplicate_backup)) != 2 { THROW 'Backup changed on restart'; }");
		await world.Query("IF array::len((SELECT * FROM scene_member_duplicate_backup WHERE original.id = scene_member:new AND original.showAs = 'Persona' AND original.isCurrent = true)) != 1 { THROW 'Lost original duplicate data'; }");
		await world.Storage.AddMemberAsync("1", "#1", "guest");
		await Assert.That(Expect(await world.Storage.GetMembersAsync("1")).Count).IsEqualTo(1);
		await world.Query("IF array::len((SELECT VALUE ->scene_member FROM object:1)[0]) != 1 { THROW 'Broken migrated traversal'; }");
	}

	[Test]
	public async Task ConcurrentFocusAndRoleGrants_PreserveOneMemberWithBothChanges()
	{
		await using var world = await CreateWorld();
		var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var writes = Enumerable.Range(0, 32).Select(i => Task.Run(async () =>
		{
			await start.Task;
			var storage = new SurrealSceneStorage(world.Accessor);
			if (i % 2 == 0)
				await storage.AddMemberAsync("1", "#1", "participant");
			else
				await storage.SetFocusAsync("#1", "1");
		})).ToArray();
		start.SetResult();
		await Task.WhenAll(writes);
		var members = Expect(await world.Storage.GetMembersAsync("1"));
		await Assert.That(members.Count).IsEqualTo(1);
		await Assert.That(members[0].Role).IsEqualTo("participant");
		await Assert.That(members[0].IsCurrent).IsTrue();
	}

	[Test]
	public async Task RemoveAndReAdd_MaintainGraphTraversal()
	{
		await using var world = await CreateWorld();
		await world.Storage.AddMemberAsync("1", "#1", "owner");
		await world.Storage.SetFocusAsync("#1", "1");
		await world.Storage.RemoveMemberAsync("1", "#1");
		await Assert.That((await world.Storage.GetCurrentSceneAsync("#1")).Value).IsTypeOf<NotFound>();
		await Assert.That(Expect(await world.Storage.GetMembersAsync("1")).Count).IsEqualTo(0);
		await world.Storage.AddMemberAsync("1", "#1", "guest");
		await world.Query("IF array::len((SELECT VALUE ->scene_member FROM object:1)[0]) != 1 { THROW 'Broken outgoing traversal'; }");
		await world.Query("IF array::len((SELECT VALUE <-scene_member FROM scene:⟨1⟩)[0]) != 1 { THROW 'Broken incoming traversal'; }");
		await Assert.That(Expect(await world.Storage.GetMemberAsync("1", "#1")).Role).IsEqualTo("guest");
		await Assert.That(Expect(await world.Storage.GetMemberAsync("1", "#1")).IsCurrent).IsFalse();
	}

	[Test]
	public async Task InvalidFocusTarget_DoesNotClearExistingFocus()
	{
		await using var world = await CreateWorld();
		await world.Storage.SetFocusAsync("#1", "1");
		await Assert.That((await world.Storage.SetFocusAsync("#1", "missing")).Value).IsTypeOf<NotFound>();
		await Assert.That(Expect(await world.Storage.GetMemberAsync("1", "#1")).IsCurrent).IsTrue();
	}

	[Test]
	public async Task Restart_DoesNotResetSceneCounter()
	{
		await using var world = await CreateWorld();
		await world.Migrate();
		var second = await world.Storage.CreateSceneAsync("#1", "#1");
		await Assert.That(second.Id).IsEqualTo("2");
	}

	[Test]
	public async Task FocusMigrationRepairsPreviouslyFocusedScenesOnceWithoutLosingMembershipData()
	{
		await using var world = await CreateWorld();
		await world.Storage.CreateSceneAsync("#1", "#1");
		await world.Storage.AddMemberAsync("1", "#1", "owner");
		await world.Storage.AddMemberAsync("2", "#1", "participant");
		await world.Storage.SetShowAsAsync("2", "#1", "Persona");
		await world.Query("UPDATE scene_member SET isCurrent = true, grantedAt = IF out = scene:⟨1⟩ { 1 } ELSE { 2 }; DELETE migration:scene_focus_pointer_v1;");
		await world.Migrate();
		await world.Migrate();
		await world.Query("IF array::len((SELECT * FROM scene_focus WHERE scene != NONE)) != 1 { THROW 'Multiple focused scenes'; }");
		await Assert.That(Expect(await world.Storage.GetMemberAsync("1", "#1")).IsCurrent).IsTrue();
		var second = Expect(await world.Storage.GetMemberAsync("2", "#1"));
		await Assert.That(second.Role).IsEqualTo("participant");
		await Assert.That(second.ShowAs).IsEqualTo("Persona");
		await world.Query("IF array::len((SELECT * FROM scene_member_duplicate_backup)) != 2 { THROW 'Focus originals not archived exactly once'; }");
	}

	[Test]
	public async Task ClearFocusPreservesMembershipAndAllowsAnotherStorageInstanceToFocus()
	{
		await using var world = await CreateWorld();
		await world.Storage.AddMemberAsync("1", "#1", "owner");
		await world.Storage.SetFocusAsync("#1", "1");
		await new SurrealSceneStorage(world.Accessor).SetFocusAsync("#1");
		var member = Expect(await world.Storage.GetMemberAsync("1", "#1"));
		await Assert.That(member.IsCurrent).IsFalse();
		await Assert.That(member.Role).IsEqualTo("owner");
		await new SurrealSceneStorage(world.Accessor).SetFocusAsync("#1", "1");
		await Assert.That(Expect(await world.Storage.GetMemberAsync("1", "#1")).IsCurrent).IsTrue();
	}

	[Test]
	[Arguments(16)]
	[Arguments(64)]
	public async Task ConcurrentFocusOnDifferentScenes_LeavesOnlyOneCurrentScene(int writers)
	{
		await using var world = await CreateWorld();
		var scenes = new List<string> { "1" };
		for (var i = 1; i < writers; i++)
			scenes.Add((await world.Storage.CreateSceneAsync("#1", "#1")).Id);
		var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var writes = scenes.Select(id => Task.Run(async () =>
		{
			await start.Task;
			await new SurrealSceneStorage(world.Accessor).SetFocusAsync("#1", id);
		})).ToArray();
		start.SetResult();
		await Task.WhenAll(writes);
		var current = Expect(await world.Storage.GetCurrentSceneAsync("#1"));
		var focused = new List<string>();
		foreach (var id in scenes)
		{
			var member = Expect(await world.Storage.GetMemberAsync(id, "#1"));
			var members = Expect(await world.Storage.GetMembersAsync(id));
			await Assert.That(members.Single().IsCurrent).IsEqualTo(member.IsCurrent);
			if (member.IsCurrent) focused.Add(id);
		}
		await Assert.That(focused.Count).IsEqualTo(1);
		await Assert.That(focused.Single()).IsEqualTo(current.Id);
		await world.Query("IF array::len((SELECT * FROM scene_focus)) != 1 { THROW 'Multiple focus pointers'; }");
	}

	[Test]
	public async Task FocusPointersAreIndependentAndSurviveRestart()
	{
		await using var world = await CreateWorld();
		await world.Query("CREATE object:2 SET key = 2, name = 'Second'");
		await world.Storage.CreateSceneAsync("#1", "#1");
		await Task.WhenAll(
			new SurrealSceneStorage(world.Accessor).SetFocusAsync("#1", "1"),
			new SurrealSceneStorage(world.Accessor).SetFocusAsync("#2", "2"));
		await world.Migrate();
		await Assert.That(Expect(await world.Storage.GetCurrentSceneAsync("#1")).Id).IsEqualTo("1");
		await Assert.That(Expect(await world.Storage.GetCurrentSceneAsync("#2")).Id).IsEqualTo("2");
		await world.Storage.SetFocusAsync("#1");
		await world.Migrate();
		await Assert.That((await world.Storage.GetCurrentSceneAsync("#1")).Value).IsTypeOf<NotFound>();
		await Assert.That(Expect(await world.Storage.GetCurrentSceneAsync("#2")).Id).IsEqualTo("2");
	}

	/// <summary>
	/// The record a lookup the test expects to succeed returned. A miss fails the test at the lookup, naming
	/// what was missing, rather than at whichever property the test reads next.
	/// </summary>
	private static T Expect<T>(Found<T> lookup)
		=> lookup.Value is T value
			? value
			: throw new InvalidOperationException($"Expected a {typeof(T).Name}; the lookup found nothing.");

	private sealed class FailedMigration : IMigrationSource
	{
		public bool RequireSuccessfulSurrealMigrations => true;
		public IEnumerable<string> SurrealStatements => ["THROW 'Required scene migration failed'", "CREATE migration:should_not_run"];
	}

	[Test]
	public async Task FailedPluginMigration_AbortsStartup()
	{
		var services = new ServiceCollection();
		services.AddSurreal($"Endpoint=mem://;Namespace=scene_members;Database=d{Guid.NewGuid():N}").AddInMemoryProvider();
		await using var provider = services.BuildServiceProvider();
		var client = provider.GetRequiredService<ISurrealDbClient>();
		await client.Connect();
		var password = Substitute.For<IPasswordService>();
		password.GenerateRandomPassword().Returns("password");
		password.HashPassword(Arg.Any<string>(), Arg.Any<string>()).Returns("hash");
		var database = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client,
			password, Substitute.For<IObjectRelationLoader>(), [new FailedMigration()]);
		await Assert.That(async () => await database.Migrate()).Throws<InvalidOperationException>();
		var laterStatement = await client.RawQuery("SELECT * FROM migration:should_not_run");
		await Assert.That(laterStatement.GetValue<List<object>>(0)!.Count).IsEqualTo(0);
	}

	private sealed class LegacyMigration : IMigrationSource
	{
		public IEnumerable<string> SurrealStatements => ["DEFINE TABLE my_thing SCHEMALESS"];
	}

	[Test]
	public async Task LegacyPluginMigration_PreservesRestartCompatibility()
	{
		var services = new ServiceCollection();
		services.AddSurreal($"Endpoint=mem://;Namespace=scene_members;Database=d{Guid.NewGuid():N}").AddInMemoryProvider();
		await using var provider = services.BuildServiceProvider();
		var client = provider.GetRequiredService<ISurrealDbClient>();
		await client.Connect();
		var password = Substitute.For<IPasswordService>();
		password.GenerateRandomPassword().Returns("password");
		password.HashPassword(Arg.Any<string>(), Arg.Any<string>()).Returns("hash");
		var database = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client,
			password, Substitute.For<IObjectRelationLoader>(), [new LegacyMigration()]);
		await database.Migrate();
		var restarted = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client,
			password, Substitute.For<IObjectRelationLoader>(), [new LegacyMigration()]);
		await restarted.Migrate();
		await Assert.That((await client.RawQuery("CREATE my_thing:test")).HasErrors).IsFalse();
	}

	[Test]
	public async Task Migration_RepairsCountersBelowExistingIds()
	{
		await using var world = await CreateWorld();
		await world.Query("CREATE scene:⟨42⟩; CREATE scene_pose:⟨73⟩; CREATE scene:custom; CREATE scene_pose:custom; UPDATE counter:scene_id, counter:pose_id SET seq = 0");
		await world.Migrate();
		var scene = await world.Storage.CreateSceneAsync("#1", "#1");
		await Assert.That(scene.Id).IsEqualTo("43");
		await world.Query("IF (SELECT VALUE seq FROM counter:pose_id)[0] != 73 { THROW 'Stale pose counter'; }");
		await world.Query("UPDATE counter:scene_id, counter:pose_id SET seq = 100");
		await world.Migrate();
		await world.Query("IF (SELECT VALUE seq FROM counter:scene_id)[0] != 100 OR (SELECT VALUE seq FROM counter:pose_id)[0] != 100 { THROW 'Counter lowered'; }");
	}

	[Test]
	public async Task Migration_ConsolidatesFocusAcrossScenesAndArchivesOriginals()
	{
		await using var world = await CreateWorld();
		await world.Storage.CreateSceneAsync("#1", "#1");
		await world.Query("""
			DELETE migration:scene_member_ids_v1; DELETE migration:scene_focus_pointer_v1;
			RELATE object:1->scene_member:older->scene:⟨1⟩ SET
				role = 'owner', showAs = 'One', isCurrent = true, grantedAt = 10, memberName = 'Player';
			RELATE object:1->scene_member:newer->scene:⟨2⟩ SET
				role = 'guest', showAs = 'Two', isCurrent = true, grantedAt = 20, memberName = 'Player';
			""");
		await world.Migrate();
		await world.Migrate();
		await Assert.That(Expect(await world.Storage.GetMemberAsync("1", "#1")).IsCurrent).IsTrue();
		var other = Expect(await world.Storage.GetMemberAsync("2", "#1"));
		await Assert.That(other.IsCurrent).IsFalse();
		await Assert.That(other.ShowAs).IsEqualTo("Two");
		await Assert.That(other.Role).IsEqualTo("guest");
		await world.Query("IF array::len((SELECT * FROM scene_member_duplicate_backup WHERE original.isCurrent = true)) != 2 { THROW 'Missing original focus data'; }");
	}

}
