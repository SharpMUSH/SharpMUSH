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
/// Pose types: the <c>TYPE`&lt;KEY&gt;</c> JSON and <c>TYPE`&lt;KEY&gt;`FORMAT</c> softcode on the Scene Logger, what
/// the plugin reads from them, how they draw a line live and in recall, and a reader hiding one.
/// Not in parallel: the types are the game's, one set for every test.
/// </summary>
[NotInParallel]
public class ScenePoseTypeIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText().Trim();

	/// <summary>What God was told while <paramref name="command"/> ran. The recorder keys on the full objid.</summary>
	private async Task<string> God(string command)
	{
		var god = DBRef.Parse(await Eval("objid(#1)"));
		var before = Notifications.CountFor(god);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(god).Skip(before));
	}

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

	/// <summary>A type key no other test uses: letters and digits, lower case.</summary>
	private static string Key() => $"t{Guid.NewGuid():N}"[..12];

	[Test]
	public async Task The_shipped_types_are_read_by_the_plugin()
	{
		await LoggerAsync();
		var types = (await Eval("scenetypes()")).Split(' ');
		await Assert.That(types.Take(3)).IsEquivalentTo(new[] { "ic", "ooc", "narration" });
		await Assert.That(await Eval("scenetype(ooc,presentation)")).IsEqualTo("band");
		await Assert.That(await Eval("scenetype(ooc,tone)")).IsEqualTo("secondary");
		await Assert.That(await Eval("scenetype(narration)")).IsEqualTo("Narration");
		await Assert.That(await Eval("scenetype(nosuchtype)")).IsEqualTo("#-1 NO SUCH POSE TYPE");
		await Assert.That(await Eval("scenetype(ic,colour)")).IsEqualTo("#-1 UNKNOWN TYPE FIELD");
	}

	[Test]
	public async Task Staff_add_a_type_whose_FORMAT_draws_it_per_reader_and_a_reader_hides_it()
	{
		var logger = await LoggerAsync();
		var room = await RoomAsync("TypeRoom");
		var staff = await PlayerAsync("TypStaff", room);
		var reader = await PlayerAsync("TypRead", room);
		await God($"@permission/allow #{staff.DbRef.Number}=layout.admin");
		var key = Key();
		var said = TestIsolationHelpers.GenerateUniqueName("Thought");
		try
		{
			await Assert.That(await As(reader, $"+scene/type/add {key}=Telepathy")).Contains("needs the layout.admin permission");
			await Assert.That(await As(staff, $"+scene/type/add {key}=Telepathy")).Contains($"Added pose type {key}.");
			await Assert.That(await As(staff, $"+scene/type/set {key}/presentation=bubble"))
				.Contains("presentation must be one of prose, band, message, aside, notice");
			await Assert.That(await Eval($"scenetype({key},presentation)")).IsEqualTo("prose")
				.Because("a refused value is not written");
			await As(staff, $"+scene/type/set {key}/presentation=message");
			await As(staff, $"+scene/type/set {key}/hidden=yes");
			await Assert.That(await Eval($"scenetype({key},presentation)")).IsEqualTo("message");
			await Assert.That(await Eval($"scenetype({key},hidden)")).IsEqualTo("1");

			// The FORMAT is softcode on the WIZARD logger, so only someone who controls it writes one.
			await God($"&TYPE`{key.ToUpperInvariant()}`FORMAT {logger}=MIND to [name(%1)]: %2");

			await As(reader, $"+scene/create {TestIsolationHelpers.GenerateUniqueName("TypeScene")}");
			var scene = await Eval($"scenefocus(#{reader.DbRef.Number})");
			await Eval($"sceneaddpose({scene},#{staff.DbRef.Number},,#{room.Number},{key},emit,,{said})");

			var hidden = await As(reader, "+scene/recall 1");
			await Assert.That(hidden).DoesNotContain(said).Because("the type starts hidden");
			await Assert.That(hidden).Contains($"1 line of {key} hidden.");

			await Assert.That(await As(reader, $"+scene/show {key}")).Contains("Telepathy lines show in your recall and log.");
			await Assert.That(await As(reader, "+scene/recall 1")).Contains($"MIND to {reader.Name}: {said}");
			await Assert.That(await As(reader, "+scene/types")).Contains("Telepathy");

			await As(reader, $"+scene/hide {key}");
			await Assert.That(await Eval($"get(#{reader.DbRef.Number}/SCENE`HIDE)")).IsEqualTo(key);
			await Assert.That(await As(reader, "+scene/hide")).Contains($"Hidden from your recall and log: {key}.");
		}
		finally
		{
			await God($"@wipe {logger}/TYPE`{key.ToUpperInvariant()}`**");
			await God($"&TYPE`{key.ToUpperInvariant()} {logger}=");
		}
		await Assert.That(await Eval($"scenetype({key})")).IsEqualTo("#-1 NO SUCH POSE TYPE");
		var removed = await As(reader, "+scene/recall 1");
		await Assert.That(removed).Contains(said).Because("a removed type draws as in character, though the reader hid it");
		await Assert.That(removed).DoesNotContain($"<{key.ToUpperInvariant()}>");
	}

	/// <summary>Nothing changes God's attributes but God, so the package's own write fails there and says so.</summary>
	[Test]
	public async Task Hiding_a_type_says_so_when_the_choice_cannot_be_saved()
	{
		await LoggerAsync();
		await Assert.That(await God("+scene/hide ooc")).Contains("Your choice could not be saved on your character.");
		await Assert.That(await Eval("get(#1/SCENE`HIDE)")).DoesNotContain("ooc");
	}

	[Test]
	public async Task A_TYPE_attribute_that_does_not_read_is_left_out_and_named_by_scene_types()
	{
		var logger = await LoggerAsync();
		var key = Key();
		try
		{
			await God($"&TYPE`{key.ToUpperInvariant()} {logger}={{\"presentation\":\"nope\"}}");
			await Assert.That(await Eval("scenetypes()")).DoesNotContain(key);
			await Assert.That(await Eval($"scenetypecheck({key},json(object,presentation,json(string,nope)))"))
				.StartsWith("#-1 presentation must be one of");
			var listed = await God("@scene/types");
			await Assert.That(listed).Contains($"TYPE`{key.ToUpperInvariant()}");
			await Assert.That(listed).Contains("presentation must be one of");
		}
		finally
		{
			await God($"&TYPE`{key.ToUpperInvariant()} {logger}=");
		}
	}

	[Test]
	public async Task Narration_is_heard_live_through_its_type_and_recorded_as_narration()
	{
		await LoggerAsync();
		var room = await RoomAsync("NarRoom");
		var poser = await PlayerAsync("NarPoser", room);
		var witness = await PlayerAsync("NarWit", room);
		await As(poser, $"+scene/create {TestIsolationHelpers.GenerateUniqueName("NarScene")}");
		var scene = await Eval($"scenefocus(#{poser.DbRef.Number})");
		var line = TestIsolationHelpers.GenerateUniqueName("Rain falls");

		var before = Notifications.CountFor(witness.DbRef);
		await As(poser, $"+scene/narrate {scene}={line}");

		await Assert.That(Notifications.For(witness.DbRef).Skip(before)).Contains(line);
		var pose = await Eval($"last(sceneposes({scene}))");
		await Assert.That(await Eval($"scenepose({scene},{pose},type)")).IsEqualTo("narration");
		await Assert.That(await Eval($"scenepose({scene},{pose},source)")).IsEqualTo("emit");
	}

	[Test]
	public async Task A_speakers_own_FORMAT_still_wins_over_the_type_for_a_captured_pose()
	{
		await LoggerAsync();
		var room = await RoomAsync("OwnRoom");
		var poser = await PlayerAsync("OwnPoser", room);
		var witness = await PlayerAsync("OwnWit", room);
		await As(poser, $"+scene/create {TestIsolationHelpers.GenerateUniqueName("OwnScene")}");
		await God($"&FORMAT`POSE #{poser.DbRef.Number}=OWNFMT %0");
		var words = TestIsolationHelpers.GenerateUniqueName("waves");

		var before = Notifications.CountFor(witness.DbRef);
		await As(poser, $"pose {words}");

		await Assert.That(Notifications.For(witness.DbRef).Skip(before)).Contains($"OWNFMT {words}");
		var scene = await Eval($"scenefocus(#{poser.DbRef.Number})");
		await Assert.That(await Eval($"scenepose({scene},[last(sceneposes({scene}))],content)"))
			.IsEqualTo($"{poser.Name} {words}").Because("the scene records the line, not what one hearer was shown");
	}
}
