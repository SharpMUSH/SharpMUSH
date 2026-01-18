namespace SharpMUSH.Server.Strategy.MessageQueue;

/// <summary>
/// Strategy for RedPanda test container configuration.
/// Reads Kafka connection settings from environment variables set by test infrastructure.
/// </summary>
public class RedPandaTestContainerStrategy : MessageQueueStrategy
{
	public override string Host => Environment.GetEnvironmentVariable("KAFKA_HOST") ?? "localhost";
	
	public override int Port
	{
		get
		{
			var portStr = Environment.GetEnvironmentVariable("KAFKA_PORT");
			return int.TryParse(portStr, out var port) ? port : 9092;
		}
	}
}
