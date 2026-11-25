using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Plugins.Scene.Storage;
using SharpMUSH.Plugins.Scene.Web;
using Scene = SharpMUSH.Plugins.Scene.Models.Scene;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// The schedule the portal's Upcoming Scenes widget reads (<c>api/scenes?filter=upcoming</c>) lists a private,
/// invite-only scene only to its owner, the players invited to it and wizards; a private scene that is not
/// invite-only, and a public one, to anyone holding scene.view.
/// </summary>
public class SceneControllerScheduleVisibilityTests
{
	private const string Invited = "#5:1700000000500";

	/// <summary>Three scheduled scenes owned by #7, the invite-only one with #5 invited.</summary>
	private sealed class ScheduleService : SceneServiceStub
	{
		private static Scene Scheduled(string id, bool isPublic, string? joinPolicy = null) =>
			SceneFixture.SceneOwnedBy("#7", isPublic) with
			{
				Id = id,
				Status = "scheduled",
				ScheduledFor = 1_800_000_000_000,
				Meta = joinPolicy is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["joinpolicy"] = joinPolicy },
			};

		public string? AskedFilter { get; private set; }

		public override Task<IReadOnlyList<Scene>> ListScenesAsync(string filter, string? viewerDbref = null,
			long? fromUtcMillis = null, long? toUtcMillis = null, int count = 50)
		{
			AskedFilter = filter;
			return Task.FromResult<IReadOnlyList<Scene>>(
			[
				Scheduled("public", isPublic: true),
				Scheduled("private", isPublic: false),
				Scheduled("invite-only", isPublic: false, joinPolicy: "invite-only"),
			]);
		}

		public override Task<Found<SceneMember>> GetMemberAsync(string sceneId, string playerDbref) =>
			Task.FromResult<Found<SceneMember>>(new NotFound());

		public override Task<Found<IReadOnlyList<SceneInvitation>>> GetInvitationsAsync(string sceneId) =>
			Task.FromResult<Found<IReadOnlyList<SceneInvitation>>>(sceneId == "invite-only"
				? new List<SceneInvitation> { new(Invited, "Juno") }
				: new List<SceneInvitation>());
	}

	private static async Task<List<string>> UpcomingFor(string? caller, bool seesEvery = false)
	{
		var service = new ScheduleService();
		var controller = new SceneController(service, new SceneReadAccessStub(mayRead: true, seesEvery))
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = SceneFixture.PrincipalFor(caller) } }
		};

		var result = await controller.ListScenes(filter: "upcoming", count: 50);
		await Assert.That(service.AskedFilter).IsEqualTo("upcoming");
		return ((IEnumerable<SceneController.SceneDto>)((OkObjectResult)result).Value!).Select(s => s.Id).ToList();
	}

	[Test]
	public async Task AStrangerSeesThePublicAndPrivateScenesButNotTheInviteOnlyOne() =>
		await Assert.That(await UpcomingFor(SceneFixture.GodObjid)).IsEquivalentTo(new[] { "public", "private" });

	[Test]
	public async Task AnInvitedPlayerSeesTheInviteOnlyScene() =>
		await Assert.That(await UpcomingFor(Invited)).IsEquivalentTo(new[] { "public", "private", "invite-only" });

	[Test]
	public async Task TheOwnerSeesTheInviteOnlyScene() =>
		await Assert.That(await UpcomingFor("#7:1700000000000")).IsEquivalentTo(new[] { "public", "private", "invite-only" });

	[Test]
	public async Task AWizardSeesTheInviteOnlyScene() =>
		await Assert.That(await UpcomingFor(SceneFixture.GodObjid, seesEvery: true)).IsEquivalentTo(new[] { "public", "private", "invite-only" });

	[Test]
	public async Task AGuestWithNoCharacterSeesNoInviteOnlyScene() =>
		await Assert.That(await UpcomingFor(null)).IsEquivalentTo(new[] { "public", "private" });
}
