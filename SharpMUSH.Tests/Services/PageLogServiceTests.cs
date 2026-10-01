using Mediator;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
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

	[Test]
	public async Task TheShippedDefaults_AreOffAndKeepForever()
	{
		var defaults = SharpMUSHOptions.Default().Chat;

		await Assert.That(defaults.PageLog).IsFalse();
		await Assert.That(defaults.PageLogRetentionDays).IsEqualTo(PageLogService.KeepForever);
	}
}
