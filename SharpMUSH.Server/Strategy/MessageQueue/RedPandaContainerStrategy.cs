namespace SharpMUSH.Server.Strategy.MessageQueue;

/// <summary>
/// Strategy for RedPanda container configuration in production/Kubernetes environments.
/// Reads Kafka connection settings from environment variables.
/// </summary>
public class RedPandaContainerStrategy : MessageQueueStrategy
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
