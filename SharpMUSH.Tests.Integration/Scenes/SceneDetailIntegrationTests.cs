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

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// Scene details: <c>+scene/set</c> writing any detail, the <c>DETAIL`&lt;KEY&gt;</c> catalogue on the Scene Logger,
/// where details show, the list filters, the <c>FILTER`&lt;POINT&gt;`&lt;NAME&gt;</c> chains, the scene-verse package,
/// and <c>+scene/mark</c> on one pose. Not in parallel: the catalogue and the filters are the game's, one for every test.
/// </summary>
[NotInParallel]
public class SceneDetailIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task<string> Html(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).Render(MarkupFormat.Html);

	private async Task God(string command) =>
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	private async Task<string> LoggerAsync()
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var logger = DBRef.Parse((await registry.GetPackageObjectsAsync("scene")).Single(o => o.Ref == "logger").Objid);
		await God($"@teleport {logger}=#2");
		return $"#{logger.Number}";
	}

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix, DBRef home)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
		await God($"@role/assign #{player.DbRef.Number}=approved");
		return player;
	}

	private async Task<DBRef> RoomAsync(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		return await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName(prefix), god));
	}

	/// <summary>A scene <paramref name="owner"/> creates and focuses, and its id.</summary>
	private async Task<string> SceneAsync(TestIsolationHelpers.TestPlayer owner)
	{
		await As(owner, $"+scene/create {TestIsolationHelpers.GenerateUniqueName("DetailScene")}");
		return await Eval($"scenefocus(#{owner.DbRef.Number})");
	}

	/// <summary>A detail key no other test uses.</summary>
	private static string Key() => $"d{Guid.NewGuid():N}"[..12];

	private async Task InstallAsync(string package)
	{
		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	[Test]
	public async Task The_scene_package_lists_no_details_and_an_owner_sets_any()
	{
		await LoggerAsync();
		var room = await RoomAsync("DetailRoom");
		var owner = await PlayerAsync("DetOwner", room);
		var other = await PlayerAsync("DetOther", room);
		var scene = await SceneAsync(owner);
		var verse = TestIsolationHelpers.GenerateUniqueName("Verse");

		await Assert.That(await Eval("scenedetails()")).IsEmpty().Because("the scene package names no details of its own");
		await Assert.That(await As(owner, "+scene/details")).Contains("This game lists no scene details. You can still set your own");
		await Assert.That(await Eval("scenedetail(nosuchdetail)")).IsEqualTo("#-1 NO SUCH SCENE DETAIL");
		await Assert.That(await Eval("scenedetailvalue(title,x)")).StartsWith("#-1 'title' is a scene field already");

		await Assert.That(await As(owner, $"+scene/set verse={verse}")).Contains($"Scene {scene}: Verse is now {verse}.");
		await Assert.That(await As(owner, "+scene/set canon=Marvel|X-Men")).Contains("Canon is now Marvel · X-Men.");
		await Assert.That(await As(other, $"+scene/set {scene}/verse=Elsewhere")).Contains("not yours to change");
		await Assert.That(await Eval($"scene({scene},verse)")).IsEqualTo(verse);
		await Assert.That(await Eval($"scene({scene},details)")).IsEqualTo("canon verse").Because("details the game does not list go by name");

		var card = await As(other, $"+scene {scene}");
		await Assert.That(card).Contains("Details").And.Contains(verse).And.Contains("Marvel · X-Men");

		await As(owner, $"pose {TestIsolationHelpers.GenerateUniqueName("waves")}");
		await Assert.That(await As(other, $"+scene/log {scene}")).Contains($"Canon: Marvel · X-Men · Verse: {verse}");

		await Assert.That(await As(other, $"+scene/list verse={verse}")).Contains("1 scene");
		await Assert.That(await As(other, "+scene/list verse=NoSuchVerseAtAll")).Contains("No scenes.");

		await Assert.That(await As(owner, "+scene/set verse=")).Contains("Verse cleared.");
		await Assert.That(await Eval($"scene({scene},details)")).IsEqualTo("canon");
	}

	[Test]
	public async Task Filters_on_the_logger_change_what_scene_shows_in_name_order()
	{
		var logger = await LoggerAsync();
		var room = await RoomAsync("FilterRoom");
		var owner = await PlayerAsync("FilOwner", room);
		var scene = await SceneAsync(owner);
		var name = Key().ToUpperInvariant();
		var marker = TestIsolationHelpers.GenerateUniqueName("Filtered");
		string[] filters = [$"CARD`{name}", $"LIST`TITLE`{name}", $"LOG`HEAD`{name}", $"POSE`RULE`{name}", $"JOIN`{name}",
			$"DETAIL`SHOW`{name}A", $"DETAIL`SHOW`{name}B"];
		try
		{
			await God($"&FILTER`CARD`{name} {logger}=%0%r{marker}-card-%1");
			await God($"&FILTER`LIST`TITLE`{name} {logger}=~t%1 %0");
			await God($"&FILTER`LOG`HEAD`{name} {logger}={marker}-head%r%0");
			await God($"&FILTER`POSE`RULE`{name} {logger}=%0 ~p%2");
			await God($"&FILTER`JOIN`{name} {logger}=%0%r{marker}-joined-%1");
			await God($"&FILTER`DETAIL`SHOW`{name}B {logger}=%0-second");
			await God($"&FILTER`DETAIL`SHOW`{name}A {logger}=%0-first");

			await Assert.That(await As(owner, "+scene/set era=1980s")).Contains("Era is now 1980s-first-second.")
				.Because("filters on one point run in name order, each given what the one before returned");
			await Assert.That(await As(owner, $"+scene {scene}")).Contains($"{marker}-card-{scene}").And.Contains("1980s-first-second");
			await Assert.That(await As(owner, "+scene/list")).Contains($"~t{scene}");
			await As(owner, $"pose {TestIsolationHelpers.GenerateUniqueName("waves")}");
			var pose = await Eval($"last(sceneposes({scene}))");
			await Assert.That(await As(owner, $"+scene/recall {scene}=1")).Contains($"Scene {scene} ~p{pose}");
			var log = await As(owner, $"+scene/log {scene}");
			await Assert.That(log.IndexOf($"{marker}-head", StringComparison.Ordinal)).IsLessThan(log.IndexOf($"Scene {scene}:", StringComparison.Ordinal));
			await Assert.That(await As(owner, $"+scene/join {scene}")).Contains("Joined and focused").And.Contains($"{marker}-joined-{scene}");
		}
		finally
		{
			foreach (var filter in filters) await God($"&FILTER`{filter} {logger}");
		}

		await Assert.That(await As(owner, $"+scene {scene}")).DoesNotContain(marker);
	}

	[Test]
	public async Task The_scene_verse_package_names_verse_canon_and_a_content_warning()
	{
		var room = await RoomAsync("VerseRoom");
		var owner = await PlayerAsync("VerOwner", room);
		var joiner = await PlayerAsync("VerJoin", room);
		var verse = TestIsolationHelpers.GenerateUniqueName("Verse");
		string scene;
		try
		{
			await InstallAsync("scene-verse");
			var logger = await LoggerAsync();
			await Assert.That((await Eval("scenedetails()")).Split(' ').Take(3))
				.IsEquivalentTo(new[] { "warning", "verse", "canon" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
				.Because("the content warning comes first, so it is read first");
			await Assert.That(await Eval("scenedetail(warning)")).IsEqualTo("Content warning");
			await Assert.That(await Eval("scenedetail(warning,tone)")).IsEqualTo("warning");
			await Assert.That(await Eval("scenedetail(verse,tone)")).IsEmpty();
			await Assert.That(await Html($"u({logger}/FUN`DETAIL`PAINT,warning,Violence)")).IsEqualTo(await Html("tone(warning,Violence)"))
				.Because("a detail's values show in its tone");
			await Assert.That(await Html($"u({logger}/FUN`DETAIL`PAINT,verse,Marvel)")).IsEqualTo(await Html("Marvel"));
			await Assert.That(await Eval("scenedetail(canon,many)")).IsEqualTo("1");
			await Assert.That(await Eval("scenedetailvalue(canon, Marvel | X-Men |)")).IsEqualTo("Marvel|X-Men");

			scene = await SceneAsync(owner);
			await Assert.That(await Eval($"scene({scene},verse)")).IsEmpty().Because("a listed detail reads empty until it is set");
			await As(owner, $"+scene/set verse={verse}");
			await Assert.That(await As(owner, "+scene/set warning=Violence")).Contains("Content warning is now Violence.");

			var listed = await As(joiner, $"+scene/list verse={verse}");
			await Assert.That(listed).Contains(verse).And.Contains("Violence").Because("the verse and the warning follow the title");
			await Assert.That(await As(joiner, $"+scene/join {scene}")).Contains("Content warning: Violence");
			var verses = await As(joiner, "+scene/verses");
			await Assert.That(verses).Contains("Verses played now").And.Contains(verse).And.Contains("+scene/list verse=<verse>");
			await Assert.That(await As(joiner, "+help scene verse")).Contains("+scene/verses");
		}
		finally
		{
			await Installer.UninstallAsync("scene-verse", force: true, CancellationToken.None);
		}

		await Assert.That(await Eval("scenedetails()")).IsEmpty().Because("uninstalling scene-verse takes its details away");
		await Assert.That(await Eval($"scene({scene},verse)")).IsEqualTo(verse).Because("scenes keep the values they were given");
		await Assert.That(await As(joiner, $"+scene/join {scene}")).DoesNotContain("Content warning");
	}

	[Test]
	public async Task A_detail_with_choices_refuses_another_and_an_open_game_takes_its_own()
	{
		var logger = await LoggerAsync();
		var room = await RoomAsync("ChoiceRoom");
		var staff = await PlayerAsync("DetStaff", room);
		var owner = await PlayerAsync("DetChoose", room);
		await God($"@permission/allow #{staff.DbRef.Number}=layout.admin");
		var key = Key();
		try
		{
			await Assert.That(await As(owner, $"+scene/detail/add {key}=Era")).Contains("needs the layout.admin permission");
			await Assert.That(await As(staff, $"+scene/detail/add {key}=Era")).Contains($"Added scene detail {key}.");
			await Assert.That(await As(staff, "+scene/detail/add status=Status")).Contains("'status' is a scene field already");
			await As(staff, $"+scene/detail/set {key}/choices=Past|Present");
			await Assert.That(await Eval($"scenedetail({key},choices)")).IsEqualTo("Past|Present");
			await Assert.That(await As(staff, $"+scene/detail/set {key}/many=maybe")).Contains("many must be true or false");

			var scene = await SceneAsync(owner);
			await Assert.That(await As(owner, $"+scene/set {key}=Future")).Contains("Era can be Past, Present");
			await Assert.That(await As(owner, $"+scene/set {key}=past")).Contains("Era is now Past.")
				.Because("a choice is written as the catalogue spells it");

			var own = Key();
			await Assert.That(await As(owner, $"+scene/set {own}=Kept")).Contains($"{own[..1].ToUpperInvariant()}{own[1..]} is now Kept.");
			await God($"&DATA`CONFIG`OPEN-DETAILS {logger}=0");
			await Assert.That(await As(owner, $"+scene/set {Key()}=Refused")).Contains("+scene/details lists them");
			await Assert.That(await Eval($"scene({scene},details)")).IsEqualTo($"{key} {own}");
		}
		finally
		{
			await God($"&DATA`CONFIG`OPEN-DETAILS {logger}=1");
			await God($"&DETAIL`{key.ToUpperInvariant()} {logger}");
		}
	}

	[Test]
	public async Task A_pose_is_marked_by_its_author_and_the_mark_shows_in_recall()
	{
		await LoggerAsync();
		var room = await RoomAsync("MarkRoom");
		var owner = await PlayerAsync("MarkOwner", room);
		var poser = await PlayerAsync("MarkPoser", room);
		var bystander = await PlayerAsync("MarkBy", room);
		var scene = await SceneAsync(owner);
		await As(poser, $"+scene/join {scene}");
		await As(poser, $"pose {TestIsolationHelpers.GenerateUniqueName("remembers")}");
		var pose = await Eval($"last(sceneposes({scene}))");

		await Assert.That(await As(bystander, $"+scene/mark {pose}=flashback")).Contains("not yours to mark");
		await Assert.That(await As(poser, $"+scene/mark {pose}=flashback")).Contains($"Pose {pose} marked flashback.");
		await Assert.That(await As(owner, $"+scene/mark {pose}=Dream")).Contains("marked dream.");
		await Assert.That(await Eval($"scenepose({scene},{pose},tags)")).IsEqualTo("dream flashback");
		await God($"@scene/setpose {pose}/tags");
		await Assert.That(await Eval($"scenepose({scene},{pose},tags)")).IsEqualTo("dream flashback").Because("without an = nothing is set");
		await Assert.That(await As(owner, $"+scene/recall {scene}=1")).Contains($"Scene {scene} · dream · flashback");

		await As(poser, $"+scene/unmark {pose}=dream");
		await Assert.That(await As(poser, $"+scene/unmark {pose}=flashback")).Contains("no longer marked flashback");
		await Assert.That(await Eval($"scenepose({scene},{pose},tags)")).IsEmpty().Because("the last mark can be taken off");
	}
}
