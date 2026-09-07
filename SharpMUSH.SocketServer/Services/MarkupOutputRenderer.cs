namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// The result of rendering serialized markup for a specific connection.
/// </summary>
/// <param name="Data">The bytes to write to the connection.</param>
/// <param name="ApplyOutputTransform">
/// Whether the caller should still run the capability-based <see cref="IOutputTransformService"/>
/// over <see cref="Data"/> (true for terminal output; false for the WebSocket markup envelope,
/// which is JSON the browser renders itself and must not be ANSI/charset-transformed).
/// </param>
public readonly record struct RenderedOutput(byte[] Data, bool ApplyOutputTransform);

/// <summary>
/// Turns serialized markup (an <c>MString</c> as JSON, carried by
/// <see cref="SharpMUSH.Messaging.Messages.MarkupOutputMessage"/> /
/// <see cref="SharpMUSH.Messaging.Messages.MarkupPromptMessage"/>) into the wire form for a
/// connection. This is where the engine's markup model meets the transport:
/// <list type="bullet">
/// <item>WebSocket (portal) connections receive a <c>{ "type": "markup", "data": &lt;markup&gt; }</c>
/// envelope so the browser can render it with the MarkupString HTML renderer.</item>
/// <item>Terminal connections are rendered to ANSI / Pueblo / MXP per the connection's negotiated
/// <see cref="ProtocolCapabilities.Format"/>, with line endings normalized.</item>
/// </list>
/// </summary>
public interface IMarkupOutputRenderer
{
	ValueTask<RenderedOutput> RenderAsync(string markup, ConnectionServerService.ConnectionData connection, CancellationToken ct = default);
}
