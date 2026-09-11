using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// What the scene verbs say: the guards that refuse, and the card that informs.
///
/// <para>Ten verbs read <c>scenefocus(%#)</c> and acted on it without asking whether it named a
/// scene. For anyone unfocused it is <c>#-1 NOT FOUND</c> — truthy and non-empty — so a wizard, whom
/// <c>FUN`OWNS</c> treats as owning every scene, was told <c>Pitch set for scene #-1 NOT FOUND.</c>
/// while nothing was written, and everyone else failed a message-less <c>@assert</c> and got no
/// output at all.</para>
/// </summary>
[NotInParallel]
public class SceneVerbSurfaceIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private static readonly string Tag = Guid.NewGuid().ToString("N")[..8];
	private readonly ConcurrentDictionary<long, DBRef> _actors = new();

	private async Task<string> Eval(string expression) => (await EvalRaw(expression)).Trim();

	/// <summary>
	/// <see cref="Eval"/> without the trim, for the one thing whose leading and trailing whitespace is
	/// the subject of the assertion rather than noise around it.
	/// </summary>
	private async Task<string> EvalRaw(string expression) =>
		(await FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();

	private async Task<CallState> God1(string command) =>
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private static string Num(string dbref)
	{
		var s = dbref.Trim();
		var colon = s.IndexOf(':');
		return colon < 0 ? s : s[..colon];
	}

	private async Task<IReadOnlyList<string>> RunAs(long handle, string command)
	{
		var actor = _actors[handle];
		var before = Notifications.CountFor(actor);
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		return [.. Notifications.For(actor).Skip(before)];
	}

	private async Task<string> CreatePlayerAsync(string name, long handle, bool pueblo = false)
	{
		await God1($"@pcreate {name}=pw-{Tag}-1");
		var dbref = (await God1($"think [pmatch({name})]")).Message?.ToPlainText()?.Trim() ?? string.Empty;
		if (!DBRef.TryParse(dbref, out var parsed) || parsed is null)
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");

		await God1($"@set {dbref}=APPROVED");
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
			pueblo
				? new ConcurrentDictionary<string, string>(new Dictionary<string, string> { ["PUEBLO"] = "1" })
				: null);
		await ConnectionService.Bind(handle, parsed.Value);
		_actors[handle] = parsed.Value;
		return dbref;
	}

	/// <summary>The Scene Logger, which carries the package's attributes.</summary>
	private async Task<string> LoggerAsync()
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var objects = await registry.GetPackageObjectsAsync("scene");
		return PackageInstallService.ParseObjid(objects.Single(o => o.Ref == "logger").Objid)!.Value.ToString();
	}

	/// <summary>The Logger's $-commands only match from the master room; other suites move it.</summary>
	private async Task PutLoggerInMasterRoomAsync() => await God1($"@teleport {await LoggerAsync()}=#2");

	/// <summary>
	/// Every verb that acts on the focused scene, run by someone who has no focus. None of them may
	/// claim to have done anything, and none may show the player a raw <c>#-1</c>.
	/// </summary>
	[Test]
	[Arguments("+scene/pitch A quiet night.")]
	[Arguments("+scene/public")]
	[Arguments("+scene/private")]
	[Arguments("+scene/start")]
	[Arguments("+scene/pause")]
	[Arguments("+scene/finish")]
	[Arguments("+scene/leave")]
	[Arguments("+scene/deactivate")]
	public async Task UnfocusedVerb_SaysSoInsteadOfActing(string command)
	{
		await PutLoggerInMasterRoomAsync();
		const long handle = 9500;
		await CreatePlayerAsync($"Lune{Tag}", handle);

		var said = await RunAs(handle, command);

		await Assert.That(said).IsNotEmpty()
			.Because("a verb that cannot run must say why; silence is indistinguishable from no such command");
		await Assert.That(said.Any(m => m.Contains("#-1", StringComparison.Ordinal))).IsFalse()
			.Because($"'{command}' leaked the not-found sentinel into a player-facing message");
		await Assert.That(said.Any(m =>
				m.Contains("set for scene", StringComparison.OrdinalIgnoreCase)
				|| m.Contains("is now", StringComparison.OrdinalIgnoreCase)
				|| m.Contains("Left the scene", StringComparison.OrdinalIgnoreCase)
				|| m.Contains("Deactivated scene", StringComparison.OrdinalIgnoreCase)))
			.IsFalse()
			.Because($"'{command}' reported success while there was no scene to act on");
	}

	/// <summary>
	/// A wizard is deliberately allowed to administer anyone's scene — <c>FUN`OWNS</c> grants it —
	/// so the refusal above must be about the missing focus, not about permission. Focused, the same
	/// command works on a scene the wizard does not own.
	/// </summary>
	/// <remarks>
	/// Driven by a WIZARD character, not by God. #1 is root: it passes every lock whatever the
	/// softcode says, so running this as God would pass even if <c>FUN`OWNS</c> granted wizards
	/// nothing at all — which is the one thing this test exists to establish.
	/// </remarks>
	[Test]
	public async Task AWizardFocusedOnAnotherPlayersScene_CanStillAdministerIt()
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9501;
		const long wizardHandle = 9504;
		await CreatePlayerAsync($"Perr{Tag}", ownerHandle);
		var wizard = await CreatePlayerAsync($"Wiz{Tag}", wizardHandle);
		await God1($"@set {wizard}=WIZARD");
		await Assert.That(await Eval($"orflags({wizard},Wr)")).IsEqualTo("1")
			.Because("the character has to actually be a wizard for this to test anything");

		await RunAs(ownerHandle, $"+scene/create Perr Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(_actors[ownerHandle].ToString())})");
		await Assert.That(sceneId).DoesNotStartWith("#-1");

		await RunAs(wizardHandle, $"+scene/join {sceneId}");
		await RunAs(wizardHandle, "+scene/pitch A wizard was here.");

		await Assert.That(await Eval($"scene({sceneId},summary)")).IsEqualTo("A wizard was here.");
	}

	/// <summary>
	/// Focus is not ownership. A member who did not create the scene is refused the owner-only verbs,
	/// and told so — this is the arm that used to produce no output at all.
	/// </summary>
	[Test]
	public async Task AFocusedNonOwner_IsRefusedInWords()
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9502;
		const long guestHandle = 9503;
		await CreatePlayerAsync($"Quill{Tag}", ownerHandle);
		await CreatePlayerAsync($"Rook{Tag}", guestHandle);

		await RunAs(ownerHandle, $"+scene/create Quill Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(_actors[ownerHandle].ToString())})");
		await RunAs(ownerHandle, "+scene/public");
		await RunAs(guestHandle, $"+scene/join {sceneId}");

		var said = await RunAs(guestHandle, "+scene/pitch Not mine to set.");

		await Assert.That(said).IsNotEmpty().Because("a refusal the player cannot see is not a refusal");
		await Assert.That(await Eval($"scene({sceneId},summary)")).IsNotEqualTo("Not mine to set.");
	}


	/// <summary>
	/// <c>+scene/info &lt;id&gt;</c> is the scene's card, and <c>+scene &lt;id&gt;</c> is the same
	/// thing — the id on its own has always meant "tell me about this scene".
	///
	/// <para>It replaces <c>+scene/who</c>, which could only ever answer one question. A player
	/// asking about a scene wants where it is, what it is about and whether anyone may watch, not
	/// just a list of names; splitting that across two verbs meant the roster was the only part with
	/// a home of its own.</para>
	/// </summary>
	[Test]
	[Arguments("+scene/info")]
	[Arguments("+scene")]
	public async Task SceneInfo_ShowsTheCastWithRolesAndWhereItIs(string verb)
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9520;
		const long castHandle = 9521;
		await CreatePlayerAsync($"Ines{Tag}", ownerHandle);
		await CreatePlayerAsync($"Joss{Tag}", castHandle);

		await RunAs(ownerHandle, $"+scene/create Ines Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(_actors[ownerHandle].ToString())})");
		await RunAs(ownerHandle, "+scene/pitch A lantern, a gate, a wait.");
		await RunAs(castHandle, $"+scene/join {sceneId}");

		var card = string.Join(" ", await RunAs(ownerHandle, $"{verb} {sceneId}"));

		await Assert.That(card).Contains("Owner").Because("the card names who owns the scene");
		await Assert.That(card).Contains("Participant").Because("and who else is in it");
		await Assert.That(card).Contains("Players").Because("the cast is a table with its own heading");
		await Assert.That(card).Contains("A lantern, a gate, a wait.").Because("the pitch is the description");
		await Assert.That(card).Contains("Where").Because("where it is happening is part of asking about it");
	}

	/// <summary>Whether anyone may watch is now worth stating: private is the exception.</summary>
	[Test]
	public async Task SceneInfo_SaysWhetherAnyoneMayWatch()
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9522;
		await CreatePlayerAsync($"Kite{Tag}", ownerHandle);

		await RunAs(ownerHandle, $"+scene/create Kite Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(_actors[ownerHandle].ToString())})");

		var open = string.Join(" ", await RunAs(ownerHandle, $"+scene/info {sceneId}"));
		await Assert.That(open).Contains("Anyone").Because("a new scene is watchable and should say so");

		await RunAs(ownerHandle, "+scene/private");
		var shut = string.Join(" ", await RunAs(ownerHandle, $"+scene/info {sceneId}"));
		await Assert.That(shut).Contains("Members").Because("the card must reflect the exception once it is made");
	}

	/// <summary>The verb it replaces is gone rather than left as a second way to ask.</summary>
	[Test]
	public async Task SceneWho_IsNoLongerACommand()
	{
		await PutLoggerInMasterRoomAsync();
		const long handle = 9523;
		await CreatePlayerAsync($"Lark{Tag}", handle);
		await RunAs(handle, $"+scene/create Lark Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(_actors[handle].ToString())})");

		var said = string.Join(" ", await RunAs(handle, $"+scene/who {sceneId}"));

		await Assert.That(said).Contains("Huh?").Because("+scene/who was replaced by +scene/info");
	}

	/// <summary>
	/// One cast line, carrying who is in the scene, what they are to it, and the name they pose
	/// under when that differs from their own.
	///
	/// <para>The card briefly had two: a Cast of personas and a Members list of characters with
	/// roles. Two lines describing the same people is a puzzle for the reader, who has to work out
	/// which name on the first line is which person on the second.</para>
	/// </summary>
	[Test]
	public async Task SceneInfo_ShowsOneCastLine_CarryingPersonaAndRole()
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9530;
		const long castHandle = 9531;
		var owner = await CreatePlayerAsync($"Mira{Tag}", ownerHandle);
		await CreatePlayerAsync($"Nolan{Tag}", castHandle);

		await RunAs(ownerHandle, $"+scene/create Mira Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(owner)})");
		await RunAs(castHandle, $"+scene/join {sceneId}");
		await RunAs(castHandle, "+scene/as The Cloaked Stranger");

		var card = string.Join(" ", await RunAs(ownerHandle, $"+scene/info {sceneId}"));

		await Assert.That(card).DoesNotContain("Cast")
			.Because("one table describes the players; a second line of personas said it again");
		await Assert.That(card).Contains($"Mira{Tag}")
			.Because("a member with no persona is listed under their own name");
		await Assert.That(card).Contains("The Cloaked Stranger")
			.Because("the name a member poses under belongs in their row, not on a line of its own");
		await Assert.That(card).Contains("Owner");
		await Assert.That(card).Contains("Participant");
	}

	/// <summary>
	/// The card's link is the game's own web address with the scene's id appended, not a base an
	/// admin has to retype into the package.
	///
	/// <para>The address is <c>mud_url</c>; unset means no URL line, which is what the harness ships
	/// and what is asserted here. The configured half cannot be driven from a test — <c>@config/set</c>
	/// is unimplemented and the option is read-only at runtime — so only the join is, against both
	/// spellings of a base address.</para>
	/// </summary>
	[Test]
	public async Task SceneInfo_BuildsItsLinkFromTheGamesAddressAndTheSceneId()
	{
		await PutLoggerInMasterRoomAsync();
		const long handle = 9540;
		await CreatePlayerAsync($"Odile{Tag}", handle);

		await RunAs(handle, $"+scene/create Odile Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(_actors[handle].ToString())})");

		var card = string.Join(" ", await RunAs(handle, $"+scene/info {sceneId}"));
		await Assert.That(card).DoesNotContain("URL")
			.Because("a game with no address of its own has no link to offer");

		var logger = await LoggerAsync();
		foreach (var configured in new[] { "https://example.test", "https://example.test/" })
		{
			var joined = await Eval($"u({logger}/FUN`NO_TRAILING_SLASH,{configured})/scenes/{sceneId}");

			await Assert.That(joined).IsEqualTo($"https://example.test/scenes/{sceneId}")
				.Because($"'{configured}' must reach the scene's page without a doubled or missing slash");
		}
	}

	/// <summary>
	/// The link is a real anchor for a client that can render one, and bare text for everyone else.
	///
	/// <para><c>tagwrap()</c> emits the Pueblo/MXP markup inline, which those clients turn into a
	/// clickable link and every other client shows as literal <c>&lt;a href=…&gt;</c> — so it has to
	/// be asked for, not assumed. The test player here has no such capability, which is the ordinary
	/// case and the one that would be spoiled by getting this wrong.</para>
	/// </summary>
	[Test]
	public async Task SceneInfo_LinksOnlyForAClientThatCanRenderOne()
	{
		await PutLoggerInMasterRoomAsync();
		const long handle = 9541;
		var who = await CreatePlayerAsync($"Perrin{Tag}", handle);
		var logger = await LoggerAsync();
		const string url = "https://example.test/scenes/7";

		var plain = await Eval($"u({logger}/FUN`URL_TEXT,{Num(who)},{url})");

		await Assert.That(plain).IsEqualTo(url)
			.Because("a client that cannot render an anchor must not be shown the markup for one");

		const long puebloHandle = 9542;
		var capable = await CreatePlayerAsync($"Quen{Tag}", puebloHandle, pueblo: true);

		var linked = await Eval($"u({logger}/FUN`URL_TEXT,{Num(capable)},{url})");

		await Assert.That(linked).IsEqualTo($"<a href=\"{url}\">{url}</a>")
			.Because("a Pueblo client is sent the anchor markup it knows how to render");
	}

	/// <summary>
	/// The explicit <c>+scene/&lt;mode&gt; &lt;id&gt;=&lt;text&gt;</c> verbs — what the portal's compose
	/// box sends — leave the poser focused on the scene they just posed into.
	///
	/// <para>Without it a pose composed on a scene's page landed in that scene and left the character's
	/// focus wherever it had been, so the very next line they typed in a client went somewhere else, or
	/// nowhere. Everything else about the portal says "you are in this scene"; the focus has to agree.</para>
	/// </summary>
	[Test]
	[Arguments("pose", "waves once.")]
	[Arguments("say", "Hello there.")]
	[Arguments("semipose", "'s hand lifts.")]
	[Arguments("emit", "A lantern gutters.")]
	public async Task WebComposeVerb_FocusesTheSceneItPostsTo(string mode, string text)
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9550;
		const long guestHandle = 9551;
		await CreatePlayerAsync($"Rue{Tag}{mode}", ownerHandle);
		var guest = await CreatePlayerAsync($"Sabel{Tag}{mode}", guestHandle);

		await RunAs(ownerHandle, $"+scene/create Rue Scene {Tag} {mode}");
		var sceneId = await Eval($"scenefocus({Num(_actors[ownerHandle].ToString())})");
		await RunAs(ownerHandle, "+scene/public");

		await Assert.That(await Eval($"scenefocus({Num(guest)})")).StartsWith("#-1")
			.Because("the guest has not joined anything yet, which is the state the portal poses from");

		await RunAs(guestHandle, $"+scene/{mode} {sceneId}={text}");

		await Assert.That(await Eval($"scenefocus({Num(guest)})")).IsEqualTo(sceneId)
			.Because($"+scene/{mode} <id>=<text> must leave the poser focused on <id>");
	}

	/// <summary>
	/// The whole path a portal-composed pose takes, whitespace included: what the compose box puts on
	/// the wire, through the verb, into the archive, and back out of <c>scenepose()</c> byte for byte.
	///
	/// <para>Three separate steps used to eat part of it — the compose box trimmed the text, the parser
	/// compressed the runs of spaces that were left and dropped the ones at a line's edges, and
	/// <c>@scene/addpose</c> trimmed the content field again on arrival. An indented pose could not be
	/// written by any route. The literal here is what <c>MushComposeEncoder</c> produces for a pose that
	/// opens on an indent, carries a blank line and indents again, so this fails if any one of the three
	/// comes back.</para>
	/// </summary>
	[Test]
	public async Task AWebComposedPose_ReachesTheArchiveWithItsWhitespaceIntact()
	{
		await PutLoggerInMasterRoomAsync();
		const long handle = 9570;
		var who = await CreatePlayerAsync($"Yarrow{Tag}", handle);

		await RunAs(handle, $"+scene/create Yarrow Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(who)})");

		// MushComposeEncoder.Encode("  She waits;\n\n    \"Well?\"")
		await RunAs(handle, $"+scene/emit {sceneId}=%b%bShe waits%;%r%r%b%b%b%b\"Well?\"");

		var poseId = await Eval($"last(sceneposes({sceneId}))");
		var content = await EvalRaw($"scenepose({sceneId},{poseId},content)");

		await Assert.That(content).IsEqualTo("  She waits;\n\n    \"Well?\"")
			.Because("every space, break and separator the author typed was spelled out; nothing on the way in may eat one");
	}

	/// <summary>
	/// Bare <c>+scene/recall</c> answers with a default window rather than nothing: two rounds of the
	/// room, which is <c>DATA`RECALL_ROUNDS</c> times the size of the cast. Someone arriving at a scene
	/// wants what they missed without first having to guess a number.
	/// </summary>
	[Test]
	public async Task BareRecall_ShowsTwoRoundsOfTheCast()
	{
		await PutLoggerInMasterRoomAsync();
		const long ownerHandle = 9560;
		const long castHandle = 9561;
		var owner = await CreatePlayerAsync($"Tarn{Tag}", ownerHandle);
		await CreatePlayerAsync($"Vell{Tag}", castHandle);

		await RunAs(ownerHandle, $"+scene/create Tarn Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(owner)})");
		await RunAs(ownerHandle, "+scene/public");
		await RunAs(castHandle, $"+scene/join {sceneId}");

		// Two members, so the default window is four. Six poses means the window has to cut something,
		// which is what makes "four" observable rather than "all of them".
		for (var i = 1; i <= 6; i++)
		{
			await RunAs(ownerHandle, $"+scene/emit {sceneId}=Beat number {i}.");
		}

		var recalled = string.Join("\n", await RunAs(ownerHandle, "+scene/recall"));

		await Assert.That(recalled).Contains("Beat number 6.").Because("the default window ends at the newest pose");
		await Assert.That(recalled).Contains("Beat number 3.").Because("two members × two rounds is four poses");
		await Assert.That(recalled).DoesNotContain("Beat number 2.")
			.Because("a default window that shows everything is not a window");
		await Assert.That(recalled).DoesNotContain("#-1")
			.Because("the sentinel must never reach a player-facing line");
	}

	/// <summary>
	/// Every recalled pose is introduced by a rule naming who posed it and its id.
	///
	/// <para>Recall printed bare content lines, which ran together: an <c>@emit</c> carries no name of
	/// its own, so four of them in a row were four anonymous paragraphs with nothing to say where one
	/// ended and the next began, and no id on screen to hand to <c>+scene/edit</c> or
	/// <c>+scene/undo</c>. The attribution is read off the pose, so it is the name the pose actually
	/// went out under — <c>+scene/as</c> afterwards does not rewrite history.</para>
	/// </summary>
	[Test]
	public async Task Recall_AttributesAndSeparatesEachPose()
	{
		await PutLoggerInMasterRoomAsync();
		const long handle = 9563;
		var who = await CreatePlayerAsync($"Zev{Tag}", handle);

		await RunAs(handle, $"+scene/create Zev Scene {Tag}");
		var sceneId = await Eval($"scenefocus({Num(who)})");
		await RunAs(handle, "+scene/as The Ferryman");
		await RunAs(handle, $"+scene/emit {sceneId}=The lamp swings.");
		var poseId = await Eval($"last(sceneposes({sceneId}))");

		var recalled = string.Join("\n", await RunAs(handle, "+scene/recall 1"));

		await Assert.That(recalled).Contains("The Ferryman")
			.Because("an emit carries no name of its own, so the rule above it is the only attribution there is");
		await Assert.That(recalled).Contains($"pose {poseId}")
			.Because("the id on screen is what +scene/edit and +scene/undo take");
		await Assert.That(recalled).Contains("The lamp swings.");

		// The rule is drawn, not just written: a separator that looks like prose separates nothing.
		await Assert.That(recalled.Any(c => c is '-' or '=' or '─')).IsTrue()
			.Because("each pose is introduced by a drawn rule, which is what makes the block readable");
	}

	/// <summary>
	/// A count that is not a whole number of at least one is refused in words.
	///
	/// <para><c>sceneposes()</c> answers a sentinel for anything below 1, and the verb would then iterate
	/// the WORDS of that sentinel — one bogus pose lookup per word, printed at the player. Zero failed
	/// differently and just as badly: <c>"0"</c> is falsy, so the bare <c>@assert</c> broke with no
	/// message, which is indistinguishable from a command that does not exist.</para>
	/// </summary>
	[Test]
	[Arguments("0")]
	[Arguments("-1")]
	[Arguments("lots")]
	public async Task Recall_WithAnImpossibleCount_SaysSo(string count)
	{
		await PutLoggerInMasterRoomAsync();
		const long handle = 9564;
		var who = await CreatePlayerAsync($"Ash{Tag}{count.Length}{count[0]}", handle);

		await RunAs(handle, $"+scene/create Ash Scene {Tag} {count}");
		var sceneId = await Eval($"scenefocus({Num(who)})");
		await RunAs(handle, $"+scene/emit {sceneId}=A beat.");

		var said = await RunAs(handle, $"+scene/recall {count}");

		await Assert.That(said).IsNotEmpty()
			.Because("a count the verb cannot use must be refused out loud, not silently");
		await Assert.That(said.Any(m => m.Contains("#-1", StringComparison.Ordinal))).IsFalse()
			.Because($"'+scene/recall {count}' leaked a sentinel into a player-facing message");
		await Assert.That(said.Any(m => m.Contains("A beat.", StringComparison.Ordinal))).IsFalse()
			.Because("a refused count must not fall through and print the log anyway");
	}

	/// <summary>
	/// Bare <c>+scene/recall</c> without a focus says so, rather than printing the not-found sentinel
	/// that <c>scenefocus()</c> answers with. Same guard the counted form now carries.
	/// </summary>
	[Test]
	[Arguments("+scene/recall")]
	[Arguments("+scene/recall 5")]
	public async Task Recall_WithoutAFocus_SaysSo(string command)
	{
		await PutLoggerInMasterRoomAsync();
		const long handle = 9562;
		await CreatePlayerAsync($"Wren{Tag}{command.Length}", handle);

		var said = await RunAs(handle, command);

		await Assert.That(said).IsNotEmpty().Because("silence is indistinguishable from no such command");
		await Assert.That(said.Any(m => m.Contains("#-1", StringComparison.Ordinal))).IsFalse()
			.Because($"'{command}' leaked the not-found sentinel into a player-facing message");
	}
}
