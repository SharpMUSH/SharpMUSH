using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
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
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private static string UniqueChannel(string prefix)
		=> $"{prefix}{Random.Shared.Next(100000, 999999)}";

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
}
