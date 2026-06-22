using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// Phase 8 boundary + ALC proofs for the relocated Scene storage.
///
/// <para><b>Boundary:</b> the core <c>ISharpDatabase</c> provider must NOT implement <see cref="ISceneService"/>
/// any more — the storage moved into the Scene plugin. Resolving <see cref="ISceneService"/> must therefore
/// hand back the plugin's storage type (an assembly that is NOT the provider's), and the provider class's own
/// interface set must no longer include <see cref="ISceneService"/>.</para>
///
/// <para><b>ALC smoke:</b> the resolved storage lives in the plugin's collectible
/// <see cref="AssemblyLoadContext"/> (a different ALC than the host default), yet a real call succeeds —
/// proving the provider's DB-client connection, handed across the boundary through the host-shared accessor,
/// unified by type identity (the make-or-break risk of this refactor).</para>
/// </summary>
[NotInParallel]
public class SceneStoragePluginBoundaryTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private ISceneService Scenes => WebAppFactory.Services.GetRequiredService<ISceneService>();
	private ISharpDatabase Database => WebAppFactory.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task CoreProvider_DoesNotImplementISceneService()
	{
		// The active provider (Arango/Memgraph/Surreal) no longer carries scene storage.
		await Assert.That(Database is ISceneService).IsFalse();
		await Assert.That(typeof(ISceneService).IsAssignableFrom(Database.GetType())).IsFalse();
	}

	[Test]
	public async Task SceneService_IsProvidedByThePluginNotTheCoreProvider()
	{
		var scenes = Scenes;
		// The storage came from the Scene plugin assembly, distinct from the core provider's assembly.
		await Assert.That(scenes.GetType().Assembly).IsNotEqualTo(Database.GetType().Assembly);
		await Assert.That(scenes.GetType().Assembly.GetName().Name).IsEqualTo("SharpMUSH.Plugins.Scene");
	}

	[Test]
	public async Task SceneService_LivesInACollectiblePluginAlc_AndACallSucceeds()
	{
		var scenes = Scenes;

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
		var created = await scenes.CreateSceneAsync(roomDbref: "", ownerDbref: "#1", title: "ALC smoke scene");
		await Assert.That(created.Id).IsNotNull().And.IsNotEmpty();

		var fetched = await scenes.GetSceneAsync(created.Id);
		await Assert.That(fetched.IsT0).IsTrue();
		await Assert.That(fetched.AsT0.Id).IsEqualTo(created.Id);
	}
}
