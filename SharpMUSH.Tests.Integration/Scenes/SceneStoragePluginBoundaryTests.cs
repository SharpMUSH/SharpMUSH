using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// Boundary + ALC proofs for the Scene storage, now that the entire scene contract surface
/// (<c>ISceneService</c> + the models) lives INSIDE <c>SharpMUSH.Plugins.Scene</c> with no shared Contracts
/// assembly. The host cannot compile-reference any scene type, so everything here is reflection over the
/// runtime-loaded plugin assembly.
///
/// <para><b>Boundary:</b> the core <c>ISharpDatabase</c> provider must NOT implement the plugin's
/// <c>ISceneService</c>; the service is supplied by the plugin's own storage type (in the plugin assembly,
/// not the provider's).</para>
///
/// <para><b>ALC smoke:</b> the resolved storage lives in the plugin's collectible
/// <see cref="AssemblyLoadContext"/> (a different ALC than the host default), yet a real round-trip call
/// succeeds — proving the provider's DB-client connection, handed across the boundary through the
/// host-shared accessor, unified by type identity (the make-or-break risk of this refactor).</para>
/// </summary>
[NotInParallel]
public class SceneStoragePluginBoundaryTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private ISharpDatabase Database => WebAppFactory.Services.GetRequiredService<ISharpDatabase>();

	/// <summary>The plugin's <c>ISceneService</c> interface type, loaded in the plugin's ALC.</summary>
	private static Type SceneServiceType =>
		AppDomain.CurrentDomain.GetAssemblies()
			.First(a => a.GetName().Name == "SharpMUSH.Plugins.Scene")
			.GetType("SharpMUSH.Plugins.Scene.Storage.ISceneService", throwOnError: true)!;

	/// <summary>The active-provider scene service, resolved from host DI by the plugin's interface type.</summary>
	private object SceneService => WebAppFactory.Services.GetRequiredService(SceneServiceType);

	[Test]
	public async Task CoreProvider_DoesNotImplementISceneService()
	{
		// The active provider (Arango/Memgraph/Surreal) no longer carries scene storage.
		await Assert.That(SceneServiceType.IsAssignableFrom(Database.GetType())).IsFalse();
	}

	[Test]
	public async Task SceneService_IsProvidedByThePluginNotTheCoreProvider()
	{
		var scenes = SceneService;
		// The storage came from the Scene plugin assembly, distinct from the core provider's assembly.
		await Assert.That(scenes.GetType().Assembly).IsNotEqualTo(Database.GetType().Assembly);
		await Assert.That(scenes.GetType().Assembly.GetName().Name).IsEqualTo("SharpMUSH.Plugins.Scene");
	}

	[Test]
	public async Task SceneService_LivesInACollectiblePluginAlc_AndACallSucceeds()
	{
		var scenes = SceneService;

		// The plugin loaded in its own (collectible) ALC, not the host default — this is the boundary the
		// DB-client types had to unify across.
		var pluginAlc = AssemblyLoadContext.GetLoadContext(scenes.GetType().Assembly);
		var hostAlc = AssemblyLoadContext.GetLoadContext(typeof(SceneStoragePluginBoundaryTests).Assembly);
		await Assert.That(pluginAlc).IsNotNull();
		await Assert.That(pluginAlc).IsNotEqualTo(hostAlc);
		await Assert.That(pluginAlc!.IsCollectible).IsTrue();

		// A real round-trip through the storage exercises the host-shared accessor's connection: create a
		// scene (owned by #1) and read it straight back. If the DB-client type identity had NOT unified,
		// the accessor-returned connection could not have been used inside the plugin and this would throw.
		// Invoked reflectively because the host cannot name the plugin's ISceneService / Scene model.
		var createTask = (Task)SceneServiceType
			.GetMethod("CreateSceneAsync")!
			.Invoke(scenes, ["", "#1", "ALC smoke scene"])!;
		await createTask;
		var created = createTask.GetType().GetProperty("Result")!.GetValue(createTask)!;
		var createdId = (string)created.GetType().GetProperty("Id")!.GetValue(created)!;
		await Assert.That(createdId).IsNotNull().And.IsNotEmpty();

		var getTask = (Task)SceneServiceType
			.GetMethod("GetSceneAsync")!
			.Invoke(scenes, [createdId])!;
		await getTask;
		var got = getTask.GetType().GetProperty("Result")!.GetValue(getTask)!;
		// OneOf<Scene, NotFound>: IsT0 must be true (found).
		var isT0 = (bool)got.GetType().GetProperty("IsT0")!.GetValue(got)!;
		await Assert.That(isT0).IsTrue();
	}
}
