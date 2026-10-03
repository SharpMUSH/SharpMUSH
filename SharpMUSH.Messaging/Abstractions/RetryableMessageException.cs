namespace SharpMUSH.Messaging.Abstractions;

/// <summary>
/// Thrown by an <see cref="IMessageConsumer{T}"/> when it did nothing with the message and running it
/// again may succeed — a dependency was briefly unavailable, say. The consumer service retries it in
/// place, with backoff, up to <c>NatsOptions.HandlerMaxAttempts</c> times, so later messages on the
/// subject stay behind it and keep their order; after the last attempt the message is terminated.
/// </summary>
/// <remarks>
/// Retrying is opt-in because the bus cannot tell how far a handler got before it failed. Any other
/// exception is a terminal failure: the message is terminated and never run again, which is the only
/// safe outcome for a handler that may already have changed state (queued a command, sent output).
/// </remarks>
public sealed class RetryableMessageException : Exception
{
	public RetryableMessageException(string message) : base(message) { }

	public RetryableMessageException(string message, Exception innerException) : base(message, innerException) { }
}
