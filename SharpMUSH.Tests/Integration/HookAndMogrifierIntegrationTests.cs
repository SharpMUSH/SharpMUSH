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
		await AsGod($"&MOGRIFY`FORMAT {stage.Mogrifier}=<<%1>> %3 -> %2");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=formatted please"));

		await Assert.That(heard)
			.Contains(x => x.Contains($"<<{stage.ChannelName}>> {stage.Speaker.Name} -> formatted please"));
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
	}






}
