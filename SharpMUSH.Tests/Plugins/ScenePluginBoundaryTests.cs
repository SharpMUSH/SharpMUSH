using System.Reflection;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// Boundary proofs: the full scene vertical now lives ENTIRELY inside the <c>SharpMUSH.Plugins.Scene</c>
/// plugin — there is NO shared <c>SharpMUSH.Plugins.Scene.Contracts</c> assembly any more. These are pure
/// reflection assertions over the loaded assemblies (no DB, no host), establishing that the engine is fully
/// scene-agnostic so the Scene plugin can be a third-party, hot-loadable plugin.
///
/// <list type="bullet">
///   <item><see cref="SharpMUSH.Server"/> carries no scene controller, no scene hub, and no scene types
///   (the controller + SceneHub + SceneEventMessage all live in the plugin).</item>
///   <item><see cref="SharpMUSH.Library"/> carries no scene models, no <c>ISceneService</c>, and no
///   <c>SceneEventMessage</c>. <c>NotificationType</c> no longer carries a <c>Scene</c> value.</item>
///   <item><see cref="SharpMUSH.Client"/> carries no scene MODELS/<c>ISceneService</c>; its only scene
///   type is its own <c>SceneEventMessage</c> wire DTO (independent of the plugin) plus its view models.</item>
///   <item>the <c>SharpMUSH.Plugins.Scene.Contracts</c> assembly is gone — nothing can load it.</item>
///   <item><see cref="SharpMUSH.Server.Hubs.GameHub"/> exposes no scene surface.</item>
/// </list>
/// </summary>
public class ScenePluginBoundaryTests
{
	private static readonly Assembly ServerAssembly = typeof(GameHub).Assembly;
	private static readonly Assembly LibraryAssembly = typeof(SharpMUSH.Library.Plugins.IPlugin).Assembly;
	private static readonly Assembly ClientAssembly = typeof(SharpMUSH.Client.Services.ConnectionStateService).Assembly;

	[Test]
	public async Task Server_HasNoSceneControllerOrHubOrTypes()
	{
		var sceneTypes = ServerAssembly.GetTypes()
			.Where(t => t.Name.Contains("Scene", StringComparison.Ordinal))
			.Select(t => t.FullName)
			.ToList();

		await Assert.That(sceneTypes)
			.IsEmpty()
			.Because($"the Server assembly must carry no scene types; found: {string.Join(", ", sceneTypes)}");
	}

	[Test]
	public async Task GameHub_HasNoSceneSurface()
	{
		var memberNames = typeof(GameHub).GetMembers(BindingFlags.Public | BindingFlags.NonPublic
			| BindingFlags.Instance | BindingFlags.Static)
			.Select(m => m.Name)
			.ToList();

		await Assert.That(memberNames.Any(n => n.Contains("Scene", StringComparison.Ordinal)))
			.IsFalse()
			.Because("GameHub must expose no scene methods/helpers");

		// The strongly-typed client surface carries no scene push either.
		var clientMembers = typeof(IGameHubClient).GetMethods().Select(m => m.Name).ToList();
		await Assert.That(clientMembers.Any(n => n.Contains("Scene", StringComparison.Ordinal)))
			.IsFalse()
			.Because("IGameHubClient must expose no ReceiveSceneMessage");
	}

	[Test]
	public async Task Library_HasNoSceneModelsOrServiceOrEventMessage()
	{
		var offenders = LibraryAssembly.GetTypes()
			.Where(t =>
				t.Name is "ISceneService" or "SceneEventMessage" or "ScenePose" or "ScenePoseEdit"
					or "ScenePlot" or "SceneMember"
				// The Scene MODEL record specifically (not e.g. NotificationType which merely had a Scene value).
				|| (t.Name == "Scene" && t.Namespace?.StartsWith("SharpMUSH.Library", StringComparison.Ordinal) == true))
			.Select(t => t.FullName)
			.ToList();

		await Assert.That(offenders)
			.IsEmpty()
			.Because($"the Library assembly must carry no scene models/ISceneService/SceneEventMessage; found: {string.Join(", ", offenders)}");
	}

	[Test]
	public async Task Client_HasNoSceneServiceOrPluginModels()
	{
		// The client keeps ONLY its own view models (SceneSummary/ScenePoseView/SceneMemberView) and its own
		// SceneEventMessage wire DTO. It must NOT carry ISceneService or the plugin's domain MODEL records.
		var offenders = ClientAssembly.GetTypes()
			.Where(t =>
				t.Name is "ISceneService" or "ScenePose" or "ScenePoseEdit" or "ScenePlot" or "SceneMember"
				|| (t.Name == "Scene"))
			.Select(t => t.FullName)
			.ToList();

		await Assert.That(offenders)
			.IsEmpty()
			.Because($"the Client assembly must carry no ISceneService or plugin scene models; found: {string.Join(", ", offenders)}");

		// Its scene DTO is the client's OWN, in SharpMUSH.Client.Models — not the plugin's type.
		var clientSceneEvent = ClientAssembly.GetType("SharpMUSH.Client.Models.SceneEventMessage");
		await Assert.That(clientSceneEvent).IsNotNull()
			.Because("the Client must define its own SceneEventMessage wire DTO");
	}

	[Test]
	public async Task SceneContractsAssembly_IsGone()
	{
		// Nothing — not the host, not a test — can load the deleted shared Contracts assembly.
		var loaded = AppDomain.CurrentDomain.GetAssemblies()
			.Any(a => a.GetName().Name == "SharpMUSH.Plugins.Scene.Contracts");
		await Assert.That(loaded).IsFalse()
			.Because("the SharpMUSH.Plugins.Scene.Contracts assembly was deleted");

		var loadFailed = false;
		try
		{
			System.Reflection.Assembly.Load("SharpMUSH.Plugins.Scene.Contracts");
		}
		catch (FileNotFoundException)
		{
			loadFailed = true;
		}
		catch (FileLoadException)
		{
			loadFailed = true;
		}

		await Assert.That(loadFailed).IsTrue()
			.Because("the SharpMUSH.Plugins.Scene.Contracts assembly must not be loadable");
	}
}
