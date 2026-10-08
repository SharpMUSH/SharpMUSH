using SharpMUSH.Library.Models;

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
		await Assert.That(await Heard(_system, $"@feed/send {_kind}/1={text}")).IsEqualTo("");

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
		await Heard(_system, $"@feed/hide {_kind}/a={Ref(_bo)}");

		await Assert.That(await EvalAs(_system.DbRef, $"feedsof({Ref(_ann)},{_kind})")).IsEqualTo($"{_kind}/a {_kind}/b");
		await Assert.That(await EvalAs(_system.DbRef, $"feeds({_kind})")).IsEqualTo($"{_kind}/a {_kind}/b");
		await Assert.That(await EvalAs(_system.DbRef, $"words(feedwho({_kind}/a))")).IsEqualTo("2");
		await Assert.That(await EvalAs(_system.DbRef, $"feedwho({_kind}/a,hide)")).IsEqualTo(await Objid(_bo));
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
}
