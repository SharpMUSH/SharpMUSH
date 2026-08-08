using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Plugins.Scene.Web;

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
	/// <summary>Builds the controller with <paramref name="claimValue"/> as the acting character (null = anonymous).</summary>
	private static SceneController ControllerFor(FixedSceneService service, string? claimValue) =>
		new(service)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext { User = SceneFixture.PrincipalFor(claimValue) }
			}
		};

	[Test]
	public async Task GetScene_PrivateSceneOwnedByCaller_IsVisible()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: false));
		var controller = ControllerFor(service, SceneFixture.GodObjid);

		var result = await controller.GetScene("scene-1");

		await Assert.That(result).IsTypeOf<OkObjectResult>();
		// The owner check must answer on its own — a membership lookup would mask a broken owner comparison.
		await Assert.That(service.MemberLookups).IsEmpty();
	}

	[Test]
	public async Task GetScene_PrivateSceneCallerIsMember_IsVisible()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#7", isPublic: false)) { MemberDbref = "#1" };
		var controller = ControllerFor(service, SceneFixture.GodObjid);

		var result = await controller.GetScene("scene-1");

		await Assert.That(result).IsTypeOf<OkObjectResult>();
		// The storage resolves the reference against the live object, so it must be handed a parseable
		// dbref — the canonical objid spelling, not a hash-stripped fragment.
		await Assert.That(service.MemberLookups).Contains(SceneFixture.GodObjid);
	}

	[Test]
	public async Task GetScene_PrivateSceneCallerIsNeitherOwnerNorMember_Is404()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#7", isPublic: false));
		var controller = ControllerFor(service, SceneFixture.GodObjid);

		var result = await controller.GetScene("scene-1");

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}

	/// <summary>A recycled dbref: same number, different creation stamp. Must not authorize.</summary>
	[Test]
	public async Task GetScene_PrivateSceneOwnedByARecycledObjid_Is404()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1:1700000000000", isPublic: false));
		var controller = ControllerFor(service, SceneFixture.GodObjid);

		var result = await controller.GetScene("scene-1");

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task GetScene_PrivateScene_IsHiddenFromAnonymousCallers()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: false));
		var controller = ControllerFor(service, claimValue: null);

		var result = await controller.GetScene("scene-1");

		await Assert.That(result).IsTypeOf<NotFoundResult>();
		// Anonymous short-circuits: no character means nothing to look a membership up by.
		await Assert.That(service.MemberLookups).IsEmpty();
	}

	[Test]
	public async Task GetScene_PublicScene_IsVisibleToAnonymousCallers()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: true));
		var controller = ControllerFor(service, claimValue: null);

		var result = await controller.GetScene("scene-1");

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task ListScenes_ReturnsOnlyTheScenesTheCallerMaySee()
	{
		var service = new FixedSceneService(
			SceneFixture.SceneOwnedBy("#1", isPublic: false) with { Id = "mine" },
			SceneFixture.SceneOwnedBy("#7", isPublic: false) with { Id = "theirs" },
			SceneFixture.SceneOwnedBy("#7", isPublic: true) with { Id = "public" });
		var controller = ControllerFor(service, SceneFixture.GodObjid);

		var result = await controller.ListScenes();

		var listed = ((IEnumerable<SceneController.SceneDto>)((OkObjectResult)result).Value!).Select(s => s.Id);
		await Assert.That(listed).IsEquivalentTo(new[] { "mine", "public" });
	}
}
