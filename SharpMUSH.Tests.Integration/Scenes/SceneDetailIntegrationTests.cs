using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// Scene details: the <c>DETAIL`&lt;KEY&gt;</c> catalogue on the Scene Logger, <c>+scene/set</c> writing a scene's
/// verse or canon, where they show, the list filters, and <c>+scene/mark</c> on one pose.
/// Not in parallel: the catalogue is the game's, one for every test.
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

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task God(string command) =>
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
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

	[Test]
	public async Task The_shipped_details_are_read_by_the_plugin()
	{
		await LoggerAsync();
		await Assert.That((await Eval("scenedetails()")).Split(' ').Take(3)).IsEquivalentTo(new[] { "verse", "canon", "warning" });
		await Assert.That(await Eval("scenedetail(verse)")).IsEqualTo("Verse");
		await Assert.That(await Eval("scenedetail(verse,listed)")).IsEqualTo("1");
		await Assert.That(await Eval("scenedetail(canon,many)")).IsEqualTo("1");
		await Assert.That(await Eval("scenedetail(nosuchdetail)")).IsEqualTo("#-1 NO SUCH SCENE DETAIL");
		await Assert.That(await Eval("scenedetail(verse,colour)")).IsEqualTo("#-1 UNKNOWN DETAIL FIELD");
		await Assert.That(await Eval("scenedetailvalue(canon, Marvel | X-Men |)")).IsEqualTo("Marvel|X-Men");
		await Assert.That(await Eval("scenedetailvalue(title,x)")).StartsWith("#-1 'title' is a scene field already");
	}

	[Test]
	public async Task An_owner_sets_details_that_show_on_the_card_the_log_and_in_lists()
	{
		await LoggerAsync();
		var room = await RoomAsync("DetailRoom");
		var owner = await PlayerAsync("DetOwner", room);
		var other = await PlayerAsync("DetOther", room);
		var scene = await SceneAsync(owner);
		var verse = TestIsolationHelpers.GenerateUniqueName("Verse");

		await Assert.That(await Eval($"scene({scene},verse)")).IsEmpty().Because("a listed detail reads empty until it is set");
		await Assert.That(await As(owner, $"+scene/set verse={verse}")).Contains($"Scene {scene}: Verse is now {verse}.");
		await Assert.That(await As(owner, $"+scene/set canon=Marvel|X-Men")).Contains("Canon is now Marvel · X-Men.");
		await Assert.That(await As(other, $"+scene/set {scene}/verse=Elsewhere")).Contains("not yours to change");
		await Assert.That(await Eval($"scene({scene},verse)")).IsEqualTo(verse);
		await Assert.That(await Eval($"scene({scene},details)")).IsEqualTo("verse canon");

		var card = await As(other, $"+scene {scene}");
		await Assert.That(card).Contains("Details").And.Contains(verse).And.Contains("Marvel · X-Men");

		await As(owner, $"pose {TestIsolationHelpers.GenerateUniqueName("waves")}");
		await Assert.That(await As(other, $"+scene/log {scene}")).Contains($"Verse: {verse} · Canon: Marvel · X-Men");

		var listed = await As(other, $"+scene/list verse={verse}");
		await Assert.That(listed).Contains(verse).And.Contains(" ·").Because("a listed detail follows the title");
		await Assert.That(listed).Contains("1 scene");
		await Assert.That(await As(other, "+scene/list verse=NoSuchVerseAtAll")).Contains("No scenes.");

		await Assert.That(await As(owner, "+scene/set verse=")).Contains("Verse cleared.");
		await Assert.That(await Eval($"scene({scene},details)")).IsEqualTo("canon");
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
			await God($"&DATA`OPEN_DETAILS {logger}=0");
			await Assert.That(await As(owner, $"+scene/set {Key()}=Refused")).Contains("+scene/details lists them");
			await Assert.That(await Eval($"scene({scene},details)")).IsEqualTo($"{key} {own}");
		}
		finally
		{
			await God($"&DATA`OPEN_DETAILS {logger}=1");
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
		await Assert.That(await As(owner, $"+scene/recall {scene}=1")).Contains($"Scene {scene} · dream · flashback");

		await As(poser, $"+scene/unmark {pose}=dream");
		await Assert.That(await As(poser, $"+scene/unmark {pose}=flashback")).Contains("no longer marked flashback");
		await Assert.That(await Eval($"scenepose({scene},{pose},tags)")).IsEmpty().Because("the last mark can be taken off");
	}
}
