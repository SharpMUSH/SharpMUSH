using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Parity tests for the speech surface — SAY, POSE, SEMIPOSE, their <c>"</c> / <c>:</c> / <c>;</c>
/// token forms, and the <c>\</c> form of @EMIT.
///
/// <para>Every expectation here was captured from a real PennMUSH 1.8.8 (netmud built from the
/// checkout the <c>tools/oracle</c> harness describes) driven over telnet with two players in one
/// room, not read out of speech.c. The interesting cases are the ones where an implementation
/// drifts: how much whitespace the token eats, whether <c>/noeval</c> is honoured, and the fact
/// that <c>';'</c> followed by a space is POSE rather than SEMIPOSE.</para>
/// </summary>
[NotInParallel]
public class SpeechCommandParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private TestIsolationHelpers.TestPlayer _speaker = null!;
	private TestIsolationHelpers.TestPlayer _listener = null!;
	private string _speakerName = null!;
	private string _room = null!;

	[Before(Test)]
	public async Task PutTwoPlayersInOneRoom()
	{
		_speaker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "Speaker");
		_listener = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "Listener");

		var dig = await God($"@dig SpeechParity{_speaker.DbRef.Number}");
		_room = dig.Message!.ToPlainText().Trim();
		await God($"@tel #{_speaker.DbRef.Number}={_room}");
		await God($"@tel #{_listener.DbRef.Number}={_room}");

		_speakerName = (await Mediator.Send(new Library.Queries.Database.GetObjectNodeQuery(_speaker.DbRef)))
			.Expect<AnySharpObject>().Object().Name;
	}

	// Both fields are still null when PutTwoPlayersInOneRoom throws part-way through (a failed @dig or
	// @tel), and an unguarded teardown would then report a NullReferenceException in place of the real
	// setup failure.
	[After(Test)]
	public async Task DisconnectPlayers()
	{
		if (_speaker is not null)
		{
			await ConnectionService.Disconnect(_speaker.Handle);
		}

		if (_listener is not null)
		{
			await ConnectionService.Disconnect(_listener.Handle);
		}
	}

	// PennMUSH: `"hello` -> You say, "hello" / One says, "hello"
	[Test]
	public async ValueTask SayToken_RoutesToSay()
	{
		var (mine, theirs) = await Speak("\"hello");
		await Assert.That(mine).Contains("You say, \"hello\"");
		await Assert.That(theirs).Contains($"{_speakerName} says, \"hello\"");
	}

	// PennMUSH: `:waves` -> One waves
	[Test]
	public async ValueTask PoseToken_RoutesToPose()
	{
		var (_, theirs) = await Speak(":waves");
		await Assert.That(theirs).Contains($"{_speakerName} waves");
	}

	// PennMUSH: `;'s thing` -> One's thing (no space inserted)
	[Test]
	public async ValueTask SemiposeToken_RoutesToSemipose()
	{
		var (_, theirs) = await Speak(";'s thing");
		await Assert.That(theirs).Contains($"{_speakerName}'s thing");
	}

	// PennMUSH src/command.c: `if (*(p + 1) && *(p + 1) == ' ') replacer = "POSE";`
	// `; waves` is a POSE, so the name and the action are separated by a space.
	[Test]
	public async ValueTask SemicolonFollowedBySpace_IsPoseNotSemipose()
	{
		var (_, theirs) = await Speak("; waves");
		await Assert.That(theirs).Contains($"{_speakerName} waves");
	}

	// PennMUSH EMIT_TOKEN is '\\': `\hello there` -> hello there
	[Test]
	public async ValueTask EmitToken_RoutesToEmit()
	{
		var (_, theirs) = await Speak("\\hello there");
		await Assert.That(theirs).Contains("hello there");
	}

	// PennMUSH sets `parse_switches = 0` for every token replacer, so the "/noeval" is message text.
	// It is still evaluated, because no NOEVAL switch was ever recognised.
	[Test]
	public async ValueTask TokenForms_DoNotParseSwitches()
	{
		var (mine, _) = await Speak("\"/noeval [add(1,2)]");
		await Assert.That(mine).Contains("You say, \"/noeval 3\"");
	}

	// PennMUSH command_argparse: `while (*f == ' ') f++` — every space after the command name goes.
	[Test]
	[Arguments("say   hello", "You say, \"hello\"")]
	[Arguments("\"  hello", "You say, \"hello\"")]
	[Arguments("  say hello", "You say, \"hello\"")]
	public async ValueTask LeadingWhitespaceIsEaten(string command, string expected)
	{
		var (mine, _) = await Speak(command);
		await Assert.That(mine).Contains(expected);
	}

	[Test]
	[Arguments("pose   waves")]
	[Arguments(":  waves")]
	public async ValueTask PoseEatsLeadingWhitespace(string command)
	{
		var (_, theirs) = await Speak(command);
		await Assert.That(theirs).Contains($"{_speakerName} waves");
	}

	// PennMUSH's command_parse runs `while (*p == ' ') p++` BEFORE the `switch (*p)` that swaps a
	// speech token for its command name, so spaces typed in front of the token do not stop it from
	// dispatching: `  "hello` is a SAY there, not `Huh?`. `{0}` stands in for the speaker's name,
	// which is not a constant and so cannot be written in an [Arguments] attribute.
	[Test]
	[Arguments("  \"hello", "{0} says, \"hello\"")]
	[Arguments("  :waves", "{0} waves")]
	[Arguments("  ; waves", "{0} waves")]
	[Arguments("  ;'s thing", "{0}'s thing")]
	[Arguments("  \\hello there", "hello there")]
	public async ValueTask SpacesBeforeASpeechToken_StillDispatch(string command, string expected)
	{
		var (mine, theirs) = await Speak(command);
		await Assert.That(theirs).Contains(string.Format(expected, _speakerName));
		await Assert.That(mine).DoesNotContain(ErrorMessages.Notifications.HuhTypeHelp);
	}

	// PennMUSH do_say has no empty-message guard: a bare `say` says nothing, loudly.
	[Test]
	[Arguments("say")]
	[Arguments("\"")]
	public async ValueTask BareSay_SaysAnEmptyString(string command)
	{
		var (mine, theirs) = await Speak(command);
		await Assert.That(mine).Contains("You say, \"\"");
		await Assert.That(theirs).Contains($"{_speakerName} says, \"\"");
	}

	// A bare pose is the name and the space do_pose always inserts; a bare semipose is just the name.
	[Test]
	[Arguments("pose", true)]
	[Arguments(":", true)]
	[Arguments("semipose", false)]
	[Arguments(";", false)]
	public async ValueTask BarePose_IsNameAndNothingElse(string command, bool withSpace)
	{
		var (_, theirs) = await Speak(command);
		await Assert.That(theirs).Contains(withSpace ? $"{_speakerName} " : _speakerName);
	}

	// PennMUSH: say/pose ARE evaluated by default — that is what the NOEVAL switch is for.
	[Test]
	public async ValueTask SayEvaluatesItsArgument()
	{
		var (mine, _) = await Speak("say [add(1,2)]");
		await Assert.That(mine).Contains("You say, \"3\"");
	}

	// PennMUSH: noeval = SW_ISSET(sw, SWITCH_NOEVAL), passed to command_argparse.
	[Test]
	public async ValueTask SayNoEval_DoesNotEvaluate()
	{
		var (mine, _) = await Speak("say/noeval [add(1,2)]");
		await Assert.That(mine).Contains("You say, \"[add(1,2)]\"");
	}

	[Test]
	public async ValueTask PoseNoEval_DoesNotEvaluate()
	{
		var (_, theirs) = await Speak("pose/noeval [add(1,2)]");
		await Assert.That(theirs).Contains($"{_speakerName} [add(1,2)]");
	}

	[Test]
	public async ValueTask EmitNoEval_DoesNotEvaluate()
	{
		var (_, theirs) = await Speak("@emit/noeval [add(1,2)]");
		await Assert.That(theirs).Contains("[add(1,2)]");
	}

	// PennMUSH's command table gives THINK a NOEVAL switch.
	[Test]
	public async ValueTask ThinkNoEval_DoesNotEvaluate()
	{
		var (mine, _) = await Speak("think/noeval [add(1,2)]");
		await Assert.That(mine).Contains("[add(1,2)]");
	}

	// Evaluation strips the braces; /noeval keeps them, because a noeval argument never reaches
	// process_expression at all.
	[Test]
	public async ValueTask BracesSurviveNoEvalAndNotEvaluation()
	{
		var (evaluated, _) = await Speak("say {braced}");
		await Assert.That(evaluated).Contains("You say, \"braced\"");

		var (raw, _) = await Speak("say/noeval {braced}");
		await Assert.That(raw).Contains("You say, \"{braced}\"");
	}

	// PennMUSH pose/nospace is semipose: no gap between the name and the action.
	[Test]
	public async ValueTask PoseNoSpace_OmitsTheGap()
	{
		var (_, theirs) = await Speak("pose/nospace waves");
		await Assert.That(theirs).Contains($"{_speakerName}waves");
	}

	// PennMUSH do_whisper key == 2: `You whisper, "hi" to Two.` / `One whispers: hi`.
	[Test]
	public async ValueTask PlainWhisper_UsesPennWording()
	{
		var listenerName = await NameOf(_listener.DbRef);
		var (mine, theirs) = await Speak($"whisper {listenerName}=hi there");
		await Assert.That(mine).Contains($"You whisper, \"hi there\" to {listenerName}.");
		await Assert.That(theirs).Contains($"{_speakerName} whispers: hi there");
	}

	// PennMUSH do_whisper key == 1: a ':' message is a pose, announced as a sense, WITH a gap.
	[Test]
	public async ValueTask PoseWhisper_SensesWithAGap()
	{
		var listenerName = await NameOf(_listener.DbRef);
		var (mine, theirs) = await Speak($"whisper {listenerName}=:waves");
		await Assert.That(mine).Contains($"{listenerName} senses: {_speakerName} waves");
		await Assert.That(theirs).Contains($"You sense: {_speakerName} waves");
	}

	// ';' takes the same branch with `gap = ""`.
	[Test]
	public async ValueTask SemiposeWhisper_SensesWithoutAGap()
	{
		var listenerName = await NameOf(_listener.DbRef);
		var (mine, theirs) = await Speak($"whisper {listenerName}=;'s thing");
		await Assert.That(mine).Contains($"{listenerName} senses: {_speakerName}'s thing");
		await Assert.That(theirs).Contains($"You sense: {_speakerName}'s thing");
	}

	// PennMUSH's /silent and /noisy decide only whether the ROOM may overhear; the whisperer's own
	// echo is unconditional. Verified live: `whisper/silent Two=quiet one` still answers
	// `You whisper, "quiet one" to Two.`
	[Test]
	public async ValueTask SilentWhisper_StillEchoesToTheWhisperer()
	{
		var listenerName = await NameOf(_listener.DbRef);
		var (mine, _) = await Speak($"whisper/silent {listenerName}=quiet one");
		await Assert.That(mine).Contains($"You whisper, \"quiet one\" to {listenerName}.");
	}

	// PennMUSH do_whisper walks the target list with next_in_list, which takes a "quoted" name whole.
	[Test]
	public async ValueTask Whisper_QuotedMultiwordNameIsOneTarget()
	{
		var lamp = await ThingInTheRoom("Brass Lamp");
		var (mine, _) = await Speak("whisper \"Brass Lamp\"=psst");
		await Assert.That(mine).Contains("You whisper, \"psst\" to Brass Lamp.");
		await Assert.That(Notifications.For(lamp)).Contains($"{_speakerName} whispers: psst");
	}

	// match_result(..., TYPE_PLAYER, ...) makes players a preference, not a requirement: a thing in the
	// room is a valid recipient, and a list mixes both.
	[Test]
	public async ValueTask Whisper_ReachesNearbyThingsAlongsidePlayers()
	{
		var listenerName = await NameOf(_listener.DbRef);
		var box = await ThingInTheRoom("Cardboard Box");
		var (mine, theirs) = await Speak($"whisper \"Cardboard Box\" {listenerName}=hi");
		await Assert.That(mine).Contains($"You whisper, \"hi\" to Cardboard Box and {listenerName}.");
		await Assert.That(theirs).Contains($"{_speakerName} whispers to Cardboard Box and {listenerName}: hi");
		await Assert.That(Notifications.For(box)).Contains($"{_speakerName} whispers to Cardboard Box and {listenerName}: hi");
	}

	// do_whisper has no self check, and MAT_ME resolves "me".
	[Test]
	public async ValueTask Whisper_ToSelfIsAllowed()
	{
		var (mine, _) = await Speak("whisper me=to myself");
		await Assert.That(mine).Contains($"You whisper, \"to myself\" to {_speakerName}.");
		await Assert.That(mine).Contains($"{_speakerName} whispers: to myself");
		await Assert.That(mine).DoesNotContain("You can't whisper to yourself.");
	}

	// speech.c:396-414 — unmatched names are gathered into one `Unable to whisper to:` line, and the
	// good names still get the whisper.
	[Test]
	public async ValueTask Whisper_UnmatchedNamesAreReportedTogether()
	{
		var listenerName = await NameOf(_listener.DbRef);
		var (mine, theirs) = await Speak($"whisper Nobodyqq {listenerName} \"No Such Thing\"=hi");
		await Assert.That(mine).Contains("Unable to whisper to: Nobodyqq \"No Such Thing\"");
		await Assert.That(mine).DoesNotContain("I don't see Nobodyqq here.");
		await Assert.That(mine).Contains($"You whisper, \"hi\" to {listenerName}.");
		await Assert.That(theirs).Contains($"{_speakerName} whispers: hi");
	}

	// MAT_NEAR: a player standing somewhere else is not a match, whatever the name.
	[Test]
	public async ValueTask Whisper_RemotePlayerIsUnmatched()
	{
		var remote = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "Remote");
		try
		{
			var (mine, _) = await Speak($"whisper #{remote.DbRef.Number}=hi");
			await Assert.That(mine).Contains($"Unable to whisper to: #{remote.DbRef.Number}");
			await Assert.That(Notifications.For(remote.DbRef)).DoesNotContain($"{_speakerName} whispers: hi");
		}
		finally
		{
			await ConnectionService.Disconnect(remote.Handle);
		}
	}

	// speech.c:396-401 — a target that fails can_interact(INTERACT_HEAR) is named in `can't hear you`
	// AND in the unable list, and is not whispered to. The speaker is a mortal, so the interact lock
	// really is evaluated against them.
	[Test]
	public async ValueTask Whisper_TargetThatCannotHearIsAFailure()
	{
		var listenerName = await NameOf(_listener.DbRef);
		await God($"@lock/interact #{_listener.DbRef.Number}=#1");
		try
		{
			var (mine, theirs) = await Speak($"whisper {listenerName}=hi");
			await Assert.That(mine).Contains($"{listenerName} can't hear you.");
			await Assert.That(mine).Contains($"Unable to whisper to: {listenerName}");
			await Assert.That(mine).DoesNotContain($"You whisper, \"hi\" to {listenerName}.");
			await Assert.That(theirs).DoesNotContain($"{_speakerName} whispers: hi");
		}
		finally
		{
			await God($"@unlock/interact #{_listener.DbRef.Number}");
		}
	}

	// speech.c:405-409 — the hundredth good target stops the scan with a warning, and those hundred are
	// still whispered to.
	[Test]
	public async ValueTask Whisper_StopsAtAHundredTargets()
	{
		var listenerName = await NameOf(_listener.DbRef);
		var targets = string.Join(' ', Enumerable.Repeat(listenerName, 101));
		var (mine, theirs) = await Speak($"whisper {targets}=hi");
		await Assert.That(mine).Contains("Too many people to whisper to.");
		await Assert.That(theirs.Count(line => line.StartsWith($"{_speakerName} whispers to "))).IsEqualTo(100);
	}

	// speech.c:429 — a noisy whisper is overheard only when a roll in [0, 100] falls below
	// whisper_loudness. At 0 it never does.
	[Test]
	public async ValueTask NoisyWhisper_AtLoudnessZero_IsNeverOverheard()
	{
		using var quiet = TestOptionsOverride.Scope(options => options with
		{
			Limit = options.Limit with { WhisperLoudness = 0 }
		});
		var (bystander, listenerName) = (await Bystander(), await NameOf(_listener.DbRef));
		try
		{
			await Speak($"whisper/noisy {listenerName}=hi");
			await Assert.That(Notifications.For(bystander.DbRef))
				.DoesNotContain($"{_speakerName} whispers to {listenerName}.");
		}
		finally
		{
			await ConnectionService.Disconnect(bystander.Handle);
		}
	}

	// get_random_u32(0, 100) never reaches 101, so every roll falls below it and the room overhears.
	// The configured maximum is 100; the override steps past it only to take the dice out of the test.
	[Test]
	public async ValueTask NoisyWhisper_AboveEveryRoll_IsOverheard()
	{
		using var loud = TestOptionsOverride.Scope(options => options with
		{
			Limit = options.Limit with { WhisperLoudness = 101 }
		});
		var (bystander, listenerName) = (await Bystander(), await NameOf(_listener.DbRef));
		try
		{
			await Speak($"whisper/noisy {listenerName}=hi");
			await Assert.That(Notifications.For(bystander.DbRef))
				.Contains($"{_speakerName} whispers to {listenerName}.");
		}
		finally
		{
			await ConnectionService.Disconnect(bystander.Handle);
		}
	}

	// speech.c:423 — "Drunk wizards...": a DARK whisperer is never overheard.
	[Test]
	public async ValueTask NoisyWhisper_FromADarkSpeaker_IsNeverOverheard()
	{
		using var loud = TestOptionsOverride.Scope(options => options with
		{
			Limit = options.Limit with { WhisperLoudness = 101 }
		});
		var (bystander, listenerName) = (await Bystander(), await NameOf(_listener.DbRef));
		await God($"@set #{_speaker.DbRef.Number}=DARK");
		try
		{
			await Speak($"whisper/noisy {listenerName}=hi");
			await Assert.That(Notifications.For(bystander.DbRef))
				.DoesNotContain($"{_speakerName} whispers to {listenerName}.");
		}
		finally
		{
			await God($"@set #{_speaker.DbRef.Number}=!DARK");
			await ConnectionService.Disconnect(bystander.Handle);
		}
	}

	private async Task<TestIsolationHelpers.TestPlayer> Bystander()
	{
		var bystander = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "Bystander");
		await God($"@tel #{bystander.DbRef.Number}={_room}");
		return bystander;
	}

	private async Task<DBRef> ThingInTheRoom(string name)
	{
		var created = await God($"@create {name}");
		var thing = DBRef.Parse(created.Message!.ToPlainText());
		await God($"@tel {thing}={_room}");
		return thing;
	}

	private async Task<string> NameOf(DBRef who)
		=> (await Mediator.Send(new Library.Queries.Database.GetObjectNodeQuery(who))).Expect<AnySharpObject>().Object().Name;

	private async Task<(string[] Mine, string[] Theirs)> Speak(string command)
	{
		var mineStart = Notifications.CountFor(_speaker.DbRef);
		var theirsStart = Notifications.CountFor(_listener.DbRef);

		var parser = WebAppFactoryArg.CommandParserFor(_speaker.DbRef, _speaker.Handle);
		await parser.CommandParse(_speaker.Handle, ConnectionService, MarkupText.Plain(command));

		return (
			[.. Notifications.For(_speaker.DbRef).Skip(mineStart)],
			[.. Notifications.For(_listener.DbRef).Skip(theirsStart)]);
	}

	private ValueTask<CallState> God(string command)
		=> WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
}
