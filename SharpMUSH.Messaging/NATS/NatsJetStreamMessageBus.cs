using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;
using System.Text.Json;

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
	private readonly NatsJSContext _js;
	private readonly ILogger<NatsJetStreamMessageBus> _logger;
	private readonly string _subjectPrefix;

	private NatsJetStreamMessageBus(
		NatsConnection nats,
		NatsJSContext js,
		string subjectPrefix,
		ILogger<NatsJetStreamMessageBus> logger)
	{
		_nats = nats;
		_js = js;
		_subjectPrefix = subjectPrefix;
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
		var nats = new NatsConnection(new NatsOpts { Url = options.Url });
		await nats.ConnectAsync();
		var js = new NatsJSContext(nats);

		try
		{
			// Ensure the stream exists before publishing
			await js.CreateOrUpdateStreamAsync(
				new StreamConfig(options.StreamName, [$"{options.SubjectPrefix}.>"])
				{
					MaxAge = options.MaxAge,
					MaxMsgSize = options.MaxMsgSize,
				},
				ct);
		}
		catch
		{
			await nats.DisposeAsync();
			throw;
		}

		return new NatsJetStreamMessageBus(nats, js, options.SubjectPrefix, logger);
	}

	/// <inheritdoc/>
	public async Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
	{
		var subject = GetSubjectForMessageType<T>();
		var json = JsonSerializer.Serialize(message);

		_logger.LogTrace("[NATS-SEND] Publishing message to subject {Subject} - Type: {MessageType}",
			subject, typeof(T).Name);

		await _js.PublishAsync(subject, json, cancellationToken: cancellationToken);

		_logger.LogTrace("[NATS-SEND] Successfully published message to subject {Subject} - Type: {MessageType}",
			subject, typeof(T).Name);
	}

	/// <inheritdoc/>
	public async Task HandlePublish<T>(T message, CancellationToken cancellationToken = default) where T : IHandleMessage
	{
		var subject = GetSubjectForMessageType<T>();
		var json = JsonSerializer.Serialize(message);

		_logger.LogTrace("[NATS-SEND] Publishing handle-based message to subject {Subject} - Type: {MessageType}, Handle: {Handle}",
			subject, typeof(T).Name, message.Handle);

		// Include handle in a header so consumers can route by connection
		var headers = new NatsHeaders { { "X-Handle", message.Handle.ToString() } };
		await _js.PublishAsync(subject, json, headers: headers, cancellationToken: cancellationToken);

		_logger.LogTrace("[NATS-SEND] Successfully published handle-based message to subject {Subject} - Type: {MessageType}, Handle: {Handle}",
			subject, typeof(T).Name, message.Handle);
	}

	private string GetSubjectForMessageType<T>()
	{
		var typeName = typeof(T).Name;
		if (typeName.EndsWith("Message", StringComparison.Ordinal))
			typeName = typeName[..^7];

		// Convert PascalCase to kebab-case (same convention as Kafka topics)
		var kebabCase = string.Concat(
			typeName.Select((c, i) => i > 0 && char.IsUpper(c) ? "-" + c : c.ToString())
		).ToLowerInvariant();

		return $"{_subjectPrefix}.{kebabCase}";
	}

	public async ValueTask DisposeAsync()
	{
		await _nats.DisposeAsync();
	}
}
