using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System.Collections.Concurrent;
using System.Text.Json;
using SharpMUSH.Messaging.Abstractions;

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
	private readonly NatsMessagingMetrics? _metrics;
	private readonly ConcurrentDictionary<string, HandledSequences> _handled = new();

	public NatsJetStreamConsumerService(
		NatsConsumerRegistry registry,
		NatsOptions options,
		IServiceProvider serviceProvider,
		ILogger<NatsJetStreamConsumerService> logger,
		NatsMessagingMetrics? metrics = null)
	{
		_registry = registry;
		_options = options;
		_serviceProvider = serviceProvider;
		_logger = logger;
		_metrics = metrics;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (_registry.Registrations.Count == 0)
		{
			_logger.LogInformation("[NATS-CONSUMER] No consumer registrations; service idle.");
			return;
		}

		var failures = 0;
		while (!stoppingToken.IsCancellationRequested)
		{
			var started = System.Diagnostics.Stopwatch.GetTimestamp();
			try
			{
				_logger.LogTrace("[NATS-CONSUMER] Connecting to NATS host {Host}",
					Uri.TryCreate(_options.Url, UriKind.Absolute, out var endpoint) ? endpoint.Host : "configured endpoint");
				await using var nats = new NatsConnection(new NatsOpts { Url = _options.Url });
				await nats.ConnectAsync();
				_registry.Attach(() => nats.ConnectionState == NatsConnectionState.Open);
				_logger.LogInformation("[NATS-CONSUMER] Connected to NATS. Ensuring stream {Stream} exists.", _options.GetConsumeStreamName());

				var js = new NatsJSContext(nats);

				// The publisher owns this stream's limits; creating it here only covers starting first.
				await NatsStreamPolicy.EnsureExistsAsync(js,
					NatsStreamPolicy.BusStream(_options.GetConsumeStreamName(), _options.GetConsumeSubjectPrefix(), _options),
					stoppingToken);

				_logger.LogInformation("[NATS-CONSUMER] Starting {Count} consumer(s) on stream {Stream}",
					_registry.Registrations.Count, _options.GetConsumeStreamName());

				await RunConsumersAsync(_registry.Registrations
					.Select(reg => (Func<CancellationToken, Task>)(ct => ConsumeAsync(js, reg, ct))), stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				_logger.LogInformation("[NATS-CONSUMER] Consumer service shutting down.");
				break;
			}
			catch (Exception ex)
			{
				// This is the recovery boundary for setup, transport and consumer failures. The
				// socket owner must stay alive even if a broker operation fails unexpectedly.
				_logger.LogError(ex, "[NATS-CONSUMER] Consumer group failed; reconnecting without stopping the host.");
			}
			// Whatever ended the group, none of its consumers is consuming until the retry resubscribes it.
			_registry.MarkAllInactive();
			if (System.Diagnostics.Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMinutes(1)) failures = 0;
			try { await Task.Delay(RetryDelay(failures++), stoppingToken); }
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
		}
	}

	internal static TimeSpan RetryDelay(int failures) =>
		TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Clamp(failures, 0, 5)))
			* (0.5 + Random.Shared.NextDouble() * 0.5));

	internal static async Task RunConsumersAsync(IEnumerable<Func<CancellationToken, Task>> consumers, CancellationToken ct)
	{
		using var group = CancellationTokenSource.CreateLinkedTokenSource(ct);
		var tasks = consumers.Select(consume => consume(group.Token)).ToArray();
		if (tasks.Length == 0) return;
		await Task.WhenAny(tasks);
		// WhenAll alone never completes while healthy siblings keep consuming. Cancel and
		// observe the entire old group before the sole retry owner replaces its connection.
		await group.CancelAsync();
		await Task.WhenAll(tasks);
		ct.ThrowIfCancellationRequested();
		throw new IOException("A NATS consumer ended before shutdown.");
	}

	private async Task ConsumeAsync(NatsJSContext js, NatsConsumerRegistration reg, CancellationToken ct)
	{
		_logger.LogInformation("[NATS-CONSUMER] Consumer starting — subject: {Subject}, durable: {Durable}",
			reg.Subject, reg.DurableName);

		// DeliverPolicy.New: a consumer identity created for the first time starts at the next message.
		// It must not run a backlog of commands meant for a consumer that no longer exists; an existing
		// durable consumer keeps its position, so a restart resumes where it stopped.
		var consumer = await js.CreateOrUpdateConsumerAsync(
			_options.GetConsumeStreamName(),
			new ConsumerConfig(reg.DurableName)
			{
				FilterSubject = reg.Subject,
				DeliverPolicy = ConsumerConfigDeliverPolicy.New,
				AckPolicy = ConsumerConfigAckPolicy.Explicit,
				AckWait = _options.AckWait,
				MaxDeliver = _options.MaxDeliver,
			},
			ct);

		// A durable created anew (its stream was deleted and recreated) numbers from 1 again, so what this
		// process finished under the old one says nothing about the sequences it will see now.
		_handled.GetOrAdd(reg.DurableName, static _ => new HandledSequences()).BeginIncarnation(consumer.Info.Created);

		_registry.MarkActive(reg.DurableName);
		_logger.LogInformation("[NATS-CONSUMER] Consumer active — subject: {Subject}, durable: {Durable}",
			reg.Subject, reg.DurableName);

		await foreach (var msg in consumer.ConsumeAsync<JsonElement>(serializer: CompressingNatsSerializer<JsonElement>.Default, cancellationToken: ct))
		{
			if (msg.Metadata is { NumDelivered: > 1 } redelivery)
				_logger.LogInformation("[NATS-CONSUMER] Delivery {Count} of message {Sequence} on {Subject}",
					redelivery.NumDelivered, redelivery.Sequence.Stream, reg.Subject);
			var outcome = await HandleDeliveryAsync(reg, msg.Data, msg.Metadata?.Sequence.Stream,
				progress => msg.AckProgressAsync(cancellationToken: progress), ct);
			// Broker failures belong to the consumer-group recovery boundary, including ACKs. An ACK lost
			// here leads to a redelivery, which HandleDeliveryAsync recognises as already handled.
			if (outcome == DeliveryOutcome.Terminated)
				await msg.AckTerminateAsync(cancellationToken: ct);
			else
				await msg.AckAsync(cancellationToken: ct);
		}
	}

	internal enum DeliveryOutcome { Handled, Duplicate, Terminated }

	/// <summary>
	/// Runs one delivery through its handler under the bus's failure policy:
	/// <list type="bullet">
	///   <item>a payload that is empty or does not deserialize is terminal — no run could succeed;</item>
	///   <item><see cref="RetryableMessageException"/> is retried in place with doubling backoff, up to
	///   <see cref="NatsOptions.HandlerMaxAttempts"/> runs, keeping later messages behind it in order;</item>
	///   <item>any other exception is terminal, because the handler may already have acted;</item>
	///   <item>a redelivery of a stream sequence this process already finished (its ACK was lost) is
	///   acknowledged without running the handler again.</item>
	/// </list>
	/// </summary>
	internal async Task<DeliveryOutcome> HandleDeliveryAsync(NatsConsumerRegistration reg, JsonElement data,
		ulong? streamSequence, Func<CancellationToken, ValueTask> progress, CancellationToken ct)
	{
		var handled = _handled.GetOrAdd(reg.DurableName, static _ => new HandledSequences());
		if (streamSequence is { } seen && handled.Contains(seen))
		{
			_metrics?.RecordDuplicate(reg.DurableName);
			_logger.LogInformation("[NATS-CONSUMER] Message {Sequence} on {Subject} was already handled; acknowledging the redelivery.",
				seen, reg.Subject);
			return DeliveryOutcome.Duplicate;
		}

		if (Deserialize(reg, data) is not { } message)
		{
			_metrics?.RecordTerminal(reg.DurableName, "unreadable");
			_logger.LogWarning("[NATS-CONSUMER] Empty or unreadable payload on subject {Subject}; terminating it.", reg.Subject);
			return Finish(handled, streamSequence, DeliveryOutcome.Terminated);
		}

		for (var attempt = 1; ; attempt++)
		{
			try
			{
				_logger.LogDebug("[NATS-CONSUMER] Received message on subject {Subject} ({Type})", reg.Subject, reg.MessageType.Name);
				using var scope = _serviceProvider.CreateScope();
				await reg.Handler(scope.ServiceProvider, message, ct);
				return Finish(handled, streamSequence, DeliveryOutcome.Handled);
			}
			catch (RetryableMessageException ex) when (attempt < _options.HandlerMaxAttempts)
			{
				_metrics?.RecordRetry(reg.DurableName);
				var delay = _options.HandlerRetryDelay * Math.Pow(2, attempt - 1);
				_logger.LogWarning(ex, "[NATS-CONSUMER] Retryable failure on subject {Subject}; attempt {Attempt} of {MaxAttempts}, next in {Delay}",
					reg.Subject, attempt, _options.HandlerMaxAttempts, delay);
				// Keep the broker from redelivering while this process is still working on the message.
				await progress(ct);
				await Task.Delay(delay, ct);
			}
			catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
			{
				// Preserve poison-message isolation across arbitrary application handlers.
				var reason = ex is RetryableMessageException ? "retries-exhausted" : "handler-error";
				_metrics?.RecordTerminal(reg.DurableName, reason);
				_logger.LogError(ex, "[NATS-CONSUMER] Error handling message on subject {Subject} ({Reason}); terminating it.",
					reg.Subject, reason);
				return Finish(handled, streamSequence, DeliveryOutcome.Terminated);
			}
		}
	}

	private static DeliveryOutcome Finish(HandledSequences handled, ulong? streamSequence, DeliveryOutcome outcome)
	{
		if (streamSequence is { } seq) handled.Add(seq);
		return outcome;
	}

	private object? Deserialize(NatsConsumerRegistration reg, JsonElement data)
	{
		if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
		try
		{
			return data.Deserialize(reg.MessageType);
		}
		catch (JsonException ex)
		{
			_logger.LogWarning(ex, "[NATS-CONSUMER] Payload on subject {Subject} is not a {Type}", reg.Subject, reg.MessageType.Name);
			return null;
		}
	}

	/// <summary>
	/// The stream sequences one consumer finished most recently, so a redelivery caused by a lost ACK is
	/// recognised. Bounded, and process-local: it covers broker reconnects, not a restart of this process.
	/// </summary>
	internal sealed class HandledSequences
	{
		private const int Capacity = 4096;
		private readonly HashSet<ulong> _set = [];
		private readonly Queue<ulong> _order = new();
		private DateTimeOffset? _incarnation;

		/// <summary>
		/// Forgets every sequence when <paramref name="created"/>, the consumer's creation time, differs from
		/// the one these sequences were recorded under.
		/// </summary>
		public void BeginIncarnation(DateTimeOffset created)
		{
			lock (_set)
			{
				if (_incarnation == created) return;
				_incarnation = created;
				_set.Clear();
				_order.Clear();
			}
		}

		public bool Contains(ulong seq)
		{
			lock (_set) return _set.Contains(seq);
		}

		public void Add(ulong seq)
		{
			lock (_set)
			{
				if (!_set.Add(seq)) return;
				_order.Enqueue(seq);
				if (_order.Count > Capacity) _set.Remove(_order.Dequeue());
			}
		}
	}
}
