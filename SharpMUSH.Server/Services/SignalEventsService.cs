using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// SIGUSR1 and SIGUSR2 as PennMUSH reads them (src/signal.c, src/timer.c:109-139): <c>SIGNAL`USR1</c> or
/// <c>SIGNAL`USR2</c> when the event handler has an attribute for it, otherwise the default, a restart for USR1 and a
/// backup for USR2. Unix only: Windows has no user signals.
/// </summary>
public sealed class SignalEventsService(
	IEventService events,
	IBackupRunner backups,
	IServerRestart restart,
	ILogger<SignalEventsService> logger) : BackgroundService
{
	private readonly Channel<UserSignal> _received = Channel.CreateUnbounded<UserSignal>();

	public enum UserSignal { Usr1, Usr2 }

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}

		using var usr1 = PosixSignalRegistration.Create(SignalNumber(UserSignal.Usr1), context => Receive(context, UserSignal.Usr1));
		using var usr2 = PosixSignalRegistration.Create(SignalNumber(UserSignal.Usr2), context => Receive(context, UserSignal.Usr2));

		try
		{
			await foreach (var signal in _received.Reader.ReadAllAsync(stoppingToken))
			{
				try
				{
					await HandleAsync(signal);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					logger.LogError(ex, "Error handling {Signal}", signal);
				}
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// Shutdown.
		}
	}

	private void Receive(PosixSignalContext context, UserSignal signal)
	{
		context.Cancel = true;
		_received.Writer.TryWrite(signal);
	}

	/// <summary>Raises the signal's event when the handler hears it, otherwise takes PennMUSH's default action.</summary>
	public async ValueTask HandleAsync(UserSignal signal)
	{
		logger.LogInformation("Received {Signal}", signal);
		switch (signal)
		{
			case UserSignal.Usr1 when await events.HasHandlerAsync(SharpEvents.SignalUsr1):
				await events.TriggerEventAsync(SharpEvents.SignalUsr1, null);
				break;
			case UserSignal.Usr1:
				await restart.RestartAsync("SIGUSR1");
				break;
			case UserSignal.Usr2 when await events.HasHandlerAsync(SharpEvents.SignalUsr2):
				await events.TriggerEventAsync(SharpEvents.SignalUsr2, null);
				break;
			case UserSignal.Usr2:
				await backups.RunAsync(null);
				break;
		}
	}

	/// <summary>The raw signal numbers: 10 and 12 on Linux, 30 and 31 on the BSDs and macOS.</summary>
	private static PosixSignal SignalNumber(UserSignal signal)
		=> (PosixSignal)(OperatingSystem.IsLinux()
			? signal == UserSignal.Usr1 ? 10 : 12
			: signal == UserSignal.Usr1 ? 30 : 31);
}
