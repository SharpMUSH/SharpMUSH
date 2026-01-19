namespace SharpMUSH.Server.Strategy.MessageQueue;

/// <summary>
/// Strategy for RedPanda test container configuration.
/// Reads Kafka connection settings from test-specific environment variables set by test infrastructure.
/// Uses KAFKA_TEST_HOST and KAFKA_TEST_PORT to distinguish from production settings.
/// </summary>
public class RedPandaTestContainerStrategy : MessageQueueStrategy
{
	public override string Host => Environment.GetEnvironmentVariable("KAFKA_TEST_HOST") ?? "localhost";
	
	public override int Port
	{
		get
		{
			var portStr = Environment.GetEnvironmentVariable("KAFKA_TEST_PORT");
			return int.TryParse(portStr, out var port) ? port : 9092;
		}
	}
}
