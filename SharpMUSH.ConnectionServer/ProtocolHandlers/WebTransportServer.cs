using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using SharpMUSH.ConnectionServer.Services;

// Kestrel's WebTransport APIs are [RequiresPreviewFeatures]; we consume them knowingly in this
// isolated, feature-flagged handler. EnablePreviewFeatures covers the attribute gating; this
// suppresses the CA2252 analyzer for just this file.
#pragma warning disable CA2252

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Accepts a WebTransport session over HTTP/3, takes its first client-initiated bidirectional
/// stream as the terminal byte pipe, and runs it through the shared <see cref="ConnectionPump"/>.
/// One bidirectional stream == one terminal connection for this spike (no multiplexing).
///
/// The experimental Kestrel WebTransport APIs used here are gated by
/// <c>EnablePreviewFeatures</c> and the <c>WebTransportAndH3Datagrams</c> runtime switch.
/// </summary>
public class WebTransportServer(
	ILogger<WebTransportServer> logger,
	ConnectionPump pump,
	IDescriptorGeneratorService descriptorGenerator)
{
	public async Task HandleAsync(HttpContext context)
	{
		var feature = context.Features.GetRequiredFeature<IHttpWebTransportFeature>();
		if (!feature.IsWebTransportRequest)
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return;
		}

		var session = await feature.AcceptAsync(context.RequestAborted);
		var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		var hostname = context.Request.Headers.Host.ToString();

		while (!context.RequestAborted.IsCancellationRequested)
		{
			var stream = await session.AcceptStreamAsync(context.RequestAborted);
			if (stream is null) return; // session ended

			var direction = stream.Features.GetRequiredFeature<IStreamDirectionFeature>();
			if (!(direction.CanRead && direction.CanWrite))
			{
				logger.LogDebug("Ignoring non-bidirectional WebTransport stream");
				continue;
			}

			var handle = descriptorGenerator.GetNextWebSocketDescriptor();
			var transport = new WebTransportTransport(
				stream.Transport.Input.AsStream(),
				stream.Transport.Output.AsStream(),
				remoteIp,
				hostname,
				() => stream.Abort());

			await pump.RunAsync(transport, handle, context.RequestAborted);
			return;
		}
	}
}
