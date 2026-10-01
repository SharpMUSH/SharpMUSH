using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>page</c> and the page log (<c>page_log</c>, a SharpMUSH extension): a delivered page is kept for its
/// sender and for each recipient it reached, only while the option is on, and never for a recipient who
/// refused it.
/// </summary>
public class PageLogCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private static IDisposable PageLog(bool on) => TestOptionsOverride.Scope(options => options with
	{
		Chat = options.Chat with { PageLog = on }
	});

	[Test]
	public async ValueTask APage_IsLoggedForTheSenderAndEachRecipient_WhileTheOptionIsOn()
	{
		var sender = await CreatePlayerAsync("PageLogSender");
		var first = await CreatePlayerAsync("PageLogFirst");
		var second = await CreatePlayerAsync("PageLogSecond");
		var message = TestIsolationHelpers.GenerateUniqueName("logged");
		try
		{
			using (PageLog(on: true))
			{
				await CommandAsync(sender, $"page {first.Name} {second.Name}=:{message}");
			}

			var sendersCopy = (await Log(sender, first, second)).Single();
			await Assert.That(sendersCopy.Message).IsEqualTo(message);
			await Assert.That(sendersCopy.Style).IsEqualTo("pose").Because("the pose token is not part of the message");
			await Assert.That(sendersCopy.Sender).IsEqualTo(sender.DbRef);
			await Assert.That(sendersCopy.SenderName).IsEqualTo(sender.Name);
			await Assert.That(sendersCopy.Recipients).IsEquivalentTo(new[] { first.DbRef, second.DbRef },
				TUnit.Assertions.Enums.CollectionOrdering.Matching);
			await Assert.That(sendersCopy.RecipientNames).IsEquivalentTo(new[] { first.Name, second.Name },
				TUnit.Assertions.Enums.CollectionOrdering.Matching);
			await Assert.That(sendersCopy.Id).IsGreaterThan(0);

			var firstsCopy = (await Log(first, sender, second)).Single();
			var secondsCopy = (await Log(second, sender, first)).Single();
			await Assert.That(firstsCopy.Id).IsEqualTo(sendersCopy.Id).Because("one page, one id, in every copy");
			await Assert.That(secondsCopy.Id).IsEqualTo(sendersCopy.Id);
		}
		finally
		{
			await DisconnectAsync(sender, first, second);
		}
	}

	[Test]
	public async ValueTask APage_IsNotLogged_WhileTheOptionIsOff()
	{
		var sender = await CreatePlayerAsync("PageUnloggedSender");
		var recipient = await CreatePlayerAsync("PageUnloggedRecipient");
		try
		{
			using (PageLog(on: false))
			{
				await CommandAsync(sender, $"page {recipient.Name}={TestIsolationHelpers.GenerateUniqueName("unlogged")}");
			}

			await Assert.That(await Log(sender, recipient)).IsEmpty();
			await Assert.That(await Log(recipient, sender)).IsEmpty();
			await Assert.That(await Mediator.Send(new GetPageConversationsQuery(recipient.DbRef))).IsEmpty();
		}
		finally
		{
			await DisconnectAsync(sender, recipient);
		}
	}

	/// <summary>
	/// A recipient who refused the page (HAVEN) gets no copy and is not in anyone's: the log keeps what was
	/// delivered, as the terminal showed it.
	/// </summary>
	[Test]
	public async ValueTask ARecipientWhoRefusedThePage_IsNotLoggedAndNotNamed()
	{
		var sender = await CreatePlayerAsync("PageLogRefusedSender");
		var recipient = await CreatePlayerAsync("PageLogRefusedTo");
		var refuser = await CreatePlayerAsync("PageLogRefuser");
		var message = TestIsolationHelpers.GenerateUniqueName("refused");
		try
		{
			await GodCommandAsync($"@set {refuser.DbRef}=HAVEN");
			using (PageLog(on: true))
			{
				await CommandAsync(sender, $"page {recipient.Name} {refuser.Name}={message}");
			}

			var sendersCopy = (await Log(sender, recipient)).Single();
			await Assert.That(sendersCopy.Recipients).IsEquivalentTo(new[] { recipient.DbRef });
			await Assert.That((await Log(recipient, sender)).Single().Message).IsEqualTo(message);
			await Assert.That(await Mediator.Send(new GetPageConversationsQuery(refuser.DbRef))).IsEmpty();
			await Assert.That(await Log(sender, recipient, refuser)).IsEmpty();
		}
		finally
		{
			await GodCommandAsync($"@set {refuser.DbRef}=!HAVEN");
			await DisconnectAsync(sender, recipient, refuser);
		}
	}

	/// <summary>A page that reached nobody is not a page, and nothing is kept.</summary>
	[Test]
	public async ValueTask APageThatReachedNobody_IsNotLogged()
	{
		var sender = await CreatePlayerAsync("PageLogNobodySender");
		var refuser = await CreatePlayerAsync("PageLogNobodyRefuser");
		try
		{
			await GodCommandAsync($"@set {refuser.DbRef}=HAVEN");
			using (PageLog(on: true))
			{
				await CommandAsync(sender, $"page {refuser.Name}=nobody hears");
			}

			await Assert.That(await Mediator.Send(new GetPageConversationsQuery(sender.DbRef))).IsEmpty();
		}
		finally
		{
			await GodCommandAsync($"@set {refuser.DbRef}=!HAVEN");
			await DisconnectAsync(sender, refuser);
		}
	}

	private async Task<IReadOnlyList<SharpPage>> Log(PagePlayer owner, params PagePlayer[] with) =>
		await Mediator.Send(new GetPageLogQuery(owner.DbRef, with.Select(other => other.DbRef).ToArray(), 0));

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

	private async Task GodCommandAsync(string command)
	{
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
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
