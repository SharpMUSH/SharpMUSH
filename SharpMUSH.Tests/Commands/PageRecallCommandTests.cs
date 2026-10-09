using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>page/recall</c>, <c>page/conversations</c>, <c>pagerecall()</c> and <c>pageconversations()</c>: a
/// player reading their own page log in game (a SharpMUSH extension; PennMUSH keeps no page log). A
/// recalled line reads as the page did when it was delivered, to the one reading it.
/// </summary>
public class PageRecallCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private const string Header = "PAGE: Recall of your pages";
	private const string Footer = "PAGE: End recall";
	private const string NoLog = "This game keeps no page log.";

	private static IDisposable PageLog(bool on) => TestOptionsOverride.Scope(options => options with
	{
		Chat = options.Chat with { PageLog = on }
	});

	/// <summary>
	/// Each recalled line is the line its reader was shown when the page was delivered: "You paged…" and
	/// "Long distance to…" for one's own, "… pages:" and "From afar…" for the other's, oldest first.
	/// </summary>
	[Test]
	public async ValueTask Recall_WithAPlayer_ShowsThatConversationAsEachSideSawIt()
	{
		var alice = await CreatePlayerAsync("PRAlice");
		var bob = await CreatePlayerAsync("PRBob");
		var say = TestIsolationHelpers.GenerateUniqueName("say");
		var pose = TestIsolationHelpers.GenerateUniqueName("pose");
		var semi = TestIsolationHelpers.GenerateUniqueName("semi");
		var reply = TestIsolationHelpers.GenerateUniqueName("reply");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name}={say}");
				await CommandAsync(alice, $"page {bob.Name}=:{pose}");
				await CommandAsync(alice, $"page {bob.Name}=;{semi}");
				await CommandAsync(bob, $"page {alice.Name}={reply}");

				string[] markers = [say, pose, semi, reply];
				var alicesLive = markers.Select(marker => LiveLine(alice, marker)).ToArray();
				var bobsLive = markers.Select(marker => LiveLine(bob, marker)).ToArray();

				var alicesRecall = await RecallAsync(alice, $"page/recall {bob.Name}");
				var bobsRecall = await RecallAsync(bob, $"page/recall {alice.Name}");

				await Assert.That(alicesRecall).IsEquivalentTo(
					[$"{Header} with {bob.Name}:", .. alicesLive, Footer], TUnit.Assertions.Enums.CollectionOrdering.Matching);
				await Assert.That(bobsRecall).IsEquivalentTo(
					[$"{Header} with {alice.Name}:", .. bobsLive, Footer], TUnit.Assertions.Enums.CollectionOrdering.Matching);
				await Assert.That(alicesLive[0]).IsEqualTo($"You paged {bob.Name} with '{say}'")
					.Because("the sender's own page reads as the sender saw it");
				await Assert.That(bobsLive[1]).IsEqualTo($"From afar, {alice.Name} {pose}");
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob);
		}
	}

	/// <summary>A list of players is the group conversation with exactly those others, in any order.</summary>
	[Test]
	public async ValueTask Recall_WithAList_IsTheGroupConversation()
	{
		var alice = await CreatePlayerAsync("PRGroupA");
		var bob = await CreatePlayerAsync("PRGroupB");
		var carol = await CreatePlayerAsync("PRGroupC");
		var group = TestIsolationHelpers.GenerateUniqueName("group");
		var pair = TestIsolationHelpers.GenerateUniqueName("pair");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name} {carol.Name}={group}");
				await CommandAsync(alice, $"page {bob.Name}={pair}");

				var groupRecall = await RecallAsync(bob, $"page/recall {carol.Name} {alice.Name}");
				var pairRecall = await RecallAsync(bob, $"page/recall {alice.Name}");

				await Assert.That(groupRecall.Count(line => line.Contains(group))).IsEqualTo(1);
				await Assert.That(groupRecall.Any(line => line.Contains(pair))).IsFalse()
					.Because("a page to Bob alone is not in the group conversation");
				await Assert.That(groupRecall[1]).IsEqualTo(LiveLine(bob, group));
				await Assert.That(pairRecall.Any(line => line.Contains(group))).IsFalse();
				await Assert.That(pairRecall.Count(line => line.Contains(pair))).IsEqualTo(1);
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob, carol);
		}
	}

	/// <summary>Players are matched as page matches them: by dbref as well as by name.</summary>
	[Test]
	public async ValueTask Recall_MatchesPlayersAsPageDoes()
	{
		var alice = await CreatePlayerAsync("PRMatchA");
		var bob = await CreatePlayerAsync("PRMatchB");
		var message = TestIsolationHelpers.GenerateUniqueName("match");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name}={message}");

				var byDbref = await RecallAsync(alice, $"page/recall #{bob.DbRef.Number}");
				var byCase = await RecallAsync(alice, $"page/recall {bob.Name.ToUpperInvariant()}");

				await Assert.That(byDbref.Count(line => line.Contains(message))).IsEqualTo(1);
				await Assert.That(byCase.Count(line => line.Contains(message))).IsEqualTo(1);
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob);
		}
	}

	[Test]
	public async ValueTask Recall_OfSomeoneUnknown_SaysSo()
	{
		var alice = await CreatePlayerAsync("PRUnknown");
		var nobody = TestIsolationHelpers.GenerateUniqueName("PRNobody");
		try
		{
			using (PageLog(on: true))
			{
				var lines = await RecallAsync(alice, $"page/recall {nobody}");
				await Assert.That(lines).IsEquivalentTo([$"I can't find who you paged with: {nobody}"]);
			}
		}
		finally
		{
			await DisconnectAsync(alice);
		}
	}

	/// <summary>With no player, the latest pages across every conversation, newest last.</summary>
	[Test]
	public async ValueTask Recall_WithoutPlayers_ShowsTheLatestPagesAcrossConversations()
	{
		var alice = await CreatePlayerAsync("PRAllA");
		var bob = await CreatePlayerAsync("PRAllB");
		var carol = await CreatePlayerAsync("PRAllC");
		var one = TestIsolationHelpers.GenerateUniqueName("one");
		var two = TestIsolationHelpers.GenerateUniqueName("two");
		var three = TestIsolationHelpers.GenerateUniqueName("three");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name}={one}");
				await CommandAsync(alice, $"page {carol.Name}={two}");
				await CommandAsync(bob, $"page {alice.Name}={three}");

				var all = await RecallAsync(alice, "page/recall");
				var lastTwo = await RecallAsync(alice, "page/recall =2");

				await Assert.That(all).IsEquivalentTo(
					[$"{Header}:", LiveLine(alice, one), LiveLine(alice, two), LiveLine(alice, three), Footer],
					TUnit.Assertions.Enums.CollectionOrdering.Matching);
				await Assert.That(lastTwo).IsEquivalentTo(
					[$"{Header}:", LiveLine(alice, two), LiveLine(alice, three), Footer],
					TUnit.Assertions.Enums.CollectionOrdering.Matching);
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob, carol);
		}
	}

	/// <summary>Ten lines unless asked for another number; the newest of them.</summary>
	[Test]
	public async ValueTask Recall_ShowsTenLinesByDefault_AndTheNumberAskedFor()
	{
		var alice = await CreatePlayerAsync("PRLinesA");
		var bob = await CreatePlayerAsync("PRLinesB");
		var marker = TestIsolationHelpers.GenerateUniqueName("line");
		try
		{
			using (PageLog(on: true))
			{
				for (var i = 0; i < 12; i++)
				{
					await CommandAsync(alice, $"page {bob.Name}={marker}-{i:00}");
				}

				var byDefault = await RecallAsync(alice, $"page/recall {bob.Name}");
				var three = await RecallAsync(alice, $"page/recall {bob.Name}=3");

				var defaultBody = byDefault[1..^1];
				await Assert.That(defaultBody.Length).IsEqualTo(10);
				await Assert.That(defaultBody[0]).Contains($"{marker}-02");
				await Assert.That(defaultBody[^1]).Contains($"{marker}-11");
				await Assert.That(three[1..^1].Select(line => line[(line.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..]))
					.IsEquivalentTo(["-09'", "-10'", "-11'"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob);
		}
	}

	/// <summary>A count that is not a whole number is refused as <c>@channel/recall</c> refuses one.</summary>
	[Test]
	[Arguments("abc")]
	[Arguments("-1")]
	public async ValueTask Recall_RefusesABadLineCount(string count)
	{
		var alice = await CreatePlayerAsync("PRBadCount");
		try
		{
			using (PageLog(on: true))
			{
				await Assert.That(await RecallAsync(alice, $"page/recall ={count}"))
					.IsEquivalentTo(["How many lines did you want to recall?"]);
			}
		}
		finally
		{
			await DisconnectAsync(alice);
		}
	}

	/// <summary>/timestamps stamps each line as <c>@channel/recall</c> stamps its lines.</summary>
	[Test]
	public async ValueTask Recall_WithTimestamps_StampsEachLineWithWhenItWasSent()
	{
		var alice = await CreatePlayerAsync("PRStampA");
		var bob = await CreatePlayerAsync("PRStampB");
		var message = TestIsolationHelpers.GenerateUniqueName("stamped");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name}={message}");
				var sent = (await Mediator.Send(new GetPageLogQuery(bob.DbRef, [alice.DbRef], 1))).Single();

				var stamped = await RecallAsync(bob, $"page/recall/timestamps {alice.Name}");

				await Assert.That(stamped[1])
					.IsEqualTo($"[{TimeFormatting.ShowTime(sent.Timestamp)}] {LiveLine(bob, message)}");
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob);
		}
	}

	/// <summary>
	/// The page alias the page carried (<c>page_aliases</c>) is in the recalled line too: the recipient read
	/// the pager's name with it, and the pager's own pose line names them without it.
	/// </summary>
	[Test]
	public async ValueTask Recall_KeepsThePageAliasThePageCarried()
	{
		var alice = await CreatePlayerAsync("PRAliasA");
		var bob = await CreatePlayerAsync("PRAliasB");
		var message = TestIsolationHelpers.GenerateUniqueName("aliased");
		try
		{
			var page = new SharpPage(1, alice.DbRef, $"{alice.Name} (al)", [bob.DbRef], [bob.Name], "pose", message,
				DateTimeOffset.UtcNow, SenderPlainName: alice.Name);
			await Mediator.Send(new RecordPageCommand(page, [alice.DbRef, bob.DbRef]));

			using (PageLog(on: true))
			{
				var bobs = await RecallAsync(bob, $"page/recall {alice.Name}");
				var alices = await RecallAsync(alice, $"page/recall {bob.Name}");

				await Assert.That(bobs[1]).IsEqualTo($"From afar, {alice.Name} (al) {message}");
				await Assert.That(alices[1]).IsEqualTo($"Long distance to {bob.Name}: {alice.Name} {message}");
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob);
		}
	}

	/// <summary>The page command keeps the pager's own name beside the name the page gave them.</summary>
	[Test]
	public async ValueTask APage_KeepsThePagersOwnName()
	{
		var alice = await CreatePlayerAsync("PROwnName");
		var bob = await CreatePlayerAsync("PROwnNameB");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name}=hello");
			}

			var page = (await Mediator.Send(new GetPageLogQuery(alice.DbRef, [bob.DbRef], 1))).Single();
			await Assert.That(page.SenderPlainName).IsEqualTo(alice.Name);
		}
		finally
		{
			await DisconnectAsync(alice, bob);
		}
	}

	[Test]
	public async ValueTask Recall_And_Conversations_SayThereIsNoLog_WhileLoggingIsOff()
	{
		var alice = await CreatePlayerAsync("PROff");
		var bob = await CreatePlayerAsync("PROffB");
		try
		{
			using (PageLog(on: false))
			{
				await Assert.That(await RecallAsync(alice, $"page/recall {bob.Name}")).IsEquivalentTo([NoLog]);
				await Assert.That(await RecallAsync(alice, "page/recall")).IsEquivalentTo([NoLog]);
				await Assert.That(await RecallAsync(alice, "page/conversations")).IsEquivalentTo([NoLog]);
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob);
		}
	}

	[Test]
	public async ValueTask Recall_And_Conversations_SaySo_WhenNothingIsLogged()
	{
		var alice = await CreatePlayerAsync("PRNone");
		var bob = await CreatePlayerAsync("PRNoneB");
		try
		{
			using (PageLog(on: true))
			{
				await Assert.That(await RecallAsync(alice, $"page/recall {bob.Name}"))
					.IsEquivalentTo([$"You have no logged pages with {bob.Name}."]);
				await Assert.That(await RecallAsync(alice, "page/recall")).IsEquivalentTo(["You have no logged pages."]);
				await Assert.That(await RecallAsync(alice, "page/conversations"))
					.IsEquivalentTo(["You have no logged page conversations."]);
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob);
		}
	}

	/// <summary>
	/// A player reads only their own copies: naming the two people in someone else's conversation shows
	/// the reader's own conversation with them, which is empty.
	/// </summary>
	[Test]
	public async ValueTask APlayer_CannotRecallAnotherPlayersPages()
	{
		var alice = await CreatePlayerAsync("PRPrivA");
		var bob = await CreatePlayerAsync("PRPrivB");
		var eve = await CreatePlayerAsync("PRPrivE");
		var secret = TestIsolationHelpers.GenerateUniqueName("secret");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name}={secret}");

				await Assert.That(await RecallAsync(eve, $"page/recall {alice.Name} {bob.Name}"))
					.IsEquivalentTo([$"You have no logged pages with {alice.Name} and {bob.Name}."]);
				await Assert.That(await RecallAsync(eve, $"page/recall {alice.Name}"))
					.IsEquivalentTo([$"You have no logged pages with {alice.Name}."]);
				await Assert.That(await RecallAsync(eve, "page/recall")).IsEquivalentTo(["You have no logged pages."]);
				await Assert.That(WebAppFactoryArg.Notifications.For(eve.DbRef).Any(line => line.Contains(secret))).IsFalse();
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob, eve);
		}
	}

	/// <summary>Conversations are listed latest first: who, how many pages, and when the last was sent.</summary>
	[Test]
	public async ValueTask Conversations_ListWhoHowManyAndWhenLatestFirst()
	{
		var alice = await CreatePlayerAsync("PRConvA");
		var bob = await CreatePlayerAsync("PRConvB");
		var carol = await CreatePlayerAsync("PRConvC");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name}=one");
				await CommandAsync(bob, $"page {alice.Name}=two");
				await CommandAsync(alice, $"page {bob.Name} {carol.Name}=three");

				var conversations = await Mediator.Send(new GetPageConversationsQuery(alice.DbRef));
				var group = conversations[0];
				var pair = conversations[1];
				var listing = await RecallAsync(alice, "page/conversations");

				await Assert.That(listing).IsEquivalentTo(
				[
					"PAGE: Your page conversations, latest first:",
					$"{string.Join(" and ", group.Names)}: 1 page, last {TimeFormatting.ShowTime(group.LastAt)}",
					$"{bob.Name}: 2 pages, last {TimeFormatting.ShowTime(pair.LastAt)}",
					"PAGE: End of list"
				], TUnit.Assertions.Enums.CollectionOrdering.Matching);
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob, carol);
		}
	}

	/// <summary><c>pagerecall()</c> returns the command's lines, without the frame, joined by %r unless told otherwise.</summary>
	[Test]
	public async ValueTask PageRecallFunction_ReturnsTheLinesAsTheCommandShowsThem()
	{
		var alice = await CreatePlayerAsync("PRFunA");
		var bob = await CreatePlayerAsync("PRFunB");
		var first = TestIsolationHelpers.GenerateUniqueName("first");
		var second = TestIsolationHelpers.GenerateUniqueName("second");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name}={first}");
				await CommandAsync(bob, $"page {alice.Name}=:{second}");

				await Assert.That(await FunctionAsync(alice, $"pagerecall({bob.Name})"))
					.IsEqualTo($"{LiveLine(alice, first)}\n{LiveLine(alice, second)}");
				await Assert.That(await FunctionAsync(alice, $"pagerecall({bob.Name},1,|)"))
					.IsEqualTo(LiveLine(alice, second));
				await Assert.That(await FunctionAsync(alice, $"pagerecall({bob.Name},2,|)"))
					.IsEqualTo($"{LiveLine(alice, first)}|{LiveLine(alice, second)}");
				await Assert.That(await FunctionAsync(alice, "pagerecall(,1)"))
					.IsEqualTo(LiveLine(alice, second)).Because("an empty list is every conversation");
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob);
		}
	}

	[Test]
	public async ValueTask PageRecallFunction_Errors()
	{
		var alice = await CreatePlayerAsync("PRFunErrA");
		var bob = await CreatePlayerAsync("PRFunErrB");
		var ambiguous = TestIsolationHelpers.GenerateUniqueName("PRAmb");
		var twinOne = await CreatePlayerAsync(ambiguous);
		var twinTwo = await CreatePlayerAsync(ambiguous);
		try
		{
			using (PageLog(on: true))
			{
				await Assert.That(await FunctionAsync(alice, $"pagerecall({TestIsolationHelpers.GenerateUniqueName("PRNobody")})"))
					.IsEqualTo("#-1 NO SUCH PLAYER");
				await Assert.That(await FunctionAsync(alice, $"pagerecall({ambiguous})"))
					.IsEqualTo("#-2 I DON'T KNOW WHICH ONE YOU MEAN");
				await Assert.That(await FunctionAsync(alice, $"pagerecall({bob.Name},abc)"))
					.IsEqualTo("#-1 ARGUMENT MUST BE INTEGER");
				await Assert.That(await FunctionAsync(alice, $"pagerecall({bob.Name})"))
					.IsEqualTo(string.Empty).Because("no pages is an empty result");
				await Assert.That(await FunctionAsync(alice, "pageconversations()")).IsEqualTo(string.Empty);
			}

			using (PageLog(on: false))
			{
				await Assert.That(await FunctionAsync(alice, $"pagerecall({bob.Name})")).IsEqualTo("#-1 PAGE LOGGING IS OFF");
				await Assert.That(await FunctionAsync(alice, "pageconversations()")).IsEqualTo("#-1 PAGE LOGGING IS OFF");
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob, twinOne, twinTwo);
		}
	}

	/// <summary>
	/// <c>pageconversations()</c> returns each conversation's others as objids, latest first, the
	/// conversations separated by | unless told otherwise; each list recalls its conversation.
	/// </summary>
	[Test]
	public async ValueTask PageConversationsFunction_ReturnsObjidListsLatestFirst()
	{
		var alice = await CreatePlayerAsync("PRFunConvA");
		var bob = await CreatePlayerAsync("PRFunConvB");
		var carol = await CreatePlayerAsync("PRFunConvC");
		var group = TestIsolationHelpers.GenerateUniqueName("group");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name}=one");
				await CommandAsync(alice, $"page {bob.Name} {carol.Name}={group}");

				var groupList = string.Join(' ', PageConversation.Normalize([bob.DbRef, carol.DbRef]));
				await Assert.That(await FunctionAsync(alice, "pageconversations()")).IsEqualTo($"{groupList}|{bob.DbRef}");
				await Assert.That(await FunctionAsync(alice, "pageconversations(%b-%b)")).IsEqualTo($"{groupList} - {bob.DbRef}");
				await Assert.That(await FunctionAsync(alice, "pagerecall(first(pageconversations(),|))"))
					.IsEqualTo(LiveLine(alice, group));
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob, carol);
		}
	}

	/// <summary>
	/// A partner who was destroyed is still in the caller's own log: found by the name the log gave them and
	/// by their objid, which is what <c>pageconversations()</c> hands out.
	/// </summary>
	[Test]
	public async ValueTask ADestroyedPartner_IsFoundByLoggedNameAndObjid()
	{
		var alice = await CreatePlayerAsync("PRGoneA");
		var bob = await CreatePlayerAsync("PRGoneB");
		var message = TestIsolationHelpers.GenerateUniqueName("gone");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name}={message}");
				var line = LiveLine(alice, message);
				await DestroyAsync(bob);

				var byName = await RecallAsync(alice, $"page/recall {bob.Name}");
				var byObjid = await RecallAsync(alice, $"page/recall {bob.DbRef}");

				await Assert.That(byName).IsEquivalentTo([$"{Header} with {bob.Name}:", line, Footer],
					TUnit.Assertions.Enums.CollectionOrdering.Matching);
				await Assert.That(byObjid).IsEquivalentTo(byName, TUnit.Assertions.Enums.CollectionOrdering.Matching);
				await Assert.That(await FunctionAsync(alice, $"pagerecall({bob.DbRef})")).IsEqualTo(line);
				await Assert.That(await FunctionAsync(alice, $"pagerecall({bob.Name})")).IsEqualTo(line);
			}
		}
		finally
		{
			await DisconnectAsync(alice);
		}
	}

	/// <summary>A group with one member destroyed is found by naming the living and the dead.</summary>
	[Test]
	public async ValueTask AGroupWithADestroyedMember_IsFoundByAllItsNames()
	{
		var alice = await CreatePlayerAsync("PRGoneGroupA");
		var bob = await CreatePlayerAsync("PRGoneGroupB");
		var carol = await CreatePlayerAsync("PRGoneGroupC");
		var message = TestIsolationHelpers.GenerateUniqueName("gonegroup");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name} {carol.Name}={message}");
				var line = LiveLine(alice, message);
				await DestroyAsync(carol);

				await Assert.That(await FunctionAsync(alice, $"pagerecall({bob.Name} {carol.Name})")).IsEqualTo(line);
				await Assert.That(await FunctionAsync(alice, $"pagerecall({carol.DbRef} {bob.Name})")).IsEqualTo(line);
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob);
		}
	}

	/// <summary>A renamed partner is found by the new name (live) and by the name the log gave them.</summary>
	[Test]
	public async ValueTask ARenamedPartner_IsFoundByOldAndNewName()
	{
		var alice = await CreatePlayerAsync("PRRenameA");
		var bob = await CreatePlayerAsync("PRRenameB");
		var renamed = TestIsolationHelpers.GenerateUniqueName("PRRenamed");
		var message = TestIsolationHelpers.GenerateUniqueName("renamed");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name}={message}");
				var line = LiveLine(alice, message);
				var bobObject = (await Mediator.Send(new GetObjectNodeQuery(bob.DbRef))).Expect<AnySharpObject>();
				await Mediator.Send(new SetNameCommand(bobObject, MarkupText.Plain(renamed)));

				await Assert.That(await FunctionAsync(alice, $"pagerecall({bob.Name})")).IsEqualTo(line);
				await Assert.That(await FunctionAsync(alice, $"pagerecall({renamed})")).IsEqualTo(line);
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob);
		}
	}

	/// <summary>
	/// A dbref recycled to a new player: <c>#dbref</c> alone is the new player, whose conversation with the
	/// caller is their own; only the old objid (or the logged name) finds the old conversation.
	/// </summary>
	[Test]
	public async ValueTask ARecycledDbref_IsTheNewPlayer_AndTheOldObjidIsTheOldConversation()
	{
		var alice = await CreatePlayerAsync("PRRecycleA");
		var holder = await CreatePlayerAsync("PRRecycleNew");
		var oldName = TestIsolationHelpers.GenerateUniqueName("PRRecycleOld");
		var oldMessage = TestIsolationHelpers.GenerateUniqueName("oldholder");
		var newMessage = TestIsolationHelpers.GenerateUniqueName("newholder");
		var previous = new DBRef(holder.DbRef.Number, holder.DbRef.CreationMilliseconds - 86_400_000);
		try
		{
			await Mediator.Send(new RecordPageCommand(new SharpPage(2, previous, oldName, [alice.DbRef], [alice.Name], "say",
				oldMessage, DateTimeOffset.UtcNow.AddDays(-1), SenderPlainName: oldName), [alice.DbRef]));

			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {holder.Name}={newMessage}");

				var byDbref = await FunctionAsync(alice, $"pagerecall(#{holder.DbRef.Number})");
				await Assert.That(byDbref).Contains(newMessage);
				await Assert.That(byDbref).DoesNotContain(oldMessage);
				await Assert.That(await FunctionAsync(alice, $"pagerecall({previous})")).IsEqualTo($"{oldName} pages: {oldMessage}");
				await Assert.That(await FunctionAsync(alice, $"pagerecall({oldName})")).IsEqualTo($"{oldName} pages: {oldMessage}");
			}
		}
		finally
		{
			await DisconnectAsync(alice, holder);
		}
	}

	/// <summary>A name twice is one partner, and the caller naming themselves among others is ignored.</summary>
	[Test]
	public async ValueTask DuplicateNamesAndOneself_DoNotChangeTheConversation()
	{
		var alice = await CreatePlayerAsync("PRDupA");
		var bob = await CreatePlayerAsync("PRDupB");
		var toBob = TestIsolationHelpers.GenerateUniqueName("tobob");
		var toSelf = TestIsolationHelpers.GenerateUniqueName("toself");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name}={toBob}");
				await CommandAsync(alice, $"page {alice.Name}={toSelf}");

				await Assert.That(await FunctionAsync(alice, $"pagerecall({bob.Name} {bob.Name})")).Contains(toBob);
				var withSelf = await FunctionAsync(alice, $"pagerecall({alice.Name} {bob.Name})");
				await Assert.That(withSelf).Contains(toBob);
				await Assert.That(withSelf).DoesNotContain(toSelf);
				var self = await FunctionAsync(alice, $"pagerecall({alice.Name})");
				await Assert.That(self).Contains(toSelf);
				await Assert.That(self).DoesNotContain(toBob);
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob);
		}
	}

	/// <summary>Every list <c>pageconversations()</c> returns recalls its conversation, destroyed members included.</summary>
	[Test]
	public async ValueTask EveryConversationList_RoundTripsThroughPageRecall()
	{
		var alice = await CreatePlayerAsync("PRRoundA");
		var bob = await CreatePlayerAsync("PRRoundB");
		var carol = await CreatePlayerAsync("PRRoundC");
		string[] messages =
		[
			TestIsolationHelpers.GenerateUniqueName("rt-self"),
			TestIsolationHelpers.GenerateUniqueName("rt-bob"),
			TestIsolationHelpers.GenerateUniqueName("rt-group")
		];
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {alice.Name}={messages[0]}");
				await CommandAsync(alice, $"page {bob.Name}={messages[1]}");
				await CommandAsync(alice, $"page {bob.Name} {carol.Name}={messages[2]}");
				await DestroyAsync(carol);

				var lists = (await FunctionAsync(alice, "pageconversations()")).Split('|');
				await Assert.That(lists.Length).IsEqualTo(3);
				var recalled = new List<string>();
				foreach (var list in lists)
				{
					recalled.Add(await FunctionAsync(alice, $"pagerecall({list})"));
				}

				// A page to oneself was shown twice live, sent and received; recall shows it once, as sent.
				await Assert.That(recalled).IsEquivalentTo(
				[
					LiveLine(alice, messages[2]),
					LiveLine(alice, messages[1]),
					$"You paged {alice.Name} with '{messages[0]}'"
				], TUnit.Assertions.Enums.CollectionOrdering.Matching);
			}
		}
		finally
		{
			await DisconnectAsync(alice, bob);
		}
	}

	/// <summary>
	/// An object that paged the caller is a conversation partner <c>page</c> itself would never find; its
	/// objid from <c>pageconversations()</c> and the name the log gave it still recall the conversation.
	/// </summary>
	[Test]
	public async ValueTask AnObjectThatPaged_IsFoundByObjidAndLoggedName()
	{
		var alice = await CreatePlayerAsync("PRObjA");
		var thingName = TestIsolationHelpers.GenerateUniqueName("PRObjThing");
		var message = TestIsolationHelpers.GenerateUniqueName("fromthing");
		try
		{
			var created = await FunctionAsync(new PagePlayer(new DBRef(1), 1, "God"), $"create({thingName})");
			var thing = (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(created)))).Expect<AnySharpObject>().Object().DBRef;
			await Mediator.Send(new RecordPageCommand(new SharpPage(3, thing, thingName, [alice.DbRef], [alice.Name], "say",
				message, DateTimeOffset.UtcNow, SenderPlainName: thingName), [alice.DbRef]));

			using (PageLog(on: true))
			{
				await Assert.That(await FunctionAsync(alice, "pageconversations()")).IsEqualTo(thing.ToString());
				await Assert.That(await FunctionAsync(alice, $"pagerecall({thing})")).IsEqualTo($"{thingName} pages: {message}");
				await Assert.That(await FunctionAsync(alice, $"pagerecall({thingName})")).IsEqualTo($"{thingName} pages: {message}");
			}
		}
		finally
		{
			await DisconnectAsync(alice);
		}
	}

	/// <summary>
	/// A partner renamed between two conversations is found by each name a conversation gave them: the old
	/// name still finds the older conversation after a newer one named them anew.
	/// </summary>
	[Test]
	public async ValueTask APartnerRenamedBetweenConversations_IsFoundByEitherLoggedName()
	{
		var alice = await CreatePlayerAsync("PRTwoNamesA");
		var bob = await CreatePlayerAsync("PRTwoNamesB");
		var carol = await CreatePlayerAsync("PRTwoNamesC");
		var renamed = TestIsolationHelpers.GenerateUniqueName("PRTwoNamesRenamed");
		var older = TestIsolationHelpers.GenerateUniqueName("older");
		var newer = TestIsolationHelpers.GenerateUniqueName("newer");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(alice, $"page {bob.Name}={older}");
				var bobObject = (await Mediator.Send(new GetObjectNodeQuery(bob.DbRef))).Expect<AnySharpObject>();
				await Mediator.Send(new SetNameCommand(bobObject, MarkupText.Plain(renamed)));
				await CommandAsync(alice, $"page {renamed} {carol.Name}={newer}");
				await DestroyAsync(bob);

				await Assert.That(await FunctionAsync(alice, $"pagerecall({bob.Name})")).IsEqualTo(LiveLine(alice, older));
				await Assert.That(await FunctionAsync(alice, $"pagerecall({renamed} {carol.Name})")).IsEqualTo(LiveLine(alice, newer));
			}
		}
		finally
		{
			await DisconnectAsync(alice, carol);
		}
	}

	/// <summary>
	/// A live player with no conversation with the caller, whose name the log gave two different partners,
	/// is ambiguous: neither the unrelated live player nor either partner is picked.
	/// </summary>
	[Test]
	public async ValueTask ALoggedNameSharedByTwoPartners_IsAmbiguous_EvenWithALivePlayerOfThatName()
	{
		var alice = await CreatePlayerAsync("PRSharedA");
		var stranger = await CreatePlayerAsync("PRSharedStranger");
		var first = new DBRef(stranger.DbRef.Number, stranger.DbRef.CreationMilliseconds - 86_400_000);
		var second = new DBRef(stranger.DbRef.Number, stranger.DbRef.CreationMilliseconds - 172_800_000);
		try
		{
			foreach (var (partner, id) in new[] { (first, 4L), (second, 5L) })
			{
				await Mediator.Send(new RecordPageCommand(new SharpPage(id, partner, stranger.Name, [alice.DbRef], [alice.Name],
					"say", "hello", DateTimeOffset.UtcNow.AddDays(-1), SenderPlainName: stranger.Name), [alice.DbRef]));
			}

			using (PageLog(on: true))
			{
				await Assert.That(await FunctionAsync(alice, $"pagerecall({stranger.Name})"))
					.IsEqualTo("#-2 I DON'T KNOW WHICH ONE YOU MEAN");
			}
		}
		finally
		{
			await DisconnectAsync(alice, stranger);
		}
	}

	/// <summary>Destroys <paramref name="player"/>, disconnected first.</summary>
	private async Task DestroyAsync(PagePlayer player)
	{
		await ConnectionService.Disconnect(player.Handle);
		await Mediator.Send(new DeleteObjectCommand(player.DbRef));
	}

	/// <summary>The line <paramref name="viewer"/> was shown, live, holding <paramref name="marker"/>.</summary>
	private string LiveLine(PagePlayer viewer, string marker) =>
		WebAppFactoryArg.Notifications.For(viewer.DbRef).Single(line => line.Contains(marker) && !line.Contains('\n'));

	/// <summary>
	/// Runs <paramref name="command"/> as <paramref name="player"/>, and returns what it told them, line by
	/// line. Only what they told themselves: a parallel test's player disconnecting in the same room is heard
	/// too, from that player.
	/// </summary>
	private async Task<string[]> RecallAsync(PagePlayer player, string command)
	{
		var before = WebAppFactoryArg.Notifications.DeliveryCountFor(player.DbRef);
		await CommandAsync(player, command);
		return WebAppFactoryArg.Notifications.DeliveriesFor(player.DbRef).Skip(before)
			.Where(delivery => delivery.Sender is { } sender && sender.Number == player.DbRef.Number)
			.SelectMany(delivery => delivery.Message.Split('\n'))
			.ToArray();
	}

	private async Task<string> FunctionAsync(PagePlayer player, string expression)
	{
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		var result = await WebAppFactoryArg.FunctionParserFor(player.DbRef).EvaluateAsync(MarkupText.Plain(expression));
		return result.ToPlainText();
	}

	private async Task<PagePlayer> CreatePlayerAsync(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		var playerObject = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>();

		return new PagePlayer(playerObject.Object().DBRef, player.Handle, playerObject.Object().Name);
	}

	private async Task CommandAsync(PagePlayer sender, string command)
	{
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		var parser = WebAppFactoryArg.CommandParserFor(sender.DbRef, sender.Handle);
		await parser.CommandParse(sender.Handle, ConnectionService, MarkupText.Plain(command));
	}

	private async Task DisconnectAsync(params PagePlayer[] players)
	{
		foreach (var player in players)
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	private sealed record PagePlayer(DBRef DbRef, long Handle, string Name);
}
