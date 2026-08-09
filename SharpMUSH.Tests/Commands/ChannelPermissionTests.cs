using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Channel privileges and channel locks are enforced, not merely stored.
///
/// <para>Every case here is a live reproduction of what a mortal could do before: join a wizard-only
/// channel, join a disabled channel, join past a join lock they fail, and speak on all three. The
/// privilege and lock data were being written and displayed correctly the whole time — seven of the nine
/// channel methods on <c>IPermissionService</c> simply had no callers.</para>
///
/// <para>Semantics follow PennMUSH <c>hdrs/extchat.h:196-222</c>; refusal wording follows
/// <c>src/extchat.c</c>.</para>
/// </summary>
public class ChannelPermissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IPermissionService PermissionService => WebAppFactoryArg.Services.GetRequiredService<IPermissionService>();
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	/// <summary>A function-evaluating parser whose executor is <paramref name="executor"/>.</summary>
	private IMUSHCodeParser FunctionParserFor(DBRef executor)
		=> WebAppFactoryArg.FunctionParserFor(executor);

	/// <summary>
	/// Runs <paramref name="action"/> and returns every message the notify mock was asked to send to
	/// <paramref name="who"/> while it ran, as plain text. Used to prove two refusals are worded
	/// identically, and that a bulk switch never names a channel.
	///
	/// <para>It snapshots a call index rather than calling <c>ClearReceivedCalls</c>. The notify mock is
	/// shared for the whole session and TUnit runs tests in parallel, so clearing it deletes calls other
	/// tests are about to assert on — it made <c>MailCommand</c>, <c>Flag_Delete_RemovesNonSystemFlag</c>
	/// and <c>Test_Respond_StatusLine_TooLong</c> fail at random. Filtering by recipient is what actually
	/// isolates these tests: every one of them creates a uniquely named player.</para>
	/// </summary>
	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var before = NotifyService.ReceivedCalls().Count();
		await action();
		return NotifiedMessagesFrom(who, before);
	}

	private List<string> NotifiedMessagesFrom(DBRef who, int skip)
		=> NotifyService.ReceivedCalls().Skip(skip).ToList()
			.Where(call => call.GetMethodInfo().Name == nameof(INotifyService.Notify))
			.Select(call => call.GetArguments())
			.Where(args => args.Length > 1 && args[0] switch
			{
				AnySharpObject o => o.Object().DBRef.Number == who.Number,
				DBRef d => d.Number == who.Number,
				_ => false
			})
			.Select(args => args[1] switch
			{
				OneOf<MString, string> m => m.Match(ms => ms.ToPlainText(), str => str),
				MString ms => ms.ToPlainText(),
				string str => str,
				_ => string.Empty
			})
			.ToList();

	/// <summary>
	/// Channel names must be unique across the whole session — <see cref="ServerWebAppFactory"/> is
	/// <see cref="SharedType.PerTestSession"/>, so every test in this class shares one database, and
	/// several of these tests create channels in a loop. A bare random suffix collides often enough to
	/// matter; <c>GenerateUniqueName</c> adds a millisecond timestamp. Its underscores are stripped only
	/// to keep the names readable in refusal messages — <c>IsValidChannelName</c> accepts them.
	/// </summary>
	private static string UniqueChannel(string prefix)
		=> TestIsolationHelpers.GenerateUniqueName(prefix).Replace("_", string.Empty);

	/// <summary>Creates a channel owned by God with exactly the given privileges.</summary>
	private async Task<SharpChannel> CreateChannel(string name, params string[] privileges)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).AsPlayer;
		await Mediator.Send(new CreateChannelCommand(MModule.single(name), privileges, god));
		return (await Mediator.Send(new GetChannelQuery(name)))!;
	}

	private async Task<bool> IsMember(string channelName, DBRef who)
	{
		var channel = await Mediator.Send(new GetChannelQuery(channelName));
		return channel is not null
					 && await channel.Members.Value.AnyAsync(x => x.Member.Object().DBRef.Number == who.Number);
	}

	private async Task<TestIsolationHelpers.TestPlayer> CreateMortal(string prefix)
		=> await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task<TestIsolationHelpers.TestPlayer> CreateFlagged(string prefix, string flag)
	{
		var player = await CreateMortal(prefix);
		await GodParser.CommandParse(1, ConnectionService, MModule.single($"@set {player.DbRef}={flag}"));
		return player;
	}

	private async Task Run(TestIsolationHelpers.TestPlayer who, string command)
		=> await GodParser.CommandParse(who.Handle, ConnectionService, MModule.single(command));

	private async Task<int> MessageCount(string channelName)
	{
		var channel = await Mediator.Send(new GetChannelQuery(channelName));
		return await Mediator.CreateStream(new GetChannelMessagesQuery(channel!.Id ?? string.Empty, int.MaxValue))
			.CountAsync();
	}

	// --- Chan_Can: the WIZARD privilege (extchat.h:198) ---------------------------------------------

	[Test]
	public async Task WizardChannel_RefusesMortalAndAdmitsWizard()
	{
		var name = UniqueChannel("WizOnly");
		await CreateChannel(name, "Player", "Wizard");

		var mortal = await CreateMortal("ChanPermMortal");
		var wizard = await CreateFlagged("ChanPermWiz", "WIZARD");

		await Run(mortal, $"@channel/on {name}");
		await Assert.That(await IsMember(name, mortal.DbRef)).IsFalse();

		await Run(wizard, $"@channel/on {name}");
		await Assert.That(await IsMember(name, wizard.DbRef)).IsTrue();
	}

	[Test]
	public async Task WizardChannel_RefusesMortalSpeech()
	{
		var name = UniqueChannel("WizSpeak");
		var channel = await CreateChannel(name, "Player", "Wizard");

		var mortal = await CreateMortal("ChanPermSpeaker");
		// Put the mortal on the channel behind the gate's back, so the refusal under test is the SPEAK
		// gate and not merely "you are not on that channel".
		var mortalObject = (await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known;
		await Mediator.Send(new AddUserToChannelCommand(channel, mortalObject));

		var before = await MessageCount(name);
		await Run(mortal, $"@chat {name}=I should not be able to say this.");

		await Assert.That(await MessageCount(name)).IsEqualTo(before);
	}

	// --- Chan_Can: the ADMIN privilege (extchat.h:198) ----------------------------------------------

	[Test]
	public async Task AdminChannel_RefusesMortalAndAdmitsRoyalty()
	{
		var name = UniqueChannel("AdminOnly");
		await CreateChannel(name, "Player", "Admin");

		var mortal = await CreateMortal("ChanPermAdminMortal");
		var royalty = await CreateFlagged("ChanPermRoyal", "ROYALTY");

		await Run(mortal, $"@channel/on {name}");
		await Assert.That(await IsMember(name, mortal.DbRef)).IsFalse();

		await Run(royalty, $"@channel/on {name}");
		await Assert.That(await IsMember(name, royalty.DbRef)).IsTrue();
	}

	[Test]
	public async Task AdminChannel_AdmitsChatPrivsPower()
	{
		var name = UniqueChannel("AdminPower");
		await CreateChannel(name, "Player", "Admin");

		var player = await CreateMortal("ChanPermChatPrivs");
		var chatPrivs = await Mediator.Send(new GetPowerQuery("Chat_Privs"));
		await Assert.That(chatPrivs).IsNotNull();

		var playerObject = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known;
		await Mediator.Send(new SetObjectPowerCommand(playerObject, chatPrivs!));

		await Run(player, $"@channel/on {name}");
		await Assert.That(await IsMember(name, player.DbRef)).IsTrue();
	}

	// --- Chan_Can: the DISABLED privilege (extchat.h:198) -------------------------------------------

	[Test]
	public async Task DisabledChannel_RefusesMortalJoin()
	{
		var name = UniqueChannel("Deaded");
		await CreateChannel(name, "Player", "Disabled");

		var mortal = await CreateMortal("ChanPermDisabled");

		await Run(mortal, $"@channel/on {name}");
		await Assert.That(await IsMember(name, mortal.DbRef)).IsFalse();
	}

	/// <summary>
	/// The DISABLED bit lives inside <c>Chan_Can</c>, so it gates speech for everyone — <c>do_chat</c>
	/// has no wizard override (<c>src/extchat.c:1539</c>). Note that JOINING a disabled channel is
	/// different: <c>src/extchat.c:1353-1362</c> lets a wizard through with a warning.
	/// </summary>
	[Test]
	public async Task DisabledChannel_RefusesWizardSpeech()
	{
		var name = UniqueChannel("DeadSpeak");
		var channel = await CreateChannel(name, "Player", "Disabled");

		var wizard = await CreateFlagged("ChanPermDeadWiz", "WIZARD");
		var wizardObject = (await Mediator.Send(new GetObjectNodeQuery(wizard.DbRef))).Known;
		await Mediator.Send(new AddUserToChannelCommand(channel, wizardObject));

		var before = await MessageCount(name);
		await Run(wizard, $"@chat {name}=Wizards do not get to talk on a dead channel.");

		await Assert.That(await MessageCount(name)).IsEqualTo(before);
	}

	// --- eval_chan_lock: CLOCK_JOIN (extchat.h:204) -------------------------------------------------

	[Test]
	public async Task JoinLock_RefusesFailingPlayerAndAdmitsPassingPlayer()
	{
		var name = UniqueChannel("Locked");
		await CreateChannel(name, "Player");

		var passing = await CreateMortal("ChanPermJoinPass");
		var failing = await CreateMortal("ChanPermJoinFail");

		await GodParser.CommandParse(1, ConnectionService,
			MModule.single($"@clock/join {name}=#{passing.DbRef.Number}"));

		await Run(failing, $"@channel/on {name}");
		await Assert.That(await IsMember(name, failing.DbRef)).IsFalse();

		await Run(passing, $"@channel/on {name}");
		await Assert.That(await IsMember(name, passing.DbRef)).IsTrue();
	}

	// --- eval_chan_lock: CLOCK_SPEAK (extchat.h:206) ------------------------------------------------

	[Test]
	public async Task SpeakLock_RefusesFailingPlayerAndAdmitsPassingPlayer()
	{
		var name = UniqueChannel("SpeakLk");
		var channel = await CreateChannel(name, "Player");

		var passing = await CreateMortal("ChanPermSpeakPass");
		var failing = await CreateMortal("ChanPermSpeakFail");

		foreach (var player in new[] { passing, failing })
		{
			var obj = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known;
			await Mediator.Send(new AddUserToChannelCommand(channel, obj));
		}

		await GodParser.CommandParse(1, ConnectionService,
			MModule.single($"@clock/speak {name}=#{passing.DbRef.Number}"));

		var before = await MessageCount(name);

		await Run(failing, $"@chat {name}=Refused.");
		await Assert.That(await MessageCount(name)).IsEqualTo(before);

		await Run(passing, $"@chat {name}=Allowed.");
		await Assert.That(await MessageCount(name)).IsEqualTo(before + 1);
	}

	/// <summary>
	/// PennMUSH checks LOUD at the call site rather than inside <c>Chan_Can_Speak</c>
	/// (<c>src/extchat.c:1539</c>): "LOUD objects bypass all speech, channel speech, and interaction
	/// @locks" (<c>hlp/pennflag.hlp:256</c>). The flag was seeded in every provider's migration and
	/// consulted nowhere.
	/// </summary>
	[Test]
	public async Task LoudPlayer_BypassesSpeakLock()
	{
		var name = UniqueChannel("LoudLk");
		var channel = await CreateChannel(name, "Player");

		var loud = await CreateFlagged("ChanPermLoud", "LOUD");
		var loudObject = (await Mediator.Send(new GetObjectNodeQuery(loud.DbRef))).Known;
		await Mediator.Send(new AddUserToChannelCommand(channel, loudObject));

		// A lock nobody passes: the channel owner is #1, and the speaker is not.
		await GodParser.CommandParse(1, ConnectionService, MModule.single($"@clock/speak {name}=#1"));

		var before = await MessageCount(name);
		await Run(loud, $"@chat {name}=LOUD speaks anyway.");

		await Assert.That(await MessageCount(name)).IsEqualTo(before + 1);
	}

	// --- Chan_Can_Cemit: the NOCEMIT privilege (extchat.h:208) --------------------------------------

	[Test]
	public async Task NoCemitChannel_RefusesCemitFromSomeoneWhoMaySpeak()
	{
		var name = UniqueChannel("NoCem");
		var channel = await CreateChannel(name, "Player", "NoCemit");

		var player = await CreateMortal("ChanPermNoCemit");
		var playerObject = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known;
		await Mediator.Send(new AddUserToChannelCommand(channel, playerObject));

		// The speak gate lets them through — only the cemit gate should not.
		await Assert.That(await PermissionService.ChannelCanSpeak(playerObject, channel)).IsTrue();
		await Assert.That(await PermissionService.ChannelCanCemit(playerObject, channel)).IsFalse();

		var before = await MessageCount(name);
		await Run(player, $"@cemit {name}=Refused.");
		await Assert.That(await MessageCount(name)).IsEqualTo(before);

		await Run(player, $"@chat {name}=Allowed.");
		await Assert.That(await MessageCount(name)).IsEqualTo(before + 1);
	}

	// --- Chan_Ok_Type (extchat.h:196) ---------------------------------------------------------------

	[Test]
	public async Task PlayerOnlyChannel_RefusesAThing()
	{
		var name = UniqueChannel("PlrOnly");
		var channel = await CreateChannel(name, "Player");

		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, "ChanPermThing");
		var thing = (await Mediator.Send(new GetObjectNodeQuery(thingDbRef))).Known;

		await Assert.That(PermissionService.ChannelOkType(thing, channel)).IsFalse();

		var objectChannelName = UniqueChannel("ObjOk");
		var objectChannel = await CreateChannel(objectChannelName, "Player", "Object");
		await Assert.That(PermissionService.ChannelOkType(thing, objectChannel)).IsTrue();
	}

	// --- Chan_Can_Hide and the Hide_Ok privilege (extchat.h:216) ------------------------------------

	/// <summary>
	/// <c>ChannelCanHide</c> tested <c>Privs.Contains("CanHide")</c>. The privilege is spelled
	/// <c>Hide_Ok</c> in <c>chan_privs</c> and is what <c>@channel/add</c> writes, so the branch could
	/// never be true — invisible only because nothing called the method.
	/// </summary>
	[Test]
	public async Task HideOkChannel_PermitsHidingAndPlainChannelDoesNot()
	{
		var hideOkName = UniqueChannel("HideOk");
		var hideOk = await CreateChannel(hideOkName, "Player", "Hide_Ok");
		var plainName = UniqueChannel("NoHide");
		var plain = await CreateChannel(plainName, "Player");

		var player = await CreateMortal("ChanPermHide");
		var playerObject = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known;
		await Mediator.Send(new AddUserToChannelCommand(hideOk, playerObject));
		await Mediator.Send(new AddUserToChannelCommand(plain, playerObject));

		await Assert.That(await PermissionService.ChannelCanHide(playerObject, hideOk)).IsTrue();
		await Assert.That(await PermissionService.ChannelCanHide(playerObject, plain)).IsFalse();
	}

	// --- Privilege names are stored canonically, and compared case-insensitively --------------------

	/// <summary>
	/// <c>@channel/add Foo=wizard</c> persisted the literal <c>"wizard"</c>, and every check read
	/// <c>Privs.Contains("Wizard")</c> — an ordinal comparison that answered "no wizard privilege" for a
	/// channel whose <c>@channel/what</c> plainly said <c>Flags: wizard</c>.
	/// </summary>
	[Test]
	public async Task ChannelAddStoresPrivilegesInCanonicalCasing()
	{
		var name = UniqueChannel("Canon");
		await GodParser.CommandParse(1, ConnectionService, MModule.single($"@channel/add {name}=wizard player"));

		var channel = await Mediator.Send(new GetChannelQuery(name));

		await Assert.That(channel).IsNotNull();
		await Assert.That(channel!.Privs).Contains("Wizard");
		await Assert.That(channel.Privs).Contains("Player");
	}

	/// <summary>
	/// PennMUSH <c>string_to_privs</c> (<c>src/privtab.c:36</c>) applies the list to the channel's
	/// existing privileges and honours <c>!priv</c>; <c>@channel/privs</c> replaced the whole set and had
	/// no negation, so setting one flag silently dropped every other.
	/// </summary>
	[Test]
	public async Task ChannelPrivsAddsToExistingPrivilegesAndNegates()
	{
		var name = UniqueChannel("PrivOr");
		await CreateChannel(name, "Player", "Quiet");

		await GodParser.CommandParse(1, ConnectionService, MModule.single($"@channel/privs {name}=Wizard"));

		var afterAdd = await Mediator.Send(new GetChannelQuery(name));
		await Assert.That(afterAdd!.Privs).Contains("Player");
		await Assert.That(afterAdd.Privs).Contains("Quiet");
		await Assert.That(afterAdd.Privs).Contains("Wizard");

		await GodParser.CommandParse(1, ConnectionService, MModule.single($"@channel/privs {name}=!Wizard"));

		var afterRemove = await Mediator.Send(new GetChannelQuery(name));
		await Assert.That(afterRemove!.Privs).Contains("Player");
		await Assert.That(afterRemove.Privs).DoesNotContain("Wizard");
	}

	// --- The live reproduction from the bug report, end to end --------------------------------------

	/// <summary>
	/// The reported reproduction, exactly: as God, create a wizard channel, a disabled channel and a
	/// join-locked one; then as a plain player join all three and speak on all three. Before this change
	/// every one of the six operations succeeded.
	/// </summary>
	[Test]
	public async Task MortalCannotJoinOrSpeakOnAnyGatedChannel()
	{
		var wizOnly = UniqueChannel("ReproWiz");
		var deaded = UniqueChannel("ReproDead");
		var locked = UniqueChannel("ReproLock");

		await GodParser.CommandParse(1, ConnectionService, MModule.single($"@channel/add {wizOnly}=wizard player"));
		await GodParser.CommandParse(1, ConnectionService, MModule.single($"@channel/add {locked}=player"));
		await GodParser.CommandParse(1, ConnectionService, MModule.single($"@clock/join {locked}=#1"));

		// PennMUSH refuses `@channel/add <name>=disabled` outright — Chan_Can is false for a disabled type
		// for everyone, wizards included (extchat.c:1736). A wizard reaches the same state through
		// @channel/privs, where Chan_Can_Priv's `Wizard(p) ||` escape applies (extchat.c:1832).
		await GodParser.CommandParse(1, ConnectionService, MModule.single($"@channel/add {deaded}=player"));
		await GodParser.CommandParse(1, ConnectionService, MModule.single($"@channel/privs {deaded}=disabled"));

		var mortimer = await CreateMortal("Mortimer");

		foreach (var name in new[] { wizOnly, deaded, locked })
		{
			await Run(mortimer, $"@channel/on {name}");
			await Assert.That(await IsMember(name, mortimer.DbRef)).IsFalse();

			var before = await MessageCount(name);
			await Run(mortimer, $"@chat {name}=Mortimer speaks.");
			await Assert.That(await MessageCount(name)).IsEqualTo(before);
		}
	}

	// --- Enumeration oracles: a refusal must not tell a caller what it is refusing ------------------

	/// <summary>
	/// A channel that does not exist and a channel that exists but is invisible to the caller must be
	/// indistinguishable — in the notification AND in the return value softcode reads. A caller who can
	/// tell them apart can enumerate every channel on the game by name.
	///
	/// <para>PennMUSH answers both with "CHAT: I don't recognize that channel." and
	/// <c>#-1 NO SUCH CHANNEL</c>: the missing case in <c>test_channel_fun</c> (<c>hdrs/extchat.h:161</c>)
	/// and the invisible case at every <c>Chan_Can_See</c> refusal in <c>src/extchat.c</c>. This
	/// repository made the same call for scenes in PR #750 (<c>SceneHub.cs:52-57</c>).</para>
	/// </summary>
	[Test]
	public async Task MissingAndInvisibleChannelsAreIndistinguishable()
	{
		var invisible = UniqueChannel("OracleHidden");
		await CreateChannel(invisible, "Player", "Wizard");
		var missing = UniqueChannel("OracleMissing");

		var mortal = await CreateMortal("ChanPermOracle");
		var parser = FunctionParserFor(mortal.DbRef);

		// The function surface is where the return value is observable to a mortal's softcode.
		var invisibleResult = await parser.FunctionParse(MModule.single($"cwho({invisible})"));
		var missingResult = await parser.FunctionParse(MModule.single($"cwho({missing})"));

		await Assert.That(invisibleResult?.Message?.ToPlainText())
			.IsEqualTo(missingResult?.Message?.ToPlainText());
		await Assert.That(invisibleResult?.Message?.ToPlainText())
			.IsEqualTo(ErrorMessages.Returns.NoSuchChannel);

		// And the same for the notification the command surface emits.
		var afterInvisible = await MessagesWhile(mortal.DbRef, () => Run(mortal, $"@channel/on {invisible}"));
		var afterMissing = await MessagesWhile(mortal.DbRef, () => Run(mortal, $"@channel/on {missing}"));

		await Assert.That(afterInvisible).IsEquivalentTo(afterMissing);
		await Assert.That(afterInvisible).Contains(ErrorMessages.Notifications.DontRecognizeThatChannel);
		await Assert.That(afterInvisible).DoesNotContain("Channel not found.");
	}

	/// <summary>
	/// <c>cwho()</c> is reachable from mortal softcode, so an ungated one is the <c>scenelist()</c> hole
	/// from PR #763 again: it returned every member's dbref on any channel, named or not.
	/// </summary>
	[Test]
	public async Task CwhoOnInvisibleChannelReturnsNothingUseful()
	{
		var name = UniqueChannel("CwhoHidden");
		var channel = await CreateChannel(name, "Player", "Wizard");

		var wizard = await CreateFlagged("ChanPermCwhoWiz", "WIZARD");
		var wizardObject = (await Mediator.Send(new GetObjectNodeQuery(wizard.DbRef))).Known;
		await Mediator.Send(new AddUserToChannelCommand(channel, wizardObject));

		var mortal = await CreateMortal("ChanPermCwho");

		var mortalResult = await FunctionParserFor(mortal.DbRef).FunctionParse(MModule.single($"cwho({name})"));
		await Assert.That(mortalResult?.Message?.ToPlainText()).IsEqualTo(ErrorMessages.Returns.NoSuchChannel);
		await Assert.That(mortalResult?.Message?.ToPlainText()).DoesNotContain($"#{wizard.DbRef.Number}");

		// The wizard, who may see it, still gets the member list.
		var wizardResult = await FunctionParserFor(wizard.DbRef).FunctionParse(MModule.single($"cwho({name})"));
		await Assert.That(wizardResult?.Message?.ToPlainText()).Contains($"#{wizard.DbRef.Number}");
	}

	/// <summary>
	/// <c>cowner()</c> and <c>cmogrifier()</c> looked a channel up without any visibility check. I had
	/// claimed that matched PennMUSH; re-auditing showed it does not — Penn puts the gate inside
	/// <c>find_channel</c> itself (<c>src/extchat.c:959</c>), so every channel function is gated there and
	/// none of them is exempt.
	/// </summary>
	[Test]
	public async Task ChannelReadingFunctionsAreGatedOnVisibility()
	{
		var name = UniqueChannel("FnHidden");
		await CreateChannel(name, "Player", "Wizard");

		var mortal = await CreateMortal("ChanPermFnRead");
		var parser = FunctionParserFor(mortal.DbRef);

		foreach (var call in new[] { $"cowner({name})", $"cmogrifier({name})", $"cwho({name})" })
		{
			var result = await parser.FunctionParse(MModule.single(call));
			await Assert.That(result?.Message?.ToPlainText()).IsEqualTo(ErrorMessages.Returns.NoSuchChannel);
		}
	}

	/// <summary>
	/// <c>channels(&lt;object&gt;)</c> judged visibility against the OBJECT rather than the caller, so
	/// naming a wizard read back that wizard's wizard-only channels; and its <c>on</c>/<c>off</c> arms
	/// applied no visibility rule at all. PennMUSH <c>fun_channels</c> (<c>src/extchat.c:3313</c>) judges
	/// against the executor and additionally hides members flagged hidden from non-Priv_Who callers.
	/// </summary>
	[Test]
	public async Task ChannelsFunctionJudgesVisibilityAgainstTheCaller()
	{
		var name = UniqueChannel("ChansHidden");
		var channel = await CreateChannel(name, "Player", "Wizard");

		var wizard = await CreateFlagged("ChanPermChansWiz", "WIZARD");
		var wizardObject = (await Mediator.Send(new GetObjectNodeQuery(wizard.DbRef))).Known;
		await Mediator.Send(new AddUserToChannelCommand(channel, wizardObject));

		var mortal = await CreateMortal("ChanPermChans");
		var parser = FunctionParserFor(mortal.DbRef);

		var named = await parser.FunctionParse(MModule.single($"channels(#{wizard.DbRef.Number})"));
		await Assert.That(named?.Message?.ToPlainText()).DoesNotContain(name);

		var off = await parser.FunctionParse(MModule.single("channels(me,off)"));
		await Assert.That(off?.Message?.ToPlainText()).DoesNotContain(name);

		var all = await parser.FunctionParse(MModule.single("channels()"));
		await Assert.That(all?.Message?.ToPlainText()).DoesNotContain(name);
	}

	/// <summary>
	/// With no channel named, <c>@channel/hide</c>, <c>/gag</c> and <c>/combine</c> walk every channel from
	/// <c>GetChannelListQuery</c> and notify per channel, routing around
	/// <see cref="ChannelHelper.GetVisibleChannelOrError"/> — so the argument-less form named exactly the
	/// channels that gate exists to hide.
	///
	/// <para>These call the handlers directly on purpose. <c>@CHANNEL</c>'s dispatcher matches
	/// <c>["HIDE"] when arg0 is not null</c> (GeneralCommands.cs:5036-5043), so the bulk path is
	/// unreachable from the command surface today and no player can currently trigger the leak. The
	/// handlers implement it regardless, and whoever wires the argument-less form — PennMUSH has it —
	/// must not reintroduce the oracle. Testing through the dispatcher would assert nothing.</para>
	/// </summary>
	[Test]
	[Arguments("hide")]
	[Arguments("gag")]
	[Arguments("combine")]
	public async Task BulkSwitchesDoNotNameChannelsTheExecutorCannotSee(string switchName)
	{
		var name = UniqueChannel($"Bulk{switchName}");
		await CreateChannel(name, "Player", "Wizard");

		var mortal = await CreateMortal($"ChanPermBulk{switchName}");
		var parser = WebAppFactoryArg.CommandParserFor(mortal.DbRef, mortal.Handle);
		var locate = WebAppFactoryArg.Services.GetRequiredService<ILocateService>();

		var messages = await MessagesWhile(mortal.DbRef, async () =>
		{
			_ = switchName switch
			{
				"hide" => await ChannelHide.Handle(parser, locate, PermissionService, Mediator, NotifyService, null, null),
				"gag" => await ChannelGag.Handle(parser, locate, PermissionService, Mediator, NotifyService, null, null, []),
				_ => await ChannelCombine.Handle(parser, locate, PermissionService, Mediator, NotifyService, null, null)
			};
		});

		foreach (var message in messages)
		{
			await Assert.That(message).DoesNotContain(name);
		}
	}

	// --- Third-party status writes need the channel's modify right ---------------------------------

	/// <summary>
	/// <c>@channel/mute</c> writes the status of a player the executor NAMES. Before this stack it wrote
	/// against the executor instead, which made it a harmless self-mute; correcting the target without
	/// adding authorization would have turned a no-op bug into "any non-guest may silence any member of
	/// any channel", which is worse than the bug.
	///
	/// <para><c>/gag</c>, <c>/hide</c>, <c>/combine</c> and <c>/title</c> only ever write the executor's
	/// own status and correctly require no modify right; <c>/hide</c> has its own <c>Chan_Can_Hide</c>
	/// gate.</para>
	/// </summary>
	[Test]
	public async Task MuteRequiresModifyRightsOnTheChannel()
	{
		var name = UniqueChannel("MuteGate");
		var channel = await CreateChannel(name, "Player");

		var victim = await CreateMortal("ChanPermMuteVictim");
		var meddler = await CreateMortal("ChanPermMuteMeddler");

		foreach (var player in new[] { victim, meddler })
		{
			var obj = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known;
			await Mediator.Send(new AddUserToChannelCommand(channel, obj));
		}

		var victimObject = (await Mediator.Send(new GetObjectNodeQuery(victim.DbRef))).Known;
		// @channel/mute resolves its target by name, not dbref.
		var victimName = victimObject.Object().Name;

		await Assert.That(await PermissionService.ChannelCanModifyAsync(
			(await Mediator.Send(new GetObjectNodeQuery(meddler.DbRef))).Known, channel)).IsFalse();

		await Run(meddler, $"@channel/mute {name}={victimName}");

		var afterMeddler = await ChannelHelper.ChannelMemberStatus(victimObject,
			(await Mediator.Send(new GetChannelQuery(name)))!);
		await Assert.That(afterMeddler!.Status.Mute ?? false).IsFalse();

		// God owns the channel, so the same command from God does mute.
		await GodParser.CommandParse(1, ConnectionService, MModule.single($"@channel/mute {name}={victimName}"));

		var afterOwner = await ChannelHelper.ChannelMemberStatus(victimObject,
			(await Mediator.Send(new GetChannelQuery(name)))!);
		await Assert.That(afterOwner!.Status.Mute ?? false).IsTrue();
	}

	// --- The oracle on the lock and admin surfaces -------------------------------------------------

	/// <summary>
	/// The lock surface is where Copilot could see the problem, and it is the worst place for it because
	/// <c>clock()</c> hands a return value to mortal softcode. Resolving through the raw lookup answered
	/// <c>#-1 NO SUCH CHANNEL</c> for a missing channel and let an invisible one through to
	/// <c>Chan_Can_Decomp</c>, which answers <c>#-1 PERMISSION DENIED</c> — so the lock stayed secret
	/// while the channel's existence did not.
	/// </summary>
	[Test]
	public async Task ClockFunctionDoesNotDistinguishHiddenFromMissing()
	{
		var hidden = UniqueChannel("ClockHidden");
		await CreateChannel(hidden, "Player", "Wizard");
		var missing = UniqueChannel("ClockMissing");

		await GodParser.CommandParse(1, ConnectionService, MModule.single($"@clock/join {hidden}=#1"));

		var mortal = await CreateMortal("ChanPermClockFn");
		var parser = FunctionParserFor(mortal.DbRef);

		var hiddenResult = await parser.FunctionParse(MModule.single($"clock({hidden})"));
		var missingResult = await parser.FunctionParse(MModule.single($"clock({missing})"));

		await Assert.That(hiddenResult?.Message?.ToPlainText())
			.IsEqualTo(missingResult?.Message?.ToPlainText());
		await Assert.That(hiddenResult?.Message?.ToPlainText()).IsEqualTo(ErrorMessages.Returns.NoSuchChannel);
		// And the lock key itself never appears.
		await Assert.That(hiddenResult?.Message?.ToPlainText()).DoesNotContain("#1");
	}

	/// <summary>
	/// The same oracle on <c>@clock</c>, the command half Copilot flagged.
	/// </summary>
	[Test]
	public async Task ClockCommandDoesNotDistinguishHiddenFromMissing()
	{
		var hidden = UniqueChannel("ClockCmdHidden");
		await CreateChannel(hidden, "Player", "Wizard");
		var missing = UniqueChannel("ClockCmdMissing");

		var mortal = await CreateMortal("ChanPermClockCmd");

		var afterHidden = await MessagesWhile(mortal.DbRef,
			() => Run(mortal, $"@clock/join {hidden}=#{mortal.DbRef.Number}"));
		var afterMissing = await MessagesWhile(mortal.DbRef,
			() => Run(mortal, $"@clock/join {missing}=#{mortal.DbRef.Number}"));

		await Assert.That(afterHidden).IsEquivalentTo(afterMissing);

		// The lock must not have been set either.
		// A channel that was never given a lock reads back null, not "".
		var channel = await Mediator.Send(new GetChannelQuery(hidden));
		await Assert.That(string.IsNullOrEmpty(channel!.JoinLock)).IsTrue();
	}

	/// <summary>
	/// <c>@clock</c> rewrote <c>Chan_Can_Modify</c> by hand, and carried the same defect the service did:
	/// <c>string.IsNullOrEmpty(channel.ModLock) || Evaluate(...)</c> is TRUE for every channel, because
	/// <c>CreateChannelCommand</c> never writes a ModLock. Any non-guest could set the join, speak, see,
	/// hide or mod lock on any channel they could see.
	/// </summary>
	[Test]
	public async Task ClockRequiresModifyRightsOnTheChannel()
	{
		var name = UniqueChannel("ClockGate");
		var channel = await CreateChannel(name, "Player");

		var meddler = await CreateMortal("ChanPermClockMeddler");
		var meddlerObject = (await Mediator.Send(new GetObjectNodeQuery(meddler.DbRef))).Known;
		await Mediator.Send(new AddUserToChannelCommand(channel, meddlerObject));

		await Assert.That(await PermissionService.ChannelCanModifyAsync(meddlerObject, channel)).IsFalse();

		await Run(meddler, $"@clock/join {name}=#{meddler.DbRef.Number}");
		await Assert.That(string.IsNullOrEmpty(
			(await Mediator.Send(new GetChannelQuery(name)))!.JoinLock)).IsTrue();

		// God owns the channel, so the same command from God does set the lock.
		await GodParser.CommandParse(1, ConnectionService, MModule.single($"@clock/join {name}=#1"));
		await Assert.That(string.IsNullOrEmpty(
			(await Mediator.Send(new GetChannelQuery(name)))!.JoinLock)).IsFalse();
	}

	/// <summary>
	/// The whole sweep in one assertion. Every channel-admin switch resolved through the raw lookup, so
	/// "Channel not found." came from the lookup and "You cannot modify this channel." came from the
	/// authorization gate — the authorization was correct and the oracle was wide open anyway. This is the
	/// subtle half of the sweep and the reason it could not be done by inspecting the gates alone.
	/// </summary>
	[Test]
	[Arguments("decompile", "")]
	[Arguments("wipe", "")]
	[Arguments("delete", "")]
	[Arguments("recall", "")]
	[Arguments("describe", "=a description")]
	[Arguments("buffer", "=50")]
	[Arguments("privs", "=quiet")]
	[Arguments("title", "=a title")]
	[Arguments("off", "")]
	public async Task AdminSwitchesDoNotDistinguishHiddenFromMissing(string switchName, string rhs)
	{
		var hidden = UniqueChannel($"Adm{switchName}H");
		await CreateChannel(hidden, "Player", "Wizard");
		var missing = UniqueChannel($"Adm{switchName}M");

		var mortal = await CreateMortal($"ChanPermAdm{switchName}");

		var afterHidden = await MessagesWhile(mortal.DbRef,
			() => Run(mortal, $"@channel/{switchName} {hidden}{rhs}"));
		var afterMissing = await MessagesWhile(mortal.DbRef,
			() => Run(mortal, $"@channel/{switchName} {missing}{rhs}"));

		await Assert.That(afterHidden).IsEquivalentTo(afterMissing);
		await Assert.That(afterHidden).Contains(ErrorMessages.Notifications.DontRecognizeThatChannel);

		// The hidden channel must still be there — /delete and /wipe are in this list.
		await Assert.That(await Mediator.Send(new GetChannelQuery(hidden))).IsNotNull();
	}
}
