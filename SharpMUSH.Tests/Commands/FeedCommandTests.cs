using SharpMUSH.Library.Commands.Database;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@feed</c> and the feed functions (<c>help @feed</c>): who may run a kind, members, sending through the
/// default line and the kind's tie-in attributes, locks, limits and taps. Each test defines a kind of its own,
/// since kinds are world-wide.
/// </summary>
public class FeedCommandTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _system = null!;
	private TestIsolationHelpers.TestPlayer _ann = null!;
	private TestIsolationHelpers.TestPlayer _bo = null!;
	private string _kind = null!;

	[Before(Test)]
	public async Task CreatePlayers()
	{
		_system = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "FeedSys");
		_ann = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "FeedAnn");
		_bo = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "FeedBo");
		_kind = "k" + Guid.NewGuid().ToString("N")[..12];
		await Cmd($"@feed/define {_kind}={Ref(_system)}");
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await Cmd($"@feed/undefine {_kind}");
		foreach (var player in new[] { _system, _ann, _bo })
			await ConnectionService.Disconnect(player.Handle);
	}

	private static string Ref(TestIsolationHelpers.TestPlayer player) => $"#{player.DbRef.Number}";

	private static string Ref(DBRef dbref) => $"#{dbref.Number}";

	/// <summary>Runs a command and returns what the executor was told, one line per notification.</summary>
	private async Task<string> Heard(TestIsolationHelpers.TestPlayer who, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who.DbRef);
		await CmdAs(who.DbRef, who.Handle, command);
		return string.Join("\n", WebAppFactoryArg.Notifications.For(who.DbRef).Skip(before));
	}

	private Task<string> Objid(TestIsolationHelpers.TestPlayer who) => Eval($"objid({Ref(who)})");

	private List<string> Lines(TestIsolationHelpers.TestPlayer who, string containing)
		=> WebAppFactoryArg.Notifications.For(who.DbRef).Where(line => line.Contains(containing, StringComparison.Ordinal)).ToList();

	[Test]
	public async Task MembersGetTheDefaultLine_ExceptTheGagged()
	{
		await Assert.That(await Heard(_system, $"@feed/join {_kind}/1={Ref(_ann)}")).Contains($"Joined {_ann.Name}");
		await Heard(_system, $"@feed/join {_kind}/1={Ref(_bo)}");
		await Assert.That(await Heard(_system, $"@feed/gag {_kind}/1={Ref(_bo)}")).Contains("now gagged");

		var text = TestIsolationHelpers.GenerateUniqueName("FeedHello");
		await Heard(_system, $"@feed/send {_kind}/1={text}");
		await Assert.That(Lines(_system, text)).IsEmpty();

		await Assert.That(Lines(_ann, text)).IsEquivalentTo(new[] { $"<{_kind}/1> {_system.Name} says, \"{text}\"" });
		await Assert.That(Lines(_bo, text)).IsEmpty();

		var pose = TestIsolationHelpers.GenerateUniqueName("FeedPose");
		await Heard(_system, $"@feed/send {_kind}/1=:{pose}");
		await Assert.That(Lines(_ann, pose)).IsEquivalentTo(new[] { $"<{_kind}/1> {_system.Name} {pose}" });

		var id = await EvalAs(_system.DbRef, $"first(feedrecall({_kind}/1,0))");
		await Assert.That(await EvalAs(_system.DbRef, $"feedmsg({id},text)")).IsEqualTo(text);
		await Assert.That(await EvalAs(_system.DbRef, $"feedmsg({id},style)")).IsEqualTo("say");
		await Assert.That(await EvalAs(_system.DbRef, $"feedmsg({id},speaker)")).IsEqualTo(await Objid(_system));
		await Assert.That(await EvalAs(_system.DbRef, $"words(feedrecall({_kind}/1,0))")).IsEqualTo("2");
	}

	[Test]
	public async Task MemberFunctions_ReadWhoIsOnAFeed()
	{
		await Heard(_system, $"@feed/join {_kind}/a={Ref(_ann)}");
		await Heard(_system, $"@feed/join {_kind}/b={Ref(_ann)}");
		await Heard(_system, $"@feed/join {_kind}/a={Ref(_bo)}");
		await Heard(_system, $"@feed/gag {_kind}/a={Ref(_bo)}");

		await Assert.That(await EvalAs(_system.DbRef, $"feedsof({Ref(_ann)},{_kind})")).IsEqualTo($"{_kind}/a {_kind}/b");
		await Assert.That(await EvalAs(_system.DbRef, $"feeds({_kind})")).IsEqualTo($"{_kind}/a {_kind}/b");
		await Assert.That(await EvalAs(_system.DbRef, $"words(feedwho({_kind}/a))")).IsEqualTo("2");
		await Assert.That(await EvalAs(_system.DbRef, $"feedwho({_kind}/a,gag)")).IsEqualTo(await Objid(_bo));
		await Assert.That(await EvalAs(_system.DbRef, $"feedmember({_kind}/b,{Ref(_bo)})")).IsEqualTo("0");

		await Heard(_system, $"@feed/send {_kind}/a=one");
		await Heard(_system, $"@feed/send {_kind}/a=two");
		await Assert.That(await EvalAs(_system.DbRef, $"feedunread({_kind}/a,{Ref(_ann)})")).IsEqualTo("2");
		await Heard(_system, $"@feed/seen {_kind}/a={Ref(_ann)}");
		await Assert.That(await EvalAs(_system.DbRef, $"feedunread({_kind}/a,{Ref(_ann)})")).IsEqualTo("0");
		await Assert.That(await EvalAs(_system.DbRef, $"feedinfo({_kind}/a,messages)")).IsEqualTo("2");

		await Assert.That(await Heard(_system, $"@feed/leave {_kind}/a={Ref(_ann)}")).Contains("Removed");
		await Assert.That(await EvalAs(_system.DbRef, $"feedmember({_kind}/a,{Ref(_ann)})")).IsEqualTo("0");
	}

	[Test]
	public async Task OnlyTheOwnersCodeAndFeedAdminRunAKind()
	{
		await Assert.That(await Heard(_ann, $"@feed/join {_kind}/1={Ref(_ann)}")).Contains("needs control of its owner");
		await Assert.That(await Heard(_ann, $"@feed/send {_kind}/1=hello")).Contains("needs control of its owner");
		await Assert.That(await EvalAs(_ann.DbRef, $"feedwho({_kind}/1)")).IsEqualTo("#-1 PERMISSION DENIED");

		// The owner runs its feeds, but changing the kind is feed.admin's.
		await Assert.That(await Heard(_system, $"@feed/set {_kind}/max_messages=5")).Contains("needs the feed.admin permission");
		await Assert.That(await Heard(_system, $"@feed/define {_kind}2={Ref(_system)}")).Contains("needs the feed.admin permission");
	}

	[Test]
	public async Task RouteFormatAndDeliver_TieTheKindIn()
	{
		var upper = _kind.ToUpperInvariant();
		await Heard(_system, $"@feed/join {_kind}/1={Ref(_bo)}");
		// ROUTE sends to the /to list instead of the members; FORMAT writes each recipient's line.
		await Cmd($"&FEED`{upper}`ROUTE {Ref(_system)}=%6");
		await Cmd($"&FEED`{upper}`FORMAT {Ref(_system)}=Text from [name(%2)] to [name(%1)] on %4: %0");

		var text = TestIsolationHelpers.GenerateUniqueName("FeedText");
		await Heard(_system, $"@feed/send/to {_kind}/1={await Objid(_ann)}/{text}");
		await Assert.That(Lines(_ann, text)).IsEquivalentTo(new[] { $"Text from {_system.Name} to {_ann.Name} on 1: {text}" });
		await Assert.That(Lines(_bo, text)).IsEmpty();

		// DELIVER takes over delivery entirely.
		await Cmd($"&FEED`{upper}`DELIVER {Ref(_system)}=@pemit %2=Delivered %4 %5 on %1");
		var delivered = TestIsolationHelpers.GenerateUniqueName("FeedDeliver");
		await Heard(_system, $"@feed/send/emit/to {_kind}/1={await Objid(_ann)}/{delivered}");
		await Assert.That(Lines(_ann, delivered)).IsEquivalentTo(new[] { $"Delivered emit {delivered} on 1" });
	}

	[Test]
	public async Task TheReadLockDecidesWhoMayBeJoined()
	{
		await Cmd($"@feed/lock {_kind}/1/read={Ref(_bo)}");

		await Assert.That(await Heard(_system, $"@feed/join {_kind}/1={Ref(_ann)}")).Contains("does not pass the read lock");
		await Assert.That(await Heard(_system, $"@feed/join {_kind}/1={Ref(_bo)}")).Contains("Joined");

		await Cmd($"@feed/unlock {_kind}/1/read");
		await Assert.That(await Heard(_system, $"@feed/join {_kind}/1={Ref(_ann)}")).Contains("Joined");
	}

	[Test]
	public async Task TheSendLockIsCheckedAgainstTheSpeaker()
	{
		// A global-style command on an object the kind belongs to: the object runs @feed, the player speaks.
		var radio = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "FeedRadio");
		var command = "+" + TestIsolationHelpers.GenerateUniqueName("fr").ToLowerInvariant();
		await Cmd($"@tel {Ref(radio)}=[loc({Ref(_ann)})]");
		await Cmd($"@feed/define {_kind}={Ref(radio)}");
		await Cmd($"&CMD {Ref(radio)}=${command} *:@feed/send {_kind}/1=%0");
		await Cmd($"@feed/lock {_kind}/send={Ref(_bo)}");

		await CmdAs(_ann.DbRef, _ann.Handle, $"{command} refused");
		await Assert.That(await Eval($"feedinfo({_kind}/1,messages)")).IsEqualTo("0");

		await Cmd($"@feed/unlock {_kind}/send");
		await CmdAs(_ann.DbRef, _ann.Handle, $"{command} allowed");
		var id = await Eval($"feedrecall({_kind}/1,1)");
		await Assert.That(await Eval($"feedmsg({id},speaker)")).IsEqualTo(await Objid(_ann));
		await Assert.That(await Eval($"feedmsg({id},executor)")).IsEqualTo((await Eval($"objid({Ref(radio)})")));
	}

	/// <summary>A line keeps the names of its speaker, executor and location, so it reads whole after they are gone.</summary>
	[Test]
	public async Task ALineKeepsItsNames_AfterItsObjectsAreDestroyed()
	{
		var radio = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "FeedGone");
		var command = "+" + TestIsolationHelpers.GenerateUniqueName("fg").ToLowerInvariant();
		await Cmd($"@tel {Ref(radio)}=[loc({Ref(_ann)})]");
		await Cmd($"@feed/define {_kind}={Ref(radio)}");
		await Cmd($"&CMD {Ref(radio)}=${command} *:@feed/send {_kind}/1=%0");
		var radioName = await Eval($"name({Ref(radio)})");
		var roomName = await Eval($"name(loc({Ref(_ann)}))");

		await CmdAs(_ann.DbRef, _ann.Handle, $"{command} still here");
		var id = await Eval($"feedrecall({_kind}/1,1)");
		await Mediator.Send(new DeleteObjectCommand(radio));

		await Assert.That(await Eval($"feedmsg({id},name)")).IsEqualTo(_ann.Name);
		await Assert.That(await Eval($"feedmsg({id},executor_name)")).IsEqualTo(radioName);
		await Assert.That(await Eval($"feedmsg({id},location_name)")).IsEqualTo(roomName);
		await Assert.That(await Eval($"feedmsg({id},text)")).IsEqualTo("still here");
	}

	/// <summary>
	/// A destroyed object leaves its feeds and loses its taps, and a kind it owned goes to its owner; a
	/// destroyed player's kinds go to the probate judge (#1 in tests), as their channels do.
	/// </summary>
	[Test]
	public async Task ADestroyedOwnersKindGoesToItsOwner_AndAPlayersToTheProbateJudge()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "FeedHeir");
		var radio = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "FeedHeirRadio");
		var kind = _kind + "h";
		try
		{
			await Cmd($"@chown {Ref(radio)}={Ref(player)}");
			await Cmd($"@feed/define {kind}={Ref(radio)}");
			await Cmd($"&LOG {Ref(radio)}=think");
			await Cmd($"@feed/tap {_kind}={Ref(radio)}/LOG");
			await Heard(_system, $"@feed/join {_kind}/1={Ref(radio)}");
			await Assert.That(await Eval($"words(feedwho({_kind}/1))")).IsEqualTo("1");

			await Cmd($"@nuke {Ref(radio)}");
			await Cmd($"@nuke {Ref(radio)}");

			await Assert.That(await Eval($"num(feedinfo({kind},owner))")).IsEqualTo(Ref(player));
			await Assert.That(await Eval($"words(feedwho({_kind}/1))")).IsEqualTo("0");
			await Assert.That(await Mediator.Send(new SharpMUSH.Library.Queries.Database.GetFeedTapsQuery(_kind))).IsEmpty();

			await Cmd($"@nuke {Ref(player)}");
			await Cmd($"@nuke {Ref(player)}");

			await Assert.That(await Eval($"num(feedinfo({kind},owner))")).IsEqualTo("#1");
		}
		finally
		{
			await Cmd($"@feed/undefine {kind}");
		}
	}

	/// <summary>An evaluation lock on the kind learns the feed from %0; a feed's own lock applies on top of it.</summary>
	[Test]
	public async Task AnEvaluationLockLearnsTheFeedFromPercentZero()
	{
		await Cmd($"&LK`OPEN {Ref(_system)}=[strmatch(%0,open*)]");
		await Assert.That(await Cmd($"@feed/lock {_kind}/read=LK`OPEN/1")).Contains("Locked read");

		await Assert.That(await Heard(_system, $"@feed/join {_kind}/open={Ref(_ann)}")).Contains($"Joined {_ann.Name}");
		await Assert.That(await Heard(_system, $"@feed/join {_kind}/shut={Ref(_ann)}")).Contains("does not pass the read lock");

		// The kind's owner may lock one feed, on top of the kind's lock, but not the kind itself.
		await Assert.That(await Heard(_system, $"@feed/lock {_kind}/open2/read={Ref(_bo)}")).Contains("Locked read");
		await Assert.That(await Heard(_system, $"@feed/join {_kind}/open2={Ref(_ann)}")).Contains("does not pass the read lock");
		await Assert.That(await Heard(_system, $"@feed/join {_kind}/open2={Ref(_bo)}")).Contains($"Joined {_bo.Name}");
		await Assert.That(await EvalAs(_system.DbRef, $"feedinfo({_kind}/open2,read)")).IsEqualTo(Ref(_bo));
		await Assert.That(await Heard(_system, $"@feed/unlock {_kind}/read")).Contains("needs the feed.admin permission");

		// Who may moderate, or talk, is the system's own business, not a feed lock.
		await Assert.That(await Cmd($"@feed/lock {_kind}/moderate={Ref(_bo)}")).Contains("two locks, read and send");
	}

	[Test]
	public async Task SendAsKeepsADisplayNameOnTheLine()
	{
		await Heard(_system, $"@feed/join {_kind}/1={Ref(_ann)}");
		var text = TestIsolationHelpers.GenerateUniqueName("FeedAs");
		await Heard(_system, $"@feed/send/as {_kind}/1=Ghost/{text}");

		await Assert.That(Lines(_ann, text)).IsEquivalentTo(new[] { $"<{_kind}/1> Ghost says, \"{text}\"" });
		var id = await EvalAs(_system.DbRef, $"feedrecall({_kind}/1,1)");
		await Assert.That(await EvalAs(_system.DbRef, $"feedmsg({id},display)")).IsEqualTo("Ghost");
		await Assert.That(await EvalAs(_system.DbRef, $"feedmsg({id},name)")).IsEqualTo(_system.Name);
	}

	[Test]
	public async Task SettingsLimitWhatIsKeptAndSent()
	{
		await Assert.That(await Cmd($"@feed/set {_kind}/max_messages=2")).Contains("Set max_messages");
		await Assert.That(await Cmd($"@feed/set {_kind}/1/max_length=5")).Contains("Set max_length");
		await Assert.That(await Cmd($"@feed/set {_kind}/style=loud")).Contains("style takes one of");

		await Assert.That(await Heard(_system, $"@feed/send {_kind}/1=too long")).Contains("longer than");
		await Heard(_system, $"@feed/send {_kind}/1=one");
		await Heard(_system, $"@feed/send {_kind}/1=two");
		await Heard(_system, $"@feed/send {_kind}/1=three");
		await Assert.That(await Eval($"iter(feedrecall({_kind}/1,0),feedmsg(##,text))")).IsEqualTo("two three");
		await Assert.That(await Eval($"feedinfo({_kind}/1,max_messages)")).IsEqualTo("2");
		await Assert.That(await Eval($"feedinfo({_kind}/2,max_length)")).IsEqualTo("0");

		await Cmd($"@feed/set {_kind}/1/logged=no");
		await Heard(_system, $"@feed/send {_kind}/1=four");
		await Assert.That(await Eval($"feedinfo({_kind}/1,messages)")).IsEqualTo("2");

		await Assert.That(await Cmd($"@feed/purge {_kind}/1")).Contains("Purged 2 lines");
		await Assert.That(await Eval($"feedrecall({_kind}/1,0)")).IsEqualTo("");
	}

	[Test]
	public async Task TapsHearEveryLineOfTheirKind()
	{
		var logger = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "FeedLogger");
		await Cmd($"&LOG {Ref(logger)}=@pemit {Ref(_ann)}=Tapped %1 from [name(%3)] to %2: %5");
		await Assert.That(await Cmd($"@feed/tap {_kind}={Ref(logger)}/LOG")).Contains("now taps");
		await Heard(_system, $"@feed/join {_kind}/1={Ref(_bo)}");

		var text = TestIsolationHelpers.GenerateUniqueName("FeedTap");
		await Heard(_system, $"@feed/send {_kind}/1={text}");
		await WebAppFactoryArg.Notifications.WaitForAsync(_ann.DbRef, text);
		await Assert.That(Lines(_ann, text)).IsEquivalentTo(new[] { $"Tapped {_kind}/1 from {_system.Name} to {await Objid(_bo)}: {text}" });

		await Assert.That(await Cmd($"@feed/untap {_kind}={Ref(logger)}/LOG")).Contains("Removed the tap");
	}

	/// <summary>The radio in <c>help @feed example</c>, as written there: global commands in the master room.</summary>
	[Test]
	public async Task TheHelpRadioExampleWorks()
	{
		var radio = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "Radio");
		try
		{
			await Cmd($"@tel {Ref(radio)}=[config(master_room)]");
			await Cmd($"@feed/define radio={Ref(radio)}");
			await Cmd($"&FEED`RADIO`FORMAT {Ref(radio)}=<Radio %4> [name(%2)]: %0");
			await Cmd($"&CMD`TUNE {Ref(radio)}=$+tune *:@dolist/inline feedsof(%#,radio)=@feed/leave ##=%#;@feed/join radio/%0=%#;@pemit %#=Tuned to %0.");
			await Cmd($"&CMD`RADIO {Ref(radio)}=$+radio *:@assert setr(f,first(feedsof(%#,radio)))=@pemit %#=Tune in first.;@feed/send %q<f>=%0");
			await Cmd($"&CMD`OFF {Ref(radio)}=$+radio/off:@dolist/inline feedsof(%#,radio)=@feed/leave ##=%#;@pemit %#=Radio off.");

			var said = TestIsolationHelpers.GenerateUniqueName("Coming");
			await Assert.That(await Heard(_ann, $"+radio {said}")).Contains("Tune in first.");
			await Assert.That(await Heard(_ann, "+tune 101.5")).Contains("Tuned to 101.5.");
			await Heard(_bo, "+tune 101.5");
			await Heard(_ann, $"+radio {said}");
			await Assert.That(Lines(_ann, said)).IsEquivalentTo(new[] { $"<Radio 101.5> {_ann.Name}: {said}" });
			await Assert.That(Lines(_bo, said)).IsEquivalentTo(new[] { $"<Radio 101.5> {_ann.Name}: {said}" });

			await Heard(_bo, "+tune 99.1");
			await Assert.That(await Eval("feedwho(radio/101.5)")).IsEqualTo(await Objid(_ann));
			await Assert.That(await Heard(_ann, "+radio/off")).Contains("Radio off.");
			await Assert.That(await Eval($"feedsof({Ref(_ann)})")).IsEqualTo("");
		}
		finally
		{
			await Cmd("@feed/undefine radio");
			await Cmd($"@tel {Ref(radio)}=#0");
		}
	}

	[Test]
	public async Task AKindTotalsItsFeeds_AndAQuietFeedStillAges()
	{
		await Cmd($"@feed/send {_kind}/1=one");
		await Cmd($"@feed/send {_kind}/1=two");
		await Cmd($"@feed/send {_kind}/2=three");
		await Assert.That(await Eval($"feedinfo({_kind},feeds)")).IsEqualTo("2");
		await Assert.That(await Eval($"feedinfo({_kind},messages)")).IsEqualTo("3");
		var stored = long.Parse(await Eval($"feedinfo({_kind},stored)"));
		await Assert.That(stored).IsEqualTo(long.Parse(await Eval($"feedinfo({_kind}/1,stored)")) + long.Parse(await Eval($"feedinfo({_kind}/2,stored)")));
		await Assert.That(stored).IsGreaterThan(long.Parse(await Eval($"feedinfo({_kind},bytes)")));
		await Assert.That(await Cmd($"@feed/info {_kind}/1")).Contains("2, ").And.Contains(" stored");

		// Nothing is written to either feed again; the upkeep pass alone ages /1.
		await Cmd($"@feed/set {_kind}/1/max_age=1h");
		var feeds = WebAppFactoryArg.Services.GetRequiredService<IFeedService>();
		await feeds.PurgeExpiredAsync(DateTimeOffset.UtcNow.AddHours(2));
		await Assert.That(await Eval($"feedinfo({_kind}/1,messages)")).IsEqualTo("0");
		await Assert.That(await Eval($"feedinfo({_kind}/1,stored)")).IsEqualTo("0");
		await Assert.That(await Eval($"feedinfo({_kind}/2,messages)")).IsEqualTo("1").Because("a feed with no max_age keeps its lines");
	}

	[Test]
	public async Task TheChannelKind_IsTheEnginesOwn()
	{
		await Assert.That(await Cmd($"@feed/define channel={Ref(_system)}")).Contains("the engine's own kind");
		await Assert.That(await Eval("feedinfo(channel,owner)")).IsEqualTo("#-1 NO SUCH FEED KIND");
	}

	[Test]
	public async Task Rename_MovesLinesAndMembers_AndMergesIntoAFeedAlreadyThereOnlyWithOverride()
	{
		await Heard(_system, $"@feed/join {_kind}/old={Ref(_ann)}");
		await Heard(_system, $"@feed/send {_kind}/old=one");
		await Heard(_system, $"@feed/send {_kind}/old=two");
		var ids = await EvalAs(_system.DbRef, $"feedrecall({_kind}/old,0)");

		await Assert.That(await Heard(_system, $"@feed/rename {_kind}/old=new")).Contains($"Moved {_kind}/old to {_kind}/new");
		await Assert.That(await EvalAs(_system.DbRef, $"feeds({_kind})")).IsEqualTo($"{_kind}/new");
		await Assert.That(await EvalAs(_system.DbRef, $"feedrecall({_kind}/new,0)")).IsEqualTo(ids);
		await Assert.That(await EvalAs(_system.DbRef, $"feedsof({Ref(_ann)},{_kind})")).IsEqualTo($"{_kind}/new");
		await Assert.That(await EvalAs(_system.DbRef, $"feedunread({_kind}/new,{Ref(_ann)})")).IsEqualTo("2");

		await Heard(_system, $"@feed/join {_kind}/other={Ref(_bo)}");
		await Heard(_system, $"@feed/send {_kind}/other=three");
		await Assert.That(await Heard(_system, $"@feed/rename {_kind}/other=new")).Contains("@feed/rename/override merges");
		await Assert.That(await EvalAs(_system.DbRef, $"words(feedrecall({_kind}/new,0))")).IsEqualTo("2");
		await Assert.That(await Heard(_system, $"@feed/join/override {_kind}/new={Ref(_bo)}")).Contains("/override goes with @feed/rename");
		await Heard(_system, $"@feed/rename/override {_kind}/other=new");
		await Assert.That(await EvalAs(_system.DbRef, $"iter(feedrecall({_kind}/new,0),feedmsg(##,text))")).IsEqualTo("one two three");
		await Assert.That(await EvalAs(_system.DbRef, $"words(feedwho({_kind}/new))")).IsEqualTo("2");

		await Assert.That(await Heard(_system, $"@feed/rename {_kind}/new=a b")).Contains("is not a feed key");
		await Assert.That(await Heard(_system, $"@feed/rename {_kind}/new=other{_kind}/x")).Contains("its own kind");
	}
}
