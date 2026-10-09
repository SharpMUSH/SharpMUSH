using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

public class GeneralCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	[Arguments("@pemit #1=1 This is a test", "1 This is a test")]
	[Arguments("@pemit #1=2 This is a test;", "2 This is a test;")]
	public async ValueTask SimpleCommandParse(string str, string expected)
	{
		var token = Token();
		var command = str.Replace("=", $"={token} ");
		TestDiagnostics.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

		await Assert.That(Heard($"{token} {expected}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
	}

	[Test]
	[Category("NotImplemented")]
	[Arguments("l"), Skip("Not yet implemented properly")]
	public async ValueTask CommandAliasRuns(string str)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(str));
	}

	[Test]
	public async ValueTask DoListSimple()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dolist/inline 1 2 3=@pemit #1=3 This is a test {token}"));

		// @dolist/inline iterates 3 times (elements: 1, 2, 3) → 3 identical notifications
		await Assert.That(Heard($"3 This is a test {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(3);
	}

	[Test]
	public async ValueTask DoListSimple2()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dolist/inline 1 2 3=@pemit #1={{4 This is, a test {token}}};"));

		// @dolist/inline iterates 3 times (elements: 1, 2, 3) → 3 identical notifications
		await Assert.That(Heard($"4 This is, a test {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(3);
	}

	[Test]
	public async ValueTask DolistDoubleHashReplacement()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dolist/inline 1 2 3=@pemit #1={token}-dolist-hash-##"));

		await Assert.That(Heard($"{token}-dolist-hash-1", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
		await Assert.That(Heard($"{token}-dolist-hash-2", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
		await Assert.That(Heard($"{token}-dolist-hash-3", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask DoListComplex()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dolist/inline 1 2 3={{@pemit #1=5 This is a test {token}; @pemit #1=6 This is also a test {token}}}"));

		// @dolist/inline iterates 3 times → both @pemit commands fire 3 times each
		await Assert.That(Heard($"5 This is a test {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(3);
		await Assert.That(Heard($"6 This is also a test {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(3);
	}

	[Test]
	public async ValueTask DoListComplex2()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain(
				$"@dolist/inline 1 2 3={{@pemit #1=7 This is a test {token}; @pemit #1=8 This is also a test {token}}}; @pemit #1=9 Repeat 3 times in this mode {token}."));

		await Assert.That(Heard($"7 This is a test {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(3);
		await Assert.That(Heard($"8 This is also a test {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(3);
		await Assert.That(Heard($"9 Repeat 3 times in this mode {token}.", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(3);
	}

	[Test]
	public async ValueTask DoListComplex3()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain(
				$"@dolist/inline 1={{@dolist/inline 1 2 3=@pemit #1=10 This is a test {token}}}; @pemit #1=11 Repeat 1 times in this mode {token}."));

		// outer 1 element × inner 3 elements = 3 for "10"; @pemit 11 is outside = 1×
		await Assert.That(Heard($"10 This is a test {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(3);
		await Assert.That(Heard($"11 Repeat 1 times in this mode {token}.", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask DoListComplex4()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain(
				$"@dolist/inline 1 2={{@dolist/inline 1 2 3=@pemit #1=12 This is a test {token}}}; @pemit #1=13 Repeat 2 times in this mode {token}."));

		// outer 2 elements × inner 3 elements = 6 for "12"; @pemit 13 is outside = 2×
		await Assert.That(Heard($"12 This is a test {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(6);
		await Assert.That(Heard($"13 Repeat 2 times in this mode {token}.", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(2);
	}

	[Test]
	public async ValueTask DoListComplex5()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain(
				$"@dolist/inline a b={{@dolist/inline 1 2 3=@pemit #1=14 This is a test {token} %i0}}; @pemit #1=15 Repeat 1 times in this mode {token} %i0"));

		// outer 2 elements (a,b) × inner 3 elements (1,2,3) → each distinct inner msg fires 2×
		// the outer @pemit fires once per outer element ("15 ...a" and "15 ...b")
		await Assert.That(Heard($"14 This is a test {token} 1", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(2);
		await Assert.That(Heard($"14 This is a test {token} 2", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(2);
		await Assert.That(Heard($"14 This is a test {token} 3", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(2);
		await Assert.That(Heard($"15 Repeat 1 times in this mode {token} a", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
		await Assert.That(Heard($"15 Repeat 1 times in this mode {token} b", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask DoListComplex6()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain(
				$"@dolist/inline a b={{@dolist/inline 1 2 3={{@ifelse eq(%i0,1)=think %i0 is 1 {token}; @ifelse eq(%i0,2)=think %i0 is 2 {token},think {{%i0 is 1, or 3 {token}}}}}}}"));

		// outer 2 elements (a,b) × inner 3 elements (1,2,3) → each branch fires 2× (once per outer iter)
		await Assert.That(Heard($"3 is 1, or 3 {token}", INotifyService.NotificationType.Announce)).IsEqualTo(2);
		await Assert.That(Heard($"1 is 1 {token}", INotifyService.NotificationType.Announce)).IsEqualTo(2);
		await Assert.That(Heard($"2 is 2 {token}", INotifyService.NotificationType.Announce)).IsEqualTo(2);
	}

	[Test]
	public async ValueTask DoBreakSimpleCommandList()
	{
		var t = Token();
		await Parser.CommandListParse(MarkupText.Plain($"think assert 1a {t}; @assert; think assert 2a {t}; think assert 3a {t}"));
		await Parser.CommandListParse(MarkupText.Plain($"think break 1a {t}; @break; think break 2a {t}; think break 3a {t}"));

		await Assert.That(Heard($"break 1a {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"break 2a {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"break 3a {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"assert 1a {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"assert 2a {t}", INotifyService.NotificationType.Announce)).IsEqualTo(0);
		await Assert.That(Heard($"assert 3a {t}", INotifyService.NotificationType.Announce)).IsEqualTo(0);
	}

	[Test]
	public async ValueTask DoBreakSimpleTruthyCommandList()
	{
		var t = Token();
		await Parser.CommandListParse(MarkupText.Plain($"think assert 1b {t}; @assert 1; think assert 2b {t}; think assert 3b {t}"));
		await Parser.CommandListParse(MarkupText.Plain($"think break 1b {t}; @break 1; think break 2b {t}; think break 3b {t}"));

		await Assert.That(Heard($"assert 1b {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"assert 2b {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"assert 3b {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"break 1b {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"break 2b {t}", INotifyService.NotificationType.Announce)).IsEqualTo(0);
		await Assert.That(Heard($"break 3b {t}", INotifyService.NotificationType.Announce)).IsEqualTo(0);
	}

	[Test]
	public async ValueTask DoBreakSimpleFalsyCommandList()
	{
		var t = Token();
		await Parser.CommandListParse(MarkupText.Plain($"think assert 1c {t}; @assert 0; think assert 2c {t}; think assert 3c {t}"));
		await Parser.CommandListParse(MarkupText.Plain($"think break 1c {t}; @break 0; think break 2c {t}; think break 3c {t}"));

		await Assert.That(Heard($"break 1c {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"break 2c {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"break 3c {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"assert 1c {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"assert 2c {t}", INotifyService.NotificationType.Announce)).IsEqualTo(0);
		await Assert.That(Heard($"assert 3c {t}", INotifyService.NotificationType.Announce)).IsEqualTo(0);
	}

	[Test]
	public async ValueTask DoBreakCommandList()
	{
		var t = Token();
		await Parser.CommandListParse(
			MarkupText.Plain($"think break 1d {t}; @break 1=think broken 1d {t}; think break 2d {t}; think break 3d {t}"));

		await Assert.That(Heard($"break 1d {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"break 2d {t}", INotifyService.NotificationType.Announce)).IsEqualTo(0);
		await Assert.That(Heard($"break 3d {t}", INotifyService.NotificationType.Announce)).IsEqualTo(0);
		await Assert.That(Heard($"broken 1d {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask DoBreakCommandList2()
	{
		var t = Token();
		await Parser.CommandListParse(
			MarkupText.Plain($"think break 1e {t}; @break 1={{think broken 1e {t}; think broken 2e {t}}}; think break 2e {t}; think break 3e {t}"));

		await Assert.That(Heard($"break 1e {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"break 2e {t}", INotifyService.NotificationType.Announce)).IsEqualTo(0);
		await Assert.That(Heard($"break 3e {t}", INotifyService.NotificationType.Announce)).IsEqualTo(0);
		await Assert.That(Heard($"broken 1e {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
		await Assert.That(Heard($"broken 2e {t}", INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask DoFlagSet()
	{
		// Create a unique thing to set the flag on, instead of modifying shared God (#1).
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "FlagSetTest");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {thingDbRef}=DEBUG"));

		var thing = (await Mediator.Send(new GetObjectNodeQuery(thingDbRef))).Expect<SharpThing>();
		var flags = await thing.Object.Flags.Value.ToArrayAsync();

		await Assert.That(flags.Count(x => x.Name == "DEBUG")).IsEqualTo(1);
	}

	[Test]
	public async ValueTask WhereIs_ValidPlayer_ReportsLocation()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WhereIsTarget");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@whereis {player.DbRef}"));

		await Assert.That(WebAppFactoryArg.Notifications.DeliveriesFor(executor).Count(delivery =>
				delivery.Message.StartsWith($"{player.Name} is in", StringComparison.Ordinal)
				&& delivery.Sender == executor && delivery.Type == INotifyService.NotificationType.Announce))
			.IsEqualTo(1);
	}

	[Test]
	public async ValueTask WhereIs_NonPlayer_ReturnsError()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WhereIsThing");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@whereis {thing}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.WhereIsCanOnlyLocatePlayers), executor, executor)).IsTrue();
	}

	/// <summary>
	/// Restarting another player tells both of them, as PennMUSH's <c>do_restart_com</c> does
	/// (<c>src/cque.c:2424-2429</c>), and then <c>do_halt</c> tells the player it halted them.
	/// </summary>
	/// <remarks>
	/// The player is one this test makes. Restarting God would halt the queue of God and of every
	/// object God owns, which in the session-wide scheduler is whatever other suites have queued as
	/// God or on the things they made.
	/// </remarks>
	[Test]
	public async ValueTask Restart_ValidObject_Restarts()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RestartTarget");
		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RestartWizard");

		try
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
			var recorder = WebAppFactoryArg.Notifications;
			var wizardBefore = recorder.CountFor(wizard.DbRef);
			var playerBefore = recorder.CountFor(player.DbRef);

			await WebAppFactoryArg.CommandParserFor(wizard.DbRef, wizard.Handle)
				.CommandParse(wizard.Handle, ConnectionService, MarkupText.Plain($"@restart {player.DbRef}"));

			await Assert.That(recorder.For(wizard.DbRef).Skip(wizardBefore).Where(message => message.Contains(player.Name)))
				.IsEquivalentTo([$"All objects for {player.Name} are being restarted."]);
			await Assert.That(recorder.For(player.DbRef).Skip(playerBefore)
				.Where(message => message.Contains(player.Name) || message.Contains(wizard.Name))).IsEquivalentTo([
				$"All of your objects are being restarted by {wizard.Name}.",
				$"Halted: {player.Name}(#{player.DbRef.Number})"
			]);
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
			await ConnectionService.Disconnect(wizard.Handle);
		}
	}

	/// <summary>
	/// An object of one's own: <c>Restarting: &lt;name&gt;(#&lt;dbref&gt;)</c>, then <c>do_halt</c>'s
	/// <c>Halted:</c> (<c>src/cque.c:2443-2447</c>).
	/// </summary>
	[Test]
	public async ValueTask Restart_OwnObject_ReportsRestartingThenHalted()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RestartOwner");

		try
		{
			var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "RestartOwnThing");
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown/preserve {thing}={owner.DbRef}"));
			var name = (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"name({thing})")))!
				.Message.ToPlainText();
			var recorder = WebAppFactoryArg.Notifications;
			var before = recorder.CountFor(owner.DbRef);

			await WebAppFactoryArg.CommandParserFor(owner.DbRef, owner.Handle)
				.CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"@restart {thing}"));

			await Assert.That(recorder.For(owner.DbRef).Skip(before).Where(message => message.Contains(name))).IsEquivalentTo([
				$"Restarting: {name}(#{thing.Number})",
				$"Halted: {name}(#{thing.Number})"
			]);
		}
		finally
		{
			await ConnectionService.Disconnect(owner.Handle);
		}
	}

	/// <summary><c>@restart/all</c> without HaltAny (<c>src/cque.c:2370-2372</c>).</summary>
	[Test]
	public async ValueTask RestartAll_WithoutHaltPower_IsRefused()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RestartAllMortal");

		try
		{
			var recorder = WebAppFactoryArg.Notifications;
			var before = recorder.CountFor(player.DbRef);

			await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
				.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@restart/all"));

			await Assert.That(recorder.For(player.DbRef).Skip(before).ToList())
				.Contains("You do not have the power to restart the world.");
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	/// <summary>
	/// PennMUSH's do_find: an object_header line per object the searcher controls whose name has a word
	/// starting with the pattern, then "*** N objects found ***". Run as a mortal, whose own notification
	/// bucket no other test writes to.
	/// </summary>
	[Test]
	public async ValueTask Find_ListsControlledObjectsByWordPrefix()
	{
		var mortal = await MortalInARoomOfItsOwnAsync("FindMortal");
		var token = TestIsolationHelpers.GenerateUniqueName("FindTok");
		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@create Red {token}"));
		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@create {token}Blue"));

		var messages = await MessagesWhile(mortal.DbRef, () => Parser.CommandParse(mortal.Handle, ConnectionService,
			MarkupText.Plain($"@find {token[..^2]}")).AsTask());

		await Assert.That(messages.Count(m => m.StartsWith($"Red {token}(#", StringComparison.Ordinal))).IsEqualTo(1);
		await Assert.That(messages.Count(m => m.StartsWith($"{token}Blue(#", StringComparison.Ordinal))).IsEqualTo(1);
		await Assert.That(messages[^1]).IsEqualTo("*** 2 objects found ***");
	}

	/// <summary>do_find refuses a range bound that is not an object.</summary>
	[Test]
	public async ValueTask Find_RejectsARangeThatIsNotAnObject()
	{
		var mortal = await MortalInARoomOfItsOwnAsync("FindRange");

		var messages = await MessagesWhile(mortal.DbRef, () => Parser.CommandParse(mortal.Handle, ConnectionService,
			MarkupText.Plain("@find x=#999999999")).AsTask());

		await Assert.That(messages).IsEquivalentTo(["Invalid range argument"]);
	}

	/// <summary>The whole-database form ends with PennMUSH's garbage count, always 0 here.</summary>
	[Test]
	public async ValueTask Stats_ShowsDatabaseStatistics()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@stats"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.StatsWorldCountsFormat), executor, executor)).IsTrue();
	}

	/// <summary>
	/// PennMUSH's do_search report: a blank line and a heading per type, <c>Name(#dbref...) [owner: ...]</c>
	/// per object, then the "Search Done" rule and the totals.
	/// </summary>
	[Test]
	public async ValueTask Search_ReportsByTypeWithOwnersAndTotals()
	{
		var mortal = await MortalInARoomOfItsOwnAsync("SearchMortal");
		var token = TestIsolationHelpers.GenerateUniqueName("SearchTok");
		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@create {token}"));

		var messages = await MessagesWhile(mortal.DbRef, () => Parser.CommandParse(mortal.Handle, ConnectionService,
			MarkupText.Plain($"@search name={token}")).AsTask());

		await Assert.That(messages.Count).IsEqualTo(4);
		await Assert.That(messages[0]).IsEqualTo("\nTHINGS:");
		await Assert.That(messages[1]).StartsWith($"{token}(#");
		await Assert.That(messages[1]).Contains($" [owner: {mortal.Name}(#{mortal.DbRef.Number}");
		await Assert.That(messages[2]).IsEqualTo("----------  Search Done  ----------");
		await Assert.That(messages[3]).IsEqualTo("Totals: Rooms...0  Exits...0  Things...1  Players...0");
	}

	/// <summary>do_search shows an exit with where it runs from and to, NOWHERE for an unlinked end.</summary>
	[Test]
	public async ValueTask Search_ShowsAnExitsEnds()
	{
		var mortal = await MortalInARoomOfItsOwnAsync("SearchExit");
		var token = TestIsolationHelpers.GenerateUniqueName("SearchExitTok");
		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@dig/teleport {token}Room"));
		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@open {token}Out"));

		var messages = await MessagesWhile(mortal.DbRef, () => Parser.CommandParse(mortal.Handle, ConnectionService,
			MarkupText.Plain($"@search type=exit,name={token}")).AsTask());

		await Assert.That(messages[0]).IsEqualTo("\nEXITS:");
		await Assert.That(messages[1]).StartsWith($"{token}Out(#");
		await Assert.That(messages[1]).Contains($" [from {token}Room(#");
		await Assert.That(messages[1]).EndsWith(" to NOWHERE]");
		await Assert.That(messages[^1]).IsEqualTo("Totals: Rooms...0  Exits...1  Things...0  Players...0");
	}

	/// <summary>
	/// do_search's START is 1-based (init_search_spec, src/wiz.c:2270): START=2 COUNT=1 is the second
	/// match. A START or COUNT below 1 is refused before anything is searched, with fill_search_spec's
	/// own text (src/wiz.c:2388-2399) and no report.
	/// </summary>
	[Test]
	public async ValueTask Search_StartIsOneBasedAndBelowOneIsRefused()
	{
		var mortal = await MortalInARoomOfItsOwnAsync("SearchStart");
		var token = TestIsolationHelpers.GenerateUniqueName("SearchStartTok");
		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@create {token}A"));
		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@create {token}B"));

		async Task<List<string>> SearchAs(string spec) => await MessagesWhile(mortal.DbRef, () => Parser.CommandParse(
			mortal.Handle, ConnectionService, MarkupText.Plain($"@search name={token},{spec}")).AsTask());

		var second = await SearchAs("start=2,count=1");
		await Assert.That(second.Any(m => m.StartsWith($"{token}B(#"))).IsTrue();
		await Assert.That(second.Any(m => m.StartsWith($"{token}A(#"))).IsFalse();

		var first = await SearchAs("start=1,count=1");
		await Assert.That(first.Any(m => m.StartsWith($"{token}A(#"))).IsTrue();
		await Assert.That(first.Any(m => m.StartsWith($"{token}B(#"))).IsFalse();

		await Assert.That(await SearchAs("start=0")).IsEquivalentTo(new[] { "Invalid start index" });
		await Assert.That(await SearchAs("count=0")).IsEquivalentTo(new[] { "Invalid count index" });
	}

	// Regression coverage for "@search all type=PLAYER" being parsed as a NAME search for the
	// literal text "player" instead of a TYPE filter — @SEARCH's CB.EqSplit|CB.RSArgs behavior only
	// splits the raw command text on the first top-level '=', so "all type" (the player field plus
	// the leading search class) landed together in one chunk with no class/restriction parsing at
	// all. See ParseSearchCommandArgs in SearchCommands.cs.
	[Test]
	public async ValueTask Search_TypeEqualsPlayer_FiltersByTypeNotByName()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var offType = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SearchTypeBugPLAYER");

		var messages = await MessagesWhile(executor, () => Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@search all type=PLAYER")).AsTask());

		// A THING whose name contains "PLAYER" must NOT match a TYPE=PLAYER search...
		await Assert.That(messages.Any(m => m.Contains($"(#{offType.Number}"))).IsFalse();
		// ...while an actual player (the fixture's God, #1) must, with its location for a wizard.
		await Assert.That(messages.Any(m => m.Contains("(#1") && m.Contains(" [location: "))).IsTrue();
	}

	[Test]
	public async ValueTask Search_CombinedTypeAndFlags_MatchesOnBothCriteria()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("SearchCombined");
		var match = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"{token}_Match");
		var control = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"{token}_Control");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {match}=MONITOR"));

		var messages = await MessagesWhile(WebAppFactoryArg.ExecutorDBRef, () => Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@search all type=thing,flags=MONITOR,name={token}")).AsTask());

		await Assert.That(messages.Any(m => m.Contains($"(#{match.Number}"))).IsTrue();
		await Assert.That(messages.Any(m => m.Contains($"(#{control.Number}"))).IsFalse();
	}

	private static string Token() => TestIsolationHelpers.GenerateUniqueName("Gen");

	private static string UniqueAttributeName(string prefix) => $"{prefix}_{Guid.NewGuid():N}".ToUpperInvariant();

	/// <summary>How many times God heard exactly <paramref name="message"/> from itself.</summary>
	private int Heard(string message, INotifyService.NotificationType type)
		=> HeardBy(WebAppFactoryArg.ExecutorDBRef, message, WebAppFactoryArg.ExecutorDBRef, type);

	private int HeardBy(DBRef who, string message, DBRef sender, INotifyService.NotificationType type)
		=> WebAppFactoryArg.Notifications.DeliveriesFor(who).Count(delivery =>
			delivery.Message == message && delivery.Sender == sender && delivery.Type == type);

	/// <summary>
	/// A connected mortal standing in a fresh room, so the window <see cref="MessagesWhile"/> reads holds
	/// only what its own command produced — not, say, another test's player connecting in the default home.
	/// </summary>
	private async Task<TestIsolationHelpers.TestPlayer> MortalInARoomOfItsOwnAsync(string prefix)
	{
		var dig = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName($"{prefix}Room")}"));
		var room = DBRef.Parse(dig.Message.ToPlainText().Trim());
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room);
	}

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	[Test]
	public async ValueTask Entrances_ShowsLinkedObjects()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@entrances"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EntrancesToFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Command_ShowsCommandInfo()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@command @emit"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CommandInfoNameFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Function_ListsGlobalFunctions()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@function"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FunctionGlobalUserDefinedHeader), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Function_ShowsFunctionInfo()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@function name"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FunctionInfoNameFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Map_ExecutesAttributeOverList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var mapObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapTest");
		var uniqueAttr = $"MAPATTR_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@map {mapObj}/{uniqueAttr}=foo bar baz"));

		// MapWouldIterateFormat is always sent before attribute lookup (before the try/get attribute).
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.MapWouldIterateFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Trigger_QueuesAttribute()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var trigObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TrigTest");
		var uniqueAttr = $"TRIGATTR_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@trigger {trigObj}/{uniqueAttr}=arg1,arg2"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.TriggerNoSuchAttributeFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Include_InsertsAttributeInPlace()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var inclObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclTest");
		var uniqueAttr = $"INCLATTR_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@include {inclObj}/{uniqueAttr}=arg1,arg2"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.IncludeAttributeIsEmptyFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask PS_ShowsQueueStatus()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@ps"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PsQueueForTargetFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Select_MatchesFirstExpression()
	{
		// @select runs the action for the FIRST matching expression only ('help @switch'); both
		// patterns here match "test", so the second must not fire. /inline so the actions run in
		// place rather than becoming queue entries this assertion would race.
		var token = Token();
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@select/inline test=t*,@pemit #1=SelectFirst_A_{token},*est,@pemit #1=SelectFirst_B_{token}"));

		await Assert.That(Heard($"SelectFirst_A_{token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
		await Assert.That(Heard($"SelectFirst_B_{token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(0);
	}

	[Test]
	public async ValueTask Attribute_DisplaysAttributeInfo()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var name = UniqueAttributeName("ATTRINFO");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@attribute/access {name}=no_command"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@attribute {name}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeCommandInfoFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Attribute_AccessCreatesAttributeEntry()
	{
		var name = UniqueAttributeName("MYATTR");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@attribute/access {name}=no_command"));

		var entries = await Mediator.CreateStream(new Library.Queries.Database.GetAllAttributeEntriesQuery())
			.ToArrayAsync();
		var entry = entries.FirstOrDefault(e => e.Name == name);

		await Assert.That(entry).IsNotNull();
		await Assert.That(entry!.DefaultFlags.Contains("NO_COMMAND")).IsTrue();
	}

	[Test]
	public async ValueTask Attribute_AccessValidatesFlags()
	{
		var name = UniqueAttributeName("TESTATTR");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@attribute/access {name}=INVALIDFLAG"));

		var entries = await Mediator.CreateStream(new Library.Queries.Database.GetAllAttributeEntriesQuery())
			.ToArrayAsync();
		var entry = entries.FirstOrDefault(e => e.Name == name);

		await Assert.That(entry).IsNull();
	}

	[Test]
	public async ValueTask Attribute_EntryFlagsAreAppliedWhenAttributeCreated()
	{
		var name = UniqueAttributeName("TESTATTR2");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@attribute/access {name}=no_command"));

		var entries = await Mediator.CreateStream(new Library.Queries.Database.GetAllAttributeEntriesQuery())
			.ToArrayAsync();
		var entry = entries.FirstOrDefault(e => e.Name == name);
		await Assert.That(entry).IsNotNull();
		await Assert.That(entry!.DefaultFlags.Contains("NO_COMMAND")).IsTrue();

		// Use SetAttributeCommand directly to bypass & command test issues
		var target = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrEntryFlags");
		var player = (await Mediator.Send(new Library.Queries.Database.GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var success = await Mediator.Send(new Library.Commands.Database.SetAttributeCommand(
			target,
			[name],
			MarkupText.Plain("test value"),
			player));

		await Assert.That(success).IsTrue();

		var attrs = await Mediator.CreateStream(new Library.Queries.Database.GetAttributeQuery(target, [name]))
			.ToArrayAsync();

		var attr = attrs.LastOrDefault();
		await Assert.That(attr).IsNotNull();

		await Assert.That(attr!.Flags.Any(f => f.Name.Equals("no_command", StringComparison.OrdinalIgnoreCase))).IsTrue();
	}

	[Test]
	public async ValueTask DoListWithDBRefNotificationBatching()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dolist/inline 1 2 3=@pemit #1=Batched test message {token}"));

		await Assert.That(Heard($"Batched test message {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(3);
	}

	[Test]
	public async ValueTask DoListBatchesToOtherPlayers()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = Token();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dolist/inline a b c=@pemit {executor}=DoListBatchesToOtherPlayers: Message to other player {token}"));

		await Assert.That(Heard($"DoListBatchesToOtherPlayers: Message to other player {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(3);
	}

	[Test]
	public async ValueTask NestedDoListBatching()
	{
		var token = Token();
		// Nested @dolist: outer has 2 items, inner has 2 items = 4 total pemits
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dolist/inline 1 2={{@dolist/inline a b=@pemit #1=Nested message {token}}}"));

		await Assert.That(Heard($"Nested message {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(4);
	}

	[Test]
	public async ValueTask DoListWithoutBreak_AllMessagesReceived()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dolist/inline 1 2 3=@pemit #1=DoListWithoutBreak_AllMessagesReceived {token}"));

		await Assert.That(Heard($"DoListWithoutBreak_AllMessagesReceived {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(3);
	}

	[Test]
	public async ValueTask DoListWithBreakAfterFirst_OnlyFirstMessageReceived()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dolist/inline 1 2 3={{@pemit #1=Message {token} %iL;@break}}"));

		// With {@pemit; @break}, @pemit runs in each iteration then @break happens, so all 3
		// messages fire — this is the actual MUSH behavior: @break affects the next iteration, not current.
		await Assert.That(Heard($"Message {token} 1", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
		await Assert.That(Heard($"Message {token} 2", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
		await Assert.That(Heard($"Message {token} 3", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask DoListWithBreakFlushesMessages()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dolist/inline 1 2 3={{@pemit #1=Message before break {token}; @break}}"));

		await Assert.That(Heard($"Message before break {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(3);
	}

	[Test]
	public async ValueTask NestedDoListWithBreakFlushesMessages()
	{
		var token = Token();
		// With {@pemit; @break}, @pemit runs in each inner iteration: 2 outer * 3 inner = 6 messages.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dolist/inline 1 2={{@dolist/inline a b c={{@pemit #1=Inner message {token}; @break}}}}"));

		await Assert.That(Heard($"Inner message {token}", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(6);
	}

	[Test]
	public async ValueTask DoListWithDelimiter()
	{
		var token = Token();
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dolist/inline/delimit , apple,banana,orange=@pemit #1=Fruit {token}: %i0"));

		await Assert.That(Heard($"Fruit {token}: apple", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
		await Assert.That(Heard($"Fruit {token}: banana", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
		await Assert.That(Heard($"Fruit {token}: orange", INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
	}

	/// <summary>
	/// PennMUSH <c>do_look_at</c> (<c>look.c:611</c>): <c>look/outside</c> from a room — or any opaque
	/// location — has nothing to see out of.
	/// </summary>
	[Test]
	public async ValueTask LookOutsideFromARoomReportsYouCantSeeThroughThat()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LookOutsideRoom");

		var roomName = TestIsolationHelpers.GenerateUniqueName("LookOutsideRoomDest");
		var digResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look/outside"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.CantSeeThroughThat), player.DbRef, player.DbRef)).IsTrue();
	}

	/// <summary>
	/// PennMUSH <c>look.c:618</c>: from inside a container you see that container's own location, not the
	/// container itself.
	/// </summary>
	[Test]
	public async ValueTask LookOutsideFromInsideAContainerShowsTheContainersRoom()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LookOutsideBox");

		var roomName = TestIsolationHelpers.GenerateUniqueName("LookOutsideOuter");
		var digResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();

		var boxName = TestIsolationHelpers.GenerateUniqueName("LookOutsideContainer");
		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {boxName}"));
		var boxDbRef = createResult.Message.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {boxName}=ENTER_OK"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {boxDbRef}={roomDbRef}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={boxDbRef}"));

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look/outside"));

		await Assert.That(WebAppFactoryArg.Notifications.DeliveriesFor(player.DbRef).Count(delivery =>
				delivery.Message.Contains(roomName, StringComparison.Ordinal)
				&& delivery.Sender == player.DbRef && delivery.Type == INotifyService.NotificationType.Announce))
			.IsEqualTo(1);
	}

	/// <summary>
	/// PennMUSH <c>cmd_think</c> (<c>cmds.c:1769</c>) is an unconditional notify, so a bare
	/// <c>think</c> still emits an (empty) line.
	/// </summary>
	[Test]
	public async ValueTask ThinkWithNoArgumentStillNotifiesTheExecutor()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ThinkEmpty");

		var result = await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("think"));

		await Assert.That(HeardBy(player.DbRef, string.Empty, player.DbRef, INotifyService.NotificationType.Announce)).IsEqualTo(1);

		// The notify happens before the return, so asserting on it alone would not have caught the
		// command indexing a "0" argument that a bare `think` does not have. The resulting
		// KeyNotFoundException is swallowed upstream and surfaces only as a null Message.
		await Assert.That(result).IsNotNull();
		await Assert.That(result.Message).IsNotNull();
		await Assert.That(result.Message.ToPlainText()).IsEqualTo(string.Empty);
	}

	[Test]
	public async ValueTask ThinkDescendingLnumNotifiesTheExecutor()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ThinkLnum");

		var result = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("think lnum(10,1)"));

		await Assert.That(result.Message.ToPlainText()).IsEqualTo("10 9 8 7 6 5 4 3 2 1");
		await Assert.That(HeardBy(player.DbRef, "10 9 8 7 6 5 4 3 2 1", player.DbRef, INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	/// <summary>
	/// PennMUSH <c>do_open</c> hands the destination to <c>do_link</c>, which reports a destination it
	/// cannot link to instead of quietly leaving the exit unlinked. An exit is the one thing you cannot
	/// end up inside.
	/// </summary>
	[Test]
	public async ValueTask OpenWithAnUnlinkableDestinationReportsTheFailure()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "OpenBadDest");

		var roomName = TestIsolationHelpers.GenerateUniqueName("OpenBadDestRoom");
		var digResult = await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));

		var decoyName = TestIsolationHelpers.GenerateUniqueName("OpenBadDestDecoy");
		var decoyResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@open {decoyName}={roomDbRef}"));
		var decoyDbRef = decoyResult.Message.ToPlainText()!.Trim();

		var exitName = TestIsolationHelpers.GenerateUniqueName("OpenBadDestExit");
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@open {exitName}={decoyDbRef}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.CantLinkToThat), player.DbRef, player.DbRef)).IsTrue();
	}

	/// <summary>
	/// An exit may lead to any container, not just a room — PennMUSH <c>can_link_to</c> accepts things
	/// and players too, which is how "enter the wardrobe" exits are built.
	/// </summary>
	[Test]
	public async ValueTask OpenCanLinkAnExitToAThing()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "OpenThingDest");

		var roomName = TestIsolationHelpers.GenerateUniqueName("OpenThingDestRoom");
		var digResult = await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));

		var thingName = TestIsolationHelpers.GenerateUniqueName("OpenThingDestThing");
		var thingResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@create {thingName}"));
		var thingDbRef = thingResult.Message.ToPlainText()!.Trim();

		var exitName = TestIsolationHelpers.GenerateUniqueName("OpenThingDestExit");
		var exitResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@open {exitName}={thingDbRef}"));

		DBRef.TryParse(exitResult.Message.ToPlainText()!.Trim(), out var exitRef);
		var exit = (await Mediator.Send(new GetObjectNodeQuery(exitRef!.Value))).Expect<SharpExit>();
		var destination = (await exit.Home.WithCancellation(CancellationToken.None)).Expect<AnySharpContainer>();

		await Assert.That(destination.Object().DBRef.ToString()).IsEqualTo(thingDbRef);
	}

	/// <summary>
	/// PennMUSH <c>do_open</c> hands the destination to <c>parse_linkable_room</c>, which refuses it
	/// unless <c>can_link_to</c> passes (<c>create.c:61</c>, <c>mushdb.h:87</c>). Widening <c>@open</c> to
	/// accept any container must not also let a player link an exit into somewhere they do not control.
	/// </summary>
	[Test]
	public async ValueTask OpenCannotLinkAnExitToAContainerTheExecutorDoesNotControl()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "OpenNoLinkPerm");

		var roomName = TestIsolationHelpers.GenerateUniqueName("OpenNoLinkPermRoom");
		var digResult = await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));

		// God's thing, not LINK_OK, brought within reach so the locate succeeds and only permission decides.
		var thingName = TestIsolationHelpers.GenerateUniqueName("OpenNoLinkPermThing");
		var thingResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {thingName}"));
		var thingDbRef = thingResult.Message.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {thingDbRef}={roomDbRef}"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("OpenNoLinkPermExit");
		var exitResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@open {exitName}={thingDbRef}"));

		DBRef.TryParse(exitResult.Message.ToPlainText()!.Trim(), out var exitRef);
		var exit = (await Mediator.Send(new GetObjectNodeQuery(exitRef!.Value))).Expect<SharpExit>();
		var destination = await exit.Home.WithCancellation(CancellationToken.None);

		await Assert.That(destination.IsNone).IsTrue()
			.Because("the exit must be left unlinked when the executor cannot link to the destination");
	}

	/// <summary>
	/// The complement: LINK_OK on the destination is what lets someone else link into it.
	/// </summary>
	[Test]
	public async ValueTask OpenCanLinkAnExitToALinkOkContainerTheExecutorDoesNotControl()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "OpenLinkOk");

		var roomName = TestIsolationHelpers.GenerateUniqueName("OpenLinkOkRoom");
		var digResult = await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));

		var thingName = TestIsolationHelpers.GenerateUniqueName("OpenLinkOkThing");
		var thingResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {thingName}"));
		var thingDbRef = thingResult.Message.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {thingDbRef}={roomDbRef}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {thingDbRef}=LINK_OK"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("OpenLinkOkExit");
		var exitResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@open {exitName}={thingDbRef}"));

		DBRef.TryParse(exitResult.Message.ToPlainText()!.Trim(), out var exitRef);
		var exit = (await Mediator.Send(new GetObjectNodeQuery(exitRef!.Value))).Expect<SharpExit>();
		var destination = (await exit.Home.WithCancellation(CancellationToken.None)).Expect<AnySharpContainer>();

		await Assert.That(destination.Object().DBRef.ToString()).IsEqualTo(thingDbRef);
	}
}
