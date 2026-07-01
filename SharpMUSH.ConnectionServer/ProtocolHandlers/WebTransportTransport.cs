using System.Text;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Adapts a single WebTransport bidirectional stream (as an input/output <see cref="Stream"/> pair)
/// to <see cref="IDuplexTransport"/> using <see cref="FrameCodec"/> length-prefixed framing.
/// Stream-based rather than tied to a Kestrel <c>ConnectionContext</c> so it is unit-testable.
/// Migration is transparent: the stream (and thus this adapter) survives a QUIC connection
/// migration underneath the session.
/// </summary>
public sealed class WebTransportTransport(
	Stream input,
	Stream output,
	string remoteIp,
	string hostname,
	Action onClose) : IDuplexTransport
{
	public string Kind => "webtransport";
	public string RemoteIp => remoteIp;
	public string Hostname => hostname;

	public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
		=> FrameCodec.WriteFrameAsync(output, data, ct);

	public async Task<string?> ReceiveTextAsync(CancellationToken ct)
	{
		var frame = await FrameCodec.ReadFrameAsync(input, ct);
		return frame is null ? null : Encoding.UTF8.GetString(frame);
	}

	public Task CloseAsync()
	{
		onClose();
		return Task.CompletedTask;
	}
}
