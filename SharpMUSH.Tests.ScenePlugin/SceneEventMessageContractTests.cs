using System.Text.Json;
using SharpMUSH.Plugins.Scene.Commands;
using SharpMUSH.Plugins.Scene.Models;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// The wire shape of the realtime scene event. The portal has its own copy of the record
/// (<c>SharpMUSH.Client.Models.SceneEventMessage</c>) and pins the SAME JSON in
/// <c>SharpMUSH.Tests.BUnit/Services/SceneEventMessageContractTests.cs</c>: the two records share no
/// assembly, so matching literals are what keeps them in step.
/// </summary>
public class SceneEventMessageContractTests
{
	/// <summary>SignalR's JSON protocol names properties in camelCase, which is what <see cref="JsonSerializerOptions.Web"/> does.</summary>
	private const string Wire =
		"""{"sceneId":"42","eventType":"pose","actorName":"Tomas","poseId":"7","content":"Tomas waves.","markup":"Tomas waves.","tags":[],"source":"ooc","location":"Lower Docks","timestamp":1790741467794,"actorObjId":"#312:1718000000","type":"ooc","meta":{"frequency":"Harbour Watch"}}""";

	private static SceneEventMessage Sample(string? actorObjId = "#312:1718000000") => new(
		SceneId: "42",
		EventType: "pose",
		ActorName: "Tomas",
		PoseId: "7",
		Content: "Tomas waves.",
		Markup: "Tomas waves.",
		Tags: [],
		Source: "ooc",
		Location: "Lower Docks",
		Timestamp: 1790741467794,
		ActorObjId: actorObjId,
		Type: "ooc",
		Meta: new Dictionary<string, string> { ["frequency"] = "Harbour Watch" });

	[Test]
	public async Task Meta_is_the_last_member_on_the_wire()
	{
		await Assert.That(JsonSerializer.Serialize(Sample(), JsonSerializerOptions.Web)).IsEqualTo(Wire);
	}

	[Test]
	public async Task ActorObjId_round_trips()
	{
		var back = JsonSerializer.Deserialize<SceneEventMessage>(Wire, JsonSerializerOptions.Web)!;

		await Assert.That(back.ActorObjId).IsEqualTo("#312:1718000000");
		await Assert.That(back.SceneId).IsEqualTo("42");
		await Assert.That(back.Type).IsEqualTo("ooc");
	}

	/// <summary>NATS carries the record with the serializer's defaults (PascalCase); it must survive that too.</summary>
	[Test]
	public async Task ActorObjId_round_trips_with_default_options()
	{
		var json = JsonSerializer.Serialize(Sample());
		var back = JsonSerializer.Deserialize<SceneEventMessage>(json)!;

		await Assert.That(back.ActorObjId).IsEqualTo("#312:1718000000");
	}

	[Test]
	public async Task A_missing_ActorObjId_reads_as_null()
	{
		var older = Wire.Replace("\"actorObjId\":\"#312:1718000000\",", "", StringComparison.Ordinal);

		var back = JsonSerializer.Deserialize<SceneEventMessage>(older, JsonSerializerOptions.Web)!;

		await Assert.That(back.ActorObjId).IsNull();
		await Assert.That(back.Timestamp).IsEqualTo(1790741467794);
	}

	[Test]
	public async Task BuildMessage_carries_the_resolved_objid()
	{
		var pose = new ScenePose(
			Id: "7",
			SceneId: "42",
			AuthorDbref: "#312",
			AuthorName: "Tomas",
			ShowAsName: string.Empty,
			OriginDbref: "#1201",
			OriginName: "Lower Docks",
			Source: "ooc",
			Tags: new List<string>(),
			Meta: new Dictionary<string, string> { ["frequency"] = "Harbour Watch" },
			CreatedAt: 1,
			IsDeleted: false,
			Content: "Tomas waves.",
			Markup: "Tomas waves.",
			EditCount: 1,
			LastEditedAt: null,
			LastEditorDbref: null,
			LastEditorName: null,
			Type: "ooc");

		var message = SceneBroadcast.BuildMessage("42", "pose", pose, "#312:1718000000");

		await Assert.That(message.ActorObjId).IsEqualTo("#312:1718000000");
		await Assert.That(message.ActorName).IsEqualTo("Tomas");
		await Assert.That(message.Type).IsEqualTo("ooc");
		await Assert.That(message.Meta!["frequency"]).IsEqualTo("Harbour Watch");
	}

	[Test]
	public async Task BuildMessage_without_a_pose_has_no_actor()
	{
		var message = SceneBroadcast.BuildMessage("42", "meta", null, null);

		await Assert.That(message.ActorObjId).IsNull();
		await Assert.That(message.ActorName).IsEmpty();
	}
}
