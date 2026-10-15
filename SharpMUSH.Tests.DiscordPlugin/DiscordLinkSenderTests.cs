using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Plugins.Discord.Outbox;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordLinkSenderTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

	private sealed class FixedClock : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private sealed class RecordingPoster(int expected) : IDiscordPoster
	{
		private readonly List<DiscordPost> _posts = [];

		public TaskCompletionSource<IReadOnlyList<DiscordPost>> All { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<ulong> PostAsync(DiscordPost post, CancellationToken cancellationToken)
		{
			lock (_posts)
			{
				_posts.Add(post);
				if (_posts.Count == expected)
				{
					All.TrySetResult([.. _posts]);
				}

				return Task.FromResult((ulong)_posts.Count);
			}
		}
	}

	[Test, Timeout(30_000)]
	public async Task LinesGoOut_InOrder_OnePostPerSpeakerRun(CancellationToken cancellationToken)
	{
		var options = new DiscordOutboxOptions(Gather: TimeSpan.Zero);
		var poster = new RecordingPoster(2);
		var sender = new DiscordLinkSender(poster, new DiscordOutbox(options, new Random(1)), options, new FixedClock(),
			NullLogger.Instance);
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var now = Now;

		sender.Send(new OutboundLine("Ann", null, "one", now));
		sender.Send(new OutboundLine("Ann", null, "two", now));
		sender.Send(new OutboundLine("Bo", null, "three", now));
		var running = sender.RunAsync(stop.Token);
		var posts = await poster.All.Task.WaitAsync(cancellationToken);
		await stop.CancelAsync();
		await Assert.That(async () => await running).Throws<OperationCanceledException>();

		await Assert.That(posts.Select(post => $"{post.Speaker}: {post.Content}"))
			.IsEquivalentTo(new[] { "Ann: one\ntwo", "Bo: three" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}
}
