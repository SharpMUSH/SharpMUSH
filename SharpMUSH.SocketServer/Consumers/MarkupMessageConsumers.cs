using SharpMUSH.SocketServer.Services;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.SocketServer.Consumers;

/// <summary>
/// Consumes <see cref="MarkupOutputMessage"/> (serialized markup) and writes it to the connection in
/// its negotiated wire form via <see cref="IMarkupOutputRenderer"/>. Terminal output is additionally
/// run through the charset <see cref="IOutputTransformService"/>; the WebSocket markup
/// envelope is JSON and is sent verbatim. A <see cref="MarkupOutputMessage.Prompt"/> goes to the
/// prompt channel; one loop handles both, so a connection sees them in the order they were published.
/// </summary>
public class MarkupOutputConsumer(
	IConnectionServerService connectionService,
	IMarkupOutputRenderer renderer,
	IOutputTransformService transformService,
	ILogger<MarkupOutputConsumer> logger)
	: IMessageConsumer<MarkupOutputMessage>
{
	private const string WebSocketConnectionType = "websocket";

	public async Task HandleAsync(MarkupOutputMessage message, CancellationToken cancellationToken = default)
	{
		var connection = connectionService.Get(message.Handle);

		if (connection == null || (message.SessionId is { } expected && connection.SessionId != expected))
		{
			logger.LogWarning("Received markup output for unknown connection handle: {Handle}", message.Handle);
			return;
		}

		if (connection.ConnectionType == WebSocketConnectionType && (message.Prompt || message.ClearPrompt))
		{
			await WritePromptFrameAsync(message, connection);
			return;
		}

		if (string.IsNullOrEmpty(message.Markup))
			return;

		try
		{
			var rendered = await renderer.RenderAsync(message.Markup, connection, message.Prompt, cancellationToken);
			var data = rendered.ApplyOutputTransform
				? await transformService.TransformAsync(rendered.Data, connection.Capabilities, cancellationToken)
				: rendered.Data;

			if (message.SessionId is { } sessionId && connectionService.Get(message.Handle)?.SessionId != sessionId) return;
			if (message.Prompt) await connection.PromptOutputFunction(data);
			else await connection.OutputFunction(data);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error sending markup output to connection {Handle}", message.Handle);
		}
	}

	/// <summary>
	/// A WebSocket client keeps its prompt apart from the output (<see cref="WebSocketPromptFrames"/>), so a
	/// prompt is marked as one and the end of its <c>@input</c> session is sent as a frame of its own. A
	/// terminal's prompt is only its last line, so a clear sends it nothing.
	/// </summary>
	private async ValueTask WritePromptFrameAsync(MarkupOutputMessage message, ConnectionServerService.ConnectionData connection)
	{
		if (message.Prompt && string.IsNullOrEmpty(message.Markup)) return;
		var frame = message.ClearPrompt
			? WebSocketPromptFrames.Clear(message.InputSession)
			: WebSocketPromptFrames.Prompt(message.Markup, message.InputSession);
		try
		{
			if (message.ClearPrompt) await connection.OutputFunction(frame);
			else await connection.PromptOutputFunction(frame);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error sending a prompt frame to connection {Handle}", message.Handle);
		}
	}
}

/// <summary>
/// Consumes <see cref="MarkupPromptMessage"/> and writes it to the connection's prompt channel,
/// mirroring <see cref="MarkupOutputConsumer"/>. An engine sends these only to connections whose
/// socket owner did not advertise <see cref="ConnectionEstablishedMessage.OrderedPrompts"/>; they
/// run on their own subject and carry no order relative to ordinary output.
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

		if (message.SessionId is { } expected && connection.SessionId != expected) return;

		if (string.IsNullOrEmpty(message.Markup))
			return;

		try
		{
			var rendered = await renderer.RenderAsync(message.Markup, connection, prompt: true, cancellationToken);
			var data = rendered.ApplyOutputTransform
				? await transformService.TransformAsync(rendered.Data, connection.Capabilities, cancellationToken)
				: rendered.Data;

			if (message.SessionId is { } sessionId && connectionService.Get(message.Handle)?.SessionId != sessionId) return;
			await connection.PromptOutputFunction(data);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error sending markup prompt to connection {Handle}", message.Handle);
		}
	}
}
