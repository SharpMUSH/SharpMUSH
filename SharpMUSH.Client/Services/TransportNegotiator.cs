using Microsoft.Extensions.Logging;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Selects the terminal transport: WebTransport when available (browser support + successful
/// connect within a timeout), otherwise WebSocket. Returns a connected <see cref="ITransportClient"/>
/// so callers are transport-agnostic. This is the client-side realization of "both side by side".
/// </summary>
public sealed class TransportNegotiator(
	ILogger<TransportNegotiator> logger,
	Func<ITransportClient> webTransportFactory,
	Func<ITransportClient> webSocketFactory)
{
	private static readonly TimeSpan WebTransportConnectTimeout = TimeSpan.FromSeconds(3);

	/// <param name="wsUri">WebSocket URI (fallback), e.g. <c>wss://host/ws</c>.</param>
	/// <param name="wtUri">
	/// WebTransport URI or null/empty to skip WebTransport, e.g. <c>https://host:4203/wt</c>
	/// (optionally with a <c>|&lt;certHashHex&gt;</c> suffix for dev).
	/// </param>
	public async Task<ITransportClient> SelectAsync(string wsUri, string? wtUri)
	{
		if (!string.IsNullOrEmpty(wtUri))
		{
			var wt = webTransportFactory();
			try
			{
				using var cts = new CancellationTokenSource(WebTransportConnectTimeout);
				await wt.ConnectAsync(wtUri).WaitAsync(cts.Token);
				logger.LogInformation("Using WebTransport transport");
				return wt;
			}
			catch (Exception ex)
			{
				logger.LogInformation(ex, "WebTransport unavailable; falling back to WebSocket");
				await wt.DisposeAsync();
			}
		}

		var ws = webSocketFactory();
		await ws.ConnectAsync(wsUri);
		logger.LogInformation("Using WebSocket transport");
		return ws;
	}
}
