using SharpMUSH.Implementation.Commands.SceneCommand;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Tests.Hubs;

/// <summary>
/// Unit tests for <see cref="SceneBroadcast"/>. Validates the realtime subject is
/// derived as <c>game.scene.{id}</c> — the exact wire <c>NatsBridgeService</c>
/// subscribes to (<c>game.scene.*</c>) and that it aligns with the SignalR
/// <c>scene:{id}</c> group key.
/// </summary>
public class SceneBroadcastTests
{
	[Test]
	[Arguments("42", "game.scene.42")]
	[Arguments("777", "game.scene.777")]
	[Arguments("abc-123", "game.scene.abc-123")]
	public async Task SubjectForScene_DerivesGameSceneSubject(string sceneId, string expected)
	{
		var subject = SceneBroadcast.SubjectForScene(sceneId);
		await Assert.That(subject).IsEqualTo(expected);
	}

	[Test]
	public async Task SubjectForScene_MatchesNatsBridgeWildcard()
	{
		// The bridge subscribes to "game.scene.*"; the publish subject must share the prefix.
		var subject = SceneBroadcast.SubjectForScene("5");
		await Assert.That(subject.StartsWith("game.scene.", StringComparison.Ordinal)).IsTrue();
	}

	[Test]
	public async Task SubjectScene_LastToken_MatchesSceneGroupKey()
	{
		// NatsBridgeService takes the SceneId from the message and forwards to
		// GameHub.SceneGroupName(sceneId); the subject id token must be the same id.
		const string sceneId = "99";
		var subject = SceneBroadcast.SubjectForScene(sceneId);
		var lastToken = subject[(subject.LastIndexOf('.') + 1)..];

		await Assert.That(lastToken).IsEqualTo(sceneId);
		await Assert.That(GameHub.SceneGroupName(sceneId)).IsEqualTo($"scene:{sceneId}");
	}
}
