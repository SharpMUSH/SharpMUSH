using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The emit-family softcode functions are the same code path as their commands in PennMUSH:
/// <c>fun_emit</c> calls <c>do_emit</c>, <c>fun_lemit</c> calls <c>do_lemit</c> and
/// <c>fun_remit</c> calls <c>do_remit</c> (<c>src/funmisc.c:221-296</c>), all of which refuse
/// to speak when the room's Speech lock rejects the speaker (<c>src/speech.c</c>). The functions
/// enforced nothing, so softcode could emit into a room its executor was Speech-locked out of.
/// <para>
/// Every test proves the delivery first with the lock passing, so the "nobody heard it" half
/// cannot pass because the setup silently failed.
/// </para>
/// </summary>
public class CommunicationFunctionSpeechLockTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private static string Token(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

	[Test]
	[Arguments("prompt", false)]
	[Arguments("nsprompt", false)]
	[Arguments("prompt", true)]
	[Arguments("nsprompt", true)]
	public async Task PromptUsesProtocolDeliveryAndSilentEchoPolicy(string name, bool function)
	{
		var scene = await SetupSceneAsync("PromptScope");
		var token = Token("promptbody");
		if (function) await Eval(scene.Speaker.Handle, $"{name}({scene.Witness.DbRef},{token})");
		else await Command(scene.Speaker.Handle, $"@{name}/silent {scene.Witness.DbRef}={token}");
		var notify = WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
		await notify.Received().Prompt(TestHelpers.MatchingObject(scene.Witness.DbRef), TestHelpers.MatchingMessage(token),
			TestHelpers.MatchingObject(scene.Speaker.DbRef), INotifyService.NotificationType.Announce);
		if (!function) await AssertNotHeardAsync(scene.Speaker.DbRef, token, "/silent suppresses the confirmation");
		if (function) await Eval(scene.Speaker.Handle, $"{name}({scene.Witness.DbRef},)");
		else await Command(scene.Speaker.Handle, $"@{name}/silent {scene.Witness.DbRef}=");
		await notify.Received().Prompt(TestHelpers.MatchingObject(scene.Witness.DbRef), TestHelpers.MatchingMessage(""),
			TestHelpers.MatchingObject(scene.Speaker.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("pemit", false)]
	[Arguments("nspemit", false)]
	[Arguments("pemit", true)]
	[Arguments("nspemit", true)]
	public async Task PortEmissionRequiresPrivilegeAndDeliversLists(string name, bool function)
	{
		var scene = await SetupSceneAsync("PortScope");
		var denied = Token("portdenied");
		if (function) await Eval(scene.Speaker.Handle, $"{name}({scene.Witness.Handle},{denied})");
		else await Command(scene.Speaker.Handle, $"@{name}/list {scene.Witness.Handle}={denied}");
		await Assert.That(Notifications.ForHandle(scene.Witness.Handle)).DoesNotContain(denied);
		var token = Token("portallowed");
		var ports = $"{scene.Speaker.Handle} {scene.Witness.Handle}";
		if (function) await Eval(1, $"{name}({ports},{token})");
		else await Command(1, $"@{name}/list {ports}={token}");
		await Assert.That(Notifications.ForHandle(scene.Speaker.Handle)).Contains(token);
		await Assert.That(Notifications.ForHandle(scene.Witness.Handle)).Contains(token);
		var mixed = Token("mixedtargets");
		if (function) await Eval(1, $"{name}({scene.Speaker.Handle} {scene.Witness.DbRef},{mixed})");
		else await Command(1, $"@{name}/list {scene.Speaker.Handle} {scene.Witness.DbRef}={mixed}");
		await Assert.That(Notifications.ForHandle(scene.Speaker.Handle)).DoesNotContain(mixed);
		await AssertHeardAsync(scene.Witness.DbRef, mixed, "a mixed list remains object targets");
	}

	[Test]
	[Arguments("emit")]
	[Arguments("remit")]
	public async Task SpeechLockUsesSpoofedSpeakerAndLoudBypassesIt(string name)
	{
		var scene = await SetupSceneAsync("SpoofSpeech");
		var enactor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SpoofEnactor");
		await Command(1, $"@power {scene.Speaker.DbRef}=Can_Spoof");
		await Command(1, $"@lock/speech {scene.Room}==#{enactor.DbRef.Number}");
		var parser = WebAppFactoryArg.CommandParserFor(scene.Speaker.DbRef, scene.Speaker.Handle);
		parser = parser.FromState(parser.CurrentState with { Enactor = enactor.DbRef });
		await Assert.That(await WebAppFactoryArg.Services.GetRequiredService<IPermissionService>().CanSpoofAs(
			await parser.CurrentState.KnownExecutorObject(Mediator), await parser.CurrentState.KnownEnactorObject(Mediator))).IsTrue();
		var lockedRoom = (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(scene.Room)))).Expect<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject>();
		await Assert.That(await WebAppFactoryArg.Services.GetRequiredService<ILockService>().Evaluate(LockType.Speech,
			lockedRoom, await parser.CurrentState.KnownEnactorObject(Mediator))).IsTrue().Because("the test's Speech key admits its enactor");
		var target = name == "remit" ? $" {scene.Room}=" : " ";
		var denied = Token("speakerdenied");
		await parser.CommandListParse(MarkupText.Plain($"@{name}{target}{denied}"));
		await AssertNotHeardAsync(scene.Witness.DbRef, denied, "the executor does not pass this Speech lock");
		var spoofed = Token("speakerallowed");
		await parser.CommandListParse(MarkupText.Plain($"@{name}/spoof{target}{spoofed}"));
		await AssertHeardAsync(scene.Witness.DbRef, spoofed, "the explicitly selected enactor passes the Speech lock: " + string.Join(" | ", Notifications.For(scene.Speaker.DbRef)));
		await Command(1, $"@set {scene.Speaker.DbRef}=LOUD");
		var loud = Token("loudallowed");
		await Command(scene.Speaker.Handle, $"@{name}{target}{loud}");
		await AssertHeardAsync(scene.Witness.DbRef, loud, "LOUD bypasses the Speech lock");
	}

	private async Task Command(long handle, string command)
		=> await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));

	private async Task<string> Eval(long handle, string expression)
	{
		var result = await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"think {expression}"));
		return result?.Message?.ToPlainText() ?? string.Empty;
	}

	private async Task<string> Dig(string prefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {name}"));
		return result.Message!.ToPlainText()!.Trim();
	}

	private sealed record Scene(
		TestIsolationHelpers.TestPlayer Speaker,
		TestIsolationHelpers.TestPlayer Witness,
		string Room);

	/// <summary>
	/// A fresh room holding a speaker and a witness. /SILENT keeps the arrival autolook from
	/// queueing output into some other test's capture window, as the shared NotifyService
	/// substitute is session-wide.
	/// </summary>
	private async Task<Scene> SetupSceneAsync(string prefix)
	{
		var room = await Dig(prefix);
		var speaker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"{prefix}Sp");
		var witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"{prefix}Wi");

		await Command(1, $"@teleport/silent {speaker.DbRef}={room}");
		await Command(1, $"@teleport/silent {witness.DbRef}={room}");

		await Assert.That(await Eval(speaker.Handle, "loc(me)")).IsEqualTo(room)
			.Because("every assertion below is vacuous unless the speaker actually moved into the room");
		await Assert.That(await Eval(witness.Handle, "loc(me)")).IsEqualTo(room)
			.Because("every assertion below is vacuous unless the witness actually moved into the room");

		return new Scene(speaker, witness, room);
	}

	private async Task AssertHeardAsync(DBRef who, string token, string because)
		=> await Assert.That(Notifications.For(who).Any(m => m.Contains(token, StringComparison.Ordinal)))
			.IsTrue().Because(because);

	private async Task AssertNotHeardAsync(DBRef who, string token, string because)
		=> await Assert.That(Notifications.For(who).Any(m => m.Contains(token, StringComparison.Ordinal)))
			.IsFalse().Because(because);

	[Test]
	[Arguments("@emit")]
	[Arguments("@nsemit")]
	[Arguments("@lemit")]
	[Arguments("@nslemit")]
	[Arguments("@remit")]
	[Arguments("@nsremit")]
	[Arguments("@oemit")]
	[Arguments("@nsoemit")]
	public async Task EmitCommandsHonorSpeechLockAndCustomFailure(string command)
	{
		var scene = await SetupSceneAsync("CmdSpeech");
		var target = command.Contains("remit") ? $" {scene.Room}="
			: command.Contains("oemit") ? $" {scene.Room}/{scene.Speaker.DbRef}=" : " ";
		var allowed = Token("allowed");
		await Command(scene.Speaker.Handle, command + target + allowed);
		await AssertHeardAsync(scene.Witness.DbRef, allowed, "open Speech lock permits delivery");
		await Command(1, $"@lock/speech {scene.Room}=#FALSE");
		var failure = Token("speechfailure");
		await Command(1, $"&SPEECH_LOCK`FAILURE {scene.Room}={failure}");
		var othersFailure = Token("othersfailure");
		await Command(1, $"&SPEECH_LOCK`OFAILURE {scene.Room}={othersFailure}");
		var denied = Token("denied");
		await Command(scene.Speaker.Handle, command + target + denied);
		await AssertNotHeardAsync(scene.Witness.DbRef, denied, "Speech lock rejects the command");
		await AssertHeardAsync(scene.Speaker.DbRef, failure, "refusal evaluates the location's Speech failure attribute");
		await AssertHeardAsync(scene.Witness.DbRef, othersFailure, "refusal evaluates the Speech failure message to others");
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task OmitFanoutReachesEachExcludedObjectsLocation(bool function, bool noSpoof)
	{
		var first = await SetupSceneAsync("OmitFirst");
		var second = await SetupSceneAsync("OmitSecond");
		var name = noSpoof ? "nsoemit" : "oemit";
		var token = Token("fanout");
		var targets = $"{first.Speaker.DbRef} {second.Speaker.DbRef}";
		if (function) await Eval(first.Speaker.Handle, $"{name}({targets},{token})");
		else await Command(first.Speaker.Handle, $"@{name} {targets}={token}");
		await AssertHeardAsync(first.Witness.DbRef, token, "the first excluded object's location hears the message");
		await AssertHeardAsync(second.Witness.DbRef, token, "the second excluded object's location hears the message");
		await AssertNotHeardAsync(first.Speaker.DbRef, token, "first exclusion is omitted");
		await AssertNotHeardAsync(second.Speaker.DbRef, token, "second exclusion is omitted");
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task ZoneEmissionRequiresControlAndChecksEachRoom(bool function, bool noSpoof)
	{
		var first = await SetupSceneAsync("ZoneOpen");
		var second = await SetupSceneAsync("ZoneLocked");
		var zone = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EmitZone");
		await Command(1, $"@chzone {first.Room}={zone}");
		await Command(1, $"@chzone {second.Room}={zone}");
		await Command(1, $"@lock/speech {second.Room}=#FALSE");
		var name = noSpoof ? "nszemit" : "zemit";
		async Task Emit(string token)
		{
			if (function) await Eval(first.Speaker.Handle, $"{name}({zone},{token})");
			else await Command(first.Speaker.Handle, $"@{name}/silent {zone}={token}");
		}
		var denied = Token("uncontrolled");
		await Emit(denied);
		await AssertNotHeardAsync(first.Witness.DbRef, denied, "mortals cannot emit into another owner's zone");
		await Command(1, $"@chown {zone}={first.Speaker.DbRef}");
		var allowed = Token("zonecontrolled");
		await Emit(allowed);
		await AssertHeardAsync(first.Witness.DbRef, allowed, "owned zone permits the unlocked room");
		await AssertNotHeardAsync(second.Witness.DbRef, allowed, "a denied room is filtered from the zone broadcast");
		var carrier = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ZoneCarrier");
		await Command(1, $"@teleport/silent {carrier}={first.Room}");
		await Command(1, $"@teleport/silent {first.Speaker.DbRef}={carrier}");
		await Command(1, $"@lock/interact {carrier}=#FALSE");
		var nested = Token("nestedzone");
		if (function) await Eval(first.Speaker.Handle, $"{name}({zone},{nested})");
		else await Command(first.Speaker.Handle, $"@{name}/noisy {zone}={nested}");
		await AssertNotHeardAsync(first.Speaker.DbRef, nested, "enumerating the immediate carrier suppresses the zone echo even if it cannot hear");
	}

	[Test]
	[Arguments("pemit", false)]
	[Arguments("nspemit", false)]
	[Arguments("pemit", true)]
	[Arguments("nspemit", true)]
	public async Task PrivateEmitHonorsPageAndHaven(string name, bool function)
	{
		var scene = await SetupSceneAsync("PrivateLocks");
		async Task Emit(string token)
		{
			if (function) await Eval(scene.Speaker.Handle, $"{name}({scene.Witness.DbRef},{token})");
			else await Command(scene.Speaker.Handle, $"@{name}/silent {scene.Witness.DbRef}={token}");
		}
		var allowed = Token("privateallowed");
		await Emit(allowed);
		await AssertHeardAsync(scene.Witness.DbRef, allowed, "the baseline must reach the player");
		await Command(1, $"@lock/page {scene.Witness.DbRef}=#FALSE");
		var pageDenied = Token("pagedenied");
		await Emit(pageDenied);
		await AssertNotHeardAsync(scene.Witness.DbRef, pageDenied, "Page lock rejects private emission");
		await Command(1, $"@unlock/page {scene.Witness.DbRef}");
		await Command(1, $"@set {scene.Witness.DbRef}=HAVEN");
		var havenDenied = Token("havendenied");
		await Emit(havenDenied);
		await AssertNotHeardAsync(scene.Witness.DbRef, havenDenied, "HAVEN rejects private emission");
	}

	[Test]
	public async Task EmitFunction_ObeysTheRoomSpeechLock()
	{
		var scene = await SetupSceneAsync("SpkEmit");

		var allowed = Token("EMITOK");
		await Eval(scene.Speaker.Handle, $"emit({allowed})");
		await AssertHeardAsync(scene.Witness.DbRef, allowed,
			"emit() delivers to the room while the Speech lock passes");

		await Command(1, $"@lock/speech {scene.Room}=#FALSE");

		var denied = Token("EMITNO");
		await Eval(scene.Speaker.Handle, $"emit({denied})");
		await AssertNotHeardAsync(scene.Witness.DbRef, denied,
			"emit() must not reach a room whose Speech lock rejects the speaker");
		await AssertHeardAsync(scene.Speaker.DbRef, "You may not speak here!",
			"the speaker is told why, exactly as @emit tells them");
	}

	[Test]
	public async Task NoSpoofEmitFunction_ObeysTheRoomSpeechLock()
	{
		var scene = await SetupSceneAsync("SpkNsEmit");

		var allowed = Token("NSEMITOK");
		await Eval(scene.Speaker.Handle, $"nsemit({allowed})");
		await AssertHeardAsync(scene.Witness.DbRef, allowed,
			"nsemit() delivers to the room while the Speech lock passes");

		await Command(1, $"@lock/speech {scene.Room}=#FALSE");

		var denied = Token("NSEMITNO");
		await Eval(scene.Speaker.Handle, $"nsemit({denied})");
		await AssertNotHeardAsync(scene.Witness.DbRef, denied,
			"nsemit() must not reach a room whose Speech lock rejects the speaker");
	}

	[Test]
	public async Task LocationEmitFunction_ObeysTheRoomSpeechLock()
	{
		var scene = await SetupSceneAsync("SpkLemit");

		var allowed = Token("LEMITOK");
		await Eval(scene.Speaker.Handle, $"lemit({allowed})");
		await AssertHeardAsync(scene.Witness.DbRef, allowed,
			"lemit() delivers to the outermost room while the Speech lock passes");

		await Command(1, $"@lock/speech {scene.Room}=#FALSE");

		var denied = Token("LEMITNO");
		await Eval(scene.Speaker.Handle, $"lemit({denied})");
		await AssertNotHeardAsync(scene.Witness.DbRef, denied,
			"lemit() must not reach a room whose Speech lock rejects the speaker");
	}

	[Test]
	public async Task NoSpoofLocationEmitFunction_ObeysTheRoomSpeechLock()
	{
		var scene = await SetupSceneAsync("SpkNsLemit");

		var allowed = Token("NSLEMITOK");
		await Eval(scene.Speaker.Handle, $"nslemit({allowed})");
		await AssertHeardAsync(scene.Witness.DbRef, allowed,
			"nslemit() delivers to the outermost room while the Speech lock passes");

		await Command(1, $"@lock/speech {scene.Room}=#FALSE");

		var denied = Token("NSLEMITNO");
		await Eval(scene.Speaker.Handle, $"nslemit({denied})");
		await AssertNotHeardAsync(scene.Witness.DbRef, denied,
			"nslemit() must not reach a room whose Speech lock rejects the speaker");
	}

	[Test]
	public async Task RoomEmitFunction_ObeysTheTargetRoomSpeechLock()
	{
		var scene = await SetupSceneAsync("SpkRemit");

		var allowed = Token("REMITOK");
		await Eval(scene.Speaker.Handle, $"remit({scene.Room},{allowed})");
		await AssertHeardAsync(scene.Witness.DbRef, allowed,
			"remit() delivers to the named room while its Speech lock passes");

		await Command(1, $"@lock/speech {scene.Room}=#FALSE");

		var denied = Token("REMITNO");
		await Eval(scene.Speaker.Handle, $"remit({scene.Room},{denied})");
		await AssertNotHeardAsync(scene.Witness.DbRef, denied,
			"remit() must not reach a room whose Speech lock rejects the speaker");
	}

	[Test]
	public async Task NoSpoofRoomEmitFunction_ObeysTheTargetRoomSpeechLock()
	{
		var scene = await SetupSceneAsync("SpkNsRemit");

		var allowed = Token("NSREMITOK");
		await Eval(scene.Speaker.Handle, $"nsremit({scene.Room},{allowed})");
		await AssertHeardAsync(scene.Witness.DbRef, allowed,
			"nsremit() delivers to the named room while its Speech lock passes");

		await Command(1, $"@lock/speech {scene.Room}=#FALSE");

		var denied = Token("NSREMITNO");
		await Eval(scene.Speaker.Handle, $"nsremit({scene.Room},{denied})");
		await AssertNotHeardAsync(scene.Witness.DbRef, denied,
			"nsremit() must not reach a room whose Speech lock rejects the speaker");
	}

	/// <summary>
	/// PennMUSH's <c>do_emit</c> speaks into <c>speech_loc(executor)</c> — the executor's immediate
	/// <c>Location()</c> — while <c>do_lemit</c> speaks into <c>absolute_room()</c>
	/// (<c>src/speech.c:1218,1333</c>). A speaker inside a container inside a room is where the two
	/// separate: emit reaches only the container's contents, lemit reaches the room's.
	/// <para>
	/// This is what makes <c>nslemit()</c>'s old <c>Where()</c> a bug rather than a synonym, and it
	/// is the arrangement any future unification of <c>@EMIT</c> with <c>emit()</c> has to survive
	/// (see the comment on <c>@EMIT</c> in GeneralCommands, and #959).
	/// </para>
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task EmitTargetsTheImmediateLocation_WhileLemitTargetsTheOutermostRoom(bool command)
	{
		var room = await Dig("SpkNestRoom");
		var speaker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SpkNestSp");
		var inside = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SpkNestIn");
		var outside = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SpkNestOut");

		var box = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SpkNestBox");

		await Command(1, $"@teleport/silent {box}={room}");
		await Command(1, $"@teleport/silent {speaker.DbRef}={box}");
		await Command(1, $"@teleport/silent {inside.DbRef}={box}");
		await Command(1, $"@teleport/silent {outside.DbRef}={room}");

		await Assert.That(await Eval(speaker.Handle, "loc(me)")).IsEqualTo(box.ToString())
			.Because("the nesting assertions are vacuous unless the speaker is inside the box");
		await Assert.That(await Eval(outside.Handle, "loc(me)")).IsEqualTo(room)
			.Because("the nesting assertions are vacuous unless the outside witness is in the room");

		var emitToken = Token("NESTEMIT");
		if (command) await Command(speaker.Handle, $"@emit {emitToken}");
		else await Eval(speaker.Handle, $"emit({emitToken})");
		await AssertHeardAsync(box, emitToken, "the immediate location itself receives the emit");
		await AssertHeardAsync(inside.DbRef, emitToken,
			"emit() speaks into the immediate location, so the box's other occupant hears it");
		await AssertNotHeardAsync(outside.DbRef, emitToken,
			"emit() speaks into the immediate location, not the outermost room");

		var lemitToken = Token("NESTLEMIT");
		if (command) await Command(speaker.Handle, $"@lemit {lemitToken}");
		else await Eval(speaker.Handle, $"lemit({lemitToken})");
		await AssertHeardAsync(outside.DbRef, lemitToken,
			"lemit() speaks into the outermost room");

		var nsLemitToken = Token("NESTNSLEMIT");
		if (command) await Command(speaker.Handle, $"@nslemit {nsLemitToken}");
		else await Eval(speaker.Handle, $"nslemit({nsLemitToken})");
		await AssertHeardAsync(outside.DbRef, nsLemitToken,
			"nslemit() is @nslemit, which is do_lemit: it speaks into the outermost room too");
	}
}
