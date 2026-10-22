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
/// The bundled chargen package, installed from the catalogue and driven the way applicants and reviewers drive
/// it: typed commands, its settings and hooks, and the routes the chargen-app portal page reads and posts to.
/// Each test installs the package and removes it again, so the queue starts empty. Not in parallel: the
/// commands it adds are global, and another test's install would replace the store.
/// </summary>
[NotInParallel]
public class ChargenIntegrationTests
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
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await SettleCommandsAsync();
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

	/// <summary>The key a player's application is filed under: their objid without # and :.</summary>
	private async Task<string> Key(TestIsolationHelpers.TestPlayer player) =>
		(await Objid(player)).TrimStart('#').Replace(':', '_');

	/// <summary>The Chargen object's dbref: the newest, since an earlier test's may not be gone yet.</summary>
	private async Task<string> Chargen() =>
		await God("think [last(squish(iter(lsearch(all,type,thing,name,Chargen),if(cand(strmatch(name(%i0),Chargen),not(hasflag(%i0,GOING))),%i0))))]");

	/// <summary>A leaf of the Chargen Store, which holds the applications.</summary>
	private async Task<string> Stored(string leaf) =>
		await God($"think [get(last(squish(iter(lsearch(all,type,thing,name,Chargen Store),if(cand(strmatch(name(%i0),Chargen Store),not(hasflag(%i0,GOING))),%i0))))/{leaf})]");

	private async Task<bool> HasRole(TestIsolationHelpers.TestPlayer player, string role) =>
		await God($"think [hasrole(#{player.DbRef.Number},{role})]") == "1";

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
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the commands.
		await SettleCommandsAsync();
	}

	private async Task UninstallAsync()
	{
		await Installer.UninstallAsync("chargen-app", force: true, CancellationToken.None);
		await Installer.UninstallAsync("chargen", force: true, CancellationToken.None);
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

	/// <summary>A datum's value from a data payload.</summary>
	private static JsonElement Datum(JsonElement data, string key) =>
		data.GetProperty("fields").GetProperty(key).GetProperty("value");

	/// <summary>Fills in the default form's required fields and submits, as <paramref name="player"/>.</summary>
	private async Task ApplyAsync(TestIsolationHelpers.TestPlayer player)
	{
		await As(player, "+app/set concept=A ferry pilot");
		await As(player, "+app/set archetype=rogue");
		await As(player, "+app/set background=Born on the barges.");
		await Assert.That(await As(player, "+app/submit")).Contains("Submitted.");
	}

	[Test]
	public async Task AnApplicationGoesBackAndForthUntilItIsApproved()
	{
		try
		{
			await InstallAsync("chargen");
			var applicant = await Player("CgApp");
			var reviewer = await Player("CgRev", "application-reviewer");
			var chargen = await Chargen();
			await God($"&ON`APPROVE {chargen}=&CHARGEN_HOOK %<character>=%<actor> said %<message>");

			await Assert.That(await As(applicant, "+app/set archetype=bard")).Contains("Archetype is one of: Fighter, Scholar, Rogue, Mystic.");
			await Assert.That(await As(applicant, "+app/set archetype=rogue")).Contains("Archetype saved.");
			await Assert.That(await God($"think [get(#{applicant.DbRef.Number}/ARCHETYPE)]")).IsEqualTo("Rogue")
				.Because("the answer is the character's own attribute, and a choice is kept as the form writes it");
			await Assert.That(await As(applicant, "&ARCHETYPE me=Fighter")).Contains("NO PERMISSION")
				.Because("only +app changes an answer, so every change shows in the next round");
			await Assert.That(await As(applicant, "+app/set nosuch=x")).Contains("There is no field 'nosuch'.");
			await Assert.That(await As(applicant, "+app/submit")).Contains("Fill in concept and background first.");
			await Assert.That(await As(reviewer, $"+app {applicant.Name}")).Contains($"{applicant.Name} has not submitted an application yet.")
				.Because("staff see an application only once it has been submitted");
			await Assert.That(await As(reviewer, "+apps/all")).DoesNotContain(applicant.Name);

			await As(applicant, "+app/set concept=A ferry pilot, who never misses a tide");
			await As(applicant, "+app/set background=Born on the river barges.");
			await Assert.That(await As(applicant, "+app/submit")).Contains("Submitted. Number 1 of 1 in the queue.");
			await Assert.That(await As(applicant, "+app/set concept=Changed")).Contains("Staff have your application.")
				.Because("a submitted application is not changed until it is withdrawn");

			await Assert.That(await As(reviewer, "+apps")).Contains(applicant.Name).And.Contains("Applications waiting");
			await Assert.That(await As(applicant, "+apps")).Contains("Only application reviewers can do that.");

			var commented = await As(applicant, "+app/comment history=Ask me anything.");
			await Assert.That(commented).Contains("Comment added.");
			await As(reviewer, $"+app/comment {applicant.Name}/history=Which river?");
			await As(reviewer, $"+app/note {applicant.Name}/history=Looks thin.");
			var own = await As(applicant, "+app");
			await Assert.That(own).Contains("Which river?").And.DoesNotContain("Looks thin.")
				.Because("a staff note is for reviewers");
			await Assert.That(await As(reviewer, $"+app {applicant.Name}")).Contains("Looks thin.").And.Contains("staff only");

			await Assert.That(await As(applicant, $"+app/approve {applicant.Name}")).Contains("Only application reviewers can do that.");
			var returned = await As(reviewer, $"+app/return {applicant.Name}=Name the river.");
			await Assert.That(returned).Contains($"Returned to {applicant.Name} for changes.");
			await Assert.That(await As(applicant, "+app")).Contains("Needs changes").And.Contains("Name the river.");

			await As(applicant, "+app/set background=Born on the barges of the Sallow.");
			await Assert.That(await As(applicant, "+app/submit")).Contains("Submitted.");
			var diff = await As(reviewer, $"+app/diff {applicant.Name}");
			await Assert.That(diff).Contains("round 1 to").And.Contains("Born on the river barges.")
				.And.Contains("Born on the barges of the Sallow.").And.DoesNotContain("A ferry pilot")
				.Because("only what changed is shown");
			await Assert.That(await God($"think [get(#{applicant.DbRef.Number}/CHARGEN`ROUND`1`BACKGROUND`1)]")).IsEqualTo("Born on the river barges.")
				.Because("each round is kept on the character");
			await Assert.That(await As(applicant, "&CHARGEN`ROUND`1`BACKGROUND`1 me=Rewritten")).Contains("NO PERMISSION")
				.Because("a character cannot rewrite what it submitted");

			await Assert.That(await As(reviewer, $"+app/approve {applicant.Name}=Welcome.")).Contains($"Approved {applicant.Name}.");
			await SettleCommandsAsync();
			await Assert.That(await HasRole(applicant, "approved")).IsTrue();
			await Assert.That(await God($"think [get(#{applicant.DbRef.Number}/CHARGEN_HOOK)]")).IsEqualTo($"{await Objid(reviewer)} said Welcome.")
				.Because("ON`APPROVE runs with the character, the reviewer and the message by name");
			await Assert.That(await As(reviewer, "+apps/decided")).Contains(applicant.Name);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task ACapKeepsAWaitlistThatFillsAsPlacesOpen()
	{
		try
		{
			await InstallAsync("chargen");
			var role = $"cgcap{Guid.NewGuid():N}"[..14];
			await God($"@role/create {role}=Staff");
			await God($"+config chargen/approved-role={role}");
			await God("+config chargen/cap=1");
			var reviewer = await Player("CgCapR", "application-reviewer");
			var first = await Player("CgCapA");
			var second = await Player("CgCapB");
			var third = await Player("CgCapC");
			await ApplyAsync(first);
			await ApplyAsync(second);
			await ApplyAsync(third);

			await As(reviewer, $"+app/approve {first.Name}");
			await Assert.That(await HasRole(first, role)).IsTrue();
			var waits = await As(reviewer, $"+app/approve {second.Name}");
			await Assert.That(waits).Contains("Every place is taken").And.Contains("Number 1 on the waitlist.");
			await Assert.That(await HasRole(second, role)).IsFalse();
			await As(reviewer, $"+app/approve {third.Name}");
			await Assert.That(await As(reviewer, "+apps/waitlist")).Contains(second.Name).And.Contains(third.Name);
			await Assert.That(await As(third, "+app")).Contains("Number 2 on the waitlist.");

			await God("+config chargen/cap=2");
			await Assert.That(await HasRole(second, role)).IsTrue().Because("a higher cap admits the first waiting");
			await Assert.That(await HasRole(third, role)).IsFalse();

			await Assert.That(await As(reviewer, $"+app/retire {first.Name}")).Contains($"Retired {first.Name}.");
			await Assert.That(await HasRole(first, role)).IsFalse();
			await Assert.That(await HasRole(third, role)).IsTrue().Because("a retired character's place goes to the next waiting");
			await Assert.That(await As(reviewer, $"+app/retire {first.Name}")).Contains("Only an approved application can be retired.");

			await Assert.That(await As(first, "+app/submit")).Contains("Submitted.")
				.Because("a retired character may apply again");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task TheFormIsTheGamesToChange()
	{
		try
		{
			await InstallAsync("chargen");
			var player = await Player("CgForm");
			await Assert.That(await As(player, "+apps/section skills=Skills")).Contains("Only staff who change the game's configuration");

			await God("+apps/section skills=Skills");
			await God("+apps/field trade=skills/Trade");
			await God("+apps/field/type trade=choice");
			await God("+apps/field/choices trade=Smith | Sailor");
			await God("+apps/field/required trade=yes");
			await Assert.That(await Stored("FIELD`TRADE`CHOICES")).IsEqualTo("Smith|Sailor");
			await Assert.That(await Stored("FIELD`TRADE`SECTION")).IsEqualTo("SKILLS");

			var fields = await As(player, "+apps/fields");
			await Assert.That(fields).Contains("Skills").And.Contains("Trade").And.Contains("Smith / Sailor");
			await Assert.That(await As(player, "+app/set trade=farmer")).Contains("Trade is one of: Smith, Sailor.");
			await As(player, "+app/set concept=A smith");
			await As(player, "+app/set archetype=fighter");
			await As(player, "+app/set background=Grew up at the forge.");
			await Assert.That(await As(player, "+app/submit")).Contains("Fill in trade first.");
			await God("+apps/field/attribute trade=skill:trade");
			await Assert.That(await Stored("FIELD`TRADE`ATTRIBUTE")).IsEqualTo(string.Empty).Because("an attribute name has no colon");
			await God("+apps/field/attribute trade=skill`trade");
			await Assert.That(await Stored("FIELD`TRADE`ATTRIBUTE")).IsEqualTo("SKILL`TRADE");
			await As(player, "+app/set trade=smith");
			await Assert.That(await God($"think [get(#{player.DbRef.Number}/SKILL`TRADE)]")).IsEqualTo("Smith");

			await God("+apps/section/remove skills");
			await Assert.That(await Stored("SECTION`SKILLS")).IsEqualTo("Skills").Because("a section with fields is not removed");
			await God("+apps/field/remove trade");
			await God("+apps/section/remove skills");
			await Assert.That(await Stored("SECTION`SKILLS")).IsEqualTo(string.Empty);
			await Assert.That(await As(player, "+app/submit")).Contains("Submitted.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AFieldWithAnAttributePatternComesInChapters()
	{
		try
		{
			await InstallAsync("chargen");
			var player = await Player("CgChap");
			var reviewer = await Player("CgChapR", "application-reviewer");
			var admin = await Player("CgChapA");
			await God($"@permission/allow #{admin.DbRef.Number}=config.admin");

			await God("+apps/field/attribute hooks=*HOOK");
			await God("+apps/field/attribute hooks=HOOK*S*");
			await Assert.That(await Stored("FIELD`HOOKS`ATTRIBUTE")).IsEqualTo(string.Empty)
				.Because("a pattern has one *, and not first");
			await God("+apps/field/attribute background=bg*");
			await God("+apps/field/chapters background=2");
			await Assert.That(await Stored("FIELD`BACKGROUND`ATTRIBUTE")).IsEqualTo("BG*");

			await God($"&BG2 #{player.DbRef.Number}=Then the war came.");
			await As(player, "+app/set concept=A ferry pilot");
			await As(player, "+app/set archetype=rogue");
			await Assert.That(await As(player, "+app/set background=Born on the barges.")).Contains("Background chapter 1 saved.")
				.Because("a field in chapters named alone is its chapter 1");
			await Assert.That(await God($"think [get(#{player.DbRef.Number}/BG1)]")).IsEqualTo("Born on the barges.");
			await Assert.That(await As(player, "+app/set background/3=More.")).Contains("Background has room for 2 chapters.");
			await Assert.That(await As(player, "+app/set concept/2=More.")).Contains("Concept has no chapters.");
			await Assert.That(await As(player, "+app/set background/two words=More.")).Contains("A chapter is one word");

			var own = await As(player, "+app");
			await Assert.That(own).Contains("Chapter 1").And.Contains("Born on the barges.").And.Contains("Chapter 2")
				.And.Contains("Then the war came.").Because("an attribute the pattern matches is a chapter, whoever set it");
			await Assert.That(await As(player, "+app/submit")).Contains("Submitted.");
			await Assert.That(await God($"think [get(#{player.DbRef.Number}/CHARGEN`ROUND`1`BACKGROUND`2)]")).IsEqualTo("Then the war came.")
				.Because("a round keeps each chapter");

			await As(reviewer, $"+app/return {player.Name}=Less war.");
			await Assert.That(await As(player, "+app/set background/2=Peace, for a while.")).Contains("Background chapter 2 saved.");
			await As(player, "+app/submit");
			var diff = await As(reviewer, $"+app/diff {player.Name}");
			await Assert.That(diff).Contains("Background chapter 2").And.Contains("Then the war came.").And.Contains("Peace, for a while.")
				.And.DoesNotContain("Born on the barges.").Because("a field in chapters shows only the chapters that changed");

			await Assert.That(await As(player, "+apps/fields")).Contains("BG* · 2 chapters");
			var shown = await As(player, "+apps/field background");
			await Assert.That(shown).Contains("Field: Background").And.Contains("in each chapter").And.Contains("up to 2")
				.And.Contains("BG*").And.Contains("CHARGEN`ROUND`<n>`BACKGROUND`<chapter>").And.DoesNotContain("FIELD`BACKGROUND`TYPE")
				.Because("only staff who change the form see the store's attributes");
			var staffView = await As(admin, "+apps/field background");
			await Assert.That(staffView).Contains("FIELD`BACKGROUND`ATTRIBUTE").And.Contains("FIELD`BACKGROUND`CHAPTERS");
			await Assert.That(await As(admin, "+apps/field nosuch")).Contains("There is no field 'nosuch'.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task ThePortalShowsABoxForEachChapter()
	{
		try
		{
			await InstallAsync("chargen");
			var applicant = await Player("CgWebChap");

			var empty = (await Http("GET", "/chargen/schema?at=", "", applicant)).ToString();
			await Assert.That(empty).Contains("field_background.1").And.DoesNotContain("field_background.2");

			var saved = await Http("POST", "/chargen/act",
				"""{"op":"save","field_concept":"A pilot","field_archetype":"Rogue","field_background.1":"Born on the barges."}""", applicant);
			await Assert.That(saved.GetProperty("ok").GetBoolean()).IsTrue().Because(saved.ToString());
			await Assert.That(saved.GetProperty("fields").GetProperty("field_background.1").GetString()).IsEqualTo("Born on the barges.");
			await Assert.That(saved.GetProperty("fields").GetProperty("field_background.2").GetString()).IsEqualTo(string.Empty)
				.Because("the next chapter gets an empty box");
			await Assert.That(await God($"think [get(#{applicant.DbRef.Number}/BG1)]")).IsEqualTo("Born on the barges.");

			var tooLong = await Http("POST", "/chargen/act",
				$$"""{"op":"save","field_background.1":"Born on the barges.","field_background.2":"{{new string('x', 8001)}}"}""", applicant);
			await Assert.That(tooLong.GetProperty("errors").GetProperty("field_background.2").GetString()).IsEqualTo("Keep background to 8000 characters.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task ThePortalRoutesFillCommentReturnAndApprove()
	{
		try
		{
			await InstallAsync("chargen");
			await InstallAsync("chargen-app");
			var applicant = await Player("CgWeb");
			var reviewer = await Player("CgWebR", "application-reviewer");
			var key = await Key(applicant);
			var app = (await WebAppFactoryArg.Services.GetRequiredService<IApplicationRegistryService>().GetApplicationAsync("chargen"))
				.Expect<SharpMUSH.Library.Models.Portal.Applications.RegisteredApplication>();
			await Assert.That(app.SchemaUrl).IsEqualTo("http/chargen/schema?at={path}");
			await Assert.That(app.NavUrl).IsEqualTo("http/chargen/nav");

			var anonymous = await Http("GET", "/chargen/schema?at=", "", null);
			await Assert.That(anonymous.ToString()).Contains("Sign in with a character to apply.");

			var form = await Http("GET", "/chargen/schema?at=", "", applicant);
			await Assert.That(form.GetProperty("kind").GetString()).IsEqualTo("form");
			await Assert.That(form.GetProperty("actions").TryGetProperty("submit", out _)).IsTrue()
				.Because("the portal's own Submit button checks the required fields first");

			var saved = await Http("POST", "/chargen/act",
				"""{"op":"save","field_concept":"A pilot, {who} never misses a tide","field_archetype":"rogue","field_background.1":""}""", applicant);
			await Assert.That(saved.GetProperty("ok").GetBoolean()).IsTrue().Because(saved.ToString());
			await Assert.That(saved.GetProperty("fields").GetProperty("field_concept").GetString()).IsEqualTo("A pilot, {who} never misses a tide")
				.Because("a value reaches the character whole, commas and braces too");
			await Assert.That(saved.GetProperty("fields").GetProperty("field_archetype").GetString()).IsEqualTo("Rogue");
			await Assert.That((await Http("GET", $"/chargen/schema?at={key}", "", reviewer)).ToString())
				.Contains("There is no such application.").Because("staff see an application only once it has been submitted");
			var tooLong = await Http("POST", "/chargen/act", $$"""{"op":"save","field_concept":"{{new string('x', 121)}}"}""", applicant);
			await Assert.That(tooLong.GetProperty("errors").GetProperty("field_concept").GetString()).IsEqualTo("Keep concept to 120 characters.");
			var missing = await Http("POST", "/chargen/act",
				"""{"op":"submit","field_concept":"A pilot","field_archetype":"Rogue","field_background.1":""}""", applicant);
			await Assert.That(missing.GetProperty("errors").GetProperty("field_background.1").GetString()).IsEqualTo("Fill this in before you submit.");
			var submitted = await Http("POST", "/chargen/act",
				"""{"field_concept":"A pilot","field_archetype":"Rogue","field_background.1":"Born on the barges."}""", applicant);
			await Assert.That(submitted.GetProperty("message").GetString()).StartsWith("Submitted. Number 1 of 1 in the queue.")
				.Because("the portal's own Submit button posts no op");

			var publicQueue = await Http("GET", "/chargen/data?at=queue", "", applicant);
			await Assert.That(Datum(publicQueue, "Waiting").GetString()).IsEqualTo("1 application");
			await Assert.That((await Http("GET", $"/chargen/schema?at={key}", "", await Player("CgWebX"))).ToString())
				.Contains("Only application reviewers can read other applications.");
			var queue = await Http("GET", "/chargen/data?at=queue", "", reviewer);
			await Assert.That(Datum(queue, "rows")[0].GetProperty("name").GetString()).IsEqualTo(applicant.Name);
			await Assert.That(Datum(queue, "rows")[0].GetProperty("key").GetString()).IsEqualTo(key);

			var hijack = await Http("POST", "/chargen/act",
				$$"""{"op":"save","key":"{{key}}","field_concept":"Not theirs"}""", reviewer);
			await Assert.That(hijack.ToString()).Contains("Only the applicant can do that.");
			var withdrawn = await Http("POST", "/chargen/act", $$"""{"op":"withdraw","key":"{{key}}"}""", reviewer);
			await Assert.That(withdrawn.ToString()).Contains("Only the applicant can do that.");

			var note = await Http("POST", "/chargen/act",
				$$"""{"op":"comment","key":"{{key}}","comment_history":"Which river?","comment_all":"Thin.","private_all":true}""", reviewer);
			await Assert.That(note.GetProperty("message").GetString()).IsEqualTo("Comment added.");
			var seen = await Http("GET", "/chargen/data?at=", "", applicant);
			await Assert.That(Datum(seen, "comments_history")[0].GetProperty("body").GetString()).IsEqualTo("Which river?");
			await Assert.That(Datum(seen, "comments_all").GetArrayLength()).IsEqualTo(0).Because("a Staff only comment is for reviewers");
			await Assert.That(Datum(await Http("GET", $"/chargen/data?at={key}", "", reviewer), "comments_all")[0].GetProperty("tag").GetString())
				.IsEqualTo("Staff only");

			var noMessage = await Http("POST", "/chargen/act", $$"""{"op":"return","key":"{{key}}","message":""}""", reviewer);
			await Assert.That(noMessage.GetProperty("errors").GetProperty("message").GetString()).IsEqualTo("Say what to change.");
			var returned = await Http("POST", "/chargen/act", $$"""{"op":"return","key":"{{key}}","message":"Name the river."}""", reviewer);
			await Assert.That(returned.GetProperty("message").GetString()).IsEqualTo($"Returned to {applicant.Name} for changes.");

			await Http("POST", "/chargen/act",
				"""{"op":"submit","field_concept":"A pilot","field_archetype":"Rogue","field_background.1":"Born on the Sallow."}""", applicant);
			var diff = Datum(await Http("GET", $"/chargen/data?at={key}", "", reviewer), "diff_history");
			await Assert.That(diff[0].GetProperty("before").GetString()).IsEqualTo("Born on the barges.");
			await Assert.That(diff[0].GetProperty("after").GetString()).IsEqualTo("Born on the Sallow.");

			await Http("POST", "/chargen/act",
				"""{"op":"submit","field_concept":"A reviewer","field_archetype":"Scholar","field_background.1":"Reads."}""", reviewer);
			var self = await Http("POST", "/chargen/act", $$"""{"op":"approve","key":"{{await Key(reviewer)}}"}""", reviewer);
			await Assert.That(self.GetProperty("errors").GetProperty("_global").GetString()).IsEqualTo("Someone else decides your own application.");
			var refused = await Http("POST", "/chargen/act", $$"""{"op":"approve","key":"{{key}}"}""", applicant);
			await Assert.That(refused.GetProperty("errors").GetProperty("_global").GetString()).IsEqualTo("Only application reviewers can do that.");

			var approved = await Http("POST", "/chargen/act", $$"""{"op":"approve","key":"{{key}}","message":"Welcome."}""", reviewer);
			await Assert.That(approved.GetProperty("message").GetString()).IsEqualTo($"Approved {applicant.Name}.");
			await Assert.That(await HasRole(applicant, "approved")).IsTrue();

			var applicantNav = (await Http("GET", "/chargen/nav", "", applicant)).GetProperty("groups");
			await Assert.That(applicantNav.GetArrayLength()).IsEqualTo(1);
			var reviewerNav = (await Http("GET", "/chargen/nav", "", reviewer)).GetProperty("groups");
			await Assert.That(reviewerNav[1].GetProperty("items")[0].GetProperty("count").GetInt32()).IsEqualTo(1)
				.Because("the reviewer's own application waits");
			await Assert.That((await Http("GET", "/chargen/nav", "", null)).GetProperty("groups").GetArrayLength()).IsEqualTo(0);
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
