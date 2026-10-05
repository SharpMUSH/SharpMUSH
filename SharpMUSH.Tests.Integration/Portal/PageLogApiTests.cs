using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// <c>api/comm/conversations</c>: the acting character's own page log (<c>page_log</c>, a SharpMUSH
/// extension), for the portal's page conversations after a reload.
///
/// <para>Driven through <see cref="CommController"/> directly, as <see cref="CommApiTests"/> is, so each
/// test picks the character it acts as: a mortal, or a wizard who must still read nothing but their own.
/// The option is turned on with <see cref="TestOptionsOverride"/>, which reaches this test's commands and
/// controller calls and no other test's.</para>
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class PageLogApiTests(ServerWebAppFactory factory)
{
	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => factory.Services.GetRequiredService<IConnectionService>();

	private Task<CommController> As(Player actor) => PortalControllers.CommControllerAs(factory, actor.DbRef);

	private static IDisposable PageLog(bool on) => TestOptionsOverride.Scope(options => options with
	{
		Chat = options.Chat with { PageLog = on }
	});

	private sealed record Player(DBRef DbRef, long Handle, string Name)
	{
		public string Objid => DbRef.ToString();
	}

	private async Task<Player> PlayerAsync(string prefix)
	{
		var created = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(factory.Services, Mediator, ConnectionService, prefix);
		var objid = (await Mediator.Send(new GetObjectNodeQuery(created.DbRef))).Expect<SharpPlayer>().Object.DBRef;
		return new Player(objid, created.Handle, created.Name);
	}

	private Task Run(Player who, string command) =>
		factory.CommandParser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command)).AsTask();

	private Task God(string command) =>
		factory.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private static string Key(params Player[] with) => string.Join(' ', with.Select(player => player.Objid));

	private static T Value<T>(ActionResult<T> result) =>
		result.Value ?? (result.Result is OkObjectResult { Value: T ok } ? ok : throw new InvalidOperationException(
			$"Expected a value, got {result.Result?.GetType().Name ?? "nothing"}."));

	private static int? Status<T>(ActionResult<T> result) => result.Result switch
	{
		ObjectResult objectResult => objectResult.StatusCode,
		StatusCodeResult statusCode => statusCode.StatusCode,
		_ => null
	};

	[Test]
	public async Task Recall_ReturnsTheConversation_ShapedAsTheCommFeedShapesAPage()
	{
		var ilsa = await PlayerAsync("PageLogIlsa");
		var wren = await PlayerAsync("PageLogWren");
		var marker = TestIsolationHelpers.GenerateUniqueName("said");
		using var _ = PageLog(on: true);

		await Run(ilsa, $"page {wren.Name}=hello {marker}");
		await Run(wren, $"page {ilsa.Name}=:waves {marker}");

		var recall = Value(await (await As(ilsa)).ConversationRecall(Key(wren), null, null, CancellationToken.None));

		await Assert.That(recall.Logging).IsTrue();
		await Assert.That(recall.Lines.Count).IsEqualTo(2);
		var (first, second) = (recall.Lines[0], recall.Lines[1]);
		await Assert.That(first.From).IsEqualTo(ilsa.Name);
		await Assert.That(first.FromObjid).IsEqualTo(ilsa.Objid);
		await Assert.That(first.To).IsEquivalentTo(new[] { wren.Name });
		await Assert.That(first.ToObjids).IsEquivalentTo(new[] { wren.Objid });
		await Assert.That(first.Style).IsEqualTo("say");
		await Assert.That(first.Text).IsEqualTo($"hello {marker}");
		await Assert.That(second.Style).IsEqualTo("pose");
		await Assert.That(second.Text).IsEqualTo($"{wren.Name} waves {marker}").Because("a pose reads with its name, as comm.message's does");
		await Assert.That(second.Id).IsGreaterThan(first.Id);
		await Assert.That(second.Ts).IsGreaterThanOrEqualTo(first.Ts);

		var wrens = Value(await (await As(wren)).ConversationRecall(Key(ilsa), null, null, CancellationToken.None));
		await Assert.That(wrens.Lines.Select(line => line.Id)).IsEquivalentTo(recall.Lines.Select(line => line.Id))
			.Because("each side reads their own copy of the same pages");
	}

	[Test]
	public async Task Recall_ReturnsTheLastNLines()
	{
		var ilsa = await PlayerAsync("PageLogLastIlsa");
		var wren = await PlayerAsync("PageLogLastWren");
		using var _ = PageLog(on: true);
		for (var i = 0; i < 4; i++)
		{
			await Run(ilsa, $"page {wren.Name}=line {i}");
		}

		var recall = Value(await (await As(wren)).ConversationRecall(Key(ilsa), 2, null, CancellationToken.None));

		await Assert.That(recall.Lines.Select(line => line.Text)).IsEquivalentTo(new[] { "line 2", "line 3" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// With a read marker's id, recall reaches back past the lines asked for to the first page after it, so a
	/// character moving to another machine gets every page they missed.
	/// </summary>
	[Test]
	public async Task Recall_ReachesBackToThePageAfterTheMarker()
	{
		var ilsa = await PlayerAsync("PageLogAfterIlsa");
		var wren = await PlayerAsync("PageLogAfterWren");
		using var _ = PageLog(on: true);
		for (var i = 0; i < 5; i++)
		{
			await Run(ilsa, $"page {wren.Name}=line {i}");
		}

		var controller = await As(wren);
		var all = Value(await controller.ConversationRecall(Key(ilsa), null, null, CancellationToken.None)).Lines;
		var seen = all.Single(line => line.Text == "line 1").Id;

		var missed = Value(await controller.ConversationRecall(Key(ilsa), 2, seen, CancellationToken.None));

		await Assert.That(missed.Lines.Select(line => line.Text)).IsEquivalentTo(new[] { "line 2", "line 3", "line 4" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>The key names the others in any order; the caller's own objid in it is ignored.</summary>
	[Test]
	public async Task Recall_OfAGroup_TakesTheOthersInAnyOrder()
	{
		var ilsa = await PlayerAsync("PageLogGroupIlsa");
		var wren = await PlayerAsync("PageLogGroupWren");
		var tomas = await PlayerAsync("PageLogGroupTomas");
		using var _ = PageLog(on: true);
		await Run(ilsa, $"page {wren.Name} {tomas.Name}=all of us");

		var controller = await As(tomas);
		var reversed = Value(await controller.ConversationRecall(Key(wren, ilsa), null, null, CancellationToken.None));
		var withSelf = Value(await controller.ConversationRecall(Key(tomas, ilsa, wren), null, null, CancellationToken.None));
		var pair = Value(await controller.ConversationRecall(Key(ilsa), null, null, CancellationToken.None));

		await Assert.That(reversed.Lines.Single().Text).IsEqualTo("all of us");
		await Assert.That(withSelf.Lines.Single().Text).IsEqualTo("all of us");
		await Assert.That(pair.Lines).IsEmpty().Because("a group page is not in the pair's conversation");
	}

	[Test]
	public async Task Conversations_ListTheCharactersOwn_WithTheLatestPage()
	{
		var ilsa = await PlayerAsync("PageLogListIlsa");
		var wren = await PlayerAsync("PageLogListWren");
		var tomas = await PlayerAsync("PageLogListTomas");
		using var _ = PageLog(on: true);
		await Run(ilsa, $"page {wren.Name}=first");
		await Run(tomas, $"page {ilsa.Name}=second");

		var list = Value(await (await As(ilsa)).Conversations(CancellationToken.None));
		var latest = Value(await (await As(ilsa)).ConversationRecall(Key(tomas), null, null, CancellationToken.None)).Lines.Single();

		await Assert.That(list.Character).IsEqualTo(ilsa.Objid);
		await Assert.That(list.Logging).IsTrue();
		await Assert.That(list.Conversations.Count).IsEqualTo(2);
		await Assert.That(list.Conversations[0].With).IsEquivalentTo(new[] { tomas.Objid }).Because("the latest first");
		await Assert.That(list.Conversations[0].Names).IsEquivalentTo(new[] { tomas.Name });
		await Assert.That(list.Conversations[0].LastId).IsEqualTo(latest.Id);
		await Assert.That(list.Conversations[1].With).IsEquivalentTo(new[] { wren.Objid });
	}

	/// <summary>
	/// While <c>page_log</c> is off nothing is recorded, and what was recorded while it was on is not
	/// served: the endpoints answer with an empty list and say logging is off, rather than 404.
	/// </summary>
	[Test]
	public async Task WithTheOptionOff_TheEndpointsSayLoggingIsOff()
	{
		var ilsa = await PlayerAsync("PageLogOffIlsa");
		var wren = await PlayerAsync("PageLogOffWren");
		using (PageLog(on: true))
		{
			await Run(ilsa, $"page {wren.Name}=while on");
		}

		using var _ = PageLog(on: false);
		await Run(ilsa, $"page {wren.Name}=while off");
		var controller = await As(wren);
		var recall = Value(await controller.ConversationRecall(Key(ilsa), null, null, CancellationToken.None));
		var list = Value(await controller.Conversations(CancellationToken.None));

		await Assert.That(recall.Logging).IsFalse();
		await Assert.That(recall.Lines).IsEmpty();
		await Assert.That(list.Logging).IsFalse();
		await Assert.That(list.Conversations).IsEmpty();

		using (PageLog(on: true))
		{
			var kept = Value(await controller.ConversationRecall(Key(ilsa), null, null, CancellationToken.None));
			await Assert.That(kept.Lines.Select(line => line.Text)).IsEquivalentTo(new[] { "while on" })
				.Because("the page sent while the option was off was never kept");
		}
	}

	/// <summary>
	/// Each copy is its owner's alone. Someone outside a conversation who names its people reads nothing,
	/// and a wizard is no different: staff control the option, not anyone's pages.
	/// </summary>
	[Test]
	public async Task NobodyElse_ReadsAConversation_NotEvenAWizard()
	{
		var ilsa = await PlayerAsync("PageLogPrivIlsa");
		var wren = await PlayerAsync("PageLogPrivWren");
		var outsider = await PlayerAsync("PageLogPrivOutsider");
		var wizard = await PlayerAsync("PageLogPrivWizard");
		await God($"@set {wizard.DbRef}=WIZARD");
		using var _ = PageLog(on: true);
		await Run(ilsa, $"page {wren.Name}=just between us");

		foreach (var snoop in new[] { outsider, wizard })
		{
			var controller = await As(snoop);
			foreach (var key in new[] { Key(ilsa), Key(wren), Key(ilsa, wren) })
			{
				var recall = Value(await controller.ConversationRecall(key, null, null, CancellationToken.None));
				await Assert.That(recall.Lines).IsEmpty().Because($"{snoop.Name} is in no page with {key}");
			}

			await Assert.That(Value(await controller.Conversations(CancellationToken.None)).Conversations).IsEmpty();
		}
	}

	[Test]
	[Arguments("Wren")]
	[Arguments("#5")]
	[Arguments("")]
	public async Task Recall_NamingSomeoneOtherThanByObjid_IsABadRequest(string key)
	{
		var ilsa = await PlayerAsync("PageLogBadKey");
		using var _ = PageLog(on: true);

		var result = await (await As(ilsa)).ConversationRecall(key, null, null, CancellationToken.None);

		await Assert.That(Status(result)).IsEqualTo(StatusCodes.Status400BadRequest);
	}

	[Test]
	public async Task WithoutACharacter_TheEndpointsAreUnauthorized()
	{
		var controller = PortalControllers.CommControllerFor(factory, new System.Security.Claims.ClaimsIdentity());

		await Assert.That(Status(await controller.Conversations(CancellationToken.None)))
			.IsEqualTo(StatusCodes.Status401Unauthorized);
		await Assert.That(Status(await controller.ConversationRecall("#1:1", null, null, CancellationToken.None)))
			.IsEqualTo(StatusCodes.Status401Unauthorized);
	}

	/// <summary>
	/// After a restart the id source has issued nothing yet, but ids the old process issued can be ahead of
	/// the clock. A marker on one of them is kept, not cut down to the clock, so the page it read stays read.
	/// The restart is a fresh id source over the host's own stored reservation.
	/// </summary>
	[Test]
	public async Task AMarkerOnAnIdAheadOfTheClock_IsKeptAfterARestart()
	{
		var ilsa = await PlayerAsync("PageLogCeilingIlsa");
		var wren = await PlayerAsync("PageLogCeilingWren");
		var host = factory.Services.GetRequiredService<IChannelMessageIdSource>();
		await host.NextAsync();
		var restarted = new SharpMUSH.Library.Services.ChannelMessageIdSource(
			factory.Services.GetRequiredService<IExpandedObjectDataService>());
		// An id the old process may have issued: past the clock, inside its stored reservation.
		var ahead = (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).Ticks / TimeSpan.TicksPerMicrosecond + 1_000_000;

		var controller = await PortalControllers.CommControllerAs(factory, ilsa.DbRef, restarted);
		var marked = Value(await controller.MarkConversation(
			new ConversationReadMarkerUpdate([wren.Objid], ahead, DateTimeOffset.UtcNow), CancellationToken.None));

		await Assert.That(marked.LastReadId).IsEqualTo(ahead);
	}

	/// <summary>A page's id is now a line id, and a conversation's marker keeps it.</summary>
	[Test]
	public async Task AConversationMarker_KeepsThePagesId()
	{
		var ilsa = await PlayerAsync("PageLogMarkIlsa");
		var wren = await PlayerAsync("PageLogMarkWren");
		using var _ = PageLog(on: true);
		await Run(wren, $"page {ilsa.Name}=read me");
		var controller = await As(ilsa);
		var line = Value(await controller.ConversationRecall(Key(wren), null, null, CancellationToken.None)).Lines.Single();

		var marked = Value(await controller.MarkConversation(
			new ConversationReadMarkerUpdate([wren.Objid], line.Id, DateTimeOffset.FromUnixTimeMilliseconds(line.Ts)),
			CancellationToken.None));

		await Assert.That(marked.LastReadId).IsEqualTo(line.Id);
		var markers = Value(await controller.Markers(CancellationToken.None));
		await Assert.That(markers.Conversations.Single(c => c.With.SequenceEqual([wren.Objid])).LastReadId).IsEqualTo(line.Id);
	}
}
