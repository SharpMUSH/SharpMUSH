using Core.Arango;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Scene.Contracts;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Storage;
using Scene = SharpMUSH.Plugins.Scene.Contracts.Scene;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// Pure-DI unit tests for the Scene plugin's <c>AddSceneSystem</c> registration seam (Phase 8). No server
/// boots and no database is touched: these build a bare <see cref="IServiceCollection"/> and assert that
/// (a) the storage matching the configured provider is selected by key, and (b) <c>AddBehavior&lt;T&gt;()</c>
/// decorators wrap the core in registration order. Proves the registration shape independently of the ALC.
/// </summary>
public class SceneSystemRegistrationTests
{
	[Test]
	public async Task AddSceneSystem_SelectsStorageMatchingConfiguredProvider()
	{
		var services = new ServiceCollection();
		// Only the Arango accessor is present (as the host does for the active provider). The factory must
		// pick the "arangodb"-keyed ArangoSceneStorage and never touch the other two keys.
		services.AddSingleton<IArangoStorageAccessor>(new FakeArangoAccessor());

		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { ["SHARPMUSH_DATABASE_PROVIDER"] = "arangodb" })
			.Build();

		services.AddSceneSystem(config);

		await using var sp = services.BuildServiceProvider();
		var svc = sp.GetRequiredService<ISceneService>();

