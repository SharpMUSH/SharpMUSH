using Core.Arango;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Storage;
using Scene = SharpMUSH.Plugins.Scene.Models.Scene;

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

	private sealed class FakeArangoAccessor : IArangoStorageAccessor
	{
		public IArangoContext Context => throw new NotSupportedException();
		public ArangoHandle Handle => throw new NotSupportedException();
		public ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();
	}

	/// <summary>A no-database storage core that records its own invocation, used to observe the chain order.</summary>
	private sealed class RecordingStorage(List<string> calls) : SceneServiceStub, ISceneStorage
	{
		public override Task<OneOf<Scene, NotFound>> GetSceneAsync(string sceneId)
		{
			calls.Add("core");
			return Task.FromResult<OneOf<Scene, NotFound>>(new NotFound());
		}
	}

	private sealed class FirstBehavior(ISceneService inner, List<string> calls) : SceneServiceStub, ISceneServiceBehavior
	{
		public override Task<OneOf<Scene, NotFound>> GetSceneAsync(string sceneId)
		{
			calls.Add("first");
			return inner.GetSceneAsync(sceneId);
		}
	}

	private sealed class SecondBehavior(ISceneService inner, List<string> calls) : SceneServiceStub, ISceneServiceBehavior
	{
		public override Task<OneOf<Scene, NotFound>> GetSceneAsync(string sceneId)
		{
			calls.Add("second");
			return inner.GetSceneAsync(sceneId);
		}
	}
}
