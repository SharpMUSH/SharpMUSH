using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Plugins.Scene.Web;
using Scene = SharpMUSH.Plugins.Scene.Models.Scene;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// README §7.5: the profile's Recent scenes and Often plays with read a character's scenes through
/// <c>?participant=</c> and <c>/partners</c>. Both answer through the same visibility rule as every
/// other scene read, so a private scene the caller could not open never shows up as a title or feeds
/// a partner count.
/// </summary>
public class SceneControllerParticipantTests
{
	private const string Tomas = "#312";
	private const string Ilsa = "#313";
	private const string Wren = "#314";

	private static Scene SceneOf(string id, bool isPublic, long lastActivity, string owner = "#9") => new(
		Id: id, Status: "finished", IsPublic: isPublic, IsTempRoom: false, ScheduledFor: null, StartedAt: 1,
		LastActivityAt: lastActivity, PoseCount: 3, OwnerDbref: owner, OwnerName: "Owner", StarterDbref: owner,
		StarterName: "Owner", RoomDbref: null, RoomName: string.Empty, Meta: new Dictionary<string, string> { ["title"] = $"Scene {id}" });

	/// <summary>Scenes with their member lists; <c>mine</c> answers the scenes a member holds, newest first.</summary>
	private sealed class MemberSceneService(Dictionary<Scene, string[]> scenes) : SceneServiceStub
	{
		public List<(string Filter, string? Viewer)> Lists { get; } = [];

		private static bool Same(string a, string b) =>
			DBRef.TryParse(a, out var x) && DBRef.TryParse(b, out var y) && x!.Value.SameObjectAs(y!.Value);

		public override Task<IReadOnlyList<Scene>> ListScenesAsync(string filter, string? viewerDbref = null,
			long? fromUtcMillis = null, long? toUtcMillis = null, int count = 50)
		{
			Lists.Add((filter, viewerDbref));
			IReadOnlyList<Scene> result = filter == "mine" && viewerDbref is not null
				? scenes.Where(kv => kv.Value.Any(m => Same(m, viewerDbref))).Select(kv => kv.Key)
					.OrderByDescending(s => s.LastActivityAt).Take(count).ToList()
				: scenes.Keys.OrderByDescending(s => s.LastActivityAt).Take(count).ToList();
			return Task.FromResult(result);
		}

		public override Task<Found<SceneMember>> GetMemberAsync(string sceneId, string playerDbref)
		{
			var members = scenes.First(kv => kv.Key.Id == sceneId).Value;
			return Task.FromResult<Found<SceneMember>>(members.FirstOrDefault(m => Same(m, playerDbref)) is { } member
				? new SceneMember(sceneId, member, member, "participant", string.Empty, false, 1)
				: new NotFound());
		}

		public override Task<Found<IReadOnlyList<SceneMember>>> GetMembersAsync(string sceneId, string? role = null)
		{
			var members = scenes.First(kv => kv.Key.Id == sceneId).Value;
			IReadOnlyList<SceneMember> list = members
				.Select(m => new SceneMember(sceneId, m, NameOf(m), "participant", string.Empty, false, 1)).ToList();
			return Task.FromResult<Found<IReadOnlyList<SceneMember>>>(list);
		}

		private static string NameOf(string dbref) => dbref switch { Tomas => "Tomas Reyes", Ilsa => "Ilsa Varn", Wren => "Wren Halloway", _ => dbref };
	}

