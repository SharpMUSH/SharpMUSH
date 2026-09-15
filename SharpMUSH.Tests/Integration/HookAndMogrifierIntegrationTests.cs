using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Integration tests for the @hook system.
/// These tests validate the complete hook workflow including command execution,
/// hook triggering, and $-command matching.
/// </summary>
public class HookIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IHookService HookService => WebAppFactoryArg.Services.GetRequiredService<IHookService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	[Test]
	public async ValueTask Hook_BeforeHook_ExecutesBeforeCommand()
	{
		var hookExists = await HookService.GetHookAsync("HOOKTEST_BEFORE", "BEFORE");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_AfterHook_ExecutesAfterCommand()
	{
		var hookExists = await HookService.GetHookAsync("HOOKTEST_AFTER", "AFTER");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_IgnoreHook_SkipsCommandWhenReturnsEmpty()
	{
		var hookExists = await HookService.GetHookAsync("HOOKTEST_IGNORE", "IGNORE");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_OverrideHook_ReplacesBuiltInCommand()
	{
		var hookExists = await HookService.GetHookAsync("HOOKTEST_OVERRIDE", "OVERRIDE");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_ExtendHook_HandlesInvalidSwitches()
	{
		var hookExists = await HookService.GetHookAsync("HOOKTEST_EXTEND", "EXTEND");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_InlineModifier_ExecutesImmediately()
	{
		var testCommand = $"HOOKTEST_INLINE_{Guid.NewGuid():N}";
		await HookService.SetHookAsync(testCommand, "BEFORE", new DBRef(1), "test_attr", inline: true);
		var hook = await HookService.GetHookAsync(testCommand, "BEFORE");
		await Assert.That(hook.IsSome()).IsTrue();
		if (hook.IsSome())
		{
			await Assert.That(hook.Expect<CommandHook>().Inline).IsTrue();
		}
		await HookService.ClearHookAsync(testCommand, "BEFORE");
	}

	[Test]
	public async ValueTask Hook_LocalizeModifier_SavesAndRestoresRegisters()
	{
		var testCommand = $"HOOKTEST_LOCALIZE_{Guid.NewGuid():N}";
		await HookService.SetHookAsync(testCommand, "BEFORE", new DBRef(1), "test_attr", localize: true);
		var hook = await HookService.GetHookAsync(testCommand, "BEFORE");
		await Assert.That(hook.IsSome()).IsTrue();
		if (hook.IsSome())
		{
			await Assert.That(hook.Expect<CommandHook>().Localize).IsTrue();
		}
		await HookService.ClearHookAsync(testCommand, "BEFORE");
	}

	[Test]
	public async ValueTask Hook_ClearRegsModifier_ClearsRegisters()
	{
		var testCommand = $"HOOKTEST_CLEARREGS_{Guid.NewGuid():N}";
		await HookService.SetHookAsync(testCommand, "BEFORE", new DBRef(1), "test_attr", clearregs: true);
		var hook = await HookService.GetHookAsync(testCommand, "BEFORE");
		await Assert.That(hook.IsSome()).IsTrue();
		if (hook.IsSome())
		{
			await Assert.That(hook.Expect<CommandHook>().ClearRegs).IsTrue();
		}
		await HookService.ClearHookAsync(testCommand, "BEFORE");
	}

	[Test]
	public async ValueTask Hook_HuhCommandHook_CustomizesUndefinedCommand()
	{
		var testCommand = $"HOOKTEST_HUHCOMMAND_{Guid.NewGuid():N}";
		await HookService.SetHookAsync(testCommand, "OVERRIDE", new DBRef(1), "test_attr");
		var hook = await HookService.GetHookAsync(testCommand, "OVERRIDE");
		await Assert.That(hook.IsSome()).IsTrue();
		await HookService.ClearHookAsync(testCommand, "OVERRIDE");
	}

	[Test]
	public async ValueTask Hook_NamedRegisters_PopulatedCorrectly()
	{
	}
}

/// <summary>
/// The channel mogrifier and per-player <c>@chatformat</c> callbacks, driven end to end: a real
/// channel, real members, <c>@chat</c> typed at a connection, and the assertions made on what each
/// member was actually notified of and on what landed in the recall buffer.
///
/// <para>Every test in here failed before the lane-K fix, and all for the same reason: both call
/// sites handed <c>AttributeService</c> a null parser, which it dereferences on its first statement
/// (<c>EvaluationRestrictions.DemandObjectDataAccess(parser.CurrentState.Restrictions)</c>), and the
/// resulting <c>NullReferenceException</c> was swallowed by a bare catch that substituted the
/// unmogrified default. The callbacks existed, were wired, were reachable — and could not run.
/// <c>@chatformat</c> carried a second fault on top: the handler looked up
/// <c>CHATFORMAT`&lt;CHANNEL&gt;</c>, an attribute PennMUSH never writes
/// (<c>src/extchat.c:3935</c> — <c>format.attr = "CHATFORMAT"</c>, unqualified, per member).</para>
///
/// <para>These cases deliberately overlap none of <see cref="Commands.MogrifierTests"/>, which drives
/// the <c>@channel/mogrifier</c> command and the individual <c>MOGRIFY`*</c> verbs. What is tested
/// here is the delivery pipeline around them: the arguments each callback is handed, who each
/// callback runs as, what each member receives, and what is buffered for <c>@channel/recall</c>.</para>
/// </summary>
public class MogrifierIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// A channel, a speaker, a listener and a mogrifier object, all uniquely named.
	/// <see cref="ServerWebAppFactory"/> is <see cref="SharedType.PerTestSession"/>, so every test in
	/// this class shares one database and one notification recorder.
	/// </summary>
	private sealed record Stage(
		string ChannelName,
		SharpChannel Channel,
		TestIsolationHelpers.TestPlayer Speaker,
		TestIsolationHelpers.TestPlayer Listener,
		string Mogrifier);

	private static string UniqueChannel(string prefix)
		=> TestIsolationHelpers.GenerateUniqueName(prefix).Replace("_", string.Empty);

	private async Task<SharpChannel> Channel(string name)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		await Mediator.Send(new CreateChannelCommand(MarkupText.Plain(name), ["Player"], god));
		return (await Mediator.Send(new GetChannelQuery(name)))!;
	}

	/// <summary>
	/// Members are added through the store rather than <c>@channel/on</c> so that nothing under test
	/// here can fail for want of a join privilege; the join gate has its own coverage in
	/// <c>ChannelPermissionTests</c>.
	/// </summary>
	private async Task Join(SharpChannel channel, DBRef who)
	{
		var obj = (await Mediator.Send(new GetObjectNodeQuery(who))).Expect<AnySharpObject>();
		await Mediator.Send(new AddUserToChannelCommand(channel, obj));
	}

	private async Task<TestIsolationHelpers.TestPlayer> Mortal(string prefix)
		=> await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task AsGod(string command)
		=> await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async Task As(TestIsolationHelpers.TestPlayer who, string command)
		=> await GodParser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command));

	/// <summary>Creates a thing owned by God and returns its dbref as <c>#N</c>.</summary>
	private async Task<string> Thing(string prefix)
	{
		var dbref = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, prefix);
		return $"#{dbref.Number}";
	}

	/// <summary>Builds the whole stage, optionally attaching the mogrifier to the channel.</summary>
	private async Task<Stage> Setup(string prefix, bool attachMogrifier = true)
	{
		var name = UniqueChannel(prefix);
		var channel = await Channel(name);
		var speaker = await Mortal($"{prefix}Speaker");
		var listener = await Mortal($"{prefix}Listener");
		await Join(channel, speaker.DbRef);
		await Join(channel, listener.DbRef);

		var mogrifier = await Thing($"{prefix}Mog");
		if (attachMogrifier)
		{
			await AsGod($"@channel/mogrifier {name}={mogrifier}");
		}

		return new Stage(name, (await Mediator.Send(new GetChannelQuery(name)))!, speaker, listener, mogrifier);
	}

	/// <summary>Everything <paramref name="who"/> was notified of while <paramref name="action"/> ran.</summary>
	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	private async Task<int> Buffered(SharpChannel channel)
		=> await Mediator.CreateStream(new GetChannelMessagesQuery(channel.Id ?? string.Empty, int.MaxValue))
			.CountAsync();

	// --- MOGRIFY`BLOCK, OVERRIDE, NOBUFFER: the three control callbacks -----------------------------

	/// <summary>
	/// A non-empty <c>MOGRIFY`BLOCK</c> refuses the line: its text goes back to the speaker alone, no
	/// member hears it, and nothing is buffered for recall.
	/// </summary>
	[Test]
	public async Task Mogrifier_BlockAttribute_BlocksMessage()
	{
		var stage = await Setup("MogBlock");
		await AsGod($"&MOGRIFY`BLOCK {stage.Mogrifier}=Not on my channel.");

		var before = await Buffered(stage.Channel);
		var heard = await MessagesWhile(stage.Listener.DbRef, async () =>
		{
			var refused = await MessagesWhile(stage.Speaker.DbRef,
				() => As(stage.Speaker, $"@chat {stage.ChannelName}=hello there"));
			await Assert.That(refused).Contains(x => x.Contains("Not on my channel."));
		});

		await Assert.That(heard).DoesNotContain(x => x.Contains("hello there"));
		await Assert.That(await Buffered(stage.Channel)).IsEqualTo(before);
	}

	/// <summary>
	/// <c>MOGRIFY`BLOCK</c> is handed the control arguments: %0 the chat type, %1 the channel name,
	/// %2 the raw message, %3 the speaker's name, %4 their channel title.
	/// </summary>
	[Test]
	public async Task MogrifierBlock_ReceivesControlArguments()
	{
		var stage = await Setup("MogBlockArgs");
		await AsGod($"&MOGRIFY`BLOCK {stage.Mogrifier}=ARGS:%0|%1|%2|%3");

		var refused = await MessagesWhile(stage.Speaker.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=argument check"));

		await Assert.That(refused)
			.Contains(x => x.Contains($"ARGS:\"|{stage.ChannelName}|argument check|{stage.Speaker.Name}"));
	}

	/// <summary>
	/// <c>MOGRIFY`OVERRIDE</c> suppresses each member's own <c>@chatformat</c>, which is the only thing
	/// it does — the channel line itself still goes out.
	/// </summary>
	[Test]
	public async Task Mogrifier_OverrideAttribute_SkipsChatFormat()
	{
		var stage = await Setup("MogOverride");
		await AsGod($"&CHATFORMAT {stage.Listener.DbRef}=MINE: %5");

		// Without OVERRIDE the listener's own format wins.
		var formatted = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=first"));
		await Assert.That(formatted).Contains(x => x.StartsWith("MINE: "));

		await AsGod($"&MOGRIFY`OVERRIDE {stage.Mogrifier}=1");

		var overridden = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=second"));
		await Assert.That(overridden).Contains(x => x.Contains("second"));
		await Assert.That(overridden).DoesNotContain(x => x.Contains("MINE: "));
	}

	/// <summary>
	/// A <c>MOGRIFY`OVERRIDE</c> that evaluates to one of the falsey forms is not an override:
	/// <c>IsEmpty</c> treats <c>0</c>, <c>#-1</c>, <c>false</c> and whitespace as "unset".
	/// </summary>
	[Test]
	public async Task MogrifierOverride_FalseyValue_LeavesChatFormatAlone()
	{
		var stage = await Setup("MogOverrideZero");
		await AsGod($"&CHATFORMAT {stage.Listener.DbRef}=MINE: %5");
		await AsGod($"&MOGRIFY`OVERRIDE {stage.Mogrifier}=0");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=still formatted"));

		await Assert.That(heard).Contains(x => x.StartsWith("MINE: "));
	}

	/// <summary>
	/// <c>MOGRIFY`NOBUFFER</c> keeps the line out of the recall buffer while still delivering it.
	/// </summary>
	[Test]
	public async Task Mogrifier_NoBuffer_DeliversButDoesNotRecall()
	{
		var stage = await Setup("MogNoBuffer");

		var before = await Buffered(stage.Channel);
		await As(stage.Speaker, $"@chat {stage.ChannelName}=this one is kept");
		await Assert.That(await Buffered(stage.Channel)).IsEqualTo(before + 1);

		await AsGod($"&MOGRIFY`NOBUFFER {stage.Mogrifier}=1");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=this one is not"));

		await Assert.That(heard).Contains(x => x.Contains("this one is not"));
		await Assert.That(await Buffered(stage.Channel)).IsEqualTo(before + 1);
	}

	// --- MOGRIFY`FORMAT and the message-part callbacks ----------------------------------------------

	/// <summary>
	/// <c>MOGRIFY`FORMAT</c> replaces the whole line for every member at once, and is handed the
	/// default rendering as %5.
	/// </summary>
	[Test]
	public async Task Mogrifier_FormatAttribute_CustomizesMessage()
	{
		var stage = await Setup("MogFormat");
		// %1 is the mogrified channel name and has its own case below; this one is about FORMAT
		// replacing the line at all.
		await AsGod($"&MOGRIFY`FORMAT {stage.Mogrifier}=(%3) -> %2");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=formatted please"));

		await Assert.That(heard)
			.Contains(x => x.Contains($"({stage.Speaker.Name}) -> formatted please"));
	}

	/// <summary>
	/// The format callback's %5 is the default rendering the channel would otherwise have sent, so a
	/// mogrifier can decorate rather than rebuild.
	/// </summary>
	[Test]
	public async Task MogrifierFormat_ReceivesDefaultRenderingAsFifthArgument()
	{
		var stage = await Setup("MogFormatDefault");
		await AsGod($"&MOGRIFY`FORMAT {stage.Mogrifier}=** %5 **");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=decorate me"));

		await Assert.That(heard).Contains(x =>
			x.Contains($"** <{stage.ChannelName}> {stage.Speaker.Name} says, \"decorate me\" **"));
	}

	/// <summary>
	/// The part callbacks each replace one component of the default rendering, and each is handed the
	/// value it is replacing as %0. They compose: the channel name, the speaker's name and the speech
	/// verb are all substituted into the one line.
	/// </summary>
	[Test]
	public async Task Mogrifier_PartAttributes_ModifyComponents()
	{
		var stage = await Setup("MogParts");
		await AsGod($"&MOGRIFY`CHANNAME {stage.Mogrifier}=~%0~");
		await AsGod($"&MOGRIFY`PLAYERNAME {stage.Mogrifier}=Mr. %0");
		await AsGod($"&MOGRIFY`SPEECHTEXT {stage.Mogrifier}=declaims");
		await AsGod($"&MOGRIFY`MESSAGE {stage.Mogrifier}=[ucstr(%0)]");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=quietly"));

		await Assert.That(heard).Contains(x =>
			x.Contains($"~<{stage.ChannelName}>~ Mr. {stage.Speaker.Name} declaims, \"QUIETLY\""));
	}

	/// <summary>
	/// A part callback is handed the full argument set, not just the value it is replacing: %1 the
	/// channel, %2 the chat type, %3 the message, %5 the speaker's name.
	/// </summary>
	[Test]
	public async Task MogrifierParts_ReceivePartArguments()
	{
		var stage = await Setup("MogPartArgs");
		await AsGod($"&MOGRIFY`CHANNAME {stage.Mogrifier}=%1/%2/%3/%5");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=part args"));

		await Assert.That(heard).Contains(x =>
			x.StartsWith($"{stage.ChannelName}/\"/part args/{stage.Speaker.Name}"));
	}

	/// <summary>
	/// A part callback that evaluates to nothing leaves its component alone — the default rendering
	/// survives an empty mogrifier, which is what makes a mogrifier safe to attach incrementally.
	/// </summary>
	[Test]
	public async Task MogrifierParts_EmptyResult_KeepsDefaultComponent()
	{
		var stage = await Setup("MogPartEmpty");
		await AsGod($"&MOGRIFY`PLAYERNAME {stage.Mogrifier}=");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=unchanged"));

		await Assert.That(heard).Contains(x =>
			x.Contains($"<{stage.ChannelName}> {stage.Speaker.Name} says, \"unchanged\""));
	}

	// --- Permissions --------------------------------------------------------------------------------

	/// <summary>
	/// The mogrifier answers to its own <c>@lock/use</c>: a speaker who fails it gets the unmogrified
	/// line. The speaker here is a mortal, because the fixtures otherwise run as God — handle 1 is a
	/// wizard, and every lock it is asked about passes vacuously.
	/// </summary>
	[Test]
	public async Task Mogrifier_UseLock_RequiredForMogrification()
	{
		var stage = await Setup("MogUseLock");
		await AsGod($"&MOGRIFY`FORMAT {stage.Mogrifier}=MOGRIFIED: %2");

		var mogrified = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=before the lock"));
		await Assert.That(mogrified).Contains(x => x.StartsWith("MOGRIFIED: "));

		// A use lock only the listener passes, so the speaker's own line is no longer mogrified.
		await AsGod($"@lock/use {stage.Mogrifier}={stage.Listener.DbRef}");

		var plain = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=after the lock"));
		await Assert.That(plain).DoesNotContain(x => x.StartsWith("MOGRIFIED: "));
		await Assert.That(plain).Contains(x => x.Contains($"says, \"after the lock\""));
	}

	/// <summary>
	/// A wizard-flagged mogrifier still mogrifies a mortal's line.
	///
	/// <para>PennMUSH's <c>mogrify</c> (<c>src/extchat.c:3702</c>) reaches the attribute through
	/// <c>call_attrib</c>, which fetches with <c>UFUN_IGNORE_PERMS</c> (<c>src/utils.c:410-419</c>), so
	/// the <c>@lock/use</c> is the only gate. Asking as the speaker instead does not merely skip a
	/// privileged mogrifier: <c>GetAttributeAsync</c> answers a refusal with <c>Error&lt;string&gt;</c>
	/// and <c>EvaluateAttributeFunctionResultAsync</c> hands that back as the RESULT, so the handler
	/// reads <c>#-1 NO PERMISSION TO EVALUATE ATTRIBUTE</c> as a successful mogrification.</para>
	/// </summary>
	[Test]
	public async Task Mogrifier_WizardFlaggedObject_StillMogrifiesMortalSpeech()
	{
		var stage = await Setup("MogWizObj");
		await AsGod($"@set {stage.Mogrifier}=WIZARD");
		await AsGod($"&MOGRIFY`FORMAT {stage.Mogrifier}=MOG| %2");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=privileged mogrifier"));

		await Assert.That(heard).Contains(x => x.Contains("MOG| privileged mogrifier"));
		await Assert.That(heard).DoesNotContain(x => x.Contains("#-1"));
	}

	/// <summary>
	/// The same object carrying <c>MOGRIFY`BLOCK</c> is the damaging case: read as the speaker, the
	/// permission refusal is non-empty, so every mortal line on the channel would be blocked and the
	/// speaker told <c>#-1 NO PERMISSION TO EVALUATE ATTRIBUTE</c>.
	/// </summary>
	[Test]
	public async Task Mogrifier_WizardFlaggedObject_BlockDoesNotRefuseEveryMortalLine()
	{
		var stage = await Setup("MogWizBlock");
		await AsGod($"@set {stage.Mogrifier}=WIZARD");
		await AsGod($"&MOGRIFY`BLOCK {stage.Mogrifier}=[if(strmatch(%2,forbidden*),Refused)]");

		var told = new List<string>();
		var heard = await MessagesWhile(stage.Listener.DbRef, async () =>
			told = await MessagesWhile(stage.Speaker.DbRef,
				() => As(stage.Speaker, $"@chat {stage.ChannelName}=perfectly ordinary")));

		await Assert.That(told).DoesNotContain(x => x.Contains("#-1"));
		await Assert.That(heard).Contains(x => x.Contains("perfectly ordinary"));
	}

	/// <summary>
	/// <c>MOGRIFY`OVERRIDE</c> and <c>MOGRIFY`NOBUFFER</c> go through PennMUSH's <c>parse_boolean</c>
	/// (<c>src/extchat.c:3811,3817</c>), which is false for anything beginning <c>#-</c> — "which will
	/// also cover our error messages" (<c>src/parse.c:239</c>). A mogrifier that errors must not switch
	/// them on.
	/// </summary>
	[Test]
	public async Task MogrifierOverride_ErrorString_IsNotTruthy()
	{
		var stage = await Setup("MogOverrideErr");
		await AsGod($"&CHATFORMAT {stage.Listener.DbRef}=MINE: %5");
		await AsGod($"&MOGRIFY`OVERRIDE {stage.Mogrifier}=#-1 NO SUCH THING");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=error is not true"));

		await Assert.That(heard).Contains(x => x.StartsWith("MINE: "));
	}

	/// <summary>
	/// The same gate the other way: <c>parse_boolean</c> is not "is this text empty". A non-blank,
	/// non-numeric string is TRUE, so the literal word <c>false</c> switches the override on.
	/// </summary>
	[Test]
	public async Task MogrifierOverride_WordFalse_IsTruthy()
	{
		var stage = await Setup("MogOverrideWord");
		await AsGod($"&CHATFORMAT {stage.Listener.DbRef}=MINE: %5");
		await AsGod($"&MOGRIFY`OVERRIDE {stage.Mogrifier}=false");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=a word is true"));

		await Assert.That(heard).Contains(x => x.Contains("a word is true"));
		await Assert.That(heard).DoesNotContain(x => x.StartsWith("MINE: "));
	}

	/// <summary>
	/// <c>MOGRIFY`FORMAT</c>'s %1 is the bracketed channel name AFTER <c>MOGRIFY`CHANNAME</c> has run
	/// (<c>argv[1] = channame</c>, <c>src/extchat.c:3908</c>), not the raw one. Handing it the raw name
	/// discards <c>CHANNAME</c> whenever <c>FORMAT</c> rebuilds the line.
	///
	/// <para><c>@chatformat</c>'s %1 is the raw name (<c>format.args[1] = ChanName(channel)</c>,
	/// <c>:3939</c>). The two differ on purpose, and the second half of this test pins that too.</para>
	/// </summary>
	[Test]
	public async Task MogrifierFormat_FirstArgumentIsTheMogrifiedChannelName()
	{
		var stage = await Setup("MogFormatChan");
		await AsGod($"&MOGRIFY`CHANNAME {stage.Mogrifier}=~%0~");
		await AsGod($"&MOGRIFY`FORMAT {stage.Mogrifier}=%1 %3: %2");
		await AsGod($"&CHATFORMAT {stage.Listener.DbRef}=raw=%1 line=%5");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=keep the brackets"));

		await Assert.That(heard).Contains(x => x.Contains(
			$"raw={stage.ChannelName} line=~<{stage.ChannelName}>~ {stage.Speaker.Name}: keep the brackets"));
	}

	/// <summary>
	/// The part callbacks see what the earlier ones produced: PennMUSH's <c>argv[4]</c>, <c>argv[5]</c>
	/// and <c>argv[6]</c> point at the very buffers <c>TITLE</c>, <c>PLAYERNAME</c> and
	/// <c>SPEECHTEXT</c> write into (<c>src/extchat.c:3822-3858</c>). %3 stays the raw message
	/// throughout, because <c>MESSAGE</c> is written last.
	/// </summary>
	[Test]
	public async Task MogrifierParts_SeeEarlierPartsResults()
	{
		var stage = await Setup("MogPartChain");
		await As(stage.Speaker, $"@channel/title {stage.ChannelName}=Squire");
		await AsGod($"&MOGRIFY`TITLE {stage.Mogrifier}=Sir");
		await AsGod($"&MOGRIFY`PLAYERNAME {stage.Mogrifier}=%4-%0");
		await AsGod($"&MOGRIFY`MESSAGE {stage.Mogrifier}=title=%4 name=%5 raw=%3");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=chained"));

		// PLAYERNAME saw TITLE's "Sir"; MESSAGE saw both, and still gets the raw message as %3.
		await Assert.That(heard).Contains(x => x.Contains(
			$"Sir Sir-{stage.Speaker.Name} says, \"title=Sir name=Sir-{stage.Speaker.Name} raw=chained\""));
	}

	/// <summary>
	/// <c>MOGRIFY`SPEECHTEXT</c> is gated on <c>CB_SPEECH</c> (<c>src/extchat.c:3847</c>): a pose has no
	/// speech verb to rewrite, so the callback is not consulted and %6 reaches
	/// <c>MOGRIFY`FORMAT</c> untouched.
	/// </summary>
	[Test]
	public async Task MogrifierSpeechText_NotConsultedForAPose()
	{
		var stage = await Setup("MogSpeechGate");
		await AsGod($"&MOGRIFY`SPEECHTEXT {stage.Mogrifier}=declaims");
		await AsGod($"&MOGRIFY`FORMAT {stage.Mogrifier}=verb=%6 line=%5");

		var speech = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=spoken"));
		await Assert.That(speech).Contains(x => x.Contains("verb=declaims"));

		var pose = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=:waves"));
		await Assert.That(pose).Contains(x => x.Contains("verb=says"));
		await Assert.That(pose).DoesNotContain(x => x.Contains("declaims"));
	}

	// --- Chat types ---------------------------------------------------------------------------------

	/// <summary>
	/// The chat type reaches the callbacks as %0 (control) / %2 (parts) and decides the default
	/// rendering: speech quotes and a verb, a pose spaces the message after the name, a semipose butts
	/// it up against the name.
	/// </summary>
	[Test]
	public async Task Mogrifier_ChatTypes_HandleDifferently()
	{
		var stage = await Setup("MogTypes");
		await AsGod($"&MOGRIFY`FORMAT {stage.Mogrifier}=TYPE=%0 DEFAULT=%5");

		var speech = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=hello"));
		await Assert.That(speech).Contains(x => x.Contains("TYPE=\" "));
		await Assert.That(speech).Contains(x => x.Contains($"{stage.Speaker.Name} says, \"hello\""));

		var pose = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=:waves"));
		await Assert.That(pose).Contains(x => x.Contains("TYPE=: "));
		await Assert.That(pose).Contains(x => x.Contains($"{stage.Speaker.Name} waves"));

		var semipose = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=;'s hat falls off"));
		await Assert.That(semipose).Contains(x => x.Contains("TYPE=; "));
		await Assert.That(semipose).Contains(x => x.Contains($"{stage.Speaker.Name}'s hat falls off"));

		// extchat.c:3781-3791 - CB_EMIT is "|", and CB_PRESENCE (which is what an announcement is) is
		// "@". SharpMUSH had the two the wrong way round; both render alike, so only %0 showed it.
		var emit = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@cemit {stage.ChannelName}=an emitted line"));
		await Assert.That(emit).Contains(x => x.Contains("TYPE=| "));
		await Assert.That(emit).Contains(x => x.Contains("an emitted line"));
	}

	// --- @chatformat, per member --------------------------------------------------------------------

	/// <summary>
	/// The receiving member's own <c>CHATFORMAT</c> shapes the line they see, and nobody else's.
	///
	/// <para>This is #1112. PennMUSH reads the attribute unqualified (<c>src/extchat.c:3935</c>); the
	/// handler was looking up <c>CHATFORMAT`&lt;CHANNEL&gt;</c>, so even with a working parser it asked
	/// for a name Penn never writes.</para>
	/// </summary>
	[Test]
	public async Task ChatFormat_IndividualPlayer_CustomizesFormat()
	{
		var stage = await Setup("ChatFmt", attachMogrifier: false);
		var bystander = await Mortal("ChatFmtBystander");
		await Join(stage.Channel, bystander.DbRef);

		await AsGod($"&CHATFORMAT {stage.Listener.DbRef}=(%1) %3: %2");

		var mine = new List<string>();
		var theirs = await MessagesWhile(bystander.DbRef, async () =>
			mine = await MessagesWhile(stage.Listener.DbRef,
				() => As(stage.Speaker, $"@chat {stage.ChannelName}=per player")));

		await Assert.That(mine)
			.Contains(x => x.Contains($"({stage.ChannelName}) {stage.Speaker.Name}: per player"));
		await Assert.That(theirs).Contains(x =>
			x.Contains($"<{stage.ChannelName}> {stage.Speaker.Name} says, \"per player\""));
		await Assert.That(theirs).DoesNotContain(x => x.Contains($"({stage.ChannelName})"));
	}

	/// <summary>
	/// The channel-qualified name the handler used to build is not a PennMUSH attribute: setting
	/// <c>CHATFORMAT`&lt;CHANNEL&gt;</c> and nothing else must leave the default rendering alone.
	/// Without this, a fix that merely supplied a parser would look like it worked.
	/// </summary>
	[Test]
	public async Task ChatFormat_ChannelQualifiedName_IsNotConsulted()
	{
		var stage = await Setup("ChatFmtQualified", attachMogrifier: false);
		await AsGod($"&CHATFORMAT`{stage.ChannelName.ToUpperInvariant()} {stage.Listener.DbRef}=WRONG: %5");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=unqualified only"));

		await Assert.That(heard).DoesNotContain(x => x.Contains("WRONG: "));
		await Assert.That(heard).Contains(x =>
			x.Contains($"<{stage.ChannelName}> {stage.Speaker.Name} says, \"unqualified only\""));
	}

	/// <summary>
	/// <c>CHATFORMAT</c>'s arguments, in order: %0 chat type, %1 channel, %2 message, %3 speaker,
	/// %4 title, %5 the default rendering, %6 the speech verb, %7 the send's noisiness.
	///
	/// <para>%7 is a literal, not the switch list SharpMUSH used to join into it: PennMUSH passes
	/// "silent" or "noisy" and nothing else (<c>src/extchat.c:3944-3948</c>). An ordinary <c>@chat</c>
	/// is noisy.</para>
	/// </summary>
	[Test]
	public async Task ChatFormat_ReceivesPennMUSHArguments()
	{
		var stage = await Setup("ChatFmtArgs", attachMogrifier: false);
		await AsGod($"&CHATFORMAT {stage.Listener.DbRef}=ARGS:%0|%1|%2|%3|%6|%7");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=arg order"));

		await Assert.That(heard).Contains(x =>
			x.Contains($"ARGS:\"|{stage.ChannelName}|arg order|{stage.Speaker.Name}|says|noisy"));
	}

	/// <summary>
	/// The format runs as the member who holds it (%!), with the speaker as enactor (%#) and as caller
	/// (%@) — PennMUSH's <c>call_attrib</c> shape, which is what lets a member's format say who is
	/// talking to them.
	/// </summary>
	[Test]
	public async Task ChatFormat_RunsAsTheMemberWithSpeakerAsEnactor()
	{
		var stage = await Setup("ChatFmtWho", attachMogrifier: false);
		await AsGod($"&CHATFORMAT {stage.Listener.DbRef}=me=%! them=%# caller=%@");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=who am i"));

		await Assert.That(heard).Contains(x => x.Contains(
			$"me=#{stage.Listener.DbRef.Number} them=#{stage.Speaker.DbRef.Number} caller=#{stage.Speaker.DbRef.Number}"));
	}

	/// <summary>
	/// A wizard member's <c>CHATFORMAT</c> applies to a line spoken by a mortal.
	///
	/// <para><c>checkprivs = 0</c> (<c>src/extchat.c:3935</c>): the attribute is the member's own and is
	/// read without asking whether the speaker could have evaluated it. Asking as the speaker — which
	/// is what the handler did — fails <c>CanEval</c> for any mortal against a privileged object, so
	/// every wizard on the channel would silently lose their format the moment a mortal spoke.</para>
	/// </summary>
	[Test]
	public async Task ChatFormat_WizardMember_AppliesToMortalSpeech()
	{
		var stage = await Setup("ChatFmtWiz", attachMogrifier: false);
		var wizard = await Mortal("ChatFmtWizard");
		await AsGod($"@set {wizard.DbRef}=WIZARD");
		await Join(stage.Channel, wizard.DbRef);
		await AsGod($"&CHATFORMAT {wizard.DbRef}=WIZ: %5");

		var heard = await MessagesWhile(wizard.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=mortal speech"));

		await Assert.That(heard).Contains(x => x.StartsWith("WIZ: "));
	}

	/// <summary>
	/// Mogrifier and <c>@chatformat</c> both run, in that order: the mogrifier shapes the channel-wide
	/// line, and what it produced is the %5 each member's own format then decorates. The buffered copy
	/// is the channel-wide one, not any member's.
	/// </summary>
	[Test]
	public async Task ChatFormat_WithMogrifier_BothApplied()
	{
		var stage = await Setup("ChatFmtMog");
		await AsGod($"&MOGRIFY`FORMAT {stage.Mogrifier}=MOG| %2");
		await AsGod($"&CHATFORMAT {stage.Listener.DbRef}=FMT| %5");

		var before = await Buffered(stage.Channel);
		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=both layers"));

		await Assert.That(heard).Contains(x => x.Contains("FMT| MOG| both layers"));

		var buffered = await Mediator.CreateStream(
				new GetChannelMessagesQuery(stage.Channel.Id ?? string.Empty, int.MaxValue))
			.ToListAsync();
		await Assert.That(buffered.Count).IsEqualTo(before + 1);
		await Assert.That(buffered[^1].Message.ToPlainText()).IsEqualTo("MOG| both layers");
	}
}
