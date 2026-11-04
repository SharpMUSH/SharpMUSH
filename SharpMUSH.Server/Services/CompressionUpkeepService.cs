using Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Every hour, keeps feed lines, attribute values and mail compressed (<see cref="SharpMUSH.Library.ICompressionStore.CompactAsync"/>):
/// trains a kind's dictionary when the world has grown enough, and rewrites the values made without the current one.
/// A world's values written before compression are rewritten by the first pass.
/// </summary>
public sealed class CompressionUpkeepService(IMediator mediator, TimeProvider time, ILogger<CompressionUpkeepService> logger)
	: BackgroundService
{
	private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

	public CompressionUpkeepService(IMediator mediator, ILogger<CompressionUpkeepService> logger)
		: this(mediator, TimeProvider.System, logger)
	{
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(Interval, time);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				foreach (var values in Enum.GetValues<CompressedValues>())
				{
					try
					{
						var compaction = await mediator.Send(new CompactValuesCommand(values, time.GetUtcNow()), stoppingToken);
						if (compaction.Trained || compaction.Rewritten > 0)
							logger.LogInformation("{Values} compressed: dictionary trained {Trained}, {Rewritten} value(s) rewritten",
								values, compaction.Trained, compaction.Rewritten);
					}
					catch (Exception ex) when (ex is not OperationCanceledException)
					{
						// The next tick tries again; values already stored stay readable as they are.
						logger.LogError(ex, "{Values} compression failed", values);
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
			logger.LogDebug("Compression upkeep stopped for shutdown");
		}
	}
}
