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
	private readonly ConcurrentDictionary<Type, string> _subjects = new();

	internal NatsJetStreamMessageBus(
		NatsConnection nats,
		INatsJSContext js,
		NatsOptions options,
		ILogger<NatsJetStreamMessageBus> logger)
	{
		_nats = nats;
		_js = js;
		_subjectPrefix = options.SubjectPrefix;
		_publishTimeout = options.PublishTimeout;
		_logger = logger;
	}

	/// <summary>
	/// Creates and initialises a <see cref="NatsJetStreamMessageBus"/>.
	/// Creates the JetStream stream if it does not already exist.
	/// </summary>
	public static async Task<NatsJetStreamMessageBus> CreateAsync(
		NatsOptions options,
		ILogger<NatsJetStreamMessageBus> logger,
		CancellationToken ct = default)
	{
		if (options.PublishTimeout <= TimeSpan.Zero || options.PublishTimeout.TotalMilliseconds > uint.MaxValue - 1)
			throw new ArgumentOutOfRangeException(nameof(options.PublishTimeout));
		var nats = new NatsConnection(new NatsOpts { Url = options.Url });
		try
		{
			await nats.ConnectAsync();
			var js = new NatsJSContext(nats);
			await js.CreateOrUpdateStreamAsync(
				new StreamConfig(options.StreamName, [$"{options.SubjectPrefix}.>"])
				{
					MaxAge = options.MaxAge,
					MaxMsgSize = options.MaxMsgSize,
				},
				ct);
			return new NatsJetStreamMessageBus(nats, js, options, logger);
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
			await _js.PublishAsync(subject, message, serializer: CompressingNatsSerializer<T>.Default,
				headers: headers, cancellationToken: deadline.Token);
		}
		catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
		{
			throw new TimeoutException($"Publishing to {subject} exceeded {_publishTimeout}.", ex);
		}
	}

	private string GetSubjectForMessageType<T>() =>
		_subjects.GetOrAdd(typeof(T), static (type, prefix) => NatsSubjects.For(type, prefix), _subjectPrefix);

	public async ValueTask DisposeAsync()
	{
		await _nats.DisposeAsync();
	}
}
