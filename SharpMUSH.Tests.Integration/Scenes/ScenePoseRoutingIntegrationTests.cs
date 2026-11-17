using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// Where an ordinary pose goes when the poser's focused scene and the room do not line up.
///
/// <para>A player focused on a scene running in another room has an ordinary <c>pose</c> refused, so it
/// is neither said here nor recorded there; <c>+scene/pose &lt;id&gt;=&lt;text&gt;</c> still reaches it from
/// anywhere. A player posing into their scene while another scene running in the same room had a pose more
/// recently is warned, since they are likely in the wrong one, and the pose still goes to theirs.</para>
/// </summary>
[NotInParallel]
public class ScenePoseRoutingIntegrationTests
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

	private static string Num(string dbref)
	{
		var s = dbref.Trim();
		var colon = s.IndexOf(':');
		return colon < 0 ? s : s[..colon];
	}

	private async Task RunAs(long handle, string command) =>
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));

	private IReadOnlyList<string> HeardBy(string objid, int fromCount) =>
		[.. Notifications.For(DBRef.Parse(objid)).Skip(fromCount)];

	private int HeardCount(string objid) => Notifications.CountFor(DBRef.Parse(objid));

	private async Task<(string Dbref, long Handle, string Name)> CreatePlayerAsync(string prefix)
	{
		var name = $"{prefix}{Guid.NewGuid():N}"[..15];
		await God1($"@pcreate {name}=pw-{name}");
		var dbref = (await God1($"think [pmatch({name})]")).Message.ToPlainText()?.Trim() ?? string.Empty;
		if (!DBRef.TryParse(dbref, out var parsed) || parsed is null)
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");

		await God1($"@role/assign {dbref}=approved");
		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, parsed.Value);
		_actors[handle] = parsed.Value;
		return (dbref, handle, name);
	}

	private async Task<string> DigAsync(string prefix)
	{
		var dug = (await God1($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}")).Message.ToPlainText()?.Trim()
			?? string.Empty;
		return dug.Split(' ').First(t => t.StartsWith('#'));
	}

	/// <summary>See <c>SceneWebComposeIntegrationTests.PutLoggerInMasterRoomAsync</c>: other suites move it.</summary>
	private async Task PutLoggerInMasterRoomAsync()
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var objects = await registry.GetPackageObjectsAsync("scene");
		var logger = DBRef.Parse(objects.Single(o => o.Ref == "logger").Objid).ToString();
		await God1($"@teleport {logger}=#2");
	}

	private async Task<string> CreateSceneAsync((string Dbref, long Handle, string Name) owner, string title)
	{
		await RunAs(owner.Handle, $"+scene/create {title}");
		var sceneId = await Eval($"scenefocus({Num(owner.Dbref)})");
		if (sceneId.StartsWith("#-1", StringComparison.Ordinal))
			throw new InvalidOperationException($"+scene/create left no focus: {sceneId}");
		return sceneId;
	}

	private async Task<int> PoseCountAsync(string sceneId) =>
		int.Parse(await Eval($"words(sceneposes({sceneId}))"));

	[Test]
	public async Task A_pose_while_focused_on_a_scene_in_another_room_is_refused()
	{
		await PutLoggerInMasterRoomAsync();
		var sceneRoom = await DigAsync("AwayScene");
		var elsewhere = await DigAsync("AwayHere");
		var poser = await CreatePlayerAsync("AwP");
		var (witness, _, _) = await CreatePlayerAsync("AwW");
		await God1($"@tel {poser.Dbref}={sceneRoom}");
		var sceneId = await CreateSceneAsync(poser, "Away scene");
		await God1($"@tel {poser.Dbref}={elsewhere}");
		await God1($"@tel {witness}={elsewhere}");
		var poses = await PoseCountAsync(sceneId);
		var poserBefore = HeardCount(poser.Dbref);
		var witnessBefore = HeardCount(witness);
		var marker = TestIsolationHelpers.GenerateUniqueName("waves");

		await RunAs(poser.Handle, $"pose {marker}");

		await Assert.That(await PoseCountAsync(sceneId)).IsEqualTo(poses);
		await Assert.That(HeardBy(witness, witnessBefore).Any(line => line.Contains(marker))).IsFalse();
		await Assert.That(HeardBy(poser.Dbref, poserBefore)
				.Any(line => line.Contains($"+scene/pose {sceneId}=") && line.Contains("somewhere else")))
			.IsTrue();

		await RunAs(poser.Handle, $"+scene/pose {sceneId}={marker} from afar");

		await Assert.That(await PoseCountAsync(sceneId)).IsEqualTo(poses + 1);
	}

	[Test]
	public async Task A_pose_into_a_quieter_scene_warns_and_still_records()
	{
		await PutLoggerInMasterRoomAsync();
		var room = await DigAsync("BusyRoom");
		var quiet = await CreatePlayerAsync("QtP");
		var busy = await CreatePlayerAsync("BsP");
		await God1($"@tel {quiet.Dbref}={room}");
		await God1($"@tel {busy.Dbref}={room}");
		var quietScene = await CreateSceneAsync(quiet, "Quiet scene");
		var busyScene = await CreateSceneAsync(busy, "Busy scene");
		await RunAs(busy.Handle, "pose talks in the busy scene.");
		var quietPoses = await PoseCountAsync(quietScene);
		var busyPoses = await PoseCountAsync(busyScene);
		var quietBefore = HeardCount(quiet.Dbref);

		await RunAs(quiet.Handle, "pose speaks up.");

		await Assert.That(await PoseCountAsync(quietScene)).IsEqualTo(quietPoses + 1);
		await Assert.That(await PoseCountAsync(busyScene)).IsEqualTo(busyPoses);
		await Assert.That(HeardBy(quiet.Dbref, quietBefore)
				.Any(line => line.Contains($"+scene/join {busyScene}") && line.Contains("more recently")))
			.IsTrue();
	}
}
