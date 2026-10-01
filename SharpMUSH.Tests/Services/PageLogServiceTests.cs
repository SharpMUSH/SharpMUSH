using Mediator;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <c>page_log_retention_days</c>: how old a logged page may get before the scheduled purge deletes it,
/// -1 meaning it never does.
/// </summary>
public class PageLogServiceTests
{
	private sealed class StoppedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

	private static (PageLogService Service, IMediator Mediator) Create(int retentionDays)
	{
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<PurgePageLogCommand>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<int>(3));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		var defaults = SharpMUSHOptions.Default();
		options.CurrentValue.Returns(defaults with { Chat = defaults.Chat with { PageLogRetentionDays = retentionDays } });
		return (new PageLogService(mediator, Substitute.For<IChannelMessageIdSource>(), options, new StoppedClock(Now)), mediator);
	}

	[Test]
	public async Task MinusOne_NeverDeletes()
	{
		var (service, mediator) = Create(-1);

		await Assert.That(await service.PurgeExpiredAsync()).IsEqualTo(0);
		await mediator.DidNotReceive().Send(Arg.Any<PurgePageLogCommand>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(30)]
	public async Task Days_DeleteWhatWasSentBeforeThatManyDaysAgo(int days)
	{
		var (service, mediator) = Create(days);

		await Assert.That(await service.PurgeExpiredAsync()).IsEqualTo(3);
		await mediator.Received(1).Send(new PurgePageLogCommand(Now.AddDays(-days)), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// A conversation is at most <see cref="PageConversation.MaxOthers"/> other people: the read-marker and
	/// recall endpoints refuse a larger one, so a larger page is not logged, rather than listed as a
	/// conversation that can never be opened.
	/// </summary>
	[Test]
	[Arguments(PageConversation.MaxOthers, true)]
	[Arguments(PageConversation.MaxOthers + 1, false)]
	public async Task APageToMoreThanAConversationHolds_IsNotLogged(int recipients, bool logged)
	{
		var mediator = Substitute.For<IMediator>();
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		var defaults = SharpMUSHOptions.Default();
		options.CurrentValue.Returns(defaults with { Chat = defaults.Chat with { PageLog = true } });
		var service = new PageLogService(mediator, Substitute.For<IChannelMessageIdSource>(), options, new StoppedClock(Now));
		var objects = new TestObjectFactory();
		var sender = objects.CreatePlayer(1000, "Pager");
		var to = Enumerable.Range(1001, recipients).Select(n => objects.CreatePlayer(n, $"Paged{n}")).ToArray();

		var page = await service.DeliveredAsync(sender, "Pager", to, "say", "hello all");

		await Assert.That(page.Recipients.Count).IsEqualTo(recipients);
		await mediator.Received(logged ? 1 : 0).Send(Arg.Any<RecordPageCommand>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task TheShippedDefaults_AreOffAndKeepForever()
	{
		var defaults = SharpMUSHOptions.Default().Chat;

		await Assert.That(defaults.PageLog).IsFalse();
		await Assert.That(defaults.PageLogRetentionDays).IsEqualTo(PageLogService.KeepForever);
	}
}
