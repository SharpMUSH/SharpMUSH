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
			await Assert.That(queue).Contains("Need a room").And.Contains("New");

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

			var deleted = await As(admin, "+bucket/delete Mod Mail");
			await Assert.That(deleted).Contains("still has jobs");
		}
		finally
		{
			await UninstallAsync();
		}
	}

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
