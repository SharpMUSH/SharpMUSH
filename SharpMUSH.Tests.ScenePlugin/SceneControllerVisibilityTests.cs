using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Plugins.Scene.Web;
using System.Security.Claims;
using Scene = SharpMUSH.Plugins.Scene.Models.Scene;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// The Scene REST API's permission boundary, exercised directly against <see cref="SceneController"/> with a
/// stub service — no database, no ALC, no HTTP pipeline — so every caller shape (owner / member / stranger /
/// anonymous) is reachable. The in-process integration suite authenticates every request as God, which is why
/// it can only cover the owner side; these cover the rest.
///
/// <para>REGRESSION: the two references being compared are spelled differently. The
/// <c>character_dbref</c> claim is a full objid minted from the session's character
/// (<c>#1:1785989066109</c>); a scene's owner dbref is resolved from a live graph edge and comes back bare
/// (<c>#1</c>). Comparing them as strings could never match, so <em>every</em> non-public scene was invisible
/// to its own owner and the API's only reachable "visible" answer was <c>IsPublic</c>.</para>
/// </summary>
public class SceneControllerVisibilityTests
{
	private const string CharacterDbrefClaim = "character_dbref";

	/// <summary>God's claim as the auth handlers actually mint it — objid, not bare dbref.</summary>
	private const string GodObjid = "#1:1785989066109";

	private static Scene SceneOwnedBy(string? ownerDbref, bool isPublic) => new(
		Id: "scene-1",
		Status: "active",
		IsPublic: isPublic,
		IsTempRoom: false,
		ScheduledFor: null,
		StartedAt: 1,
		LastActivityAt: 2,
		PoseCount: 0,
		OwnerDbref: ownerDbref,
		OwnerName: "God",
		StarterDbref: ownerDbref,
		StarterName: "God",
		RoomDbref: null,
		RoomName: string.Empty,
		Meta: new Dictionary<string, string>());

	/// <summary>Builds the controller with <paramref name="claimValue"/> as the acting character (null = anonymous).</summary>
	private static SceneController ControllerFor(StubSceneService service, string? claimValue)
	{
		var identity = claimValue is null
			? new ClaimsIdentity()
			: new ClaimsIdentity([new Claim(CharacterDbrefClaim, claimValue)], "TestScheme");

		return new SceneController(service)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
			}
		};
	}

	[Test]
	public async Task GetScene_PrivateSceneOwnedByCaller_IsVisible()
	{
		var service = new StubSceneService(SceneOwnedBy("#1", isPublic: false));
		var controller = ControllerFor(service, GodObjid);

		var result = await controller.GetScene("scene-1");

		await Assert.That(result).IsTypeOf<OkObjectResult>();
		// The owner check must answer on its own — a membership lookup would mask a broken owner comparison.
		await Assert.That(service.MemberLookups).IsEmpty();
	}

	[Test]
	public async Task GetScene_PrivateSceneCallerIsMember_IsVisible()
	{
		var service = new StubSceneService(SceneOwnedBy("#7", isPublic: false)) { MemberDbref = "#1" };
		var controller = ControllerFor(service, GodObjid);

		var result = await controller.GetScene("scene-1");

		await Assert.That(result).IsTypeOf<OkObjectResult>();
		// The storage resolves the reference against the live object, so it must be handed a parseable
		// dbref — the canonical objid spelling, not a hash-stripped fragment.
		await Assert.That(service.MemberLookups).Contains(GodObjid);
	}

	[Test]
	public async Task GetScene_PrivateSceneCallerIsNeitherOwnerNorMember_Is404()
	{
		var service = new StubSceneService(SceneOwnedBy("#7", isPublic: false));
		var controller = ControllerFor(service, GodObjid);

		var result = await controller.GetScene("scene-1");

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}

	/// <summary>A recycled dbref: same number, different creation stamp. Must not authorize.</summary>
	[Test]
	public async Task GetScene_PrivateSceneOwnedByARecycledObjid_Is404()
	{
		var service = new StubSceneService(SceneOwnedBy("#1:1700000000000", isPublic: false));
		var controller = ControllerFor(service, GodObjid);

		var result = await controller.GetScene("scene-1");

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task GetScene_PrivateScene_IsHiddenFromAnonymousCallers()
	{
		var service = new StubSceneService(SceneOwnedBy("#1", isPublic: false));
		var controller = ControllerFor(service, claimValue: null);

		var result = await controller.GetScene("scene-1");

		await Assert.That(result).IsTypeOf<NotFoundResult>();
		// Anonymous short-circuits: no character means nothing to look a membership up by.
		await Assert.That(service.MemberLookups).IsEmpty();
	}

	[Test]
	public async Task GetScene_PublicScene_IsVisibleToAnonymousCallers()
	{
		var service = new StubSceneService(SceneOwnedBy("#1", isPublic: true));
		var controller = ControllerFor(service, claimValue: null);

		var result = await controller.GetScene("scene-1");

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task ListScenes_ReturnsOnlyTheScenesTheCallerMaySee()
	{
		var service = new StubSceneService(
			SceneOwnedBy("#1", isPublic: false) with { Id = "mine" },
			SceneOwnedBy("#7", isPublic: false) with { Id = "theirs" },
			SceneOwnedBy("#7", isPublic: true) with { Id = "public" });
		var controller = ControllerFor(service, GodObjid);

		var result = await controller.ListScenes();

		var listed = ((IEnumerable<SceneController.SceneDto>)((OkObjectResult)result).Value!).Select(s => s.Id);
		await Assert.That(listed).IsEquivalentTo(new[] { "mine", "public" });
	}

	/// <summary>
	/// Scene service returning a fixed set of scenes, recording the dbrefs it was asked to look memberships
	/// up by (the second half of the same spelling bug — a mangled reference resolves to no object at all).
	/// </summary>
	private sealed class StubSceneService(params Scene[] scenes) : SceneServiceStub
	{
		/// <summary>The member dbref that holds a membership edge on every scene, or null for none.</summary>
		public string? MemberDbref { get; init; }

		public List<string> MemberLookups { get; } = [];

		public override Task<OneOf<Scene, NotFound>> GetSceneAsync(string sceneId) =>
			Task.FromResult(scenes.FirstOrDefault(s => s.Id == sceneId) is { } scene
				? OneOf<Scene, NotFound>.FromT0(scene)
				: new NotFound());

		public override Task<IReadOnlyList<Scene>> ListScenesAsync(string filter, string? viewerDbref = null,
			long? fromUtcMillis = null, long? toUtcMillis = null, int count = 50) =>
			Task.FromResult<IReadOnlyList<Scene>>(scenes);

		public override Task<OneOf<SceneMember, NotFound>> GetMemberAsync(string sceneId, string playerDbref)
		{
			MemberLookups.Add(playerDbref);

			// Match the storages, which resolve the reference through the live object rather than comparing
			// strings: a bare dbref and its objid name the same player.
			var matches = MemberDbref is { } member
				&& DBRef.TryParse(member, out var expected)
				&& DBRef.TryParse(playerDbref, out var actual)
				&& expected!.Value.SameObjectAs(actual!.Value);

			return Task.FromResult(matches
				? OneOf<SceneMember, NotFound>.FromT0(
					new SceneMember(sceneId, MemberDbref, "God", "participant", string.Empty, true, 3))
				: new NotFound());
		}
	}
}
