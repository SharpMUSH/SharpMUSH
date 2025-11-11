namespace SharpMUSH.Messaging.Configuration;

/// <summary>
/// Configuration options for the message queue
/// </summary>
public class MessageQueueOptions
{
	/// <summary>
	/// The RabbitMQ host address
	/// </summary>
	public string Host { get; set; } = "localhost";

	/// <summary>
	/// The RabbitMQ port
	/// </summary>
	public int Port { get; set; } = 5672;

	/// <summary>
	/// The RabbitMQ username
	/// </summary>
	public string Username { get; set; } = "guest";

	/// <summary>
	/// The RabbitMQ password
	/// </summary>
	public string Password { get; set; } = "guest";

	/// <summary>
	/// The virtual host to use
	/// </summary>
	public string VirtualHost { get; set; } = "/";

	/// <summary>
	/// Timeout for request/response messages in seconds
	/// </summary>
	public int RequestTimeoutSeconds { get; set; } = 5;

	/// <summary>
	/// Number of retry attempts for failed messages
	/// </summary>
	public int RetryCount { get; set; } = 3;

	/// <summary>
	/// Delay between retry attempts in seconds
	/// </summary>
	public int RetryDelaySeconds { get; set; } = 5;

	/// <summary>
	/// Whether to use durable queues (survive broker restart)
	/// </summary>
	public bool DurableQueues { get; set; } = true;

	/// <summary>
	/// Message TTL in hours (0 = no expiration)
	/// </summary>
	public int MessageTtlHours { get; set; } = 24;
}
