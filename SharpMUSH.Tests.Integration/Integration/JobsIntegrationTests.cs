using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The bundled jobs package, installed from the catalogue and driven the way players and staff drive it:
/// typed commands, its bucket hooks, and the routes the jobs-app portal page reads and posts to. Each test
/// installs the package and removes it again, so job numbers start at 1. Not in parallel: the commands it
/// adds are global, and another test's install would renumber the jobs.
/// </summary>
[NotInParallel]
public class JobsIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private async Task<string> God(string command) =>
		(await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message?.ToPlainText()?.Trim() ?? string.Empty;

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>A connected player in a room of its own, holding <paramref name="role"/> when one is named.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix, string? role = null)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
		if (role is not null)
		{
			await God($"@role/assign #{player.DbRef.Number}={role}");
		}

		return player;
	}

	private async Task<string> Objid(TestIsolationHelpers.TestPlayer player) => await God($"think [objid(#{player.DbRef.Number})]");

	private async Task InstallAsync(string package)
	{
		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the commands and starts the timer.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task UninstallAsync()
	{
		await Installer.UninstallAsync("jobs-app", force: true, CancellationToken.None);
		await Installer.UninstallAsync("jobs", force: true, CancellationToken.None);
	}

	private async Task<JsonElement> Http(string method, string path, string body, TestIsolationHelpers.TestPlayer? viewer)
	{
		var dispatcher = WebAppFactoryArg.Services.GetRequiredService<IHttpHandlerCommandDispatcher>();
		DBRef? who = viewer is null ? null : DBRef.Parse(await Objid(viewer));
		var response = (await dispatcher.DispatchAsync(method, path, body, [], IHttpHandlerCommandDispatcher.UnknownAddress, who))
			.Expect<HttpHandlerResult>();
		await Assert.That(response.Status).IsEqualTo(200).Because(response.Body);
		await Assert.That(response.ContentType).StartsWith("application/json");
		return JsonDocument.Parse(response.Body).RootElement.Clone();
	}

	[Test]
	public async Task AJobPassesItsTurnBetweenPlayerAndStaffAndReopensWhenThePlayerAnswers()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsP");
			var staff = await Player("JobsS", "job-admin");

			var filed = await As(player, "+request Need a room=Please, a room with [brackets] and 50%.");
			await Assert.That(filed).Contains("Filed job 1 in Requests: Need a room.");
			await Assert.That(await God("think [job(1,state)]")).IsEqualTo("new");

			var queue = await As(staff, "+jobs");
			await Assert.That(queue).Contains("Need a room").And.Contains("New").And.Contains("+jobs/all lists every open job");
			await Assert.That(await As(player, "+jobs")).Contains("+job/old lists your closed ones")
				.And.DoesNotContain("+jobs/all").Because("the staff-only hints are for staff");
			await Assert.That(await As(staff, "+buckets")).Contains("* you work it \u00b7 +request");
			await Assert.That(await As(player, "+buckets")).DoesNotContain("you work it");

			await As(staff, "+job/claim 1");
			await Assert.That(await God("think [job(1,state)]")).IsEqualTo("staff");
			await As(staff, "+job/due 1=-1h");
			await Assert.That(await God("think [job(1,overdue)]")).IsEqualTo("1")
				.Because("a job waiting on staff past its due date is overdue");

			var told = await As(staff, "+job/reply 1=Which floor?");
			await Assert.That(told).Contains("You replied to job 1.");
			await Assert.That(await God("think [job(1,state)]")).IsEqualTo("player");
			await Assert.That(await God("think [job(1,overdue)]")).IsEqualTo("0")
				.Because("a job waiting on its player is never overdue");

			var read = await As(player, "+myjob 1");
			await Assert.That(read).Contains("Waiting on you").And.Contains("Which floor?")
				.And.Contains("Please, a room with [brackets] and 50%.")
				.Because("text is stored as typed, and a bare [text] is not a help link");

			await As(player, "+ticket/reply 1=The second.");
			await Assert.That(await God("think [job(1,state)]")).IsEqualTo("staff");

			await As(staff, "+job/note 1=Staff eyes only.");
			await Assert.That(await As(player, "+job 1")).DoesNotContain("Staff eyes only.")
				.Because("a staff note is hidden from the player");

			await As(staff, "+job/approve 1=Built.");
			await Assert.That(await God("think [job(1,state)] [job(1,outcome)]")).IsEqualTo("closed approved");

			var reopened = await As(player, "+job/reply 1=One more thing.");
			await Assert.That(reopened).Contains("opened it again");
			await Assert.That(await God("think [job(1,state)]")).IsEqualTo("staff");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task EveryCommandNameIsTheSameCommand()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsAlias");
			await As(player, "+request Building/Broken exit=The north exit goes nowhere.");
			await As(player, "+request/softcode Code help=think [add(1,2)]; @pemit %#=hi%r");

			var lists = new List<string>();
			foreach (var name in new[] { "+job", "+jobs", "+myjob", "+myjobs", "+ticket", "+tickets" })
			{
				lists.Add(await As(player, name));
			}

			await Assert.That(lists.Distinct().Count()).IsEqualTo(1)
				.Because("+job, +jobs, +myjob, +myjobs, +ticket and +tickets behave alike");
			await Assert.That(lists[0]).Contains("Broken exit").And.Contains("Building");

			await Assert.That(await As(player, "+tickets 2")).Contains("think [add(1,2)]; @pemit %#=hi%r")
				.Because("a /softcode message shows exactly as typed");

			await Assert.That(await As(player, "+job/frobnicate 1")).Contains("+job has no /frobnicate switch.");
			await Assert.That(await As(player, "+job/reply 99=Hello?")).Contains("There is no job 99.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task BucketsArePermissionsAndRoles()
	{
		try
		{
			await InstallAsync("jobs");
			var admin = await Player("JobsAdm", "job-admin");
			var builder = await Player("JobsBld");
			var bld = await Objid(builder);
			await Assert.That(await God($"think [permission({bld},softcode.jobs.building)]")).IsEqualTo("0");
			await God($"@role/assign {bld}=job-admin-building");
			await Assert.That(await God($"think [permission({bld},softcode.jobs.building)]")).IsEqualTo("1")
				.Because("each preloaded bucket has a Job Admin - <Bucket> role allowing its permission");

			var created = await As(admin, "+bucket/create Mod Mail=Messages to the moderators.");
			await Assert.That(created).Contains("Bucket Mod Mail is ready.");
			await God($"@role/assign {bld}=job-admin-mod_mail");
			await Assert.That(await God($"think [permission({bld},softcode.jobs.mod_mail)]")).IsEqualTo("1")
				.Because("+bucket/create defines the bucket's permission and a role allowing it");

			await As(admin, "+request Mod Mail/Hello=Hi there.");
			await Assert.That(await As(builder, "+jobs")).Contains("Hello");

			var filer = await Player("JobsFiler");
			await As(filer, "+request Feedback/Idea=More plots.");
			var idea = await God("think [first(jobs(Feedback))]");
			await God($"@role/assign {bld}=job-admin-feedback");
			await Assert.That(await As(builder, $"think [job({idea},title)]/[job({idea},filer)]")).IsEqualTo("Idea/")
				.Because("Feedback hides who filed it from the staff who work it");
			await Assert.That(await As(filer, $"think [job({idea},filer)]")).IsEqualTo(await Objid(filer));
			await Assert.That(await As(admin, $"think [job({idea},filer)]")).IsEqualTo(await Objid(filer));

			await As(admin, "+bucket/set Mod Mail/reopen=0");
			await As(filer, "+request Mod Mail/Bye=Leaving.");
			var bye = await God("think [first(jobs(Mod Mail))]");
			await As(admin, $"+job/complete {bye}=Done.");
			await Assert.That(await As(filer, $"+job/reply {bye}=One more.")).Contains($"Job {bye} is closed.")
				.Because("a bucket that reopens after 0 days never reopens");
			await Assert.That(await God($"think [job({bye},state)]")).IsEqualTo("closed");

			var deleted = await As(admin, "+bucket/delete Mod Mail");
			await Assert.That(deleted).Contains("Mod Mail still has 2 jobs. +bucket/delete Mod Mail=<bucket> moves them there first.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task ADisabledBucketKeepsItsJobsAndADeletedOneMovesThem()
	{
		try
		{
			await InstallAsync("jobs");
			var admin = await Player("JobsMgA", "job-admin");
			var worker = await Player("JobsMgW", "job-admin-plots");
			var player = await Player("JobsMgP");

			await Assert.That(await As(player, "+pitch Heist=A bank job.")).Contains("Filed job 1 in Plots");
			await Assert.That(await As(player, "+bug Exit=Broken exit.")).Contains("Filed job 2 in Bugs");

			await Assert.That(await As(worker, "+bucket/disable Plots")).Contains("Only a Job Admin can do that.");
			await Assert.That(await As(admin, "+bucket/disable Requests")).Contains("Requests is where +request files.");
			await Assert.That(await As(admin, "+bucket/disable Plots"))
				.Contains("Plots takes no new jobs now. Its jobs stay there and are worked as before.");
			await Assert.That(await As(admin, "+bucket/disable Plots")).Contains("Plots is not taking new jobs already.");

			await Assert.That(await As(player, "+request Plots/Another=One more.")).Contains("Plots is not taking new jobs.");
			await Assert.That(await As(player, "+pitch Again=Still pitching.")).Contains("Plots is not taking new jobs.");
			await Assert.That(await As(admin, "+job/trans 2=Plots")).Contains("Plots is not taking new jobs.");
			await Assert.That(await God("think [job(2,bucket)]")).IsEqualTo("Bugs");
			await Assert.That(await As(worker, "+job/reply 1=Tell me more.")).DoesNotContain("not taking")
				.Because("the jobs a disabled bucket holds are still worked");
			await Assert.That(await God("think [job(1,state)]")).IsEqualTo("player");
			await Assert.That(await As(player, "+buckets")).Contains("Plots (disabled)");
			await Assert.That(await As(admin, "+bucket Plots")).Contains("Taking jobs");

			await Assert.That(await As(admin, "+bucket/enable Plots")).Contains("Plots takes new jobs again.");
			await Assert.That(await As(player, "+pitch Again=Still pitching.")).Contains("Filed job 3 in Plots");

			await Assert.That(await As(admin, "+bucket/delete Plots")).Contains("Plots still has 2 jobs.");
			await Assert.That(await As(admin, "+bucket/delete Plots=Plots")).Contains("Pick another bucket to move the jobs to.");
			await Assert.That(await As(admin, "+bucket/delete Plots=Nowhere")).Contains("There is no bucket called Nowhere.");
			await Assert.That(await As(admin, "+bucket/delete Plots=Requests"))
				.Contains("Bucket Plots is deleted, with its role and permission. 2 jobs moved to Requests.");
			await Assert.That(await God("think [job(1,bucket)]/[job(3,bucket)]")).IsEqualTo("Requests/Requests");
			await Assert.That(await As(admin, "+bucket Plots")).Contains("There is no bucket called Plots.");
			await Assert.That(await God($"think [permission({await Objid(worker)},softcode.jobs.plots)]")).IsNotEqualTo("1")
				.Because("the bucket's permission goes with it");

			await Assert.That(await As(admin, "+bucket/create Mod-Mail=Moderators.")).Contains("Bucket Mod-Mail is ready.");
			await Assert.That(await As(admin, "+bucket/create Mod Mail=Moderators.")).Contains("Mod-Mail already has the permission softcode.jobs.mod_mail.")
				.Because("two names with one slug would share a permission");
			await Assert.That(await As(admin, "+bucket/rename Mod-Mail=Requests")).Contains("There is a Requests bucket already.");
			await Assert.That(await As(admin, "+bucket/rename Mod-Mail=Moderators")).Contains("The bucket is called Moderators now.");
			await Assert.That(await As(admin, "+bucket/delete Moderators")).Contains("Bucket Moderators is deleted");

			await Assert.That(await As(admin, "+bucket/rename Requests=Help Desk")).Contains("The bucket is called Help Desk now.");
			await Assert.That(await As(player, "+request Lost=Where am I?")).Contains("Filed job 4 in Help Desk")
				.Because("renaming the default bucket keeps +request filing there");
			await Assert.That(await As(admin, "+bucket/disable Help Desk")).Contains("Help Desk is where +request files.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task ABigBucketIsDeletedABatchAtATime()
	{
		try
		{
			await InstallAsync("jobs");
			var admin = await Player("JobsBgA", "job-admin");
			var player = await Player("JobsBgP");
			for (var i = 1; i <= 25; i++)
			{
				await As(player, $"+bug Exit {i}=Broken exit {i}.");
			}

			var before = Notifications.CountFor(admin.DbRef);
			await Assert.That(await As(admin, "+bucket/delete Bugs=Requests"))
				.Contains("Bucket Bugs takes no new jobs now. Its 25 jobs are moving to Requests; it is deleted, with its role and permission, once they have all moved.")
				.Because("25 jobs is more than one queue entry moves");

			await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(await God("think [iter(lnum(1,25),job(##,bucket))]")).IsEqualTo(string.Join(" ", Enumerable.Repeat("Requests", 25)));
			await Assert.That(await As(admin, "+bucket Bugs")).Contains("There is no bucket called Bugs.");
			await Assert.That(string.Join("\n", Notifications.For(admin.DbRef).Skip(before)))
				.Contains("Bucket Bugs is deleted, with its role and permission. 25 jobs moved to Requests.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task ThePortalCreatesDisablesAndDeletesBuckets()
	{
		try
		{
			await InstallAsync("jobs");
			await InstallAsync("jobs-app");
			var admin = await Player("JobsWbA", "job-admin");
			var worker = await Player("JobsWbW", "job-admin-bugs");
			var player = await Player("JobsWbP");

			var list = await Http("GET", "/jobs/schema?at=buckets", "", admin);
			await Assert.That(list.GetProperty("kind").GetString()).IsEqualTo("form");
			await Assert.That(Elements(list).Any(e => e.TryGetProperty("label", out var l) && l.GetString() == "Create bucket")).IsTrue();
			var workerList = await Http("GET", "/jobs/schema?at=buckets", "", worker);
			await Assert.That(workerList.GetProperty("kind").GetString()).IsEqualTo("view")
				.Because("only a Job Admin makes buckets");

			var refused = await Http("POST", "/jobs/act", """{"op":"newbucket","new_name":"Ideas","new_description":"Ideas."}""", worker);
			await Assert.That(refused.GetProperty("errors").GetProperty("_global").GetString()).IsEqualTo("Only a Job Admin can do that.");
			var badName = await Http("POST", "/jobs/act", """{"op":"newbucket","new_name":"","new_description":""}""", admin);
			await Assert.That(badName.GetProperty("errors").GetProperty("new_name").GetString()).StartsWith("A bucket name is up to 20");
			var taken = await Http("POST", "/jobs/act", """{"op":"newbucket","new_name":"bugs","new_description":""}""", admin);
			await Assert.That(taken.GetProperty("errors").GetProperty("new_name").GetString()).IsEqualTo("There is a Bugs bucket already.");
			var made = await Http("POST", "/jobs/act", """{"op":"newbucket","new_name":"Ideas","new_description":"Ideas for the game."}""", admin);
			await Assert.That(made.GetProperty("ok").GetBoolean()).IsTrue().Because(made.ToString());
			await Assert.That(made.GetProperty("redirect").GetString()).IsEqualTo("/apps/jobs/buckets/ideas");
			await Assert.That(await As(admin, "+bucket Ideas")).Contains("Ideas for the game.").And.Contains("job-admin-ideas");

			var filed = await Http("POST", "/jobs/act", """{"op":"file","bucket":"Ideas","title":"Dragons","text":"More dragons."}""", player);
			await Assert.That(filed.GetProperty("ok").GetBoolean()).IsTrue().Because(filed.ToString());

			var page = await Http("GET", "/jobs/schema?at=buckets/ideas", "", admin);
			var labels = Elements(page).Where(e => e.TryGetProperty("label", out _)).Select(e => e.GetProperty("label").GetString()).ToList();
			await Assert.That(labels).Contains("Rename").And.Contains("Disable").And.Contains("Delete bucket").And.Contains("Move its jobs to");
			var moveTo = Elements(page).Single(e => e.TryGetProperty("key", out var k) && k.GetString() == "moveto");
			await Assert.That(moveTo.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("value").GetString()))
				.Contains("requests").And.DoesNotContain("ideas");
			await Assert.That(Elements(await Http("GET", "/jobs/schema?at=buckets/bugs", "", worker))
				.Any(e => e.TryGetProperty("label", out var l) && l.GetString() == "Delete bucket")).IsFalse()
				.Because("someone who only works a bucket changes its settings, not whether it exists");
			await Assert.That(Elements(await Http("GET", "/jobs/schema?at=buckets/requests", "", admin))
				.Any(e => e.TryGetProperty("label", out var l) && l.GetString() is "Delete bucket" or "Disable")).IsFalse()
				.Because("the bucket +request files in is never disabled or deleted");

			var off = await Http("POST", "/jobs/act", """{"op":"disablebucket","bucket":"ideas"}""", admin);
			await Assert.That(off.GetProperty("ok").GetBoolean()).IsTrue().Because(off.ToString());
			await Assert.That(off.GetProperty("data").GetProperty("fields").GetProperty("Status").GetProperty("value").GetString()).IsEqualTo("Disabled: no new jobs");
			var nav = (await Http("GET", "/jobs/nav", "", admin)).GetProperty("groups")[1].GetProperty("items");
			await Assert.That(nav.EnumerateArray().Single(i => i.GetProperty("label").GetString() == "Ideas").GetProperty("icon").GetString()).IsEqualTo("folder_off");
			var rows = (await Http("GET", "/jobs/data?at=buckets", "", admin)).GetProperty("fields").GetProperty("buckets").GetProperty("value");
			await Assert.That(rows.EnumerateArray().Single(r => r.GetProperty("slug").GetString() == "ideas").GetProperty("status").GetString()).IsEqualTo("Disabled");
			var notNow = await Http("POST", "/jobs/act", """{"op":"file","bucket":"Ideas","title":"Griffins","text":"Also griffins."}""", player);
			await Assert.That(notNow.GetProperty("errors").GetProperty("bucket").GetString()).IsEqualTo("Ideas is not taking new jobs.");
			var newForm = Elements(await Http("GET", "/jobs/schema?at=new", "", player)).Single(e => e.TryGetProperty("key", out var k) && k.GetString() == "bucket");
			await Assert.That(newForm.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("value").GetString())).DoesNotContain("Ideas");
			var on = await Http("POST", "/jobs/act", """{"op":"enablebucket","bucket":"ideas"}""", admin);
			await Assert.That(on.GetProperty("message").GetString()).IsEqualTo("Ideas takes new jobs again.");

			var renamed = await Http("POST", "/jobs/act", """{"op":"renamebucket","bucket":"ideas","rename":"Big Ideas"}""", admin);
			await Assert.That(renamed.GetProperty("ok").GetBoolean()).IsTrue().Because(renamed.ToString());
			await Assert.That(renamed.GetProperty("schema").GetProperty("title").GetString()).IsEqualTo("Big Ideas");

			var mustMove = await Http("POST", "/jobs/act", """{"op":"deletebucket","bucket":"ideas"}""", admin);
			await Assert.That(mustMove.GetProperty("errors").GetProperty("moveto").GetString()).IsEqualTo("Big Ideas still has 1 job. Pick a bucket to move them to.");
			var gone = await Http("POST", "/jobs/act", """{"op":"deletebucket","bucket":"ideas","moveto":"requests"}""", admin);
			await Assert.That(gone.GetProperty("ok").GetBoolean()).IsTrue().Because(gone.ToString());
			await Assert.That(gone.GetProperty("message").GetString()).IsEqualTo("Bucket Big Ideas is deleted, with its role and permission. 1 job moved to Requests.");
			await Assert.That(gone.GetProperty("redirect").GetString()).IsEqualTo("/apps/jobs/buckets");
			await Assert.That(await God("think [job(1,bucket)]")).IsEqualTo("Requests");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>Every element of every section of a schema document.</summary>
	private static IEnumerable<JsonElement> Elements(JsonElement schema) =>
		schema.GetProperty("pages").EnumerateArray()
			.SelectMany(p => p.GetProperty("sections").EnumerateArray())
			.SelectMany(s => s.GetProperty("elements").EnumerateArray());

	[Test]
	public async Task ABucketHookRunsWithItsArguments()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsHook");
			var requests = await God("think [first(iter(lsearch(all,type,thing),if(cand(strmatch(name(%i0),Requests),not(hasflag(%i0,GOING))),%i0)))]");
			await God($"&ON`FILE {requests}=@pemit %1=HOOKFILE %0 [name(%1)] %2 %3");

			await As(player, "+request Commas, kept=Text.");
			await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(string.Join("\n", Notifications.For(player.DbRef)))
				.Contains($"HOOKFILE 1 {player.Name} Commas, kept command");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task ThePortalRoutesListFileAndAnswerJobs()
	{
		try
		{
			await InstallAsync("jobs");
			await InstallAsync("jobs-app");
			var player = await Player("JobsWeb");
			var staff = await Player("JobsWebS", "job-admin");
			var app = (await WebAppFactoryArg.Services.GetRequiredService<IApplicationRegistryService>().GetApplicationAsync("jobs"))
				.Expect<SharpMUSH.Library.Models.Portal.Applications.RegisteredApplication>();
			await Assert.That(app.SchemaUrl).IsEqualTo("http/jobs/schema?at={path}");

			var filed = await Http("POST", "/jobs/act",
				"""{"op":"file","bucket":"bugs","title":"From the web","text":"It **broke**, badly.","softcode":false}""", player);
			await Assert.That(filed.GetProperty("ok").GetBoolean()).IsTrue();
			await Assert.That(filed.GetProperty("redirect").GetString()).IsEqualTo("/apps/jobs/1");

			var queue = await Http("GET", "/jobs/data?at=", "", staff);
			var rows = queue.GetProperty("fields").GetProperty("jobs").GetProperty("value");
			await Assert.That(rows.GetArrayLength()).IsEqualTo(1);
			await Assert.That(rows[0].GetProperty("title").GetString()).IsEqualTo("From the web");
			await Assert.That(rows[0].GetProperty("bucket").GetString()).IsEqualTo("Bugs");
			await Assert.That(rows[0].GetProperty("num").GetString()).IsEqualTo("1");
			await Assert.That(rows[0].GetProperty("unread").GetString()).IsEqualTo("Unread")
				.Because("staff have not read the new job yet");
			var mine = await Http("GET", "/jobs/data?at=", "", player);
			await Assert.That(mine.GetProperty("fields").GetProperty("jobs").GetProperty("value")[0].GetProperty("unread").GetString()).IsEqualTo("")
				.Because("the filer has read what they wrote");
			await Assert.That((await Http("GET", "/jobs/schema?at=", "", staff)).GetProperty("title").GetString()).IsEqualTo("Needs attention")
				.Because("the page is titled with the view it shows, not the application again");
			await Assert.That(app.Icon).IsEqualTo("support_agent");
			await Assert.That(app.NavPlacement).IsEqualTo("Support");
			await Assert.That(app.NavUrl).IsEqualTo("http/jobs/nav");

			var staffNav = (await Http("GET", "/jobs/nav", "", staff)).GetProperty("groups");
			var views = staffNav[0].GetProperty("items");
			await Assert.That(views[0].GetProperty("label").GetString()).IsEqualTo("Needs attention");
			await Assert.That(views[0].GetProperty("count").GetInt32()).IsEqualTo(1);
			await Assert.That(staffNav[1].GetProperty("label").GetString()).IsEqualTo("Buckets");
			var bugs = staffNav[1].GetProperty("items").EnumerateArray().Single(i => i.GetProperty("label").GetString() == "Bugs");
			await Assert.That(bugs.GetProperty("path").GetString()).IsEqualTo("/apps/jobs?bucket=bugs");
			await Assert.That(bugs.GetProperty("count").GetInt32()).IsEqualTo(1);
			var playerNav = (await Http("GET", "/jobs/nav", "", player)).GetProperty("groups");
			await Assert.That(playerNav.GetArrayLength()).IsEqualTo(1).Because("a player works no buckets");
			await Assert.That(playerNav[0].GetProperty("items")[0].GetProperty("label").GetString()).IsEqualTo("My open jobs");
			await Assert.That((await Http("GET", "/jobs/nav", "", null)).GetProperty("groups").GetArrayLength()).IsEqualTo(0);

			var inBugs = await Http("GET", "/jobs/data?at=&bucket=bugs", "", staff);
			await Assert.That(inBugs.GetProperty("fields").GetProperty("jobs").GetProperty("value").GetArrayLength()).IsEqualTo(1);
			await Assert.That((await Http("GET", "/jobs/data?at=&bucket=plots", "", staff)).GetProperty("fields").GetProperty("jobs").GetProperty("value").GetArrayLength()).IsEqualTo(0);
			await Assert.That((await Http("GET", "/jobs/schema?at=&bucket=bugs", "", staff)).GetProperty("title").GetString()).IsEqualTo("Open jobs in Bugs");

			var buckets = (await Http("GET", "/jobs/data?at=buckets", "", staff)).GetProperty("fields").GetProperty("buckets").GetProperty("value");
			await Assert.That(buckets.EnumerateArray().Any(b => b.GetProperty("slug").GetString() == "bugs")).IsTrue();
			var form = await Http("GET", "/jobs/data?at=buckets/bugs", "", staff);
			await Assert.That(form.GetProperty("fields").GetProperty("Permission").GetProperty("value").GetString()).IsEqualTo("softcode.jobs.bugs");
			var badSave = await Http("POST", "/jobs/act",
				"""{"op":"bucket","bucket":"bugs","description":"Code problems.","turnaround":-2,"reopen":3,"anonymous":"none","priority":"high","keep":"","hidden":false}""", staff);
			await Assert.That(badSave.GetProperty("ok").GetBoolean()).IsFalse();
			await Assert.That(badSave.GetProperty("errors").TryGetProperty("turnaround", out _)).IsTrue();
			var saved = await Http("POST", "/jobs/act",
				"""{"op":"bucket","bucket":"bugs","description":"Code problems.","turnaround":5,"reopen":3,"anonymous":"none","priority":"high","keep":"","hidden":false}""", staff);
			await Assert.That(saved.GetProperty("ok").GetBoolean()).IsTrue().Because(saved.ToString());
			await Assert.That(await As(staff, "+bucket Bugs")).Contains("Code problems.").And.Contains("5 days").And.Contains("high");
			var notTheirs = await Http("POST", "/jobs/act", """{"op":"bucket","bucket":"bugs","description":"Mine now."}""", player);
			await Assert.That(notTheirs.GetProperty("ok").GetBoolean()).IsFalse();

			var schema = await Http("GET", "/jobs/schema?at=1", "", staff);
			await Assert.That(schema.GetProperty("kind").GetString()).IsEqualTo("form");
			await Assert.That(schema.ToString()).Contains("\"Approve\"").Because("staff who work the bucket get its buttons");
			await Assert.That((await Http("GET", "/jobs/schema?at=1", "", player)).ToString()).DoesNotContain("\"Approve\"");

			var refused = await Http("POST", "/jobs/act", """{"op":"reply","job":1,"text":""}""", player);
			await Assert.That(refused.GetProperty("ok").GetBoolean()).IsFalse();
			await Assert.That(refused.GetProperty("errors").GetProperty("text").GetString()).IsEqualTo("Write something first.");

			var replied = await Http("POST", "/jobs/act", """{"op":"reply","job":1,"text":"Looking, now.","kind":"reply"}""", staff);
			await Assert.That(replied.GetProperty("ok").GetBoolean()).IsTrue();
			var entries = replied.GetProperty("data").GetProperty("fields").GetProperty("entries").GetProperty("value");
			await Assert.That(entries.EnumerateArray().Any(e => e.GetProperty("body").GetString() == "Looking, now.")).IsTrue();
			await Assert.That(await God("think [job(1,state)]")).IsEqualTo("player");

			var stranger = await Player("JobsWebX");
			var hidden = await Http("GET", "/jobs/schema?at=1", "", stranger);
			await Assert.That(hidden.ToString()).Contains("There is no job 1.");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
