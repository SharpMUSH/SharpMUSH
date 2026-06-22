using System.Reflection;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// Phase 9 boundary proofs: the full scene vertical now lives in the Scene plugin + its Contracts assembly,
/// NOT in the core "main" assemblies. These are pure reflection assertions over the loaded assemblies — no
/// DB, no host.
///
/// <list type="bullet">
///   <item><see cref="SharpMUSH.Server"/> carries no scene controller, no scene hub, and no scene types
///   (the controller + SceneHub moved into the plugin; SceneEventMessage moved into Contracts).</item>
///   <item><see cref="SharpMUSH.Library"/> carries no scene models, no <c>ISceneService</c>, and no
///   <c>SceneEventMessage</c> (all moved into <c>SharpMUSH.Plugins.Scene.Contracts</c>). The single
///   <c>NotificationType.Scene</c> enum value deliberately remains (an enum cannot be partially extended).</item>
///   <item><see cref="SharpMUSH.Server.Hubs.GameHub"/> no longer exposes any scene surface.</item>
/// </list>
/// </summary>
public class ScenePluginBoundaryTests
{
	private static readonly Assembly ServerAssembly = typeof(GameHub).Assembly;
	private static readonly Assembly LibraryAssembly = typeof(SharpMUSH.Library.Plugins.IPlugin).Assembly;

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
			.Because("GameHub must expose no scene methods/helpers after Phase 9");

		// The strongly-typed client surface carries no scene push either.
		var clientMembers = typeof(IGameHubClient).GetMethods().Select(m => m.Name).ToList();
		await Assert.That(clientMembers.Any(n => n.Contains("Scene", StringComparison.Ordinal)))
			.IsFalse()
			.Because("IGameHubClient must expose no ReceiveSceneMessage after Phase 9");
	}

	[Test]
	public async Task Library_HasNoSceneModelsOrServiceOrEventMessage()
	{
		var offenders = LibraryAssembly.GetTypes()
			.Where(t =>
				t.Name is "ISceneService" or "SceneEventMessage" or "ScenePose" or "ScenePoseEdit"
					or "ScenePlot" or "SceneMember"
				// The Scene MODEL record specifically (not e.g. NotificationType which merely has a Scene value).
				|| (t.Name == "Scene" && t.Namespace?.StartsWith("SharpMUSH.Library", StringComparison.Ordinal) == true))
			.Select(t => t.FullName)
			.ToList();

		await Assert.That(offenders)
			.IsEmpty()
			.Because($"the Library assembly must carry no scene models/ISceneService/SceneEventMessage; found: {string.Join(", ", offenders)}");
	}

	[Test]
	public async Task SceneContractTypes_LiveInTheContractsAssembly()
	{
		// The contract types resolve to the Contracts assembly (host-shared), proving the move landed there.
		await Assert.That(typeof(SharpMUSH.Plugins.Scene.Contracts.ISceneService).Assembly.GetName().Name)
			.IsEqualTo("SharpMUSH.Plugins.Scene.Contracts");
		await Assert.That(typeof(SharpMUSH.Plugins.Scene.Contracts.SceneEventMessage).Assembly.GetName().Name)
			.IsEqualTo("SharpMUSH.Plugins.Scene.Contracts");
		await Assert.That(typeof(SharpMUSH.Plugins.Scene.Contracts.Scene).Assembly.GetName().Name)
			.IsEqualTo("SharpMUSH.Plugins.Scene.Contracts");
	}
}
