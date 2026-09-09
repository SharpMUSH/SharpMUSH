using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Two things every other MUSH does that this one did not: a channel name may be abbreviated, and
/// <c>@channel/recall</c> replays a conversation forwards inside a header and a footer.
///
/// <para>PennMUSH's matcher is <c>find_channel</c> and its two scoped variants
/// (<c>src/extchat.c:943-1160</c>); its recall is <c>do_chan_recall</c> (<c>:3990-4098</c>).</para>
/// </summary>
public class ChannelMatchRecallTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private static string UniqueChannel(string prefix)
		=> TestIsolationHelpers.GenerateUniqueName(prefix).Replace("_", string.Empty);

	private async Task<SharpChannel> CreateChannel(string name, params string[] privileges)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).AsPlayer;
		await Mediator.Send(new CreateChannelCommand(MarkupText.Plain(name), privileges, god));
		return (await Mediator.Send(new GetChannelQuery(name)))!;
	}

	private async Task<TestIsolationHelpers.TestPlayer> CreateMortal(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

		// Same reason as ChannelPermissionTests.CreateMortal: every test player otherwise starts in the
		// shared DefaultHome, where another test's arrival/departure lines land inside these windows.
		var roomName = TestIsolationHelpers.GenerateUniqueName($"{prefix}Room");
		var digResult = await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@teleport/quiet {player.DbRef}={digResult.Message!.ToPlainText().Trim()}"));

		return player;
	}

	private async Task Run(TestIsolationHelpers.TestPlayer who, string command)
		=> await GodParser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command));

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	private async Task<bool> IsMember(string channelName, DBRef who)
	{
		var channel = await Mediator.Send(new GetChannelQuery(channelName));
		return channel is not null
					 && await channel.Members.Value.AnyAsync(x => x.Member.Object().DBRef.Number == who.Number);
	}

	// --- find_channel: abbreviations resolve ------------------------------------------------------

	/// <summary>
	/// A channel name may be abbreviated: PennMUSH prefix-matches it against the channels the caller can
	/// see (<c>string_prefix</c>, <c>src/extchat.c:1104</c>), so <c>@channel/on pub</c> joins
	/// <c>Public</c>.
	/// </summary>
	[Test]
	public async Task ChannelOn_AcceptsAnAbbreviation()
	{
		var name = UniqueChannel("MatchJoin");
		await CreateChannel(name, "Player", "Open");
		var mortal = await CreateMortal("ChanMatchJoiner");

		await Run(mortal, $"@channel/on {name[..8]}");

		await Assert.That(await IsMember(name, mortal.DbRef)).IsTrue();
	}

	[Test]
	public async Task ChannelOff_AcceptsAnAbbreviation()
	{
		var name = UniqueChannel("MatchLeave");
		var channel = await CreateChannel(name, "Player", "Open");
		var mortal = await CreateMortal("ChanMatchLeaver");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known));

		await Run(mortal, $"@channel/off {name[..9]}");

		await Assert.That(await IsMember(name, mortal.DbRef)).IsFalse();
	}

	/// <summary>
	/// <c>find_channel</c> (<c>src/extchat.c:955-961</c>) returns on an exact name before it considers a
	/// single prefix candidate, so a channel whose full name was typed always wins.
	/// </summary>
	[Test]
	public async Task ExactNameBeatsALongerChannelItPrefixes()
	{
		var stem = UniqueChannel("MatchStem");
		await CreateChannel(stem, "Player", "Open");
		await CreateChannel($"{stem}Extra", "Player", "Open");
		var mortal = await CreateMortal("ChanMatchExact");

		await Run(mortal, $"@channel/on {stem}");

		await Assert.That(await IsMember(stem, mortal.DbRef)).IsTrue();
		await Assert.That(await IsMember($"{stem}Extra", mortal.DbRef)).IsFalse();
	}

	/// <summary>
	/// Two prefix matches is <c>CMATCH_AMBIG</c>: Penn refuses and lists what the abbreviation could
	/// have meant (<c>test_channel</c>, <c>src/extchat.c:190-196</c>).
	/// </summary>
	[Test]
	public async Task AmbiguousAbbreviationRefusesAndListsTheCandidates()
	{
		var stem = UniqueChannel("MatchAmbig");
		await CreateChannel($"{stem}Alpha", "Player", "Open");
		await CreateChannel($"{stem}Beta", "Player", "Open");
		var mortal = await CreateMortal("ChanMatchAmbig");

		var messages = await MessagesWhile(mortal.DbRef, () => Run(mortal, $"@channel/on {stem}"));

		await Assert.That(messages).Contains(ErrorMessages.Notifications.DontKnowWhichChannel);
		await Assert.That(messages).Contains(
			$"{ErrorMessages.Notifications.ChatPartialMatchesAre} {stem}Alpha {stem}Beta");
		await Assert.That(await IsMember($"{stem}Alpha", mortal.DbRef)).IsFalse();
		await Assert.That(await IsMember($"{stem}Beta", mortal.DbRef)).IsFalse();
	}

	/// <summary>
	/// A channel the viewer may not see is not a candidate, so it can neither be joined by abbreviation
	/// nor made to create an ambiguity that betrays its existence — the same rule
	/// <see cref="ChannelPermissionTests"/> defends for exact names.
	/// </summary>
	[Test]
	public async Task AbbreviationSkipsChannelsTheViewerCannotSee()
	{
		var stem = UniqueChannel("MatchHidden");
		await CreateChannel($"{stem}Open", "Player", "Open");
		await CreateChannel($"{stem}Secret", "Player", "Wizard");
		var mortal = await CreateMortal("ChanMatchHidden");

		await Run(mortal, $"@channel/on {stem}");

		// One visible candidate, so no ambiguity and no hint that the wizard channel is there.
		await Assert.That(await IsMember($"{stem}Open", mortal.DbRef)).IsTrue();
	}

	/// <summary>
	/// <c>channel_join_self</c> matches against channels the joiner is NOT on
	/// (<c>find_channel_partial_off</c>, <c>src/extchat.c:1328</c>), and falls back to the on-scope only
	/// to say "you are already on that one".
	/// </summary>
	[Test]
	public async Task JoiningAChannelAlreadyJoinedSaysSo()
	{
		var name = UniqueChannel("MatchRejoin");
		var channel = await CreateChannel(name, "Player", "Open");
		var mortal = await CreateMortal("ChanMatchRejoin");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known));

		var messages = await MessagesWhile(mortal.DbRef, () => Run(mortal, $"@channel/on {name[..10]}"));

		await Assert.That(messages).Contains(
			string.Format(ErrorMessages.Notifications.ChatAlreadyOnChannel, name));
	}

	/// <summary>
	/// The mirror of the above: <c>channel_leave_self</c> matches on-scope first
	/// (<c>src/extchat.c:1387</c>), so leaving a channel one never joined names the channel rather than
	/// denying it exists.
	/// </summary>
	[Test]
	public async Task LeavingAChannelNeverJoinedSaysSo()
	{
		var name = UniqueChannel("MatchNotOn");
		await CreateChannel(name, "Player", "Open");
		var mortal = await CreateMortal("ChanMatchNotOn");

		var messages = await MessagesWhile(mortal.DbRef, () => Run(mortal, $"@channel/off {name[..9]}"));

		await Assert.That(messages).Contains(
			string.Format(ErrorMessages.Notifications.ChatNotOnChannel, name));
	}

	/// <summary>
	/// The join scope is not cosmetic: a player already on <c>&lt;stem&gt;One</c> can still say
	/// <c>@channel/on &lt;stem&gt;</c> to reach <c>&lt;stem&gt;Two</c>, because the channel they are on is
	/// not a candidate. Under a single all-channels matcher this would be ambiguous.
	/// </summary>
	[Test]
	public async Task JoinScopeIgnoresChannelsAlreadyJoined()
	{
		var stem = UniqueChannel("MatchScope");
		var joined = await CreateChannel($"{stem}One", "Player", "Open");
		await CreateChannel($"{stem}Two", "Player", "Open");
		var mortal = await CreateMortal("ChanMatchScope");
		await Mediator.Send(new AddUserToChannelCommand(joined,
			(await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known));

		await Run(mortal, $"@channel/on {stem}");

		await Assert.That(await IsMember($"{stem}Two", mortal.DbRef)).IsTrue();
	}

	// --- do_chan_recall ---------------------------------------------------------------------------

	/// <summary>
	/// Recall replays oldest line first, framed by the header and footer Penn prints, with a
	/// <c>show_time</c> stamp per line (<c>src/extchat.c:4072-4097</c>).
	/// </summary>
	[Test]
	public async Task Recall_IsChronologicalAndFramedAndStamped()
	{
		var name = UniqueChannel("RecallOrder");
		var channel = await CreateChannel(name, "Player", "Open");
		var mortal = await CreateMortal("ChanRecallOrder");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known));

		await Run(mortal, $"@chat {name}=first line");
		await Run(mortal, $"@chat {name}=second line");
		await Run(mortal, $"@chat {name}=third line");

		var messages = await MessagesWhile(mortal.DbRef, () => Run(mortal, $"@channel/recall {name}"));
		var recall = string.Join("\n", messages);

		await Assert.That(recall).Contains(string.Format(ErrorMessages.Notifications.ChatRecallFromChannel, name));
		await Assert.That(recall).Contains(ErrorMessages.Notifications.ChatEndRecall);

		var first = recall.IndexOf("first line", StringComparison.Ordinal);
		var second = recall.IndexOf("second line", StringComparison.Ordinal);
		var third = recall.IndexOf("third line", StringComparison.Ordinal);

		await Assert.That(first).IsGreaterThan(-1);
		await Assert.That(second).IsGreaterThan(first).Because("recall replays a conversation forwards");
		await Assert.That(third).IsGreaterThan(second).Because("recall replays a conversation forwards");

		var header = recall.IndexOf(
			string.Format(ErrorMessages.Notifications.ChatRecallFromChannel, name), StringComparison.Ordinal);
		await Assert.That(header).IsLessThan(first).Because("the header opens the recall");
		await Assert.That(recall.IndexOf(ErrorMessages.Notifications.ChatEndRecall, StringComparison.Ordinal))
			.IsGreaterThan(third).Because("the footer closes the recall");

		// show_time's stamp: "[Mon Sep 08 16:32:11 2026] ..." — the bracketed prefix is what /quiet drops.
		await Assert.That(recall).Contains($"{DateTimeOffset.Now.ToLocalTime():yyyy}] ");
	}

	/// <summary>
	/// <c>/quiet</c> drops the timestamps and nothing else (<c>src/extchat.c:4085</c>) — the header and
	/// footer stay.
	/// </summary>
	[Test]
	public async Task RecallQuiet_DropsTimestampsButKeepsTheFrame()
	{
		var name = UniqueChannel("RecallQuiet");
		var channel = await CreateChannel(name, "Player", "Open");
		var mortal = await CreateMortal("ChanRecallQuiet");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known));

		await Run(mortal, $"@chat {name}=quiet line");

		var messages = await MessagesWhile(mortal.DbRef, () => Run(mortal, $"@channel/recall/quiet {name}"));
		var recall = string.Join("\n", messages);

		await Assert.That(recall).Contains(ErrorMessages.Notifications.ChatEndRecall);
		await Assert.That(recall).Contains("quiet line");
		await Assert.That(recall).DoesNotContain($"{DateTimeOffset.Now.ToLocalTime():yyyy}] ");
	}

	/// <summary>
	/// Penn prints the "use =0" footer only when the recall did not already show the whole buffer
	/// (<c>src/extchat.c:4093</c>).
	/// </summary>
	[Test]
	public async Task Recall_SuggestsTheFullBufferOnlyWhenItTruncated()
	{
		var name = UniqueChannel("RecallFull");
		var channel = await CreateChannel(name, "Player", "Open");
		var mortal = await CreateMortal("ChanRecallFull");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known));

		foreach (var i in Enumerable.Range(1, 4))
		{
			await Run(mortal, $"@chat {name}=line {i}");
		}

		var footer = string.Format(ErrorMessages.Notifications.ChatRecallEntireBuffer, name);

		var truncated = string.Join("\n",
			await MessagesWhile(mortal.DbRef, () => Run(mortal, $"@channel/recall {name}=2")));
		await Assert.That(truncated).Contains(footer);
		await Assert.That(truncated).DoesNotContain("line 1");
		await Assert.That(truncated).Contains("line 4");

		var everything = string.Join("\n",
			await MessagesWhile(mortal.DbRef, () => Run(mortal, $"@channel/recall {name}=0")));
		await Assert.That(everything).DoesNotContain(footer);
		await Assert.That(everything).Contains("line 1");
	}

	/// <summary>
	/// <c>crecall()</c> reads the same buffer, so it inherited the same reversal
	/// (<c>fun_crecall</c>, <c>src/extchat.c:3554-3574</c>). Its lines are separated by the output
	/// separator, a space by default.
	/// </summary>
	[Test]
	public async Task Crecall_IsChronologicalAndSeparated()
	{
		var name = UniqueChannel("RecallFun");
		var channel = await CreateChannel(name, "Player", "Open");
		var mortal = await CreateMortal("ChanRecallFun");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known));

		await Run(mortal, $"@chat {name}=alpha");
		await Run(mortal, $"@chat {name}=omega");

		var parser = WebAppFactoryArg.FunctionParserFor(mortal.DbRef);
		var result = (await parser.FunctionParse(MarkupText.Plain($"crecall({name},10,,|)")))!.Message!.ToPlainText();

		await Assert.That(result.IndexOf("alpha", StringComparison.Ordinal))
			.IsLessThan(result.IndexOf("omega", StringComparison.Ordinal));
		await Assert.That(result).Contains("|");
	}

	/// <summary>
	/// PennMUSH lets anyone who COULD join a channel recall from it (<c>src/extchat.c:4050</c>); this
	/// required membership, so a player could not read a public channel's history before joining it.
	/// </summary>
	[Test]
	public async Task Recall_IsAllowedToANonMemberWhoCouldJoin()
	{
		var name = UniqueChannel("RecallNonMember");
		var channel = await CreateChannel(name, "Player", "Open");
		var member = await CreateMortal("ChanRecallSpeaker");
		var outsider = await CreateMortal("ChanRecallOutsider");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(member.DbRef))).Known));

		await Run(member, $"@chat {name}=overheard");

		var messages = await MessagesWhile(outsider.DbRef, () => Run(outsider, $"@channel/recall {name}"));

		await Assert.That(string.Join("\n", messages)).Contains("overheard");
	}

	// --- do_channel_who / fun_cwho ----------------------------------------------------------------

	/// <summary>
	/// <c>@channel/hide</c> hides you from <c>@channel/who</c> (sharpchat.md:211, and PennMUSH's
	/// <c>do_channel_who</c>, <c>src/extchat.c:2963</c>). <c>cwho()</c> answers to the same rule.
	/// </summary>
	[Test]
	public async Task ChannelWho_HidesAMemberWhoHid()
	{
		var name = UniqueChannel("WhoHide");
		var channel = await CreateChannel(name, "Player", "Open", "Hide_Ok");
		var hider = await CreateMortal("ChanWhoHider");
		var watcher = await CreateMortal("ChanWhoWatcher");
		foreach (var who in new[] { hider, watcher })
		{
			await Mediator.Send(new AddUserToChannelCommand(channel,
				(await Mediator.Send(new GetObjectNodeQuery(who.DbRef))).Known));
		}

		var hiderName = (await Mediator.Send(new GetObjectNodeQuery(hider.DbRef))).Known.Object().Name;

		await Run(hider, $"@channel/hide {name}=yes");

		var watcherName = (await Mediator.Send(new GetObjectNodeQuery(watcher.DbRef))).Known.Object().Name;
		var seen = string.Join("\n", await MessagesWhile(watcher.DbRef, () => Run(watcher, $"@channel/who {name}")));
		await Assert.That(seen).Contains(watcherName).Because("the control: an unhidden member IS listed");
		await Assert.That(seen).DoesNotContain(hiderName);

		var funResult = (await WebAppFactoryArg.FunctionParserFor(watcher.DbRef)
			.FunctionParse(MarkupText.Plain($"cwho({name})")))!.Message!.ToPlainText();
		await Assert.That(funResult).DoesNotContain($"#{hider.DbRef.Number}");
	}

	/// <summary>
	/// The other half of <c>do_channel_who</c>'s gate: a player who is not connected is not listed
	/// (<c>src/extchat.c:2963</c>). Every test player created without a handle is offline.
	/// </summary>
	[Test]
	public async Task ChannelWho_SkipsDisconnectedMembers()
	{
		var name = UniqueChannel("WhoOffline");
		var channel = await CreateChannel(name, "Player", "Open");
		var offline = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "ChanWhoOffline");
		var watcher = await CreateMortal("ChanWhoOnlooker");

		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(offline))).Known));
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(watcher.DbRef))).Known));

		var offlineName = (await Mediator.Send(new GetObjectNodeQuery(offline))).Known.Object().Name;
		var watcherName = (await Mediator.Send(new GetObjectNodeQuery(watcher.DbRef))).Known.Object().Name;
		var seen = string.Join("\n", await MessagesWhile(watcher.DbRef, () => Run(watcher, $"@channel/who {name}")));

		await Assert.That(seen).Contains(watcherName).Because("the control: a connected member IS listed");
		await Assert.That(seen).DoesNotContain(offlineName);
	}

	// --- fun_cbufferadd ---------------------------------------------------------------------------

	/// <summary>
	/// <c>cbufferadd()</c> writes into the recall buffer without broadcasting
	/// (<c>src/extchat.c:2394</c>), so <c>crecall()</c> reads back what it wrote.
	/// </summary>
	[Test]
	public async Task Cbufferadd_WritesIntoTheRecallBuffer()
	{
		var name = UniqueChannel("BufferAdd");
		var channel = await CreateChannel(name, "Player", "Open");
		var owner = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Known;
		await Mediator.Send(new AddUserToChannelCommand(channel, owner));

		await WebAppFactoryArg.FunctionParserFor(new DBRef(1))
			.FunctionParse(MarkupText.Plain($"cbufferadd({name},a reconstructed line)"));

		var recall = (await WebAppFactoryArg.FunctionParserFor(new DBRef(1))
			.FunctionParse(MarkupText.Plain($"crecall({name})")))!.Message!.ToPlainText();

		await Assert.That(recall).Contains("a reconstructed line");
	}

	/// <summary>
	/// The gate is <c>Chan_Can_Modify</c>, not membership (<c>src/extchat.c:2393</c>): a member who does
	/// not control the channel must not be able to forge its history.
	/// </summary>
	[Test]
	public async Task Cbufferadd_RefusesAMemberWhoCannotModifyTheChannel()
	{
		var name = UniqueChannel("BufferAddDenied");
		var channel = await CreateChannel(name, "Player", "Open");
		var mortal = await CreateMortal("ChanBufferForger");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known));

		var result = (await WebAppFactoryArg.FunctionParserFor(mortal.DbRef)
			.FunctionParse(MarkupText.Plain($"cbufferadd({name},forged)")))!.Message!.ToPlainText();

		await Assert.That(result).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	// --- fun_clock --------------------------------------------------------------------------------

	/// <summary>
	/// <c>clock()</c> takes its lock type as a suffix on the channel argument
	/// (<c>src/extchat.c:3383</c>), and answers <c>#-1 NO SUCH LOCK TYPE</c> to anything it does not
	/// recognise.
	/// </summary>
	[Test]
	public async Task Clock_ReadsTheLockTypeFromTheChannelArgument()
	{
		var name = UniqueChannel("ClockRead");
		await CreateChannel(name, "Player", "Open");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@clock/speak {name}=#1"));

		var parser = WebAppFactoryArg.FunctionParserFor(new DBRef(1));

		await Assert.That((await parser.FunctionParse(MarkupText.Plain($"clock({name}/speak)")))!
			.Message!.ToPlainText()).IsNotEmpty();
		await Assert.That((await parser.FunctionParse(MarkupText.Plain($"clock({name}/join)")))!
			.Message!.ToPlainText()).IsEmpty();
		await Assert.That((await parser.FunctionParse(MarkupText.Plain($"clock({name}/nonsense)")))!
			.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.NoSuchLockType);
	}

	// --- do_chan_title ----------------------------------------------------------------------------

	/// <summary>
	/// <c>@channel/title &lt;channel&gt;</c> with no <c>=</c> asks what your title is; an <c>=</c> with
	/// nothing after it clears it (<c>rhs_present</c>, <c>src/extchat.c:3145</c>).
	/// </summary>
	[Test]
	public async Task ChannelTitle_WithoutAnEqualsAsksRatherThanClears()
	{
		var name = UniqueChannel("TitleQuery");
		var channel = await CreateChannel(name, "Player", "Open");
		var mortal = await CreateMortal("ChanTitleAsker");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known));

		await Run(mortal, $"@channel/title {name}=the Bold");

		var asked = await MessagesWhile(mortal.DbRef, () => Run(mortal, $"@channel/title {name}"));
		await Assert.That(asked).Contains(
			string.Format(ErrorMessages.Notifications.ChatYourTitleOnIs, name, "the Bold"));

		// The title survived being asked about.
		var status = await ChannelHelper.ChannelMemberStatus(
			(await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known,
			(await Mediator.Send(new GetChannelQuery(name)))!);
		await Assert.That(status!.Status.Title?.ToPlainText()).IsEqualTo("the Bold");

		await Run(mortal, $"@channel/title {name}=");
		var cleared = await ChannelHelper.ChannelMemberStatus(
			(await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known,
			(await Mediator.Send(new GetChannelQuery(name)))!);
		await Assert.That(cleared!.Status.Title?.ToPlainText() ?? string.Empty).IsEmpty();
	}

	// --- ok_channel_name --------------------------------------------------------------------------

	/// <summary>
	/// PennMUSH's <c>ok_channel_name</c> (<c>src/extchat.c:1855</c>) has no minimum length, so the
	/// three-character names half the MUSHes in existence use — <c>OOC</c>, <c>RP</c> — are legal.
	/// </summary>
	[Test]
	public async Task ChannelAdd_AcceptsAThreeCharacterName()
	{
		var name = $"O{TestIsolationHelpers.GenerateUniqueName("x").Replace("_", string.Empty)[^2..]}";

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@channel/add {name}=player"));

		await Assert.That(await Mediator.Send(new GetChannelQuery(name))).IsNotNull();
	}

	// --- do_chan_decompile ------------------------------------------------------------------------

	/// <summary>
	/// A decompile has to be able to rebuild what it describes (<c>src/extchat.c:2833-2876</c>):
	/// privileges, owner, locks, description and membership, not just the name.
	/// </summary>
	[Test]
	public async Task Decompile_EmitsEverythingNeededToRebuildTheChannel()
	{
		var name = UniqueChannel("Decomp");
		var channel = await CreateChannel(name, "Player", "Open");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Known));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@clock/speak {name}=#1"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@channel/describe {name}=a decompiled channel"));

		var result = await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@channel/decompile {name}"));
		var decompiled = result.Message!.ToPlainText();

		await Assert.That(decompiled).Contains($"@channel/add {name} = Player Open");
		await Assert.That(decompiled).Contains($"@channel/chown {name} = ");
		await Assert.That(decompiled).Contains($"@clock/speak {name} = ");
		// The registered switch is DESCRIBE and this dispatcher matches switch names exactly, so a
		// decompile that emitted Penn's abbreviated "@channel/desc" would not replay.
		await Assert.That(decompiled).Contains($"@channel/describe {name} = a decompiled channel");
		await Assert.That(decompiled).Contains($"@channel/on {name} = *");
	}

	// --- do_chan_wipe -----------------------------------------------------------------------------

	/// <summary>
	/// <c>@channel/wipe</c> removes every member (<c>channel_wipe</c>, <c>src/extchat.c:2216</c>), which
	/// is what the help file says it does. Resizing the buffer is <c>@channel/buffer</c>'s job.
	/// </summary>
	[Test]
	public async Task Wipe_RemovesEveryMember()
	{
		var name = UniqueChannel("Wipe");
		var channel = await CreateChannel(name, "Player", "Open");
		var member = await CreateMortal("ChanWipeMember");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(member.DbRef))).Known));

		await Assert.That(await IsMember(name, member.DbRef)).IsTrue();

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@channel/wipe {name}"));

		await Assert.That(await IsMember(name, member.DbRef)).IsFalse();
	}

	// --- do_chat ----------------------------------------------------------------------------------

	/// <summary>
	/// The <c>open</c> privilege is documented as "You may speak on the channel even when you are not
	/// listening to it", and is PennMUSH's <c>Channel_Open</c> check (<c>src/extchat.c:1553</c>).
	/// </summary>
	[Test]
	public async Task Chat_OnAnOpenChannelDoesNotRequireMembership()
	{
		var name = UniqueChannel("OpenSpeak");
		var channel = await CreateChannel(name, "Player", "Open");
		var listener = await CreateMortal("ChanOpenListener");
		var outsider = await CreateMortal("ChanOpenOutsider");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(listener.DbRef))).Known));

		var heard = await MessagesWhile(listener.DbRef,
			() => Run(outsider, $"@chat {name}=spoken from outside"));

		await Assert.That(string.Join("\n", heard)).Contains("spoken from outside");
	}

	/// <summary>
	/// The control: without <c>open</c>, a non-member is refused with PennMUSH's wording
	/// (<c>src/extchat.c:1557</c>).
	/// </summary>
	[Test]
	public async Task Chat_OnAClosedChannelStillRequiresMembership()
	{
		var name = UniqueChannel("ClosedSpeak");
		await CreateChannel(name, "Player");
		var outsider = await CreateMortal("ChanClosedOutsider");

		var messages = await MessagesWhile(outsider.DbRef, () => Run(outsider, $"@chat {name}=let me in"));

		await Assert.That(messages).Contains(ErrorMessages.Notifications.ChatMustBeOnChannelToSpeak);
	}

	// --- do_cemit ---------------------------------------------------------------------------------

	/// <summary>
	/// <c>do_cemit</c> (<c>src/extchat.c:1622-1655</c>) gates on <c>Chan_Can_Cemit</c> and nothing else,
	/// which is what lets the wizard or the channel-owning object emit onto a channel it does not listen
	/// to. <c>cemit()</c> and <c>nscemit()</c> are the same command with a different spelling and answer
	/// to the same gate — no stricter, so softcode is not shut out of what the command allows, and no
	/// looser, so it is not a way around it.
	/// </summary>
	[Test]
	[Arguments("@cemit")]
	[Arguments("@nscemit")]
	[Arguments("cemit")]
	[Arguments("nscemit")]
	public async Task Cemit_DoesNotRequireMembership(string spelling)
	{
		var name = UniqueChannel($"Cemit{spelling.TrimStart('@')}");
		var channel = await CreateChannel(name, "Player", "Open");
		var listener = await CreateMortal($"ChanCemitEar{spelling.TrimStart('@')}");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(listener.DbRef))).Known));

		// Creating a channel joins its creator to it, so take God back off: the point of the test is an
		// authorized emitter who is NOT a member.
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Known;
		await Mediator.Send(new RemoveUserFromChannelCommand(
			(await Mediator.Send(new GetChannelQuery(name)))!, god));
		await Assert.That(await IsMember(name, new DBRef(1))).IsFalse();

		var heard = await MessagesWhile(listener.DbRef, async () =>
		{
			if (spelling.StartsWith('@'))
			{
				await GodParser.CommandParse(1, ConnectionService,
					MarkupText.Plain($"{spelling} {name}=emitted from outside"));
			}
			else
			{
				await WebAppFactoryArg.FunctionParserFor(new DBRef(1))
					.FunctionParse(MarkupText.Plain($"{spelling}({name},emitted from outside)"));
			}
		});

		await Assert.That(string.Join("\n", heard)).Contains("emitted from outside");
	}

	// --- The command and function spellings answer to one implementation ---------------------------

	/// <summary>
	/// <c>do_cemit</c> keeps the open-channel rule that <c>do_chat</c> has (<c>src/extchat.c:1667</c>):
	/// on a channel WITHOUT the <c>open</c> privilege a non-member is refused, and the See_All + Pemit_All
	/// bypass is the only way past it. The mortal here has neither, so all four spellings refuse — the
	/// counterpart to <see cref="Cemit_DoesNotRequireMembership"/>, which uses an open channel.
	/// </summary>
	[Test]
	[Arguments("@cemit")]
	[Arguments("@nscemit")]
	[Arguments("cemit")]
	[Arguments("nscemit")]
	public async Task Cemit_OnAClosedChannelRefusesANonMember(string spelling)
	{
		var name = UniqueChannel($"Closed{spelling.TrimStart('@')}");
		var channel = await CreateChannel(name, "Player");
		var listener = await CreateMortal($"ChanClosedEar{spelling.TrimStart('@')}");
		var outsider = await CreateMortal($"ChanClosedOut{spelling.TrimStart('@')}");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(listener.DbRef))).Known));

		var heard = await MessagesWhile(listener.DbRef, async () =>
		{
			if (spelling.StartsWith('@'))
			{
				await Run(outsider, $"{spelling} {name}=should not arrive");
			}
			else
			{
				await WebAppFactoryArg.FunctionParserFor(outsider.DbRef)
					.FunctionParse(MarkupText.Plain($"{spelling}({name},should not arrive)"));
			}
		});

		await Assert.That(string.Join("\n", heard)).DoesNotContain("should not arrive");
	}

	/// <summary>
	/// <c>crecall()</c> and <c>@channel/recall</c> reach the same <c>ChannelRecall.SelectAsync</c>, so
	/// neither hands back history the other refuses.
	/// </summary>
	[Test]
	public async Task Recall_RefusesTheSameNonMembersInBothSpellings()
	{
		var name = UniqueChannel("RecallParity");
		var channel = await CreateChannel(name, "Player", "Wizard");
		var member = await CreateMortal("ChanParityMember");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(member.DbRef))).Known));
		await Run(member, $"@chat {name}=wizard business");

		// A mortal cannot join a Wizard channel, so neither spelling may recall from it.
		var mortal = await CreateMortal("ChanParityMortal");

		var commandOutput = string.Join("\n",
			await MessagesWhile(mortal.DbRef, () => Run(mortal, $"@channel/recall {name}")));
		var functionOutput = (await WebAppFactoryArg.FunctionParserFor(mortal.DbRef)
			.FunctionParse(MarkupText.Plain($"crecall({name})")))!.Message!.ToPlainText();

		await Assert.That(commandOutput).DoesNotContain("wizard business");
		await Assert.That(functionOutput).DoesNotContain("wizard business");
	}

	// --- Can_Nspemit --------------------------------------------------------------------------------

	/// <summary>
	/// PennMUSH <c>Can_Nspemit</c> (<c>hdrs/mushdb.h:33</c>) is <c>Wizard(x) || CAN_SPOOF power</c>. The
	/// power is seeded as <c>Can_Spoof</c> (<c>PowerSeed.cs:16</c>); <c>NOSPOOF</c> is a FLAG, so a
	/// predicate testing it as a power matches nothing and collapses to the wizard half — which denies
	/// every non-wizard the power exists to grant.
	///
	/// <para>Asserted through <c>nscemit()</c> because it is the shortest route to the predicate, but the
	/// predicate is shared with the whole <c>@pemit</c>/<c>@nsemit</c> family.</para>
	/// </summary>
	[Test]
	public async Task CanNoSpoof_IsGrantedByTheCanSpoofPower()
	{
		var permissions = WebAppFactoryArg.Services.GetRequiredService<IPermissionService>();
		var mortal = await CreateMortal("ChanSpoofPower");
		var mortalObject = (await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known;

		await Assert.That(await permissions.CanNoSpoof(mortalObject)).IsFalse()
			.Because("the control: a plain mortal may not spoof");

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@power {mortal.DbRef}=Can_Spoof"));

		var granted = (await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Known;
		await Assert.That(await permissions.CanNoSpoof(granted)).IsTrue();
	}

	/// <summary>
	/// A THING hiding on a channel is withheld from <c>@channel/who</c> and <c>cwho()</c> just as a player
	/// is (<c>do_channel_who</c>, <c>src/extchat.c:2963</c>). PennMUSH's <c>fun_cwho</c> exempts THINGs
	/// from the hide test, so its two listings disagree; this takes the command's rule for both.
	/// </summary>
	[Test]
	public async Task ChannelWho_HidesAHiddenThing()
	{
		var name = UniqueChannel("WhoThing");
		var channel = await CreateChannel(name, "Player", "Object", "Open", "Hide_Ok");
		var watcher = await CreateMortal("ChanWhoThingWatcher");
		await Mediator.Send(new AddUserToChannelCommand(channel,
			(await Mediator.Send(new GetObjectNodeQuery(watcher.DbRef))).Known));

		var thingName = TestIsolationHelpers.GenerateUniqueName("ChanThing").Replace("_", string.Empty);
		var created = await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {thingName}"));
		var thingRef = DBRef.TryParse(created.Message!.ToPlainText().Trim(), out var parsed)
			? parsed!.Value
			: throw new InvalidOperationException($"@create did not return a dbref: {created.Message}");
		var thing = (await Mediator.Send(new GetObjectNodeQuery(thingRef))).Known;
		await Mediator.Send(new AddUserToChannelCommand(channel, thing));

		var visible = string.Join("\n",
			await MessagesWhile(watcher.DbRef, () => Run(watcher, $"@channel/who {name}")));
		await Assert.That(visible).Contains(thingName)
			.Because("the control: an unhidden THING is listed even though it is never connected");

		await Mediator.Send(new UpdateChannelUserStatusCommand(
			(await Mediator.Send(new GetChannelQuery(name)))!, thing,
			new SharpChannelStatus(null, null, true, null, null)));

		var hidden = string.Join("\n",
			await MessagesWhile(watcher.DbRef, () => Run(watcher, $"@channel/who {name}")));
		await Assert.That(hidden).DoesNotContain(thingName);

		var funResult = (await WebAppFactoryArg.FunctionParserFor(watcher.DbRef)
			.FunctionParse(MarkupText.Plain($"cwho({name})")))!.Message!.ToPlainText();
		await Assert.That(funResult).DoesNotContain($"#{thingRef.Number}");
	}

	/// <summary>
	/// A channel name is bounded by PennMUSH's <c>CHAN_NAME_LEN</c> (<c>hdrs/extchat.h:105</c>), which is
	/// a constant rather than a setting, and is why <c>@channel/list</c>'s Name column is 30 wide.
	/// <c>chan_title_len</c> bounds a member's title and is a different thing.
	/// </summary>
	[Test]
	public async Task ChannelAdd_RefusesANameLongerThanPennsLimit()
	{
		var tooLong = new string('c', ChannelHelper.MaxChannelNameLength + 1);
		var longest = new string('c', ChannelHelper.MaxChannelNameLength);

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@channel/add {tooLong}=player"));
		await Assert.That(await Mediator.Send(new GetChannelQuery(tooLong))).IsNull();

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@channel/add {longest}=player"));
		await Assert.That(await Mediator.Send(new GetChannelQuery(longest))).IsNotNull()
			.Because("the limit is inclusive");
	}
}