	private static SceneController ControllerFor(MemberSceneService service, string? caller) =>
		new(service)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext { User = SceneFixture.PrincipalFor(caller) }
			}
		};

	private static MemberSceneService World() => new(new Dictionary<Scene, string[]>
	{
		[SceneOf("1", isPublic: true, lastActivity: 10)] = [Tomas, Ilsa],
		[SceneOf("2", isPublic: true, lastActivity: 30)] = [Tomas, Ilsa, Wren],
		[SceneOf("3", isPublic: false, lastActivity: 40)] = [Tomas, Wren],
		[SceneOf("4", isPublic: true, lastActivity: 50)] = [Ilsa, Wren],
	});

	[Test]
	public async Task Participant_ListsThatCharactersVisibleScenes_NewestFirst()
	{
		var service = World();
		var result = await ControllerFor(service, caller: null).ListScenes(participant: Tomas);

		var scenes = ((IEnumerable<SceneController.SceneDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(scenes.Select(s => s.Id).ToList()).IsEquivalentTo(new[] { "2", "1" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("scene 3 is private and the anonymous caller is not in it; scene 4 is not Tomas's");
		await Assert.That(service.Lists).Contains(("mine", Tomas));
	}

	[Test]
	public async Task Participant_Count_IsAppliedAfterVisibility()
	{
		var service = new MemberSceneService(new Dictionary<Scene, string[]>
		{
			[SceneOf("1", isPublic: true, lastActivity: 10)] = [Tomas],
			[SceneOf("2", isPublic: true, lastActivity: 20)] = [Tomas],
			[SceneOf("5", isPublic: false, lastActivity: 50)] = [Tomas],
			[SceneOf("6", isPublic: false, lastActivity: 60)] = [Tomas],
		});

		var result = await ControllerFor(service, caller: null).ListScenes(count: 2, participant: Tomas);

		var scenes = ((IEnumerable<SceneController.SceneDto>)((OkObjectResult)result).Value!).Select(s => s.Id).ToList();
		await Assert.That(scenes).IsEquivalentTo(new[] { "2", "1" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("the two newest scenes are private to Tomas; the two newest the caller may see are older");
	}

	[Test]
	public async Task List_Count_IsAppliedAfterVisibility()
	{
		var service = new MemberSceneService(new Dictionary<Scene, string[]>
		{
			[SceneOf("1", isPublic: true, lastActivity: 10)] = [Tomas],
			[SceneOf("5", isPublic: false, lastActivity: 50)] = [Tomas],
			[SceneOf("6", isPublic: false, lastActivity: 60)] = [Tomas],
		});

		var result = await ControllerFor(service, caller: null).ListScenes(count: 1);

		var scenes = ((IEnumerable<SceneController.SceneDto>)((OkObjectResult)result).Value!).Select(s => s.Id).ToList();
		await Assert.That(scenes).IsEquivalentTo(new[] { "1" }).Because("the newest visible scene, not the newest scene");
	}

	/// <summary>
	/// However many hidden scenes sort first, the list is read at most twice — the window asked for, then the
	/// whole list — rather than once per doubling, and the count is clamped.
	/// </summary>
	[Test]
	public async Task List_ReadsAtMostTwice_AndClampsTheCount()
	{
		var world = new Dictionary<Scene, string[]> { [SceneOf("old", isPublic: true, lastActivity: 1)] = [Tomas] };
		for (var i = 0; i < 40; i++) world[SceneOf($"hidden{i}", isPublic: false, lastActivity: 100 + i)] = [Wren];
		var service = new MemberSceneService(world);

		var result = await ControllerFor(service, caller: null).ListScenes(count: 1);

		var scenes = ((IEnumerable<SceneController.SceneDto>)((OkObjectResult)result).Value!).Select(s => s.Id).ToList();
		await Assert.That(scenes).IsEquivalentTo(new[] { "old" });
		await Assert.That(service.Lists.Count).IsEqualTo(2);

		for (var i = 0; i < SceneController.MaxListCount + 10; i++) world[SceneOf($"open{i}", isPublic: true, lastActivity: 1000 + i)] = [Tomas];
		var many = await ControllerFor(service, caller: null).ListScenes(count: 100_000);
		await Assert.That(((IEnumerable<SceneController.SceneDto>)((OkObjectResult)many).Value!).Count()).IsEqualTo(SceneController.MaxListCount);
	}

	[Test]
	public async Task Participant_Offset_PagesThroughTheVisibleScenes()
	{
		var world = new Dictionary<Scene, string[]>();
		for (var i = 0; i < 6; i++) world[SceneOf($"open{i}", isPublic: true, lastActivity: 10 + i)] = [Tomas];
		for (var i = 0; i < 4; i++) world[SceneOf($"hidden{i}", isPublic: false, lastActivity: 100 + i)] = [Tomas];
		var service = new MemberSceneService(world);

		var result = await ControllerFor(service, caller: null).ListScenes(count: 2, participant: Tomas, offset: 2);

		var scenes = ((IEnumerable<SceneController.SceneDto>)((OkObjectResult)result).Value!).Select(s => s.Id).ToList();
		await Assert.That(scenes).IsEquivalentTo(new[] { "open3", "open2" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("the offset counts the scenes the caller may see, not the hidden ones before them");
	}

	[Test]
	public async Task Participant_StateAndSearch_NarrowTheList()
	{
		static Scene With(string id, string status, long lastActivity, string title, string room = "", string? pitch = null) =>
			SceneOf(id, isPublic: true, lastActivity) with
			{
				Status = status,
				RoomName = room,
				Meta = pitch is null
					? new Dictionary<string, string> { ["title"] = title }
					: new Dictionary<string, string> { ["title"] = title, ["summary"] = pitch },
			};

		var service = new MemberSceneService(new Dictionary<Scene, string[]>
		{
			[With("live", "active", 50, "Salt Market at Dusk")] = [Tomas],
			[With("paused", "paused", 40, "Night Watch", room: "Lower Docks")] = [Tomas],
			[With("planned", "new", 30, "Ferry Steps")] = [Tomas],
			[With("done", "finished", 20, "The Lamplighters", pitch: "A meeting at the docks")] = [Tomas],
		});
		var controller = ControllerFor(service, caller: null);

		async Task<List<string>> Ids(string? state, string? search) =>
			((IEnumerable<SceneController.SceneDto>)((OkObjectResult)await controller.ListScenes(participant: Tomas, state: state, search: search)).Value!)
				.Select(s => s.Id).ToList();

		await Assert.That(await Ids("live", null)).IsEquivalentTo(new[] { "live" });
		await Assert.That(await Ids("upcoming", null)).IsEquivalentTo(new[] { "paused", "planned" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(await Ids("finished", null)).IsEquivalentTo(new[] { "done" });
		await Assert.That(await Ids(null, "DOCKS")).IsEquivalentTo(new[] { "paused", "done" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("the room of one and the pitch of the other hold the text");
		await Assert.That(await Ids("finished", "market")).IsEmpty();
		await Assert.That(await controller.ListScenes(participant: Tomas, state: "someday")).IsTypeOf<BadRequestObjectResult>();
	}

	[Test]
	public async Task Partners_CountOverTheLast50VisibleScenes_NotTheLast50Scenes()
	{
		var scenes = new Dictionary<Scene, string[]> { [SceneOf("public", isPublic: true, lastActivity: 1)] = [Tomas, Ilsa] };
		for (var i = 0; i < 50; i++)
		{
			scenes[SceneOf($"private-{i}", isPublic: false, lastActivity: 100 + i)] = [Tomas, Wren];
		}

		var result = await ControllerFor(new MemberSceneService(scenes), caller: null).GetPartners(Tomas);

		var partners = ((IEnumerable<SceneController.ScenePartnerDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(partners.Select(p => p.Dbref).ToList()).IsEquivalentTo(new[] { Ilsa })
			.Because("fifty newer private scenes do not push the one visible scene out of the window");
	}

	[Test]
	public async Task Participant_ThatIsNotADbref_Is400()
	{
		var result = await ControllerFor(World(), caller: null).ListScenes(participant: "Tomas");
		await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
	}

	[Test]
	public async Task Partners_CountSharedVisibleScenes_MostSharedFirst_WithoutTheCharacter()
	{
		var result = await ControllerFor(World(), caller: null).GetPartners(Tomas);

		var partners = ((IEnumerable<SceneController.ScenePartnerDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(partners.Select(p => (p.Dbref, p.Scenes)).ToList())
			.IsEquivalentTo(new[] { (Ilsa, 2), (Wren, 1) }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("Wren shares the private scene 3 too, which this caller cannot see");
		await Assert.That(partners[0].Name).IsEqualTo("Ilsa Varn");
	}

	[Test]
	public async Task Partners_SeeThePrivateScene_WhenTheCallerIsInIt()
	{
		var result = await ControllerFor(World(), caller: "#314:1700000000000").GetPartners(Tomas);

		var partners = ((IEnumerable<SceneController.ScenePartnerDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(partners.Single(p => p.Dbref == Wren).Scenes).IsEqualTo(2);
	}

	[Test]
	public async Task Partners_ForABadDbref_Is400()
	{
		var result = await ControllerFor(World(), caller: null).GetPartners("nobody");
		await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
	}
}
