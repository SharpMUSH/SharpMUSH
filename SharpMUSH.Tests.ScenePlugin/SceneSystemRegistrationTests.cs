using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
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
	public async Task AddSceneSystem_DefaultsToLightningStorage()
	{
		var services = new ServiceCollection();
		services.AddSingleton<ILightningStorageAccessor>(new FakeLightningAccessor());
		var config = new ConfigurationBuilder().Build();

		services.AddSceneSystem(config);

		await using var sp = services.BuildServiceProvider();
		var svc = sp.GetRequiredService<ISceneService>();

		await Assert.That(svc).IsTypeOf<LightningSceneStorage>();
	}

	[Test]
	public async Task AddSceneSystem_SelectsLightningStorage_WhenProviderIsLightning()
	{
		var services = new ServiceCollection();
		services.AddSingleton<ILightningStorageAccessor>(new FakeLightningAccessor());

		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { ["SHARPMUSH_DATABASE_PROVIDER"] = "lightning" })
			.Build();

		services.AddSceneSystem(config);

		await using var sp = services.BuildServiceProvider();
		var svc = sp.GetRequiredService<ISceneService>();

		await Assert.That(svc).IsTypeOf<LightningSceneStorage>();
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

	/// <summary>
	/// A no-database Lightning accessor. Only <c>OpenTable</c> is exercised: the storage opens its tables
	/// in its constructor, which is the part of the registration this test reaches.
	/// </summary>
	private sealed class FakeLightningAccessor : ILightningStorageAccessor
	{
		public T Read<T>(Func<ITx, T> read) => throw new NotSupportedException();

		public ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken ct = default) =>
			throw new NotSupportedException();

		public IAsyncEnumerable<(byte[] Key, byte[] Value)> RangeAsync(TableDef table, byte[] prefix,
			int pageSize = 256, CancellationToken ct = default) => throw new NotSupportedException();

		public TableDef OpenTable(string name, bool duplicates) =>
			duplicates ? TableDef.Index(name, duplicates: true) : TableDef.Node(name);

		public ValueTask CopyToAsync(string path, bool compact = true, CancellationToken ct = default) =>
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
