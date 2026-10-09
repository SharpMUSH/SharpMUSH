using System.Text.Json;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The portal's copy of the Scene plugin's realtime event must read and write the plugin's wire shape
/// exactly. The plugin pins the same literal in
/// <c>SharpMUSH.Tests.ScenePlugin/SceneEventMessageContractTests.cs</c>; the two records share no
/// assembly (the plugin loads in its own AssemblyLoadContext), so matching literals keep them in step.
/// </summary>
public class SceneEventMessageContractTests
{
	private const string Wire =
		"""{"sceneId":"42","eventType":"pose","actorName":"Tomas","poseId":"7","content":"Tomas waves.","markup":"Tomas waves.","tags":[],"source":"ooc","location":"Lower Docks","timestamp":1790741467794,"actorObjId":"#312:1718000000","type":"ooc"}""";

	[Test]
	public async Task The_client_record_writes_the_plugin_wire_shape()
	{
		var message = new SceneEventMessage(
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
			ActorObjId: "#312:1718000000",
			Type: "ooc");

		await Assert.That(JsonSerializer.Serialize(message, JsonSerializerOptions.Web)).IsEqualTo(Wire);
	}

	[Test]
	public async Task The_client_record_reads_ActorObjId()
	{
		var message = JsonSerializer.Deserialize<SceneEventMessage>(Wire, JsonSerializerOptions.Web)!;

		await Assert.That(message.ActorObjId).IsEqualTo("#312:1718000000");
		await Assert.That(message.Type).IsEqualTo("ooc");
	}

	[Test]
	public async Task An_event_without_ActorObjId_reads_as_null()
	{
		var older = Wire.Replace("\"actorObjId\":\"#312:1718000000\",", "", StringComparison.Ordinal);

		var message = JsonSerializer.Deserialize<SceneEventMessage>(older, JsonSerializerOptions.Web)!;

		await Assert.That(message.ActorObjId).IsNull();
	}
}
