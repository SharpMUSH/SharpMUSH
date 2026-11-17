using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// A private scene runs like any other: it shows in its room, anyone there may join it, and its owner and cast
/// pose in it. Only once it is finished does it leave everyone's view but the view of those who took part.
/// </summary>
[NotInParallel]
public class ScenePrivacyIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private readonly ConcurrentDictionary<long, DBRef> _actors = new();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task<CallState> God1(string command) =>
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async Task<IReadOnlyList<string>> RunAs(long handle, string command)
	{
		var actor = _actors[handle];
		var before = Notifications.CountFor(actor);
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		return [.. Notifications.For(actor).Skip(before)];
	}

	/// <summary>What <paramref name="expression"/> evaluates to as the player behind <paramref name="handle"/>.</summary>
	private async Task<string> EvalAs(long handle, string expression)
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("Said");
		var heard = await RunAs(handle, $"think {marker} [{expression}]");
		var line = heard.FirstOrDefault(m => m.StartsWith(marker, StringComparison.Ordinal)) ?? string.Empty;
		return line.Length > marker.Length ? line[(marker.Length + 1)..].Trim() : string.Empty;
	}

	private static string Num(string dbref) => dbref.Split(':')[0].Trim();

	private async Task<string> RoomAsync() =>
		(await God1($"@dig {TestIsolationHelpers.GenerateUniqueName("PrivateRoom")}")).Message.ToPlainText().Trim();

	private async Task<(string Dbref, long Handle)> CreatePlayerAsync(string room)
	{
		var name = TestIsolationHelpers.GenerateUniqueName("Priv");
		await TestIsolationHelpers.CreateNamedTestPlayerAsync(WebAppFactoryArg.Services,
			WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>(), name, DBRef.Parse(room));
		var dbref = (await God1($"think [pmatch({name})]")).Message.ToPlainText()?.Trim() ?? string.Empty;
		if (!DBRef.TryParse(dbref, out var parsed) || parsed is null)
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");

		await God1($"@role/assign {dbref}=approved");
		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, parsed.Value);
		_actors[handle] = parsed.Value;
		return (dbref, handle);
	}

	private async Task PutLoggerInMasterRoomAsync()
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var objects = await registry.GetPackageObjectsAsync("scene");
		var logger = DBRef.Parse(objects.Single(o => o.Ref == "logger").Objid).ToString();
		await God1($"@teleport {logger}=#2");
	}

	/// <summary>A running private scene the owner made, in a room of its own, and its id.</summary>
	private async Task<(string Room, string Owner, long OwnerHandle, string SceneId)> RunningPrivateSceneAsync()
	{
		await PutLoggerInMasterRoomAsync();
		var room = await RoomAsync();
		var (owner, ownerHandle) = await CreatePlayerAsync(room);
		await RunAs(ownerHandle, $"+scene/create {TestIsolationHelpers.GenerateUniqueName("Hush")}");
		var sceneId = await Eval($"scenefocus({Num(owner)})");
		await Assert.That(sceneId).DoesNotStartWith("#-1");
		await RunAs(ownerHandle, "+scene/private");
		await RunAs(ownerHandle, "+scene/start");
		await Assert.That(await Eval($"scene({sceneId},public)")).IsEqualTo("0");
		await Assert.That(await Eval($"scene({sceneId},status)")).IsEqualTo("active");
		return (room, owner, ownerHandle, sceneId);
	}

	[Test]
	public async Task Owner_PosesInTheirPrivateScene()
	{
		var (_, _, ownerHandle, sceneId) = await RunningPrivateSceneAsync();

		var typed = TestIsolationHelpers.GenerateUniqueName("Typed");
		await RunAs(ownerHandle, $"pose waves {typed}");
		await Assert.That(await Eval($"scenepose({sceneId},[last(sceneposes({sceneId}))],content)")).Contains(typed);

		var web = TestIsolationHelpers.GenerateUniqueName("Web");
		var heard = await RunAs(ownerHandle, $"+scene/pose {sceneId}=nods {web}");
		await Assert.That(await Eval($"scenepose({sceneId},[last(sceneposes({sceneId}))],content)")).Contains(web)
			.Because($"saw instead: [{string.Join(" // ", heard)}]");

		var finished = await RunAs(ownerHandle, "+scene/finish");
		await Assert.That(finished.Any(m => m.Contains($"Scene {sceneId} finished.", StringComparison.Ordinal)))
			.IsTrue().Because($"saw instead: [{string.Join(" // ", finished)}]");
	}

	[Test]
	public async Task Owner_PosesInTheirPrivateScene_AfterLeavingAndRejoining()
	{
		var (_, _, ownerHandle, sceneId) = await RunningPrivateSceneAsync();
		await RunAs(ownerHandle, "+scene/leave");
		await RunAs(ownerHandle, $"+scene/join {sceneId}");

		var typed = TestIsolationHelpers.GenerateUniqueName("Back");
		await RunAs(ownerHandle, $"pose returns {typed}");
		await Assert.That(await Eval($"scenepose({sceneId},[last(sceneposes({sceneId}))],content)")).Contains(typed);
	}

	/// <summary>
	/// Leaving before posing drops the owner from the cast, as it does anyone. The scene is still theirs: the portal
	/// shows it to them, and its compose box and Finish button send verbs that must not answer "No such scene".
	/// </summary>
	[Test]
	public async Task Owner_OutOfTheCast_StillPosesAndFinishesById()
	{
		var (_, _, ownerHandle, sceneId) = await RunningPrivateSceneAsync();
		await RunAs(ownerHandle, "+scene/leave");

		var web = TestIsolationHelpers.GenerateUniqueName("Web");
		var heard = await RunAs(ownerHandle, $"+scene/pose {sceneId}=nods {web}");
		await Assert.That(await Eval($"scenepose({sceneId},[last(sceneposes({sceneId}))],content)")).Contains(web)
			.Because($"saw instead: [{string.Join(" // ", heard)}]");

		await RunAs(ownerHandle, "+scene/leave");
		var finished = await RunAs(ownerHandle, $"+scene/finish {sceneId}");
		await Assert.That(finished.Any(m => m.Contains($"Scene {sceneId} finished.", StringComparison.Ordinal)))
			.IsTrue().Because($"saw instead: [{string.Join(" // ", finished)}]");
	}

	[Test]
	public async Task RunningPrivateScene_ShowsInItsRoom_AndAnyoneThereMayJoinAndPose()
	{
		var (room, _, _, sceneId) = await RunningPrivateSceneAsync();
		var (_, visitorHandle) = await CreatePlayerAsync(room);

		await Assert.That(await EvalAs(visitorHandle, $"scenewhere({room})")).IsEqualTo(sceneId);
		await Assert.That(await EvalAs(visitorHandle, $"scene({sceneId},status)")).IsEqualTo("active");

		var joined = await RunAs(visitorHandle, $"+scene/join {sceneId}");
		await Assert.That(joined.Any(m => m.Contains($"Joined and focused on scene {sceneId}", StringComparison.Ordinal)))
			.IsTrue().Because($"saw instead: [{string.Join(" // ", joined)}]");

		var typed = TestIsolationHelpers.GenerateUniqueName("Visitor");
		await RunAs(visitorHandle, $"pose arrives {typed}");
		await Assert.That(await Eval($"scenepose({sceneId},[last(sceneposes({sceneId}))],content)")).Contains(typed);
	}

	[Test]
	public async Task FinishedPrivateScene_IsSeenOnlyByThoseWhoTookPart()
	{
		var (room, owner, ownerHandle, sceneId) = await RunningPrivateSceneAsync();
		var (_, castHandle) = await CreatePlayerAsync(room);
		var (_, outsiderHandle) = await CreatePlayerAsync(room);
		await RunAs(castHandle, $"+scene/join {sceneId}");
		await RunAs(castHandle, $"pose speaks {TestIsolationHelpers.GenerateUniqueName("Cast")}");

		await RunAs(ownerHandle, "+scene/finish");
		await Assert.That(await Eval($"scene({sceneId},status)")).IsEqualTo("finished");

		await Assert.That(await EvalAs(outsiderHandle, $"scene({sceneId},status)")).IsEqualTo("#-1 PERMISSION");
		await Assert.That(await EvalAs(outsiderHandle, $"member([scenelist(finished)],{sceneId})")).IsEqualTo("0");
		var joined = await RunAs(outsiderHandle, $"+scene/join {sceneId}");
		await Assert.That(joined.Any(m => m.Contains($"No such scene: {sceneId}", StringComparison.Ordinal)))
			.IsTrue().Because($"saw instead: [{string.Join(" // ", joined)}]");

		await Assert.That(await EvalAs(castHandle, $"scene({sceneId},status)")).IsEqualTo("finished");
		await Assert.That(await EvalAs(ownerHandle, $"scene({sceneId},status)")).IsEqualTo("finished");
		await Assert.That(await EvalAs(ownerHandle, $"scene({sceneId},owner)")).StartsWith(Num(owner));
	}
}
