using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// NATS JetStream implementation of <see cref="IMessageBus"/>.
/// Adapter alongside <see cref="KafkaFlow.KafkaFlowMessageBus"/> for performance comparison.
/// Messages are serialised as JSON and published to a persistent JetStream stream.
/// Topic naming mirrors the Kafka convention: PascalCase type name → kebab-case subject suffix.
/// </summary>
public sealed class NatsJetStreamMessageBus : IMessageBus, IAsyncDisposable
{
	private readonly NatsConnection _nats;
	private readonly INatsJSContext _js;
	private readonly TimeSpan _publishTimeout;
	private readonly ILogger<NatsJetStreamMessageBus> _logger;
	private readonly string _subjectPrefix;
	private readonly string _streamName;
	private readonly NatsMessagingMetrics? _metrics;
	private readonly ConcurrentDictionary<Type, string> _subjects = new();

	internal NatsJetStreamMessageBus(
		NatsConnection nats,
		INatsJSContext js,
		NatsOptions options,
		ILogger<NatsJetStreamMessageBus> logger,
		NatsMessagingMetrics? metrics = null)
	{
		_nats = nats;
		_js = js;
		_subjectPrefix = options.SubjectPrefix;
		_streamName = options.StreamName;
		_metrics = metrics;
		_publishTimeout = options.PublishTimeout;
		_logger = logger;
	}

	/// <summary>
	/// Creates and initialises a <see cref="NatsJetStreamMessageBus"/>. This process owns the stream it
	/// publishes to, so it creates it or brings it to the configured limits
	/// (<see cref="NatsStreamPolicy"/>).
	/// </summary>
	public static async Task<NatsJetStreamMessageBus> CreateAsync(
		NatsOptions options,
		ILogger<NatsJetStreamMessageBus> logger,
		CancellationToken ct = default,
		NatsMessagingMetrics? metrics = null)
	{
		if (options.PublishTimeout <= TimeSpan.Zero || options.PublishTimeout.TotalMilliseconds > uint.MaxValue - 1)
			throw new ArgumentOutOfRangeException(nameof(options.PublishTimeout));
		var config = NatsStreamPolicy.BusStream(options.StreamName, options.SubjectPrefix, options);
		var nats = await NatsStartupConnection.ConnectAsync(options.Url, options.ConnectTimeout, ct);
		try
		{
			var js = new NatsJSContext(nats);
			await NatsStreamPolicy.ApplyAsync(js, config, ct);
			return new NatsJetStreamMessageBus(nats, js, options, logger, metrics);
		}
		catch
		{
			await nats.DisposeAsync();
			throw;
		}

	}

	/// <inheritdoc/>
	public async Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
	{
		var subject = GetSubjectForMessageType<T>();

		_logger.LogTrace("[NATS-SEND] Publishing message to subject {Subject} - Type: {MessageType}",
			subject, typeof(T).Name);

		await PublishCoreAsync(subject, message, null, cancellationToken);

		_logger.LogTrace("[NATS-SEND] Successfully published message to subject {Subject} - Type: {MessageType}",
			subject, typeof(T).Name);
	}

	/// <inheritdoc/>
	public async Task HandlePublish<T>(T message, CancellationToken cancellationToken = default) where T : IHandleMessage
	{
		var subject = GetSubjectForMessageType<T>();

		_logger.LogTrace("[NATS-SEND] Publishing handle-based message to subject {Subject} - Type: {MessageType}, Handle: {Handle}",
			subject, typeof(T).Name, message.Handle);

		var headers = new NatsHeaders { { "X-Handle", message.Handle.ToString() } };
		await PublishCoreAsync(subject, message, headers, cancellationToken);

		_logger.LogTrace("[NATS-SEND] Successfully published handle-based message to subject {Subject} - Type: {MessageType}, Handle: {Handle}",
			subject, typeof(T).Name, message.Handle);
	}

	private async Task PublishCoreAsync<T>(string subject, T message, NatsHeaders? headers,
		CancellationToken cancellationToken)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(_publishTimeout);
		try
		{
			var acknowledgement = await _js.PublishAsync(subject, message, serializer: CompressingNatsSerializer<T>.Default,
				headers: headers, cancellationToken: deadline.Token);
			// A full discard-new stream answers with an error rather than an exception; without this
			// check the refused message would look published.
			acknowledgement.EnsureSuccess();
		}
		catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
		{
			_metrics?.RecordRejected(_streamName, "timeout");
			throw new TimeoutException($"Publishing to {subject} exceeded {_publishTimeout}.", ex);
		}
		catch (NatsJSApiException ex)
		{
			_metrics?.RecordRejected(_streamName, RejectionReason(ex.Error));
			_logger.LogWarning("[NATS-SEND] Stream {Stream} refused a message on {Subject}: {Error}",
				_streamName, subject, ex.Error.Description);
			throw;
		}
	}

	/// <summary>JetStream's code for a stream that is at its byte or message budget.</summary>
	private const int StreamStoreFailed = 10077;

	internal static string RejectionReason(ApiError error) => error.ErrCode switch
	{
		StreamStoreFailed when error.Description?.Contains("maximum", StringComparison.OrdinalIgnoreCase) == true => "full",
		StreamStoreFailed => "store-failed",
		_ => $"error-{error.ErrCode}"
	};

	private string GetSubjectForMessageType<T>() =>
		_subjects.GetOrAdd(typeof(T), static (type, prefix) => NatsSubjects.For(type, prefix), _subjectPrefix);

	public async ValueTask DisposeAsync()
	{
		await _nats.DisposeAsync();
	}
}
