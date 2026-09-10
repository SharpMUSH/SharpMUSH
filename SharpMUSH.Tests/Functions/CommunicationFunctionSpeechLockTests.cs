using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
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
	public async Task EmitTargetsTheImmediateLocation_WhileLemitTargetsTheOutermostRoom()
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
		await Eval(speaker.Handle, $"emit({emitToken})");
		await AssertHeardAsync(inside.DbRef, emitToken,
			"emit() speaks into the immediate location, so the box's other occupant hears it");
		await AssertNotHeardAsync(outside.DbRef, emitToken,
			"emit() speaks into the immediate location, not the outermost room");

		var lemitToken = Token("NESTLEMIT");
		await Eval(speaker.Handle, $"lemit({lemitToken})");
		await AssertHeardAsync(outside.DbRef, lemitToken,
			"lemit() speaks into the outermost room");

		var nsLemitToken = Token("NESTNSLEMIT");
		await Eval(speaker.Handle, $"nslemit({nsLemitToken})");
		await AssertHeardAsync(outside.DbRef, nsLemitToken,
			"nslemit() is @nslemit, which is do_lemit: it speaks into the outermost room too");
	}
}
