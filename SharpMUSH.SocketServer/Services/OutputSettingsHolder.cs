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

	private readonly TaskCompletionSource _received = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>The <c>ascii_translations</c> table; empty for none.</summary>
	public IReadOnlyDictionary<string, string> AsciiTranslations => _asciiTranslations;

	/// <summary>Completes when the first settings from the main process arrive.</summary>
	public Task Received => _received.Task;

	public void Replace(OutputSettingsMessage message)
	{
		_asciiTranslations = message.AsciiTranslations ?? new Dictionary<string, string>();
		_received.TrySetResult();
	}
}

/// <summary>Takes each <see cref="OutputSettingsMessage"/> the main process sends.</summary>
public sealed class OutputSettingsConsumer(OutputSettingsHolder holder) : IMessageConsumer<OutputSettingsMessage>
{
	public Task HandleAsync(OutputSettingsMessage message, CancellationToken cancellationToken = default)
	{
		holder.Replace(message);
		return Task.CompletedTask;
	}
}
