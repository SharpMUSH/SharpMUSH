using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// @message and message() with /say, /pose, /semipose or /emit: the line is heard as that kind of speech, and the
/// speaker must pass the Speech lock of where they speak, as the built-in speech commands require.
/// <para>
/// Every refusal test first proves delivery with the lock open, so "nobody heard it" cannot pass because the setup
/// silently failed.
/// </para>
/// </summary>
public class MessageSpeechTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private static string Token(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

	[Test]
	[Arguments("say", INotifyService.NotificationType.Say, false)]
	[Arguments("pose", INotifyService.NotificationType.Pose, false)]
	[Arguments("semipose", INotifyService.NotificationType.SemiPose, false)]
	[Arguments("emit", INotifyService.NotificationType.Emit, false)]
	[Arguments("say", INotifyService.NotificationType.Say, true)]
	[Arguments("pose", INotifyService.NotificationType.Pose, true)]
	[Arguments("semipose", INotifyService.NotificationType.SemiPose, true)]
	[Arguments("emit", INotifyService.NotificationType.Emit, true)]
	public async Task SpeechKindIsHeardAsThatSpeech(string kind, INotifyService.NotificationType expected, bool function)
	{
		var scene = await SetupSceneAsync("MsgKind");
		var token = Token("kind");
		await Send(scene, kind, function, $"{scene.Witness.DbRef}", token);

		await Assert.That(DeliveriesTo(scene.Witness.DbRef, token).Select(delivery => delivery.Type)).IsEquivalentTo([expected]);
	}

	[Test]
	public async Task PlainMessageStaysAPrivateEmit()
	{
		var scene = await SetupSceneAsync("MsgPlain");
		var token = Token("plain");
		await Command(scene.Speaker.Handle, $"@message/silent {scene.Witness.DbRef}={token},NO_SUCH_FORMAT");

		await Assert.That(DeliveriesTo(scene.Witness.DbRef, token).Select(delivery => delivery.Type))
			.IsEquivalentTo([INotifyService.NotificationType.PrivateEmit]);
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	public async Task SpeechKindHonorsTheSpeechLock(bool function, bool remit)
	{
		var scene = await SetupSceneAsync("MsgLock");
		var recipients = remit ? scene.Room : $"{scene.Witness.DbRef}";
		var switches = remit ? "say remit" : "say";
		var allowed = Token("allowed");
		await Send(scene, switches, function, recipients, allowed);
		await AssertHeardAsync(scene.Witness.DbRef, allowed, "an open Speech lock lets the line through");

		await Command(1, $"@lock/speech {scene.Room}=#FALSE");
		var failure = Token("failure");
		await Command(1, $"&SPEECH_LOCK`FAILURE {scene.Room}={failure}");
		var denied = Token("denied");
		await Send(scene, switches, function, recipients, denied);
		await AssertNotHeardAsync(scene.Witness.DbRef, denied, "the Speech lock refuses the speaker");
		await AssertHeardAsync(scene.Speaker.DbRef, failure, "the refusal runs the location's Speech failure message");

		if (!remit)
		{
			var plain = Token("plain");
			await Command(scene.Speaker.Handle, $"@message/silent {recipients}={plain},NO_SUCH_FORMAT");
			await AssertHeardAsync(scene.Witness.DbRef, plain, "a plain @message is a @pemit, which checks no Speech lock");
		}

		await Command(1, $"@set {scene.Speaker.DbRef}=LOUD");
		var loud = Token("loud");
		await Send(scene, switches, function, recipients, loud);
		await AssertHeardAsync(scene.Witness.DbRef, loud, "LOUD passes the Speech lock");
	}

	[Test]
	public async Task SpoofedSpeechIsAdmittedAsTheEnactor()
	{
		var scene = await SetupSceneAsync("MsgSpoof");
		var enactor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MsgSpoofEnactor");
		await Command(1, $"@teleport/silent {enactor.DbRef}={scene.Room}");
		await Command(1, $"@power {scene.Speaker.DbRef}=Can_Spoof");
		await Command(1, $"@lock/speech {scene.Room}==#{enactor.DbRef.Number}");
		var parser = WebAppFactoryArg.CommandParserFor(scene.Speaker.DbRef, scene.Speaker.Handle);
		parser = parser.FromState(parser.CurrentState with { Enactor = enactor.DbRef });

		var unspoofed = Token("unspoofed");
		await parser.CommandListParse(MarkupText.Plain($"@message/say/silent {scene.Witness.DbRef}={unspoofed},NO_SUCH_FORMAT"));
		await AssertNotHeardAsync(scene.Witness.DbRef, unspoofed, "the executor itself does not pass the Speech lock");

		var spoofed = Token("spoofed");
		await parser.CommandListParse(MarkupText.Plain($"@message/say/spoof/silent {scene.Witness.DbRef}={spoofed},NO_SUCH_FORMAT"));
		await Assert.That(DeliveriesTo(scene.Witness.DbRef, spoofed).Select(delivery => delivery.Sender))
			.IsEquivalentTo([(DBRef?)enactor.DbRef]).Because("the enactor passes the lock and is the one heard");
	}

	[Test]
	[Arguments("say", INotifyService.NotificationType.NSSay)]
	[Arguments("pose", INotifyService.NotificationType.NSPose)]
	[Arguments("semipose", INotifyService.NotificationType.NSSemiPose)]
	[Arguments("emit", INotifyService.NotificationType.NSEmit)]
	public async Task NospoofSpeechIsHeardAsNospoofSpeech(string kind, INotifyService.NotificationType expected)
	{
		var scene = await SetupSceneAsync("MsgNospoof");
		await Command(1, $"@power {scene.Speaker.DbRef}=Can_Spoof");
		var token = Token("nospoof");
		await Send(scene, $"{kind} nospoof", false, $"{scene.Witness.DbRef}", token);

		await Assert.That(DeliveriesTo(scene.Witness.DbRef, token).Select(delivery => delivery.Type)).IsEquivalentTo([expected]);
	}

	[Test]
	public async Task RemitSkipsOnlyTheRoomsThatRefuse()
	{
		var scene = await SetupSceneAsync("MsgRemitRooms");
		var open = await Dig("MsgRemitOpen");
		var openWitness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MsgRemitOpenWi");
		await Command(1, $"@teleport/silent {openWitness.DbRef}={open}");
		await Command(1, $"@lock/speech {scene.Room}=#FALSE");

		var token = Token("rooms");
		await Send(scene, "say remit", false, $"{scene.Room} {open}", token);
		await AssertNotHeardAsync(scene.Witness.DbRef, token, "the refusing room is skipped");
		await AssertHeardAsync(openWitness.DbRef, token, "the open room still hears it");
	}

	[Test]
	public async Task RemitWithOemitChecksTheOemitRoomToo()
	{
		var scene = await SetupSceneAsync("MsgRemitOemit");
		var open = await Dig("MsgRemitOemitOpen");
		var openWitness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MsgRemitOemitOpenWi");
		await Command(1, $"@teleport/silent {openWitness.DbRef}={open}");
		await Command(1, $"@lock/speech {scene.Room}=#FALSE");

		var token = Token("remitoemit");
		await Send(scene, "say remit oemit", false, open, token);
		await AssertHeardAsync(openWitness.DbRef, token, "the open /remit room hears it");
		await AssertNotHeardAsync(scene.Witness.DbRef, token, "the locked room /oemit speaks in does not");
	}

	/// <summary>/oemit speaks in the executor's room, so that room's Speech lock decides, and the refusal goes to the
	/// spoofed speaker.</summary>
	[Test]
	public async Task OemitSpeechIsAdmittedByTheRoomItIsSpokenIn()
	{
		var scene = await SetupSceneAsync("MsgOemit");
		var elsewhere = await Dig("MsgOemitElsewhere");
		var enactor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MsgOemitEnactor");
		await Command(1, $"@teleport/silent {enactor.DbRef}={elsewhere}");
		await Command(1, $"@lock/speech {elsewhere}=#FALSE");
		await Command(1, $"@power {scene.Speaker.DbRef}=Can_Spoof");
		var parser = WebAppFactoryArg.CommandParserFor(scene.Speaker.DbRef, scene.Speaker.Handle);
		parser = parser.FromState(parser.CurrentState with { Enactor = enactor.DbRef });

		var allowed = Token("oemitopen");
		await parser.CommandListParse(MarkupText.Plain($"@message/say/oemit/spoof/silent {scene.Speaker.DbRef}={allowed},NO_SUCH_FORMAT"));
		await AssertHeardAsync(scene.Witness.DbRef, allowed, "the executor's open room admits it, whatever the speaker's own room says");

		await Command(1, $"@lock/speech {scene.Room}=#FALSE");
		var failure = Token("oemitfailure");
		await Command(1, $"&SPEECH_LOCK`FAILURE {scene.Room}={failure}");
		var denied = Token("oemitdenied");
		await parser.CommandListParse(MarkupText.Plain($"@message/say/oemit/spoof/silent {scene.Speaker.DbRef}={denied},NO_SUCH_FORMAT"));
		await AssertNotHeardAsync(scene.Witness.DbRef, denied, "the executor's room refuses the speaker");
		await AssertHeardAsync(enactor.DbRef, failure, "the spoofed speaker is told");
		await AssertNotHeardAsync(scene.Speaker.DbRef, failure, "the object running the code is not");
	}

	/// <summary>`help @message speech`'s example: a softcode say through @message/say/spoof/remit, refused by the room.</summary>
	[Test]
	public async Task HelpExampleIsRefusedByTheRoom()
	{
		var scene = await SetupSceneAsync("MsgHelp");
		var globals = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgHelpGlobals");
		await Command(1, $"@set {globals}=WIZARD");
		await Command(1, $"@teleport/silent {globals}={scene.Room}");
		var verb = Token("fsay");
		await Command(1, $"&cmd.fsay {globals}=${verb} *: @message/say/spoof/remit loc(%#)=[moniker(%#)] says\\, \"%0\", SAYFORMAT, %0");

		var allowed = Token("hello");
		await Command(scene.Speaker.Handle, $"{verb} {allowed}");
		await Assert.That(Notifications.For(scene.Witness.DbRef)).Contains(line => line.EndsWith($" says, \"{allowed}\"", StringComparison.Ordinal))
			.Because("the open room hears the say");

		await Command(1, $"@lock/speech {scene.Room}=#FALSE");
		var denied = Token("hello");
		await Command(scene.Speaker.Handle, $"{verb} {denied}");
		await AssertNotHeardAsync(scene.Witness.DbRef, denied, "the room's Speech lock refuses the speaker");
		await AssertHeardAsync(scene.Speaker.DbRef, "You may not speak there!", "the help's Output line");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task TwoSpeechKindsAreRefused(bool function)
	{
		var scene = await SetupSceneAsync("MsgTwoKinds");
		var token = Token("twokinds");
		await Send(scene, "say pose", function, $"{scene.Witness.DbRef}", token);

		await AssertNotHeardAsync(scene.Witness.DbRef, token, "a line cannot be two kinds of speech");
		await AssertHeardAsync(scene.Speaker.DbRef, "Choose one of /say, /pose, /semipose and /emit.", "the speaker is told why");
	}

	/// <summary>@message/<switches> or message() with the same switches, as <paramref name="scene"/>'s speaker.</summary>
	private async Task Send(Scene scene, string switches, bool function, string recipients, string token)
	{
		if (function)
		{
			// message()'s switches are its 14th argument, after ten format arguments.
			await Command(scene.Speaker.Handle, $"think message({recipients},{token},NO_SUCH_FORMAT,{new string(',', 9)},{switches})");
		}
		else
		{
			await Command(scene.Speaker.Handle, $"@message/{switches.Replace(' ', '/')}/silent {recipients}={token},NO_SUCH_FORMAT");
		}
	}

	private List<TestHelpers.NotificationRecorder.Delivery> DeliveriesTo(DBRef who, string token)
		=> Notifications.DeliveriesFor(who).Where(delivery => delivery.Message.Contains(token, StringComparison.Ordinal)).ToList();

	private async Task AssertHeardAsync(DBRef who, string token, string because)
		=> await Assert.That(Notifications.For(who).Any(m => m.Contains(token, StringComparison.Ordinal)))
			.IsTrue().Because(because);

	private async Task AssertNotHeardAsync(DBRef who, string token, string because)
		=> await Assert.That(Notifications.For(who).Any(m => m.Contains(token, StringComparison.Ordinal)))
			.IsFalse().Because(because);

	private async Task Command(long handle, string command)
		=> await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));

	private async Task<string> Dig(string prefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {name}"));
		return result.Message.ToPlainText()!.Trim();
	}

	private sealed record Scene(TestIsolationHelpers.TestPlayer Speaker, TestIsolationHelpers.TestPlayer Witness, string Room);

	/// <summary>A fresh room holding a speaker and a witness, so what each hears is theirs alone.</summary>
	private async Task<Scene> SetupSceneAsync(string prefix)
	{
		var room = await Dig(prefix);
		var speaker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"{prefix}Sp");
		var witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"{prefix}Wi");
		await Command(1, $"@teleport/silent {speaker.DbRef}={room}");
		await Command(1, $"@teleport/silent {witness.DbRef}={room}");
		return new Scene(speaker, witness, room);
	}
}
