using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System.Text.Json;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Hosted background service that drives NATS JetStream consumers registered via
/// <see cref="NatsConsumerRegistry"/>.  One durable consumer loop is started per
/// <see cref="NatsConsumerRegistration"/>; all loops run concurrently inside this
/// single hosted service.
/// </summary>
public sealed class NatsJetStreamConsumerService : BackgroundService
{
	private readonly NatsConsumerRegistry _registry;
	private readonly NatsOptions _options;
	private readonly IServiceProvider _serviceProvider;
	private readonly ILogger<NatsJetStreamConsumerService> _logger;

	public NatsJetStreamConsumerService(
		NatsConsumerRegistry registry,
		NatsOptions options,
		IServiceProvider serviceProvider,
		ILogger<NatsJetStreamConsumerService> logger)
	{
		_registry = registry;
		_options = options;
		_serviceProvider = serviceProvider;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (_registry.Registrations.Count == 0)
		{
			_logger.LogDebug("[NATS-CONSUMER] No consumer registrations; service idle.");
			return;
		}

		var nats = new NatsConnection(new NatsOpts { Url = _options.Url });
		await nats.ConnectAsync();
		var js = new NatsJSContext(nats);

		// Ensure the stream exists (idempotent — mirrors what the publisher does)
		await js.CreateOrUpdateStreamAsync(
			new StreamConfig(_options.StreamName, [$"{_options.SubjectPrefix}.>"])
			{
				MaxAge = _options.MaxAge,
			},
			stoppingToken);

		_logger.LogInformation("[NATS-CONSUMER] Starting {Count} consumer(s) on stream {Stream}",
			_registry.Registrations.Count, _options.StreamName);

		var tasks = _registry.Registrations
			.Select(reg => ConsumeAsync(js, reg, stoppingToken))
			.ToList();

		await Task.WhenAll(tasks);
		await nats.DisposeAsync();
	}

	private async Task ConsumeAsync(NatsJSContext js, NatsConsumerRegistration reg, CancellationToken ct)
	{
		_logger.LogDebug("[NATS-CONSUMER] Consumer starting — subject: {Subject}, durable: {Durable}",
			reg.Subject, reg.DurableName);

		try
		{
			var consumer = await js.CreateOrUpdateConsumerAsync(
				_options.StreamName,
				new ConsumerConfig(reg.DurableName)
				{
					FilterSubject = reg.Subject,
					DeliverPolicy = ConsumerConfigDeliverPolicy.New,
					AckPolicy = ConsumerConfigAckPolicy.Explicit,
				},
				ct);

			await foreach (var msg in consumer.ConsumeAsync<string>(cancellationToken: ct))
			{
				try
				{
					if (msg.Data is null)
					{
						_logger.LogWarning("[NATS-CONSUMER] Null payload on subject {Subject}; acking and skipping.", reg.Subject);
						await msg.AckAsync(cancellationToken: ct);
						continue;
					}

					var message = JsonSerializer.Deserialize(msg.Data, reg.MessageType);
					if (message is null)
					{
						_logger.LogWarning("[NATS-CONSUMER] Deserialisation returned null on subject {Subject}; acking and skipping.", reg.Subject);
						await msg.AckAsync(cancellationToken: ct);
						continue;
					}

					using var scope = _serviceProvider.CreateScope();
					await reg.Handler(scope.ServiceProvider, message, ct);
					await msg.AckAsync(cancellationToken: ct);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					_logger.LogError(ex, "[NATS-CONSUMER] Error handling message on subject {Subject}", reg.Subject);
					await msg.AckAsync(cancellationToken: ct);
				}
			}
		}
		catch (OperationCanceledException)
		{
			_logger.LogDebug("[NATS-CONSUMER] Consumer for subject {Subject} stopped.", reg.Subject);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[NATS-CONSUMER] Consumer for subject {Subject} failed fatally.", reg.Subject);
		}
	}
}
