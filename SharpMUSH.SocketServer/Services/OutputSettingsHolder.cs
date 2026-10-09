using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// The game settings each connection's output is rendered with, as the main process last sent them
/// (<see cref="OutputSettingsMessage"/>).
/// </summary>
public sealed class OutputSettingsHolder
{
	private volatile string _asciiTranslations = string.Empty;

	/// <summary>The <c>ascii_translations</c> option, as written; empty for none.</summary>
	public string AsciiTranslations => _asciiTranslations;

	public void Replace(OutputSettingsMessage message) => _asciiTranslations = message.AsciiTranslations ?? string.Empty;
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
