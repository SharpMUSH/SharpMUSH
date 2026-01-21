using Microsoft.Extensions.Hosting;

namespace SharpMUSH.Server.Strategy.MessageQueue;

/// <summary>
/// Strategy for RedPanda container configuration in production/Kubernetes environments.
/// Reads Kafka connection settings from environment variables.
/// </summary>
public class RedPandaContainerStrategy(string host, string port) : MessageQueueStrategy
{
	public override string Host => host;
	
	public override int Port => int.TryParse(port, out var portInt) ? portInt : 9092;
}
