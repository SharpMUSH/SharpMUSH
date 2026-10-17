using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Server.Services.Imports;

/// <summary>Runs before any hosted consumers start; ordinary restarts keep their normal resume/reconciliation behavior.</summary>
public sealed class ImportConnectionResetService(ImportPromotionJournal journal, IConnectionStateStore states,
	IMessageBus messages, ILogger<ImportConnectionResetService> logger) : IHostedLifecycleService
{
	public async Task StartingAsync(CancellationToken cancellationToken)
	{
		if (!journal.RequiresConnectionReset) return;
		foreach (var (handle, _) in await states.GetAllConnectionsAsync(cancellationToken))
		{
			await messages.Publish(new DisconnectConnectionMessage(handle, "World import committed; reconnect and sign in again."), cancellationToken);
			await states.RemoveConnectionAsync(handle, cancellationToken);
		}
		journal.CompleteConnectionReset();
		logger.LogInformation("Outgoing-world transport bindings were removed before imported-world consumers started");
	}
	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
