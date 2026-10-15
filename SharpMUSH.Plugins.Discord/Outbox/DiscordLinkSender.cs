using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.Plugins.Discord.Outbox;

/// <summary>Posts to one Discord channel, through its webhook.</summary>
public interface IDiscordPoster
{
	/// <returns>The id of the message Discord created.</returns>
	Task<ulong> PostAsync(DiscordPost post, CancellationToken cancellationToken);
}

/// <summary>
/// Drains one link's <see cref="DiscordOutbox"/> into its <see cref="IDiscordPoster"/>, one post at a time. Links
/// run side by side; within a link, lines leave in the order they were said.
/// </summary>
public sealed class DiscordLinkSender(
	IDiscordPoster poster,
	DiscordOutbox outbox,
	DiscordOutboxOptions options,
	TimeProvider time,
	ILogger logger)
{
	private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
		new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

	public DiscordLinkState State
	{
		get
		{
			lock (outbox)
			{
				return outbox.State;
			}
		}
	}

	public LinkBroken? Fault
	{
		get
		{
			lock (outbox)
			{
				return outbox.Fault;
			}
		}
	}

	public void Send(OutboundLine line)
	{
		lock (outbox)
		{
			outbox.Enqueue(line);
		}

		_wake.Writer.TryWrite(true);
	}

	/// <summary>An admin fixed the link; what is still young enough goes out.</summary>
	public void Resume()
	{
		lock (outbox)
		{
			outbox.Resume();
		}

		_wake.Writer.TryWrite(true);
	}

	public async Task RunAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			await _wake.Reader.ReadAsync(cancellationToken);
			// The first line of a burst waits a moment, so the lines after it can join its post.
			if (options.GatherWindow > TimeSpan.Zero)
			{
				await Task.Delay(options.GatherWindow, time, cancellationToken);
			}

			await DrainAsync(cancellationToken);
		}
	}

	private async Task DrainAsync(CancellationToken cancellationToken)
	{
		while (Next() is { } post)
		{
			var outcome = await PostAsync(post, cancellationToken);
			TimeSpan wait;
			lock (outbox)
			{
				wait = outbox.Report(outcome, time.GetUtcNow());
			}

			Log(outcome);
			if (wait > TimeSpan.Zero)
			{
				await Task.Delay(wait, time, cancellationToken);
			}
		}
	}

	private DiscordPost? Next()
	{
		lock (outbox)
		{
			return outbox.Next(time.GetUtcNow());
		}
	}

	private async Task<PostOutcome> PostAsync(DiscordPost post, CancellationToken cancellationToken)
	{
		try
		{
			return new Posted(await poster.PostAsync(post, cancellationToken));
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			return DiscordFailures.Classify(exception);
		}
	}

	private void Log(PostOutcome outcome)
	{
		switch (outcome)
		{
			case LinkBroken broken:
				logger.LogWarning("[Discord] A link stopped ({Code}): {Reason}", broken.Code, broken.Reason);
				break;
			case Rejected rejected:
				logger.LogWarning("[Discord] Discord refused a message: {Reason}", rejected.Reason);
				break;
			case Transient transient:
				logger.LogInformation("[Discord] A post will be tried again: {Reason}", transient.Reason);
				break;
		}
	}
}
