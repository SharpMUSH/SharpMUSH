using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core.Interfaces;
using TUnit.AspNetCore;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using KafkaFlow;

namespace SharpMUSH.Tests;

/// <summary>
/// Integration test factory for SharpMUSH.ConnectionServer.
/// Manages test infrastructure lifecycle and provides access to services.
/// IMPORTANT: This factory starts the ConnectionServer on REAL network ports (4201 for telnet, 4202 for HTTP)
/// to enable integration testing of TCP/telnet connections and Kafka messaging.
/// </summary>
public class ConnectionServerWebAppFactory : TestWebApplicationFactory<SharpMUSH.ConnectionServer.Program>, IAsyncInitializer, IAsyncDisposable
{
	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	[ClassDataSource<RedPandaTestServer>(Shared = SharedType.PerTestSession)]
	public required RedPandaTestServer RedPandaTestServer { get; init; }
	
	[ClassDataSource<RedisTestServer>(Shared = SharedType.PerTestSession)]
	public required RedisTestServer RedisTestServer { get; init; }

	public new IServiceProvider Services => _app!.Services;
	private WebApplication? _app;
	private Task? _runTask;
	private CancellationTokenSource? _cts;

	public virtual async Task InitializeAsync()
	{
		var redisPort = RedisTestServer.Instance.GetMappedPublicPort(6379);
		var redisConnection = $"localhost:{redisPort}";

		var kafkaHost = RedPandaTestServer.Instance.GetBootstrapAddress();

		// Create topics for ConnectionServer
		await CreateKafkaTopicsAsync(kafkaHost);

		// Set environment variables for ConnectionServer configuration
		Environment.SetEnvironmentVariable("REDIS_CONNECTION", redisConnection);
		Environment.SetEnvironmentVariable("KAFKA_HOST", kafkaHost);

		// Create the actual ConnectionServer application using Program.CreateHostBuilderAsync
		_app = await SharpMUSH.ConnectionServer.Program.CreateHostBuilderAsync([]);

		// Start Kafka bus
		var bus = _app.Services.CreateKafkaBus();
		await bus.StartAsync();

		// Start the application running in the background
		_cts = new CancellationTokenSource();
		_runTask = Task.Run(async () =>
		{
			try
			{
				await _app.StartAsync(_cts.Token);
			}
			catch (OperationCanceledException)
			{
				// Expected during shutdown
			}
		}, _cts.Token);

		// Wait for the server to start listening
		await Task.Delay(1000);
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
			"gmcp-signal",
			"msdp-update",
			"mssp-update",
			"naws-update",
			"connection-established",
			"connection-closed",
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
		
		// Stop the ConnectionServer
		if (_cts is not null)
		{
			await _cts.CancelAsync();
			_cts.Dispose();
		}

		if (_runTask is not null)
		{
			try
			{
				await _runTask;
			}
			catch (OperationCanceledException)
			{
				// Expected when canceling
			}
		}

		if (_app is not null)
		{
			await _app.DisposeAsync();
		}
	}
}
