using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes <see cref="MarkupOutputMessage"/> (serialized markup) and writes it to the connection in
/// its negotiated wire form via <see cref="IMarkupOutputRenderer"/>. Terminal output is additionally
/// run through the capability-based <see cref="IOutputTransformService"/>; the WebSocket markup
/// envelope is JSON and is sent verbatim.
/// </summary>
public class MarkupOutputConsumer(
	IConnectionServerService connectionService,
	IMarkupOutputRenderer renderer,
	IOutputTransformService transformService,
	ILogger<MarkupOutputConsumer> logger)
	: IMessageConsumer<MarkupOutputMessage>
{
	public async Task HandleAsync(MarkupOutputMessage message, CancellationToken cancellationToken = default)
	{
		var connection = connectionService.Get(message.Handle);

		if (connection == null)
		{
			logger.LogWarning("Received markup output for unknown connection handle: {Handle}", message.Handle);
			return;
		}

		if (string.IsNullOrEmpty(message.Markup))
			return;

		try
		{
			var rendered = renderer.Render(message.Markup, connection);
			var data = rendered.ApplyOutputTransform
				? await transformService.TransformAsync(rendered.Data, connection.Capabilities, connection.Preferences)
				: rendered.Data;

			await connection.OutputFunction(data);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error sending markup output to connection {Handle}", message.Handle);
		}
	}
}

/// <summary>
/// Consumes <see cref="MarkupPromptMessage"/> and writes it to the connection's prompt channel,
/// mirroring <see cref="MarkupOutputConsumer"/>.
/// </summary>
public class MarkupPromptConsumer(
	IConnectionServerService connectionService,
	IMarkupOutputRenderer renderer,
	IOutputTransformService transformService,
	ILogger<MarkupPromptConsumer> logger)
	: IMessageConsumer<MarkupPromptMessage>
{
	public async Task HandleAsync(MarkupPromptMessage message, CancellationToken cancellationToken = default)
	{
		var connection = connectionService.Get(message.Handle);

		if (connection == null)
		{
			logger.LogWarning("Received markup prompt for unknown connection handle: {Handle}", message.Handle);
			return;
		}

		if (string.IsNullOrEmpty(message.Markup))
			return;

		try
		{
			var rendered = renderer.Render(message.Markup, connection);
			var data = rendered.ApplyOutputTransform
				? await transformService.TransformAsync(rendered.Data, connection.Capabilities, connection.Preferences)
				: rendered.Data;

			await connection.PromptOutputFunction(data);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error sending markup prompt to connection {Handle}", message.Handle);
		}
	}
}
