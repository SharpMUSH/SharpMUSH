using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>
/// WebTransport terminal client over browser JS interop (<c>wwwroot/js/webtransport.js</c>).
/// QUIC connection migration is handled by the browser's transport stack, so this class does not
/// implement reconnection itself — the point of WebTransport here is that the session survives a
/// network change without a reconnect. Falls back to WebSocket (via the negotiator) when the browser
/// lacks WebTransport or the connect fails.
/// </summary>
public sealed class WebTransportClientService(IJSRuntime js, ILogger<WebTransportClientService> logger)
	: ITransportClient
{
	private DotNetObjectReference<WebTransportClientService>? _ref;
	private string? _sessionId;

	public string Kind => "webtransport";
	public bool IsConnected => _sessionId is not null;

	public event EventHandler<string>? MessageReceived;

	/// <param name="uri">
	/// Absolute https URL to <c>/wt</c>. An optional dev cert SHA-256 may be appended as
	/// <c>"&lt;url&gt;|&lt;hex&gt;"</c> so the browser can pin a self-signed cert.
	/// </param>
	public async Task ConnectAsync(string uri)
	{
		var parts = uri.Split('|', 2);
		var url = parts[0];
		var certHash = parts.Length > 1 ? parts[1] : null;

		if (!await js.InvokeAsync<bool>("sharpWebTransport.isSupported"))
			throw new NotSupportedException("WebTransport is not available in this browser.");

		_ref = DotNetObjectReference.Create(this);
		_sessionId = await js.InvokeAsync<string>("sharpWebTransport.connect", url, certHash, _ref);
		logger.LogInformation("WebTransport connected: {Url}", url);
	}

	public async Task SendAsync(string message)
	{
		if (_sessionId is not null)
			await js.InvokeVoidAsync("sharpWebTransport.send", _sessionId, message);
	}

	public async Task DisconnectAsync()
	{
		if (_sessionId is not null)
			await js.InvokeVoidAsync("sharpWebTransport.close", _sessionId);
		_sessionId = null;
	}

	public void ClearSendBuffer()
	{
		// WebTransport has no client-side send buffer in this spike; migration avoids the gap.
	}

	/// <summary>Invoked from JS for each decoded text frame.</summary>
	[JSInvokable]
	public void OnFrame(string text) => MessageReceived?.Invoke(this, text);

	/// <summary>Invoked from JS when the underlying session closes.</summary>
	[JSInvokable]
	public void OnClosed() => _sessionId = null;

	public async ValueTask DisposeAsync()
	{
		await DisconnectAsync();
		_ref?.Dispose();
	}
}
