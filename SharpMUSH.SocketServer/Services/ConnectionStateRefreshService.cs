using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>Renews live connection state and resume credentials without replacing player bindings.</summary>
public sealed class ConnectionStateRefreshService(
	IConnectionServerService connections,
	IConnectionStateStore store,
	ILogger<ConnectionStateRefreshService> logger,
	SessionSinkRegistry? sinks = null,
	IResumeTokenStore? resumeTokens = null,
	IConfiguration? configuration = null) : BackgroundService
{
	private readonly TimeSpan _tokenRefreshInterval = TimeSpan.FromHours(
		(configuration?.GetValue("Replay:RetentionHours", 24.0) ?? 24.0) / 2);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
		while (await timer.WaitForNextTickAsync(stoppingToken))
			await RefreshAsync(stoppingToken);
	}

	public async Task RefreshAsync(CancellationToken ct)
	{
		foreach (var connection in connections.GetAll().Where(connection =>
			connection.ConnectionType != "websocket" || sinks is null || sinks.Get(connection.Handle)?.Current is not null))
		{
			try
			{
				await store.UpdateMetadataAsync(connection.Handle, "GatewayLastSeen",
					DateTimeOffset.UtcNow.ToString("O"), ct);
			}
			catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
			{
				logger.LogWarning(ex, "Could not refresh connection {Handle}; retaining the socket", connection.Handle);
			}
			if (connection.ConnectionType == "websocket" && resumeTokens is not null
				&& sinks?.Get(connection.Handle) is { } sink)
			{
				try
				{
					await RefreshTokenAsync(connection.Handle, sink, ct);
				}
				catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
				{
					logger.LogWarning(ex, "Could not refresh resume token for connection {Handle}; retaining its previous token", connection.Handle);
				}
			}
		}
	}

	private async Task RefreshTokenAsync(long handle, SessionSink sink, CancellationToken ct)
	{
		if (sink.Current is null) return;
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
		deadline.CancelAfter(TimeSpan.FromSeconds(5));
		await sink.OutputGate.WaitAsync(deadline.Token);
		try
		{
			var now = DateTimeOffset.UtcNow;
			if (sink.Current is not { } transport || string.IsNullOrEmpty(sink.SessionId)
				|| now - sink.TokenIssuedAt < _tokenRefreshInterval) return;
			var token = await resumeTokens!.MintAsync(handle, sink.SessionId, deadline.Token);
			await transport.SendAsync(SeqEnvelope.ResumeToken(token), deadline.Token);
			// Keep the previous token valid until its normal TTL: successful sending does not prove
			// client receipt. Both remain subject to atomic consumption and session revocation.
			sink.TokenIssuedAt = now;
		}
		finally
		{
			sink.OutputGate.Release();
		}
	}
}
