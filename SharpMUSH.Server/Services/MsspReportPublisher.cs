using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Keeps the connection servers' copy of the MSSP report current: they answer the MSSP telnet option
/// from it (<see cref="MSSPReportMessage"/>). The report is rebuilt every <see cref="Interval"/>, at
/// once when the configuration changes or a connection server asks, and sent only when it differs
/// from the last one sent, or when asked.
/// </summary>
/// <remarks>
/// <c>PLAYERS</c> is the part that moves on its own, so a crawler on the telnet option can see a count
/// up to <see cref="Interval"/> old. <c>MSSP-REQUEST</c> builds the report when it is asked.
/// </remarks>
public sealed class MsspReportPublisher(
	IMsspReportService report,
	IMessageBus bus,
	IOptionsMonitor<SharpMUSHOptions> options,
	ILogger<MsspReportPublisher> logger) : BackgroundService
{
	public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

	/// <summary>True for a forced send; false for "rebuild and send if changed".</summary>
	private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
		new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.DropOldest });

	private string? _lastSent;

	/// <summary>A connection server asked for the report: send it whether or not it changed.</summary>
	public void RequestSend() => _wake.Writer.TryWrite(true);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var subscription = options.OnChange((_, _) => _wake.Writer.TryWrite(false));
		using var timer = new PeriodicTimer(Interval);
		var tick = Task.CompletedTask;
		var woken = Task.CompletedTask;
		var force = true;

		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				await PublishAsync(force, stoppingToken);

				tick = tick.IsCompleted ? timer.WaitForNextTickAsync(stoppingToken).AsTask() : tick;
				// Each wait is kept until it completes: a fresh one per timer tick would leave the last one
				// pending on the channel, and an idle server would pile them up.
				woken = woken.IsCompleted ? _wake.Reader.WaitToReadAsync(stoppingToken).AsTask() : woken;
				await Task.WhenAny(tick, woken);

				force = false;
				while (_wake.Reader.TryRead(out var forced))
				{
					force |= forced;
				}
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// The host is stopping; the connection server keeps the last report it was sent.
		}
	}

	private async Task PublishAsync(bool force, CancellationToken ct)
	{
		try
		{
			var variables = (await report.BuildAsync())
				.Select(variable => new MSSPVariable(variable.Name, [.. variable.Values]))
				.ToArray();
			var fingerprint = string.Join('\n', variables.Select(v => v.Name + '\t' + string.Join('\t', v.Values)));
			if (!force && fingerprint == _lastSent)
			{
				return;
			}

			await bus.Publish(new MSSPReportMessage(variables), ct);
			_lastSent = fingerprint;
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
		{
			logger.LogWarning(ex, "Could not send the MSSP report to the connection servers; it is retried on the next pass");
		}
	}
}
