using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// The game settings each connection's output is rendered with, as the main process last sent them
/// (<see cref="OutputSettingsMessage"/>).
/// </summary>
public sealed class OutputSettingsHolder
{
	private volatile IReadOnlyDictionary<string, string> _asciiTranslations = new Dictionary<string, string>();

	private volatile string? _mediaBaseUrl;

	private readonly TaskCompletionSource _received = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>The <c>ascii_translations</c> table; empty for none.</summary>
	public IReadOnlyDictionary<string, string> AsciiTranslations => _asciiTranslations;

	/// <summary><c>media_base_url</c>, sent to a GMCP client as <c>Client.Media.Default</c>; null for none.</summary>
	public string? MediaBaseUrl => _mediaBaseUrl;

	/// <summary>Completes when the first settings from the main process arrive.</summary>
	public Task Received => _received.Task;

	/// <summary>Takes the settings the main process sent; true when <see cref="MediaBaseUrl"/> is now a different address.</summary>
	public bool Replace(OutputSettingsMessage message)
	{
		_asciiTranslations = message.AsciiTranslations ?? new Dictionary<string, string>();
		var mediaBaseUrl = string.IsNullOrWhiteSpace(message.MediaBaseUrl) ? null : message.MediaBaseUrl.Trim();
		var changed = Interlocked.Exchange(ref _mediaBaseUrl, mediaBaseUrl) != mediaBaseUrl;
		_received.TrySetResult();
		return changed;
	}
}

/// <summary>
/// Takes each <see cref="OutputSettingsMessage"/> the main process sends. When <c>media_base_url</c> arrives or changes,
/// every telnet connection that already agreed to GMCP is sent the new <c>Client.Media.Default</c>: a connection
/// otherwise hears it only when it agrees, which can be before the first settings arrive.
/// </summary>
public sealed class OutputSettingsConsumer(
	OutputSettingsHolder holder,
	IConnectionServerService connections,
	ILogger<OutputSettingsConsumer> logger) : IMessageConsumer<OutputSettingsMessage>
{
	public async Task HandleAsync(OutputSettingsMessage message, CancellationToken cancellationToken = default)
	{
		if (!holder.Replace(message) || ClientMediaFrames.Default(holder.MediaBaseUrl) is not { } media) return;

		foreach (var connection in connections.GetAll())
		{
			if (connection.ConnectionType == "websocket" || connection.Capabilities.GmcpModules is null
				|| connection.GMCPFunction is not { } send) continue;

			try
			{
				await send(media.Package, media.Json);
			}
			catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
			{
				logger.LogDebug(ex, "Could not send Client.Media.Default to connection {Handle}", connection.Handle);
			}
		}
	}
}
