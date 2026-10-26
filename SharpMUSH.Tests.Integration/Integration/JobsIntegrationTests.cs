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

	// Ordinary triggers may enqueue more triggers; command completion includes those descendants.
	private ValueTask SettleCommandsAsync() => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>()
		.DrainImmediateQueueForTests(TimeSpan.FromSeconds(60));
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private async Task<string> God(string command)
	{
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		await SettleCommandsAsync();
		return result.Message.ToPlainText()?.Trim() ?? string.Empty;
	}

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		await SettleCommandsAsync();
		var before = Notifications.CountFor(player.DbRef);
		var beforeHandle = Notifications.CountForHandle(player.Handle);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await SettleCommandsAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before).Concat(Notifications.ForHandle(player.Handle).Skip(beforeHandle)));
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
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Server.Services.PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Server.Services.PackageManifestResolver>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IBundledPackageBootstrap>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the commands and starts the timer.
		await SettleCommandsAsync();
	}

	private async Task UninstallAsync()
	{
		await Installer.UninstallAsync("jobs-app", force: true, CancellationToken.None);
		await WebAppFactoryArg.Services.UninstallWithBridgesAsync("jobs");
	}

	private async Task<JsonElement> Http(string method, string path, string body, TestIsolationHelpers.TestPlayer? viewer, string? formInstance = null)
	{
		var dispatcher = WebAppFactoryArg.Services.GetRequiredService<IHttpHandlerCommandDispatcher>();
		DBRef? who = viewer is null ? null : DBRef.Parse(await Objid(viewer));
		var response = (await dispatcher.DispatchAsync(method, path, body, formInstance is null ? [] : [("X-SharpMUSH-Form-Instance", formInstance)], IHttpHandlerCommandDispatcher.UnknownAddress, who))
			.Expect<HttpHandlerResult>();
		await Assert.That(response.Status).IsEqualTo(200).Because(response.Body);
		await Assert.That(response.ContentType).StartsWith("application/json");
		return JsonDocument.Parse(response.Body).RootElement.Clone();
	}

	[Test]
	public async Task DraftAdapterSharesReviewAndFileAcrossTerminalAndPortal()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsDraft");
			await Assert.That(await As(player, "+request/draft new")).Contains("Bucket");
			await As(player, "+request/draft set title=Literal request");
			await As(player, "+request/draft set text=[set(me,W)]%;say private");
			await Assert.That(await As(player, "think Normal command")).Contains("Normal command");
			var privateBefore = Notifications.CountFor(player.DbRef);
			await Assert.That(await As(player, "+request/draft review")).Contains("Literal request").And.Contains("[set(me,W)]%;say private");
			await Assert.That(Notifications.For(player.DbRef).Skip(privateBefore)).DoesNotContain(line => line.Contains("Literal request")).Because("draft text must go only to the editing descriptor, not every connection of this character");
			var second = player with { Handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, player.DbRef) };
			await Assert.That(await As(second, "+request/draft file")).Contains("No active draft");
			await Assert.That(await As(player, "+request/draft back")).Contains("Literal request");
			await As(player, "+request/draft review");
			await Assert.That(await As(player, "+request/draft file")).Contains("Filed job 1.");
			await Assert.That(await As(player, "+request/draft file")).Contains("Filed job 1.");
			await Assert.That(await God("think [job(2,state)]")).IsEqualTo("#-1 NO SUCH JOB");
			var instance = Guid.NewGuid().ToString("N");
			var reviewed = await Http("POST", "/jobs/act", """{"op":"draftreview","bucket":"Requests","title":"Web request","text":"literal [add(1,2)]","softcode":false}""", player, instance);
			await Assert.That(reviewed.GetProperty("ok").GetBoolean()).IsTrue().Because(reviewed.GetRawText());
			await Assert.That(reviewed.GetProperty("schema").GetProperty("kind").GetString()).IsEqualTo("view");
			var operation = reviewed.GetProperty("operation_id").GetString();
			var file = JsonSerializer.Serialize(new { op = "draftfile", operation_id = operation });
			var filed = await Http("POST", "/jobs/act", file, player, instance);
			await Assert.That(filed.GetProperty("ok").GetBoolean()).IsTrue().Because(filed.GetRawText());
			await Assert.That((await Http("POST", "/jobs/act", file, player, instance)).GetRawText()).IsEqualTo(filed.GetRawText());
			await Assert.That((await Http("POST", "/jobs/act", file, player, Guid.NewGuid().ToString("N"))).GetProperty("ok").GetBoolean()).IsFalse();
			await Assert.That(await God("think [job(3,state)]")).IsEqualTo("#-1 NO SUCH JOB");
			var staff = await Player("JobsDraftStaff", "job-admin");
			var closing = Guid.NewGuid().ToString("N");
			var beforeClose = await Http("POST", "/jobs/act", """{"op":"draftreview","bucket":"Bugs","title":"Closed later","text":"Keep this draft"}""", player, closing);
			await Assert.That(beforeClose.GetProperty("ok").GetBoolean()).IsTrue();
			await As(staff, "+bucket/disable Bugs");
			var rejected = await Http("POST", "/jobs/act", JsonSerializer.Serialize(new { op = "draftfile", operation_id = beforeClose.GetProperty("operation_id").GetString() }), player, closing);
			await Assert.That(rejected.GetProperty("ok").GetBoolean()).IsFalse();
			await Assert.That(rejected.GetProperty("errors").TryGetProperty("bucket", out _)).IsTrue();
			await Assert.That(rejected.GetProperty("schema").GetProperty("kind").GetString()).IsEqualTo("form");
			await Assert.That(rejected.TryGetProperty("data", out _)).IsFalse().Because("a failed File must not overwrite the retained client draft with null prefill");
			await As(player, "+request/draft new");
			await As(player, "+request/draft cancel");
			await Assert.That(await As(player, "+request/draft file")).Contains("No active draft");
			await As(player, "+request/draft new");
			await ConnectionService.Unbind(player.Handle);
			await ConnectionService.Bind(player.Handle, player.DbRef);
			await Assert.That(await As(player, "+request/draft file")).Contains("No active draft");
			var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("jobs");
			var jobs = objects.Single(o => o.Ref == "jobs").Objid;
			await Assert.That(await As(player, "help @appdraft")).Contains("keeps a draft of a form");
			await Assert.That(await As(player, "help appfields()")).Contains("appfields(<declaration>, <JSON fields>)");
			await Assert.That(await God($"think [get([u({jobs}/FUN`JOB`FIND,1)]/JOB`ENTRY`1`TEXT)]")).IsEqualTo("[set(me,W)]%;say private");
			var declaration = System.Text.Json.Nodes.JsonNode.Parse(await God($"think [get({jobs}/FORM)]"))!.AsArray();
			declaration[1]!["validation"]!["max_length"] = 6;
			declaration.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"kind":"field","key":"extra","label":"Extra field","type":"text","default":"added once"}"""));
			declaration.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"kind":"field","key":"flag","label":"Flag","type":"boolean","default":false}"""));
			declaration.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"kind":"field","key":"zero","label":"Zero","type":"number","default":0}"""));
			declaration.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"kind":"field","key":"absent","label":"Absent","type":"text"}"""));
			declaration.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"kind":"field","key":"nullable","label":"Nullable","type":"text"}"""));
			declaration.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"kind":"field","key":"empty","label":"Empty","type":"text"}"""));
			await God($"&FORM {jobs}={declaration.ToJsonString()}");
			var changedForm = await Http("GET", "/jobs/schema?at=new", "", player);
			await Assert.That(Elements(changedForm).Single(e => e.TryGetProperty("key", out var k) && k.GetString() == "title").GetProperty("validation").GetProperty("max_length").GetInt32()).IsEqualTo(6);
			await Assert.That(Elements(changedForm).Any(e => e.TryGetProperty("key", out var k) && k.GetString() == "extra")).IsTrue();
			await Assert.That(await As(player, "+request Seven77=Text")).Contains("at most 6");
			await Assert.That(await As(player, "+request/draft new")).Contains("Extra field").And.Contains("added once");
			await As(player, "+request/draft set title=Seven77");
			await As(player, "+request/draft set text=Text");
			await Assert.That(await As(player, "+request/draft review")).Contains("at most 6");
			var changedValidation = await Http("POST", "/jobs/act", """{"op":"draftreview","bucket":"Requests","title":"Seven77","text":"Text"}""", player, Guid.NewGuid().ToString("N"));
			await Assert.That(changedValidation.GetProperty("errors").GetProperty("title").GetString()).Contains("at most 6");
			await As(player, "+request/draft set title=Small");
			await As(player, "+request/draft set flag=true");
			await Assert.That(await As(player, "+request/draft review")).Contains("Flag: true");
			var typedInstance = Guid.NewGuid().ToString("N");
			var typedReview = await Http("POST", "/jobs/act", """{"op":"draftreview","bucket":"Requests","title":"Small","text":"Text","nullable":null,"empty":"","flag":false}""", player, typedInstance);
			var typedBack = await Http("POST", "/jobs/act", """{"op":"draftback"}""", player, typedInstance);
			foreach (var response in new[] { typedReview, typedBack })
			{
				var fields = response.GetProperty("data").GetProperty("fields");
				await Assert.That(fields.GetProperty("zero").GetProperty("value").GetInt32()).IsEqualTo(0);
				await Assert.That(fields.TryGetProperty("absent", out _)).IsFalse();
				await Assert.That(fields.GetProperty("nullable").GetProperty("value").ValueKind).IsEqualTo(JsonValueKind.Null);
				await Assert.That(fields.GetProperty("flag").GetProperty("value").GetBoolean()).IsFalse();
				await Assert.That(fields.GetProperty("empty").GetProperty("value").GetString()).IsEqualTo("");
			}
			var zeroReview = await Http("POST", "/jobs/act", """{"op":"draftreview","bucket":"Requests","title":"Small","text":"Text","zero":0}""", player, typedInstance);
			await Assert.That(zeroReview.GetProperty("data").GetProperty("fields").GetProperty("zero").GetProperty("value").GetInt32()).IsEqualTo(0);


		}
		finally { await UninstallAsync(); }
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
			await Assert.That(await As(player, "+jobs/1")).Contains("Need a room").Because("a list's page is a last switch");
			await Assert.That(await As(player, "+jobs/old/2")).Contains("There is one page - you asked for page 2.");
			await Assert.That(await As(player, "+job/reply/2 1=Hi")).Contains("+job/reply takes no page number.");
			await Assert.That(await As(player, "+request/2 Hi=There")).Contains("+request has no /2 switch.");

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
			foreach (var name in new[] { "+job", "+jobs", "+myjob", "+myjobs", "+ticket", "+tickets", "+requests" })
			{
				lists.Add(await As(player, name));
			}

			await Assert.That(lists.Distinct().Count()).IsEqualTo(1)
				.Because("+job, +jobs, +myjob, +myjobs, +ticket, +tickets and +requests behave alike");
			await Assert.That(lists[0]).Contains("Broken exit").And.Contains("Building");

			await Assert.That(await As(player, "+tickets 2")).Contains("think [add(1,2)]; @pemit %#=hi%r")
				.Because("a /softcode message shows exactly as typed");
			await Assert.That(await As(player, "+request 1")).Contains("The north exit goes nowhere.")
				.Because("+request <#> reads a job, as in AnomalyJobs");

			await Assert.That(await As(player, "+job/frobnicate 1")).Contains("+job has no /frobnicate switch.");
			await Assert.That(await As(player, "+job/reply 99=Hello?")).Contains("There is no job 99.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task HelpersAreNamedAsTheTyperMeansThemAndCanBeRemoved()
	{
		try
		{
			await InstallAsync("jobs");
			var filer = await Player("JobsHlpF");
			var friend = await Player("JobsHlpH");
			var staff = await Player("JobsHlpS", "job-admin");
			await As(filer, "+request Lost key=I lost my key.");

			await Assert.That(await As(filer, "+job/addhelper 1=me")).Contains($"{filer.Name} can read and answer job 1 already.")
				.Because("me is whoever typed it, not the jobs object");
			await Assert.That(await God("think [job(1,helpers)]")).IsEqualTo(string.Empty);
			await Assert.That(await As(filer, "+job/addhelper 1=")).Contains("+job/addhelper 1=<player>");
			await Assert.That(await As(filer, "+job/remhelper 1")).Contains("+job/remhelper 1=<player>");

			await Assert.That(await As(filer, $"+job/addhelper 1={friend.Name}")).Contains($"{friend.Name} can read and answer job 1 now.");
			await Assert.That(await God("think [job(1,helpers)]")).IsEqualTo(await Objid(friend));

			var told = await As(friend, "+job/reply 1=Any news?");
			await Assert.That(told).Contains("You replied to job 1.");
			var staffReply = Notifications.CountFor(friend.DbRef);
			var filerBefore = Notifications.CountFor(filer.DbRef);
			await As(staff, "+job/reply 1=Still looking.");
			await Assert.That(string.Join("\n", Notifications.For(friend.DbRef).Skip(staffReply)))
				.Contains($"{staff.Name} replied to job 1: Lost key.").And.DoesNotContain("your job")
				.Because("a helper did not file the job");
			await Assert.That(string.Join("\n", Notifications.For(filer.DbRef).Skip(filerBefore)))
				.Contains($"{staff.Name} replied to your job 1: Lost key.");

			await Assert.That(await As(filer, $"+job/remhelper 1={staff.Name}")).Contains($"{staff.Name} is not a helper on job 1.");
			await Assert.That(await As(filer, $"+job/remhelper 1={friend.Name}")).Contains($"{friend.Name} is no longer a helper on job 1.");
			await Assert.That(await God("think [job(1,helpers)]")).IsEqualTo(string.Empty)
				.Because("clearing the helper attribute removes it whatever empty_attrs says");
			await Assert.That(await As(friend, "+job/reply 1=Hello?")).Contains("There is no job 1.");

			await As(filer, $"+job/addhelper 1={friend.Name}");
			await Assert.That(await As(friend, "+job/remhelper 1=me")).Contains($"{friend.Name} is no longer a helper on job 1.")
				.Because("a helper can step away from a job");

			await As(staff, "+job/claim 1");
			await As(staff, "+job/unclaim 1");
			await Assert.That(await God("think [job(1,handlers)]")).IsEqualTo(string.Empty);
			await Assert.That(await As(staff, "+job/unclaim 1")).Contains("You don't handle job 1.");
			await Assert.That(await As(staff, "+job/unwatch 1")).Contains("You don't watch job 1.");
			await As(staff, "+job/watch 1");
			await Assert.That(await As(staff, "+job/unwatch 1")).Contains("You no longer watch job 1.");
			await Assert.That(await God("think [job(1,watchers)]")).IsEqualTo(string.Empty);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task MessagesSayWhatHappenedAndPlayersSeeOnlyTheirSide()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsMsgP");
			var staff = await Player("JobsMsgS", "job-admin");
			await As(player, "+request Room=A room, please.");

			await Assert.That(await As(player, "+job 1")).Contains("New").And.DoesNotContain("With staff")
				.Because("a new job reads New, as +help jobs says");

			await As(staff, "+job/claim 1");
			await As(staff, "+job/note 1=Staff eyes only.");
			await As(staff, "+job/reply 1=Which floor?");
			var read = await As(player, "+job 1");
			await Assert.That(read).Contains("#1").And.Contains("#2").And.Contains("#3").And.DoesNotContain("#4")
				.Because("a player's messages are numbered without the hidden ones: the request, the bucket's first reply and staff's");
			await Assert.That(await As(staff, "+job 1")).Contains("#4");

			await Assert.That(await As(player, "+job/edit 1/1=nomatch/zzz"))
				.Contains("Message 1 on job 1 has no \"nomatch\" in it. Nothing changed.");
			await Assert.That(await As(player, "+job/edit 1=a/b")).Contains("+job/edit <#>/<message>=<old text>/<new text>");
			await Assert.That(await As(player, "+job/edit 1/3=floor/x")).Contains("You can only edit your own messages.")
				.Because("message 3 to the player is staff's reply, not the hidden note");
			await Assert.That(await As(player, "+job/edit 1/1=room/suite")).Contains("Message 1 on job 1 is changed.");
			await Assert.That(await As(player, "+job 1")).Contains("A suite, please.");

			await Assert.That(await As(player, "+job/options")).Contains("Job mail is on. +job/options mail=off stops it.");
			await As(player, "+job/options mail=off");
			await Assert.That(await As(player, "+job/options")).Contains("Job mail is off. +job/options mail=on starts it.");

			await Assert.That(await As(player, "+job/rep 1=Second floor.")).Contains("You replied to job 1.")
				.Because("a unique prefix names its switch");
			await Assert.That(await As(player, "+job/re 1")).Contains("+job/re could be");
			await Assert.That(await As(player, "+job/reply/soft 1=[b]")).Contains("You replied to job 1.");
			await Assert.That(await As(player, "+job/reply")).Contains("+job/reply <#>=<text>");

			await As(player, "+job/cancel 1");
			var closed = await As(player, "+job/cancel 1");
			await Assert.That(closed).Contains("Job 1 is closed. +job/reply 1=<text> opens it again.").And.DoesNotContain("/reopen");

			var bucket = await As(player, "+bucket Requests");
			await Assert.That(bucket).Contains("Turnaround").And.DoesNotContain("Work lock").And.DoesNotContain("Permission")
				.And.DoesNotContain("+bucket/set").And.Contains("+request Requests/<title>=<text>");
			await Assert.That(await As(staff, "+bucket Requests")).Contains("Work lock").And.Contains("+bucket/set");
			await Assert.That(await As(staff, "+bucket/dis Bugs")).Contains("Bugs takes no new jobs now.");
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
			foreach (var staffRole in new[] { "wizard", "royalty" })
			{
				var staff = await Objid(await Player("JobsRole", staffRole));
				await Assert.That(await God($"think [permission({staff},softcode.jobs.admin)]")).IsEqualTo("1")
					.Because($"the package grants softcode.jobs.admin to the {staffRole} role");
			}

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

			await SettleCommandsAsync();
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
			await SettleCommandsAsync();
			await Assert.That(string.Join("\n", Notifications.For(player.DbRef)))
				.Contains($"HOOKFILE 1 {player.Name} Commas, kept command")
				.Because("a hook written for the positions still reads them");

			await God($"&ON`FILE {requests}=@pemit %<filer>=HOOKNAMED %<job>/%0 [name(%<filer>)] %<title> %<how>");
			await As(player, "+request Names, too=Text.");
			await SettleCommandsAsync();
			await Assert.That(string.Join("\n", Notifications.For(player.DbRef)))
				.Contains($"HOOKNAMED 2/2 {player.Name} Names, too command")
				.Because("every hook argument also comes by name, the same value as its position");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task TheApiTakesAJobsPartsByName()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsApi");
			var filer = await Objid(player);
			var jobs = await God("think [first(iter(lsearch(all,type,thing),if(cand(strmatch(name(%i0),Jobs),not(hasflag(%i0,GOING))),%i0)))]");
			var code = await God($"think [create({TestIsolationHelpers.GenerateUniqueName("JobsApiCode")})]");
			await God($"&RUN {code}=think [setq(title,Filed\\, from code)][setq(text,Text\\, with commas.)]; @include/args {jobs}/INC`API`FILE=player,{filer},bucket,Requests,title,%q<title>,text,%q<text>; &FILED {code}=%q<job>; @include/args {jobs}/INC`API`REPLY=job,%q<job>,actor,{filer},text,A reply\\, too.; @include/args {jobs}/INC`API`CLOSE=job,%q<job>,actor,{filer},outcome,canceled");
			// The API's references to the Jobs object read the including object's PM`REFS`JOBS, which Jobs holds.
			await God($"@force {jobs}=@include {code}/RUN");

			await Assert.That(await God($"think [get({code}/FILED)]")).IsEqualTo("1");
			await Assert.That(await God("think [job(1,title)]/[job(1,state)]/[job(1,outcome)]")).IsEqualTo("Filed, from code/closed/canceled");
			await Assert.That(await As(player, "+job 1")).Contains("Text, with commas.").And.Contains("A reply, too.")
				.Because("text handed over by name arrives whole");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task CloseAndTurnHooksNameTheirArguments()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsHookN");
			var staff = await Player("JobsHookNS", "job-admin");
			var requests = await God("think [first(iter(lsearch(all,type,thing),if(cand(strmatch(name(%i0),Requests),not(hasflag(%i0,GOING))),%i0)))]");
			await God($"&ON`COMPLETE {requests}=@pemit %<filer>=HOOKDONE %<job> %0 [name(%<actor>)] [name(%2)] %<entry>=%3");
			await God($"&ON`TURN {requests}=@pemit %<actor>=HOOKTURN %<job> %<from>>%<to> %2>%3");

			await As(player, "+request Done soon=Please.");
			await As(staff, "+job/claim 1");
			await As(staff, "+job/complete 1=Finished, with commas.");
			await SettleCommandsAsync();
			await Assert.That(string.Join("\n", Notifications.For(player.DbRef)))
				.Contains($"HOOKDONE 1 1 {staff.Name} {player.Name} 5=5")
				.Because("after the bucket's first reply (2), the claim and its turn are log entries 3 and 4, the closing message 5");
			await Assert.That(string.Join("\n", Notifications.For(staff.DbRef)))
				.Contains("HOOKTURN 1 new>staff new>staff");

			await Assert.That(await As(staff, "+help jobs hooks"))
				.Contains("%<job> (%0): job number.").And.Contains("%<handler> (%2): the handler")
				.And.Contains("Shown only to: Jobs staff")
				.Because("the hooks' help names each argument beside its position, for those who work a bucket");
			await Assert.That(await As(player, "+help jobs hooks")).Contains("No local topic")
				.Because("a player who works no bucket is not shown the staff topics");
			await Assert.That(await As(player, "+help jobs api"))
				.Contains("Jobs/INC`API`FILE=player,%#,bucket,Applications,title,Application for")
				.Because("the API takes the job's parts by name");
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

			var replied = await Http("POST", "/jobs/act", """{"op":"reply","job":1,"text":"{Looking}, now.","kind":"reply"}""", staff);
			await Assert.That(replied.GetProperty("ok").GetBoolean()).IsTrue();
			var entries = replied.GetProperty("data").GetProperty("fields").GetProperty("entries").GetProperty("value");
			await Assert.That(entries.EnumerateArray().Any(e => e.GetProperty("body").GetString() == "{Looking}, now.")).IsTrue()
				.Because("a reply reaches the job whole, commas and braces too");
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

	[Test]
	public async Task ListsReadTheIndexesAndOnlyJobsInTheStore()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsIdxP");
			var requests = await Bucket("Requests");
			await As(player, "+request Room=A room, please.");
			await Assert.That(await God($"think [words(indexed(JOB`BUCKET,=,objid({requests})))]")).IsEqualTo("1")
				.Because("installing the package indexes JOB`BUCKET");

			var forged = await God("think [create(JobsForged)]");
			var bucketId = await God($"think [objid({requests})]");
			await God($"&JOB`STATE {forged}=new");
			await God($"&JOB`BUCKET {forged}={bucketId}");
			await God($"&JOB`NUM {forged}=99");
			await Assert.That(await God($"think [words(indexed(JOB`BUCKET,=,objid({requests})))]")).IsEqualTo("2");
			await Assert.That(await God("think [jobs(Requests)]")).IsEqualTo("1")
				.Because("a thing outside the store is not a job, whatever it holds");
			await God($"@destroy/override {forged}");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	private async Task<string> Bucket(string name) =>
		await God($"think [first(iter(lsearch(all,type,thing),if(cand(strmatch(name(%i0),{name}),hasattr(%i0,SLUG),not(hasflag(%i0,GOING))),%i0)))]");

	[Test]
	public async Task ANewJobGetsTheBucketsFirstReplyWithHowSoonItUsuallyAnswers()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsAckP");
			var staff = await Player("JobsAckS", "job-admin");
			var requests = await Bucket("Requests");

			var filed = await As(player, "+request Room=A room, please.");
			await Assert.That(filed).Contains("Filed job 1 in Requests: Room. Staff aim to answer within 3 days.")
				.Because("until it has measured three jobs, a bucket promises its turnaround");
			await Assert.That(await God("think [job(1,entries)]")).IsEqualTo("2");
			var read = await As(player, "+job 1");
			await Assert.That(read).Contains("Requests \u00b7").And.Contains("Thanks. Staff have your request and will answer here.")
				.And.Contains("Within 3 days");
			await Assert.That(await As(player, "+jobs")).DoesNotContain("*1").Because("the first reply does not make the job unread for its filer");

			await As(staff, "+job/reply 1=Which floor?");
			await Assert.That(await God($"think [words(get({requests}/STATS`ANSWER))]")).IsEqualTo("1")
				.Because("staff's first reply is one sample of how soon the bucket answers");
			await As(staff, "+job/reply 1=Second floor it is.");
			await Assert.That(await God($"think [words(get({requests}/STATS`ANSWER))]")).IsEqualTo("1")
				.Because("only the first answer counts");
			await As(staff, "+job/approve 1=Done.");
			await Assert.That(await God($"think [words(get({requests}/STATS`CLOSE))]")).IsEqualTo("1")
				.Because("closing a player's job is one sample of how soon the bucket closes");

			await God($"&STATS`ANSWER {requests}=7200 14400 30000 9000 86400");
			await God($"&STATS`CLOSE {requests}=90000 200000 150000");
			var bucket = await As(staff, "+bucket Requests");
			await Assert.That(bucket).Contains("About 4 hours (the last 5 jobs)")
				.And.Contains("About 2 days (the last 3 jobs)");
			await Assert.That(await As(player, "+buckets")).Contains("about 4 hours");
			await Assert.That(await As(player, "+request Desc=A description.")).Contains("Staff usually answer in about 4 hours.");

			await God("+config jobs/wait-sample=3");
			await As(staff, "+job/reply 2=Done.");
			await Assert.That(await God($"think [get({requests}/STATS`ANSWER)]")).StartsWith("9000 86400 ")
				.Because("the bucket keeps only the latest wait-sample answers");

			await As(staff, "+bucket/set Requests/acknowledge=");
			await As(player, "+request Quiet=No first reply.");
			await Assert.That(await God("think [job(3,entries)]")).IsEqualTo("1").Because("an empty acknowledge sends none");
			await As(staff, "+bucket/set Requests/acknowledge=Got it.");
			await As(staff, $"+job/create Requests/From staff=Staff work.");
			await Assert.That(await God("think [job(4,entries)]")).IsEqualTo("1").Because("staff filing need no acknowledgement");
		}
		finally
		{
			await God("+config/reset jobs/wait-sample");
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AnAuditedBucketApprovesAtOnceAndEachRevertRunsItsOwnHook()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsAudP");
			var other = await Player("JobsAudO");
			var staff = await Player("JobsAudS", "job-admin");
			var building = await Bucket("Building");
			await God($"&ON`APPROVE {building}=@pemit %<filer>=AUDAPPROVE %<job> [name(%<actor>)]");
			await God($"&ON`REVERT {building}=@pemit %<filer>=AUDREVERT %<job> [name(%<actor>)] %<entry>=%3");

			await Assert.That(await As(staff, "+bucket/set Building/audit=yes")).Contains("Building audit is yes.");
			var filed = await As(player, "+request Building/Bench=A bench in the plaza.");
			await Assert.That(filed).Contains("It is approved at once, and staff look it over later.")
				.And.DoesNotContain("Building approved your job").Because("the filer is told once");
			await SettleCommandsAsync();
			await Assert.That(string.Join("\n", Notifications.For(player.DbRef))).Contains("AUDAPPROVE 1 Building")
				.Because("approving at once runs the bucket's own ON`APPROVE, as the bucket");
			await Assert.That(await God("think [job(1,outcome)] [job(1,audit)]")).IsEqualTo("approved pending");

			await As(other, "+request Building/Lamp=A lamp.");
			await Assert.That(await As(staff, "+jobs/audit")).Contains("Bench").And.Contains("Lamp").And.Contains("Approved, to check");
			await Assert.That(await As(staff, "+job/audit 2")).Contains("Job 2 is checked: its approval stands.");
			await Assert.That(await God("think [job(2,audit)]")).IsEqualTo("passed");
			await Assert.That(await As(staff, "+jobs/audit")).Contains("Bench").And.DoesNotContain("Lamp");

			await Assert.That(await As(player, "+job/revert 1=No.")).Contains("Only staff");
			await Assert.That(await As(staff, "+job/revert 1=The plaza has no room.")).Contains("Job 1 is reverted.");
			await SettleCommandsAsync();
			await Assert.That(string.Join("\n", Notifications.For(player.DbRef))).Contains($"AUDREVERT 1 {staff.Name} 3=3");
			await Assert.That(await God("think [job(1,outcome)] [job(1,audit)]")).IsEqualTo("reverted reverted");
			await Assert.That(await As(player, "+job 1")).Contains("Reverted").And.Contains("The plaza has no room.");
			await Assert.That(await As(staff, "+jobs/audit")).Contains("No jobs.");

			await Assert.That(await As(staff, $"+bucket/lock Building/audit=#{staff.DbRef.Number}")).Contains($"Building audit lock: #{staff.DbRef.Number}");
			await As(player, "+request Building/Door=A door.");
			await Assert.That(await God("think [job(3,state)] [job(3,entries)]")).IsEqualTo("new 2")
				.Because("someone the audit lock refuses waits for staff, with the bucket's first reply");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AChecklistIsCopiedOntoEachJobAndTickedOffByStaff()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsChkP");
			var staff = await Player("JobsChkS", "job-admin");
			var applications = await Bucket("Applications");
			await God($"&ON`CHECK {applications}=@pemit %<actor>=CHECKED %<job> %<step> %<done>");

			await Assert.That(await As(staff, "+bucket/set Applications/checklist=Sheet checked|Background, read twice")).Contains("Sheet checked and Background, read twice");
			await Assert.That(await As(staff, $"+bucket/set Applications/checklist={new string('x', 101)}")).Contains("Checklist is up to 20 steps");
			await As(player, "+request Applications/Mara=Please.");
			await As(staff, "+bucket/set Applications/checklist=Changed later");

			var read = await As(player, "+job 1");
			await Assert.That(read).Contains("0 of 2 done").And.Contains("1. [ ] Sheet checked").And.Contains("2. [ ] Background, read twice")
				.Because("a job keeps the checklist its bucket had when it was filed");
			await Assert.That(await As(player, "+job/check 1=1")).Contains("Only staff");
			await Assert.That(await As(staff, "+job/check 1=3")).Contains("Job 1 has no step 3.");
			await Assert.That(await As(staff, "+job/check 1=1")).Contains("Step 1 of job 1 is done: Sheet checked");
			await SettleCommandsAsync();
			await Assert.That(string.Join("\n", Notifications.For(staff.DbRef))).Contains("CHECKED 1 1 1");
			await Assert.That(await God("think [job(1,done)]/[job(1,steps)]")).IsEqualTo("1/2");
			await Assert.That(await As(player, "+job 1")).Contains($"1. [x] Sheet checked \u00b7 {staff.Name}");
			await As(staff, "+job/uncheck 1=1");
			await Assert.That(await God("think [job(1,done)]")).IsEqualTo("0");

			await Assert.That(await As(staff, "+job/status 1=Reading the background next.")).Contains("The player reads on job 1: Reading the background next.");
			await Assert.That(await As(player, "+job 1")).Contains("Update:").And.Contains("Reading the background next.");
			await Assert.That(await God("think [job(1,update)]")).IsEqualTo("Reading the background next.");
			await Assert.That(await As(staff, "+job/status 1=")).Contains("Job 1 has no update now.");
			await Assert.That(await God("think [job(1,update)]")).IsEqualTo(string.Empty);

			var saved = await Http("POST", "/jobs/act",
				"""{"op":"bucket","bucket":"applications","checklist":["Sheet checked","Two references, one of them staff"]}""", staff);
			await Assert.That(saved.GetProperty("ok").GetBoolean()).IsTrue().Because(saved.ToString());
			await Assert.That(await God($"think [get({applications}/CHECKLIST)]")).IsEqualTo("Sheet checked|Two references, one of them staff")
				.Because("the portal posts the checklist as a list, so a step may hold a comma");
			var form = await Http("GET", "/jobs/data?at=buckets/applications", "", staff);
			await Assert.That(form.GetProperty("fields").GetProperty("checklist").GetProperty("value").GetArrayLength()).IsEqualTo(2);
			var job = await Http("GET", "/jobs/data?at=1", "", staff);
			await Assert.That(job.GetProperty("fields").GetProperty("steps").GetProperty("value").GetArrayLength()).IsEqualTo(2);

			var cleared = await Http("POST", "/jobs/act", """{"op":"bucket","bucket":"applications","checklist":[]}""", staff);
			await Assert.That(cleared.GetProperty("ok").GetBoolean()).IsTrue().Because(cleared.ToString());
			await Assert.That(await God($"think [get({applications}/CHECKLIST)]")).IsEqualTo(string.Empty)
				.Because("removing every step in the portal clears the checklist");

			await Assert.That(await As(staff, "+bucket/lock Applications/audit=(((")).Contains("That is not a lock.");
			await Assert.That(await As(staff, "+bucket/lock Applications/work=")).Contains("Someone has to work Applications");
			var badLock = await Http("POST", "/jobs/act", """{"op":"bucket","bucket":"applications","audit_lock":"((("}""", staff);
			await Assert.That(badLock.GetProperty("errors").GetProperty("audit_lock").GetString()).IsEqualTo("That is not a lock.");
			var noWorkers = await Http("POST", "/jobs/act", """{"op":"bucket","bucket":"applications","work_lock":""}""", staff);
			await Assert.That(noWorkers.GetProperty("errors").GetProperty("work_lock").GetString()).StartsWith("Someone has to work the bucket");
			var locked = await Http("POST", "/jobs/act", """{"op":"bucket","bucket":"applications","audit_lock":"role^approved"}""", staff);
			await Assert.That(locked.GetProperty("ok").GetBoolean()).IsTrue().Because(locked.ToString());
			await Assert.That(await God($"think [lock({applications}/audit)]")).IsEqualTo("ROLE^approved");
			var lockForm = await Http("GET", "/jobs/data?at=buckets/applications", "", staff);
			await Assert.That(lockForm.GetProperty("fields").GetProperty("audit_lock").GetProperty("value").GetString()).IsEqualTo("ROLE^approved");

			var worker = await Player("JobsChkW", "job-admin-applications");
			var refused = await Http("POST", "/jobs/act", """{"op":"bucket","bucket":"applications","audit_lock":""}""", worker);
			await Assert.That(refused.GetProperty("errors").GetProperty("_global").GetString()).IsEqualTo("Only a Job Admin can change a bucket's locks.")
				.Because("someone who works a bucket may change its settings, but not who works it");
			await Assert.That(await God($"think [lock({applications}/audit)]")).IsEqualTo("ROLE^approved");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task SearchFindsOnlyTheJobsThatMentionTheText()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsSrch");
			var staff = await Player("JobsSrchS", "job-admin");
			await As(player, "+request Need a room=Somewhere quiet.");
			await As(player, "+request Fix the light=The lamp in the hall is out.");
			await As(player, "+request Paint=The walls are grey.");

			await Assert.That(await As(staff, "+jobs/search lamp")).Contains("Fix the light")
				.And.DoesNotContain("Need a room").And.DoesNotContain("Paint")
				.Because("only the job whose text mentions it matches");
			await Assert.That(await As(staff, "+jobs/search room")).Contains("Need a room").And.DoesNotContain("Fix the light")
				.Because("a title that mentions the text matches too");

			await Assert.That(await As(staff, "+job/merge 3=1")).Contains("Job 3 is merged into job 1.");
			await Assert.That(await As(staff, "+job 1")).Contains("Somewhere quiet.").And.Contains("The walls are grey.")
				.Because("the merged job's messages follow the job's own");
			await Assert.That(await God("think [job(3,state)]")).IsEqualTo("#-1 NO SUCH JOB");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task TheSettingsAreChangedWithConfig()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsCfg");
			var staff = await Player("JobsCfgS", "job-admin");
			await God($"@permission/allow #{staff.DbRef.Number}=config.admin");

			await Assert.That(await As(staff, "+config jobs/default-bucket=Nowhere")).Contains("There is no bucket called Nowhere.");
			await Assert.That(await As(staff, "+config jobs/default-bucket=Bugs")).Contains("jobs/default-bucket is now Bugs, was Requests.");
			await Assert.That(await As(player, "+request Broken door=It sticks.")).Contains("Filed job 1 in Bugs: Broken door.");
			await Assert.That(await As(staff, "+bucket/disable Bugs")).Contains("Point +config jobs/default-bucket elsewhere first.");
			await As(staff, "+bucket/rename Bugs=Faults");
			await Assert.That(await As(player, "+request Leaky tap=It drips.")).Contains("Filed job 2 in Faults: Leaky tap.")
				.Because("renaming the default bucket keeps +request filing there");
			await Assert.That(await As(staff, "+config jobs/move-batch=0")).DoesNotContain("is now");
			await Assert.That(await As(staff, "+config jobs/typo-bucket=Plots")).Contains("jobs/typo-bucket is now Plots");
			await Assert.That(await As(player, "+typo Wrong word=In the hall.")).Contains("Filed job 3 in Plots");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task StaffHearWhatWaitsAtLoginAndPlayersGetRepliesByMail()
	{
		try
		{
			await InstallAsync("jobs");
			var player = await Player("JobsLog");
			var staff = await Player("JobsLogS", "job-admin");
			await As(player, "+request Need a lamp=Please.");

			var before = Notifications.CountFor(staff.DbRef);
			await God($"@trigger/args #9/PLAYER`CONNECT`JOBS=objid,{await Objid(staff)},connections,1,descriptor,{staff.Handle}");
			await SettleCommandsAsync();
			await Assert.That(string.Join("\n", Notifications.For(staff.DbRef).Skip(before))).Contains("1 need staff").And.Contains("+jobs lists them.")
				.Because("the event handler's player`connect tells staff what waits");

			await Assert.That(await As(player, "think [mail()]")).IsEqualTo("0");
			await As(staff, "+job/reply 1=Which room?");
			await SettleCommandsAsync();
			await Assert.That(await As(player, "think [mail()]")).IsEqualTo("1").Because("a staff reply is mailed to the filer");
			await Assert.That(await As(player, "think [mail(1)]")).Contains("Which room?");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
