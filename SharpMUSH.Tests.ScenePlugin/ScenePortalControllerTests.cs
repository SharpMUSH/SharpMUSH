using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Storage;
using SharpMUSH.Plugins.Scene.Web;
using Scene = SharpMUSH.Plugins.Scene.Models.Scene;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// The drafts and calendar API: a draft is the acting character's own and only for a scene it may see; the public
/// feed lists public scenes, a private link the scenes its character may see, and a replaced link nothing.
/// </summary>
public class ScenePortalControllerTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 6, 0, 0, TimeSpan.Zero);

	private string _path = null!;
	private LightningDatabase _db = null!;
	private LightningScenePortalStore _store = null!;

	[Before(Test)]
	public async Task Open()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-scene-portal-api-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await _db.Migrate();
		_store = new LightningScenePortalStore(_db);
	}

	[After(Test)]
	public async Task Close()
	{
		await _db.DisposeAsync();
		try
		{
			if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: a lingering mdb.lck can outlive the writer join.
		}
	}

	private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	private ScenePortalController ControllerFor(ISceneService service, string? claimValue)
	{
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(SharpMUSHOptions.Default());
		var context = new DefaultHttpContext { User = SceneFixture.PrincipalFor(claimValue) };
		context.Request.Scheme = "https";
		context.Request.Host = new HostString("game.example");
		return new ScenePortalController(service, _store, options, new FrozenClock(Now))
		{
			ControllerContext = new ControllerContext { HttpContext = context }
		};
	}

	private static Scene Scheduled(string id, string owner, bool isPublic, TimeSpan from) =>
		SceneFixture.SceneOwnedBy(owner, isPublic) with
		{
			Id = id,
			Status = "scheduled",
			ScheduledFor = (Now + from).ToUnixTimeMilliseconds(),
			Meta = new Dictionary<string, string> { ["title"] = $"Scene {id} title" }
		};

	private static string Feed(IActionResult result) => ((ContentResult)result).Content!;

	[Test]
	public async Task ADraft_SavedOnOneDevice_IsReadOnAnother()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: true));

		var saved = await ControllerFor(service, SceneFixture.GodObjid)
			.SaveDraft("scene-1", new ScenePortalController.SceneDraftRequest("ooc", "Back in five.", null));
		var read = await ControllerFor(service, SceneFixture.GodObjid).GetDraft("scene-1");

		await Assert.That(saved).IsTypeOf<OkObjectResult>();
		var draft = (ScenePortalController.SceneDraftDto)((OkObjectResult)read).Value!;
		await Assert.That(draft.Mode).IsEqualTo("ooc");
		await Assert.That(draft.Text).IsEqualTo("Back in five.");
		await Assert.That(draft.UpdatedAt).IsEqualTo(Now.ToUnixTimeMilliseconds());
	}

	[Test]
	public async Task ADraft_IsNotAnotherCharactersToRead()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: true));
		await ControllerFor(service, SceneFixture.GodObjid)
			.SaveDraft("scene-1", new ScenePortalController.SceneDraftRequest("emit", "Mine.", null));

		var read = await ControllerFor(service, SceneFixture.StrangerObjid).GetDraft("scene-1");

		await Assert.That(((ScenePortalController.SceneDraftDto)((OkObjectResult)read).Value!).Text).IsEmpty();
	}

	/// <summary>
	/// A pose sent from the phone empties the draft: the empty one is newer than the copy the desktop still holds,
	/// so the desktop empties too instead of putting the sent pose back.
	/// </summary>
	[Test]
	public async Task AnEmptyDraft_IsKeptWithItsTime()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: true));
		var controller = ControllerFor(service, SceneFixture.GodObjid);
		await controller.SaveDraft("scene-1", new ScenePortalController.SceneDraftRequest("emit", "Something.", null));

		await controller.SaveDraft("scene-1", new ScenePortalController.SceneDraftRequest("emit", "", null));

		var read = (ScenePortalController.SceneDraftDto)((OkObjectResult)await controller.GetDraft("scene-1")).Value!;
		await Assert.That(read.Text).IsEmpty();
		await Assert.That(read.UpdatedAt).IsEqualTo(Now.ToUnixTimeMilliseconds());
	}

	[Test]
	public async Task DraftsNeedACharacter()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: true));

		var read = await ControllerFor(service, null).GetDraft("scene-1");
		var write = await ControllerFor(service, null).SaveDraft("scene-1", new ScenePortalController.SceneDraftRequest("emit", "x", null));

		await Assert.That(read).IsTypeOf<UnauthorizedResult>();
		await Assert.That(write).IsTypeOf<UnauthorizedResult>();
	}

	[Test]
	public async Task AScenesDraft_IsRefusedWhenTheSceneIsNotTheCallersToSee()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#7", isPublic: false));

		var write = await ControllerFor(service, SceneFixture.GodObjid)
			.SaveDraft("scene-1", new ScenePortalController.SceneDraftRequest("emit", "x", null));

		await Assert.That(write).IsTypeOf<NotFoundResult>();
	}

	[Test]
	[Arguments("")]
	[Arguments("two words")]
	[Arguments("+scene/emit")]
	public async Task ADraftsMode_IsOneWord(string mode)
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: true));

		var write = await ControllerFor(service, SceneFixture.GodObjid)
			.SaveDraft("scene-1", new ScenePortalController.SceneDraftRequest(mode, "x", null));

		await Assert.That(write).IsTypeOf<BadRequestObjectResult>();
	}

	[Test]
	public async Task ThePublicFeed_ListsOnlyPublicScenes()
	{
		var service = new FixedSceneService(
			Scheduled("2", "#1", isPublic: true, TimeSpan.FromDays(2)),
			Scheduled("3", "#1", isPublic: false, TimeSpan.FromDays(3)));

		var feed = Feed(await ControllerFor(service, null).PublicCalendar());

		await Assert.That(feed).Contains("UID:scene-2@game.example");
		await Assert.That(feed).DoesNotContain("UID:scene-3@");
		await Assert.That(feed).Contains("URL:https://game.example/scenes/2");
	}

	[Test]
	public async Task ThePublicFeed_KeepsARecentSceneAndDropsAnOldOne()
	{
		var service = new FixedSceneService(
			Scheduled("2", "#1", isPublic: true, -TimeSpan.FromDays(3)) with { Status = "finished" },
			Scheduled("3", "#1", isPublic: true, -TimeSpan.FromDays(30)) with { Status = "finished" });

		var feed = Feed(await ControllerFor(service, null).PublicCalendar());

		await Assert.That(feed).Contains("UID:scene-2@");
		await Assert.That(feed).DoesNotContain("UID:scene-3@");
	}

	[Test]
	public async Task APrivateLink_ListsThePrivateScenesItsCharacterRuns()
	{
		var service = new FixedSceneService(
			Scheduled("2", "#1", isPublic: true, TimeSpan.FromDays(2)),
			Scheduled("3", "#1", isPublic: false, TimeSpan.FromDays(3)),
			Scheduled("4", "#7", isPublic: false, TimeSpan.FromDays(4)));
		var made = (ScenePortalController.SceneCalendarLinkDto)((OkObjectResult)await ControllerFor(service, SceneFixture.GodObjid)
			.CreateCalendarLink()).Value!;
		var secret = made.Url!.Split('/').Last().Replace(".ics", "");

		var everything = Feed(await ControllerFor(service, null).PersonalCalendar(secret));
		var mine = Feed(await ControllerFor(service, null).PersonalCalendar(secret, "mine"));

		await Assert.That(made.Url).StartsWith("https://game.example/api/scenes/calendar/");
		await Assert.That(everything).Contains("UID:scene-2@");
		await Assert.That(everything).Contains("UID:scene-3@");
		await Assert.That(everything).DoesNotContain("UID:scene-4@");
		await Assert.That(mine).DoesNotContain("UID:scene-4@");
		await Assert.That(mine).Contains("UID:scene-3@");
	}

	[Test]
	public async Task AReplacedLink_ListsNothing()
	{
		var service = new FixedSceneService(Scheduled("2", "#1", isPublic: true, TimeSpan.FromDays(2)));
		var controller = ControllerFor(service, SceneFixture.GodObjid);
		var first = ((ScenePortalController.SceneCalendarLinkDto)((OkObjectResult)await controller.CreateCalendarLink()).Value!).Url!;
		await controller.CreateCalendarLink();

		var result = await ControllerFor(service, null).PersonalCalendar(first.Split('/').Last().Replace(".ics", ""));

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task TheLinkIsShownOnlyWhenMade()
	{
		var service = new FixedSceneService();
		var controller = ControllerFor(service, SceneFixture.GodObjid);
		await controller.CreateCalendarLink();

		var link = (ScenePortalController.SceneCalendarLinkDto)((OkObjectResult)controller.GetCalendarLink()).Value!;

		await Assert.That(link.Exists).IsTrue();
		await Assert.That(link.Url).IsNull();
		await Assert.That(link.CreatedAt).IsEqualTo(Now.ToUnixTimeMilliseconds());
	}
}
