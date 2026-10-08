using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Default <see cref="IServerRestart"/>: broadcasts, then asks the host to stop. The host's lifetime is read only
/// when a restart is asked for, so the engine's services can be built without a host (the importer's tests do).
/// </summary>
public sealed class ServerRestartService(
	Lazy<IHostApplicationLifetime> lifetime,
	IGameBroadcastService broadcast,
	ILogger<ServerRestartService> logger) : IServerRestart
{
	/// <summary>How long the process waits after the broadcast, so the message and the caller's answer go out first.</summary>
	private static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

	private int _pending;

	/// <inheritdoc />
	public bool Pending => Volatile.Read(ref _pending) == 1;

	/// <inheritdoc />
	public async ValueTask<bool> RestartAsync(string requestedBy)
	{
		if (Interlocked.Exchange(ref _pending, 1) == 1)
		{
			return false;
		}

		logger.LogWarning("Restart requested by {RequestedBy}.", requestedBy);
		await broadcast.BroadcastAsync(string.Format(ErrorMessages.Notifications.GameRebootBy, requestedBy));
		_ = Task.Run(async () =>
		{
			await Task.Delay(Grace);
			lifetime.Value.StopApplication();
		});
		return true;
	}
}
