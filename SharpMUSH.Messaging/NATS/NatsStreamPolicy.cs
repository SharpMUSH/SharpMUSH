using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// The one definition of a bus stream's limits, shared by the process that publishes to a stream and
/// the process that consumes from it.
/// </summary>
/// <remarks>
/// <para>
/// A bus stream is a queue between two processes, not an archive: interest retention removes a message
/// as soon as every consumer that wants it has acknowledged it, so its size is the unprocessed backlog.
/// Discard-new makes a full backlog reject the next publication instead of deleting the oldest message,
/// which would be a command or an output frame no one has handled yet.
/// </para>
/// <para>
/// Only the publisher of a stream owns its configuration and reconciles it on startup. The consumer
/// creates the stream when it is missing, with the same definition, and otherwise leaves it alone, so
/// two processes started with different settings do not undo each other on every restart.
/// </para>
/// </remarks>
public static class NatsStreamPolicy
{
	/// <summary>JetStream's error code for "stream not found".</summary>
	internal const int StreamNotFound = 10059;

	/// <summary>JetStream's error code for "stream name already in use with a different configuration".</summary>
	internal const int StreamNameInUse = 10058;

	public static StreamConfig BusStream(string name, string subjectPrefix, NatsOptions options)
	{
		options.Validate();
		return new StreamConfig(name, [$"{subjectPrefix}.>"])
		{
			Retention = StreamConfigRetention.Interest,
			Discard = StreamConfigDiscard.New,
			MaxAge = options.MaxAge,
			MaxBytes = options.MaxBytes,
			MaxMsgs = options.MaxMsgs,
			MaxMsgSize = options.MaxMsgSize,
		};
	}

	/// <summary>The publisher's side: create the stream, or bring an existing one to this definition.</summary>
	public static async Task ApplyAsync(INatsJSContext js, StreamConfig config, CancellationToken ct) =>
		await js.CreateOrUpdateStreamAsync(config, ct);

	/// <summary>
	/// The consumer's side: create the stream when it does not exist yet; an existing stream keeps the
	/// configuration its publisher gave it. Returns whether this call created it.
	/// </summary>
	public static async Task<bool> EnsureExistsAsync(INatsJSContext js, StreamConfig config, CancellationToken ct)
	{
		try
		{
			await js.GetStreamAsync(config.Name!, cancellationToken: ct);
			return false;
		}
		catch (NatsJSApiException ex) when (ex.Error.ErrCode == StreamNotFound)
		{
			return await CreateUnlessRacedAsync(js, config, ct);
		}
	}

	private static async Task<bool> CreateUnlessRacedAsync(INatsJSContext js, StreamConfig config, CancellationToken ct)
	{
		try
		{
			await js.CreateStreamAsync(config, ct);
			return true;
		}
		catch (NatsJSApiException ex) when (ex.Error.ErrCode == StreamNameInUse)
		{
			// The publisher created it between the lookup and this call; its configuration stands.
			return false;
		}
	}
}
