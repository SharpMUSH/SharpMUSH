using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Plugins.Scene.Web;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Storage;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// A cancelled scene (+scene/cancel) never ran: the scene lists leave it out, and only its own page still
/// opens it.
/// </summary>
public class LightningSceneListTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;
	private LightningSceneStorage _scenes = null!;

	[Before(Test)]
	public async Task Open()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-scene-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await _db.Migrate();
		_scenes = new LightningSceneStorage(_db);
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

	[Test]
	[Arguments("recent")]
	[Arguments("mine")]
	public async Task ACancelledScene_IsLeftOutOfTheLists(string filter)
	{
		var owner = $"#{(await _db.CreatePlayerAsync("Ines", "pw", new DBRef(0), new DBRef(0), 0)).Number}";
		var kept = await _scenes.CreateSceneAsync("#0", owner, "Kept");
		var cancelled = await _scenes.CreateSceneAsync("#0", owner, "Called off");
		await _scenes.AddMemberAsync(kept.Id, owner, "owner");
		await _scenes.AddMemberAsync(cancelled.Id, owner, "owner");
		await _scenes.SetSceneMetaAsync(cancelled.Id, "status", "cancelled");

		var listed = await _scenes.ListScenesAsync(filter, owner);

		await Assert.That(listed.Select(s => s.Id)).Contains(kept.Id);
		await Assert.That(listed.Select(s => s.Id)).DoesNotContain(cancelled.Id);
	}

	/// <summary>
	/// A window applies before the limit, so finished scenes with no time, however recently active, never crowd
	/// a timed one out of the calendar's slice.
	/// </summary>
	[Test]
	public async Task AWindowedFinishedList_LeavesOutUntimedScenesBeforeTheLimit()
	{
		var timed = await _scenes.CreateSceneAsync("#0", "#1", "Timed");
		await _scenes.SetSceneMetaAsync(timed.Id, "scheduledfor", "5000");
		await _scenes.SetSceneMetaAsync(timed.Id, "status", "finished");
		foreach (var name in new[] { "Untimed one", "Untimed two" })
		{
			var untimed = await _scenes.CreateSceneAsync("#0", "#1", name);
			await _scenes.SetSceneMetaAsync(untimed.Id, "status", "finished");
		}

		var windowed = await _scenes.ListScenesAsync("finished", fromUtcMillis: 1000, count: 1);
		var unwindowed = await _scenes.ListScenesAsync("finished", count: 10);

		await Assert.That(windowed.Select(s => s.Id)).IsEquivalentTo([timed.Id]);
		await Assert.That(unwindowed.Count).IsEqualTo(3);
	}
	[Test]
	public async Task CreationKeepsPitchAndScheduleTogether()
	{
		var when = DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeMilliseconds();
		var scene = await _scenes.CreateSceneAsync("", "#1", "A journey", "Bring a lantern", when);
		await Assert.That(scene.Meta["summary"]).IsEqualTo("Bring a lantern");
		await Assert.That(scene.Status).IsEqualTo("scheduled");
		await Assert.That(scene.ScheduledFor).IsEqualTo(when);
		await Assert.That((await _scenes.ListScenesAsync("upcoming")).Select(s => s.Id)).Contains(scene.Id);
	}

	[Test]
	public async Task ConcurrentJoinersCannotOverfillTheScene()
	{
		var scene = await _scenes.CreateSceneAsync("#0", "#1", "One seat");
		await _scenes.SetSceneMetaAsync(scene.Id, "playercap", "1");
		var results = await Task.WhenAll(Enumerable.Range(20, 12)
			.Select(n => _scenes.JoinSceneAsync(scene.Id, $"#{n}")));
		await Assert.That(results.Count(r => r is SharpMUSH.Plugins.Scene.Models.SceneMember)).IsEqualTo(1);
		await Assert.That((await _scenes.GetMembersAsync(scene.Id)).Expect<IReadOnlyList<SharpMUSH.Plugins.Scene.Models.SceneMember>>().Count).IsEqualTo(1);
	}

	[Test]
	public async Task InviteOnlyRequiresAnInvitationAndRejoiningPreservesTheOwner()
	{
		var scene = await _scenes.CreateSceneAsync("#0", "#1");
		await _scenes.SetSceneMetaAsync(scene.Id, "joinpolicy", "invite-only");
		await Assert.That(await _scenes.JoinSceneAsync(scene.Id, "#20") is SharpMUSH.Library.DiscriminatedUnions.Error<string>).IsTrue();
		await _scenes.InvitePlayerAsync(scene.Id, "#20");
		await Assert.That((await _scenes.JoinSceneAsync(scene.Id, "#20")).Expect<SharpMUSH.Plugins.Scene.Models.SceneMember>().Role).IsEqualTo("participant");
		await _scenes.AddMemberAsync(scene.Id, "#1", "owner");
		await Assert.That((await _scenes.JoinSceneAsync(scene.Id, "#1")).Expect<SharpMUSH.Plugins.Scene.Models.SceneMember>().Role).IsEqualTo("owner");
	}

	[Test]
	public async Task InvalidScheduleDoesNotCreateAnyScene()
	{
		await Assert.That(async () => await _scenes.CreateSceneAsync("", "#1", "No pitch", " ", DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeMilliseconds()))
			.Throws<ArgumentException>();
		await Assert.That((await _scenes.ListScenesAsync("recent")).Count).IsEqualTo(0);
	}

	[Test]
	public async Task MetadataEditsCannotInvalidateTheCapOrEvictMembers()
	{
		var scene = await _scenes.CreateSceneAsync("#0", "#1");
		await _scenes.AddMemberAsync(scene.Id, "#1", "owner");
		await _scenes.AddMemberAsync(scene.Id, "#20", "gm");
		await _scenes.AddMemberAsync(scene.Id, "#21", "observer");
		await _scenes.SetSceneMetaAsync(scene.Id, "playercap", "2");
		await _scenes.SetSceneMetaAsync(scene.Id, "joinpolicy", "limited");
		foreach (var (invalid, reason) in new[]
		{
			("-1", "A player cap is a whole number from 0 to 10000."),
			("10001", "A player cap is a whole number from 0 to 10000."),
			("1.5", "A player cap is a whole number from 0 to 10000."),
			("no", "A player cap is a whole number from 0 to 10000."),
			("0", "A limited scene needs a player cap."),
			("1", "More players than that are already seated.")
		})
			await Assert.That((await _scenes.SetSceneMetaAsync(scene.Id, "playercap", invalid)).Expect<SharpMUSH.Library.DiscriminatedUnions.Error<string>>().Value).IsEqualTo(reason);
		await Assert.That((await _scenes.SetSceneMetaAsync(scene.Id, "joinpolicy", "closed")).Expect<SharpMUSH.Library.DiscriminatedUnions.Error<string>>().Value)
			.IsEqualTo("A join policy is open, invite-only or limited.");
		await Assert.That((await _scenes.AddMemberAsync(scene.Id, "#21", "participant")).Expect<SharpMUSH.Library.DiscriminatedUnions.Error<string>>().Value)
			.IsEqualTo("This scene has reached its player cap.");
		await Assert.That((await _scenes.JoinSceneAsync(scene.Id, "#20")).Expect<SharpMUSH.Plugins.Scene.Models.SceneMember>().Role).IsEqualTo("gm");
		await Assert.That((await _scenes.GetSceneAsync(scene.Id)).Expect<SharpMUSH.Plugins.Scene.Models.Scene>().Meta["playercap"]).IsEqualTo("2");
	}

	[Test]
	public async Task ApiUpcomingStateIncludesAnAtomicallyScheduledScene()
	{
		var scene = await _scenes.CreateSceneAsync("", "#1", "Later", "A complete pitch", DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeMilliseconds());
		var controller = new SceneController(_scenes, new SceneReadAccessStub())
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
		};
		var response = (OkObjectResult)await controller.ListScenes(filter: "upcoming", state: "upcoming");
		var listed = ((IEnumerable<SceneController.SceneDto>)response.Value!).ToList();
		await Assert.That(listed.Select(s => s.Id)).Contains(scene.Id);
		await Assert.That(listed.Single(s => s.Id == scene.Id).Meta["summary"]).IsEqualTo("A complete pitch");
	}

	[Test]
	public async Task PendingInvitationIsNotMembershipOrAPrivateApiVisibilityGrant()
	{
		var guest = $"#{(await _db.CreatePlayerAsync("Invitee", "pw", new DBRef(0), new DBRef(0), 0)).Number}";
		var scene = await _scenes.CreateSceneAsync("#0", "#1");
		await _scenes.AddMemberAsync(scene.Id, "#1", "owner");
		await _scenes.SetSceneMetaAsync(scene.Id, "public", "0");
		await _scenes.SetSceneMetaAsync(scene.Id, "joinpolicy", "invite-only");
		await _scenes.InvitePlayerAsync(scene.Id, guest);
		var controller = new SceneController(_scenes, new SceneReadAccessStub())
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = SceneFixture.PrincipalFor(guest) } }
		};
		await Assert.That(await controller.GetScene(scene.Id)).IsTypeOf<NotFoundResult>();
		await Assert.That(await _scenes.GetMemberAsync(scene.Id, guest) is SharpMUSH.Library.DiscriminatedUnions.NotFound).IsTrue();
		await Assert.That((await _scenes.GetMembersAsync(scene.Id)).Expect<IReadOnlyList<SharpMUSH.Plugins.Scene.Models.SceneMember>>().Count).IsEqualTo(1);
		await Assert.That((await _scenes.ListScenesAsync("mine", guest)).Count).IsEqualTo(0);
		var ownerController = new SceneController(_scenes, new SceneReadAccessStub())
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = SceneFixture.PrincipalFor("#1") } }
		};
		var partners = (OkObjectResult)await ownerController.GetPartners("#1");
		await Assert.That(((IEnumerable<SceneController.ScenePartnerDto>)partners.Value!).Count()).IsEqualTo(0);
		await _scenes.JoinSceneAsync(scene.Id, guest);
		await Assert.That(await controller.GetScene(scene.Id)).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task ARefusedJoinKeepsItsPendingInvitationButAcceptanceConsumesIt()
	{
		var scene = await _scenes.CreateSceneAsync("#0", "#1");
		await _scenes.AddMemberAsync(scene.Id, "#1", "owner");
		await _scenes.SetSceneMetaAsync(scene.Id, "joinpolicy", "invite-only");
		await _scenes.SetSceneMetaAsync(scene.Id, "playercap", "1");
		await _scenes.InvitePlayerAsync(scene.Id, "#20");
		await Assert.That(await _scenes.JoinSceneAsync(scene.Id, "#20") is SharpMUSH.Library.DiscriminatedUnions.Error<string>).IsTrue();
		await _scenes.SetSceneMetaAsync(scene.Id, "playercap", "2");
		await Assert.That(await _scenes.JoinSceneAsync(scene.Id, "#21") is SharpMUSH.Library.DiscriminatedUnions.Error<string>).IsTrue();
		await Assert.That((await _scenes.JoinSceneAsync(scene.Id, "#20")).Expect<SharpMUSH.Plugins.Scene.Models.SceneMember>().Role).IsEqualTo("participant");
		await _scenes.RemoveMemberAsync(scene.Id, "#20");
		await Assert.That(await _scenes.JoinSceneAsync(scene.Id, "#20") is SharpMUSH.Library.DiscriminatedUnions.Error<string>).IsTrue();
	}

}
