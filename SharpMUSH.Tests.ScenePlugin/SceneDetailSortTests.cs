using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Plugins.Scene.Web;
using Scene = SharpMUSH.Plugins.Scene.Models.Scene;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// A detail kept for code (<c>hidden</c>) and a detail that orders scenes by another (<c>sort</c>), as the timeline
/// package's <c>icdate</c> sorts by <c>icsecs</c>; and <c>api/scenes?sort=</c>, which a character's timeline reads.
/// </summary>
public class SceneDetailSortTests
{
	private const string Tomas = "#312";

	private static SceneDetail Detail(Result<SceneDetail> read) => read switch
	{
		SceneDetail detail => detail,
		Error<string> error => throw new InvalidOperationException(error.Value),
	};

	[Test]
	[Arguments("joinpolicy")]
	[Arguments("playercap")]
	public async Task ParticipationFieldsCannotBeReconfiguredAsCustomDetails(string key)
	{
		await Assert.That(SceneDetails.Normalize(key) is Error<string>).IsTrue();
	}

	[Test]
	public async Task A_detail_reads_hidden_and_the_key_it_sorts_by()
	{
		var shown = Detail(SceneDetails.Read("icdate", """{"label":"IC date","sort":"ICSECS"}"""));
		await Assert.That(shown.Sort).IsEqualTo("icsecs");
		await Assert.That(shown.Hidden).IsFalse();

		var kept = Detail(SceneDetails.Read("icsecs", """{"label":"IC moment","hidden":true}"""));
		await Assert.That(kept.Hidden).IsTrue();
		await Assert.That(kept.Sort).IsEmpty();
	}

	[Test]
	[Arguments("""{"sort":"icdate"}""", "sort names another detail, not this one")]
	[Arguments("""{"sort":"title"}""", "sort: 'title' is a scene field already, so it can't be a detail")]
	[Arguments("""{"sort":3}""", "sort must be text")]
	[Arguments("""{"hidden":"yes"}""", "hidden must be true or false")]
	public async Task A_bad_hidden_or_sort_is_refused(string json, string reason)
	{
		var read = SceneDetails.Read("icdate", json);
		await Assert.That(read is Error<string> { Value: var refused } ? refused : "read").IsEqualTo(reason);
	}

	[Test]
	[Arguments("1773516600", 1773516600)]
	[Arguments(" -86400 ", -86400)]
	[Arguments("", null)]
	[Arguments("Spring", null)]
	public async Task A_sort_value_is_a_number_or_none(string value, long? expected)
	{
		await Assert.That(SceneDetails.SortValue(value)).IsEqualTo((decimal?)expected);
	}

	private static Scene SceneOf(string id, long lastActivity, string? icsecs, bool isPublic = true)
	{
		var meta = new Dictionary<string, string> { ["title"] = $"Scene {id}" };
		if (icsecs is not null) meta["icsecs"] = icsecs;
		return new(Id: id, Status: "finished", IsPublic: isPublic, IsTempRoom: false, ScheduledFor: null, StartedAt: 1,
			LastActivityAt: lastActivity, PoseCount: 3, OwnerDbref: Tomas, OwnerName: "Tomas", StarterDbref: Tomas,
			StarterName: "Tomas", RoomDbref: null, RoomName: string.Empty, Meta: meta);
	}

	/// <summary>Every scene is Tomas's; the list answers newest first, as the storage does.</summary>
	private sealed class TimelineService(params Scene[] scenes) : SceneServiceStub
	{
		public override Task<IReadOnlyList<Scene>> ListScenesAsync(string filter, string? viewerDbref = null,
			long? fromUtcMillis = null, long? toUtcMillis = null, int count = 50) =>
			Task.FromResult<IReadOnlyList<Scene>>(scenes.OrderByDescending(s => s.LastActivityAt).Take(count).ToList());

		public override Task<Found<SceneMember>> GetMemberAsync(string sceneId, string playerDbref) =>
			Task.FromResult<Found<SceneMember>>(new NotFound());
	}

	private static List<string> Ids(IActionResult result) =>
		((IEnumerable<SceneController.SceneDto>)((OkObjectResult)result).Value!).Select(s => s.Id).ToList();

	private static SceneController ControllerFor(TimelineService service) =>
		new(service, new SceneReadAccessStub())
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext { User = SceneFixture.PrincipalFor(null) }
			}
		};

	[Test]
	public async Task Sort_orders_scenes_by_the_number_earliest_first_and_the_ones_without_last()
	{
		var service = new TimelineService(
			SceneOf("1", lastActivity: 10, icsecs: "1773516600"),
			SceneOf("2", lastActivity: 20, icsecs: null),
			SceneOf("3", lastActivity: 30, icsecs: "-86400"),
			SceneOf("4", lastActivity: 40, icsecs: "1767225600"),
			SceneOf("5", lastActivity: 50, icsecs: "99", isPublic: false));

		var all = Ids(await ControllerFor(service).ListScenes(participant: Tomas, sort: "icsecs"));
		await Assert.That(all).IsEquivalentTo(new[] { "3", "4", "1", "2" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("a flashback before 1970 comes first, an undated scene last, and a private scene the caller is not in never");

		var page = Ids(await ControllerFor(service).ListScenes(count: 2, participant: Tomas, offset: 1, sort: "icsecs"));
		await Assert.That(page).IsEquivalentTo(new[] { "4", "1" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("offset and count page through the sorted list, not the newest-first one");
	}
}
