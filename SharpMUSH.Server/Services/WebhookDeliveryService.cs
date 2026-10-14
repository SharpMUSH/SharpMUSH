using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Services.Webhooks;
using SharpMUSH.Library.Webhooks;

namespace SharpMUSH.Server.Services;

/// <summary>Runs the webhook dispatcher: sends each delivery when it comes due, until the host stops.</summary>
public sealed class WebhookDeliveryService(
	WebhookDispatcher dispatcher,
	WebhookService webhooks,
	ILogger<WebhookDeliveryService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await dispatcher.WaitAsync(stoppingToken);
				await dispatcher.RunDueAsync(webhooks.Current, webhooks.MarkGoneAsync, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Webhook delivery pass failed");
			}
		}
	}

	/// <summary>
	/// The handler webhook deliveries go through: no redirects (a 3xx is an answer, not a new address), and
	/// unless <see cref="WebhookOptions.AllowPrivateNetworks"/> is set, a connection only to public addresses,
	/// checked on the addresses the name resolves to when connecting, so a name cannot be pointed inward later.
	/// </summary>
	public static SocketsHttpHandler Handler(WebhookOptions options) => new()
	{
		AllowAutoRedirect = false,
		PooledConnectionLifetime = TimeSpan.FromMinutes(5),
		ConnectCallback = options.AllowPrivateNetworks ? null : ConnectPublicAsync,
	};

	private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
	{
		var host = context.DnsEndPoint.Host;
		var addresses = IPAddress.TryParse(host, out var literal)
			? [literal]
			: await Dns.GetHostAddressesAsync(host, cancellationToken);
		var allowed = addresses.Where(WebhookAddress.IsPublic).ToArray();
		if (allowed.Length == 0)
		{
			throw new HttpRequestException($"{host} is on a private network");
		}

		var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
		try
		{
			await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
			return new NetworkStream(socket, ownsSocket: true);
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}
}