		await Assert.That(svc).IsTypeOf<ArangoSceneStorage>();
	}

	[Test]
	public async Task AddSceneSystem_AppliesBehaviorsInOrderAroundServiceCall()
	{
		var calls = new List<string>();

		var services = new ServiceCollection();
		services.AddSingleton(calls);
		// Force the active key to surrealdb and override its keyed storage with a recording fake so the
		// chain is observable without a database.
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { ["SHARPMUSH_DATABASE_PROVIDER"] = "surrealdb" })
			.Build();

		services.AddSceneSystem(config)
			.AddBehavior<FirstBehavior>()
			.AddBehavior<SecondBehavior>();

		// Replace the surrealdb-keyed storage core with the recording fake (last keyed registration wins).
		services.AddKeyedSingleton<ISceneStorage>(
			SceneSystemServiceCollectionExtensions.SurrealKey,
			(sp, _) => new RecordingStorage(sp.GetRequiredService<List<string>>()));

		await using var sp = services.BuildServiceProvider();
		var svc = sp.GetRequiredService<ISceneService>();

		_ = await svc.GetSceneAsync("scene:1");

		// Last-added behavior is outermost: Second wraps First wraps the storage core.
		await Assert.That(calls).IsEquivalentTo(new[] { "second", "first", "core" });
	}

	// ── Fakes ──────────────────────────────────────────────────────────────────────

	private sealed class FakeArangoAccessor : IArangoStorageAccessor
	{
		public IArangoContext Context => throw new NotSupportedException();
		public ArangoHandle Handle => throw new NotSupportedException();
		public ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();
	}

	/// <summary>A no-database storage core that records its own invocation, used to observe the chain order.</summary>
	private sealed class RecordingStorage(List<string> calls) : SceneStub, ISceneStorage
	{
		public override Task<OneOf<Scene, NotFound>> GetSceneAsync(string sceneId)
		{
			calls.Add("core");
			return Task.FromResult<OneOf<Scene, NotFound>>(new NotFound());
		}
	}

	private sealed class FirstBehavior(ISceneService inner, List<string> calls) : SceneStub, ISceneServiceBehavior
	{
		public override Task<OneOf<Scene, NotFound>> GetSceneAsync(string sceneId)
		{
			calls.Add("first");
			return inner.GetSceneAsync(sceneId);
		}
	}

	private sealed class SecondBehavior(ISceneService inner, List<string> calls) : SceneStub, ISceneServiceBehavior
	{
		public override Task<OneOf<Scene, NotFound>> GetSceneAsync(string sceneId)
		{
			calls.Add("second");
			return inner.GetSceneAsync(sceneId);
		}
	}

	/// <summary>Throwing base so the fakes only override the one method the tests exercise.</summary>
	private abstract class SceneStub : ISceneService
	{
		public virtual Task<Scene> CreateSceneAsync(string roomDbref, string ownerDbref, string title = "") => throw new NotSupportedException();
		public virtual Task<OneOf<Scene, NotFound>> GetSceneAsync(string sceneId) => throw new NotSupportedException();
		public Task<OneOf<Scene, NotFound>> SetSceneMetaAsync(string sceneId, string key, string value) => throw new NotSupportedException();
		public Task<IReadOnlyList<Scene>> ListScenesAsync(string filter, string? viewerDbref = null, long? fromUtcMillis = null, long? toUtcMillis = null, int count = 50) => throw new NotSupportedException();
		public Task<OneOf<Scene, NotFound>> GetActiveSceneInRoomAsync(string roomDbref) => throw new NotSupportedException();
		public Task<OneOf<ScenePose, NotFound, Error<string>>> AddPoseAsync(string sceneId, string authorDbref, string showAs, string originDbref, string source, IReadOnlyList<string> tags, string content) => throw new NotSupportedException();
		public Task<OneOf<ScenePose, NotFound>> GetPoseAsync(string poseId) => throw new NotSupportedException();
		public Task<OneOf<IReadOnlyList<ScenePose>, NotFound>> GetPosesAsync(string sceneId, string? authorDbref = null, int? count = null) => throw new NotSupportedException();
		public Task<OneOf<ScenePose, NotFound>> SetPoseMetaAsync(string poseId, string key, string value) => throw new NotSupportedException();
		public Task<OneOf<ScenePose, NotFound>> EditPoseAsync(string poseId, string editorDbref, string content) => throw new NotSupportedException();
		public Task<OneOf<ScenePose, NotFound, Error<string>>> UndoPoseAsync(string poseId) => throw new NotSupportedException();
		public Task<OneOf<ScenePose, NotFound, Error<string>>> RedoPoseAsync(string poseId) => throw new NotSupportedException();
		public Task<OneOf<ScenePose, NotFound, Error<string>>> MovePoseAsync(string poseId, string afterPoseId) => throw new NotSupportedException();
		public Task<OneOf<ScenePose, NotFound>> DeletePoseAsync(string poseId) => throw new NotSupportedException();
		public Task<OneOf<IReadOnlyList<ScenePoseEdit>, NotFound>> GetPoseEditsAsync(string poseId) => throw new NotSupportedException();
		public Task<OneOf<SceneMember, NotFound>> AddMemberAsync(string sceneId, string playerDbref, string role) => throw new NotSupportedException();
		public Task<OneOf<None, NotFound>> RemoveMemberAsync(string sceneId, string playerDbref) => throw new NotSupportedException();
		public Task<OneOf<IReadOnlyList<SceneMember>, NotFound>> GetMembersAsync(string sceneId, string? role = null) => throw new NotSupportedException();
		public Task<OneOf<SceneMember, NotFound>> GetMemberAsync(string sceneId, string playerDbref) => throw new NotSupportedException();
		public Task<OneOf<None, NotFound>> SetFocusAsync(string playerDbref, string? sceneId = null) => throw new NotSupportedException();
		public Task<OneOf<Scene, NotFound>> GetCurrentSceneAsync(string playerDbref) => throw new NotSupportedException();
		public Task<OneOf<SceneMember, NotFound>> SetShowAsAsync(string sceneId, string playerDbref, string showAs) => throw new NotSupportedException();
		public Task<ScenePlot> UpsertPlotAsync(string? plotId, string title, string description, string ownerDbref) => throw new NotSupportedException();
		public Task<OneOf<ScenePlot, NotFound>> GetPlotAsync(string plotId) => throw new NotSupportedException();
		public Task<OneOf<None, NotFound>> LinkSceneToPlotAsync(string plotId, string sceneId) => throw new NotSupportedException();
		public Task<OneOf<None, NotFound>> UnlinkSceneFromPlotAsync(string plotId, string sceneId) => throw new NotSupportedException();
		public Task<OneOf<IReadOnlyList<string>, NotFound>> GetTagsAsync(string sceneId) => throw new NotSupportedException();
		public Task<OneOf<IReadOnlyList<string>, NotFound>> GetCastAsync(string sceneId) => throw new NotSupportedException();
	}
}
