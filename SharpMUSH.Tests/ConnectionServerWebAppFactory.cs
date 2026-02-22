using Confluent.Kafka;
using Confluent.Kafka.Admin;
using TUnit.AspNetCore;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Integration test factory for SharpMUSH.ConnectionServer.
/// Manages test infrastructure lifecycle and provides access to services.
/// </summary>
public class ConnectionServerWebAppFactory : TestWebApplicationFactory<SharpMUSH.ConnectionServer.Program>, IAsyncInitializer, IAsyncDisposable
{
	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	[ClassDataSource<RedPandaTestServer>(Shared = SharedType.PerTestSession)]
	public required RedPandaTestServer RedPandaTestServer { get; init; }

	[ClassDataSource<RedisTestServer>(Shared = SharedType.PerTestSession)]
	public required RedisTestServer RedisTestServer { get; init; }

	public new IServiceProvider Services => _server!.Services;
	private ConnectionServerTestWebApplicationBuilderFactory<SharpMUSH.ConnectionServer.Program>? _server;

	public virtual async Task InitializeAsync()
	{
		var redisPort = RedisTestServer.Instance.GetMappedPublicPort(6379);
		var redisConnection = $"localhost:{redisPort}";

		var kafkaHost = RedPandaTestServer.Instance.GetBootstrapAddress();

		await CreateKafkaTopicsAsync(kafkaHost);

		_server = new ConnectionServerTestWebApplicationBuilderFactory<SharpMUSH.ConnectionServer.Program>(
			redisConnection,
			kafkaHost);
	}

	private static async Task CreateKafkaTopicsAsync(string bootstrapServers)
	{
		// Format can be: "//127.0.0.1:9092/", "kafka://127.0.0.1:9092", or "127.0.0.1:9092"
		var cleanedAddress = bootstrapServers;

		if (cleanedAddress.Contains("://"))
		{
			cleanedAddress = cleanedAddress.Substring(cleanedAddress.IndexOf("://") + 3);
		}

		cleanedAddress = cleanedAddress.TrimStart('/');
		cleanedAddress = cleanedAddress.TrimEnd('/');

		var config = new AdminClientConfig
		{
			BootstrapServers = cleanedAddress,
			SocketTimeoutMs = 10000,
			ApiVersionRequestTimeoutMs = 10000
		};

		using var adminClient = new AdminClientBuilder(config).Build();

		var topics = new List<string>
		{
			"telnet-input",
			"telnet-output",
			"telnet-prompt",
			"websocket-input",
			"websocket-output",
			"websocket-prompt",
			"gmcp-output",
			"disconnect-connection",
			"broadcast",
			"update-player-preferences"
		};

		var topicSpecifications = topics.Select(topic => new TopicSpecification
		{
			Name = topic,
			NumPartitions = 1,
			ReplicationFactor = 1
		}).ToList();

		try
		{
			await adminClient.CreateTopicsAsync(topicSpecifications);

			await Task.Delay(2000);
		}
		catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists || r.Error.Code == ErrorCode.NoError))
		{
			// Topics already exist or were created successfully, which is fine
		}
	}

	public new async ValueTask DisposeAsync()
	{
		GC.SuppressFinalize(this);
		await ValueTask.CompletedTask;
	}
}
