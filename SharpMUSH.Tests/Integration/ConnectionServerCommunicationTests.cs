using System.Net.Sockets;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Messages;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Integration tests that verify ConnectionServer and Server properly communicate through Kafka.
/// Tests telnet connections and validates that messages are published to the correct Kafka topics.
/// </summary>
[NotInParallel]
public class ConnectionServerCommunicationTests
{
	[ClassDataSource<ConnectionServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ConnectionServerWebAppFactory ConnectionServerFactory { get; init; }

	[ClassDataSource<RedPandaTestServer>(Shared = SharedType.PerTestSession)]
	public required RedPandaTestServer RedPandaTestServer { get; init; }

	/// <summary>
	/// Verifies that a telnet connection can be established to the ConnectionServer.
	/// </summary>
	[Test]
	public async ValueTask TelnetConnection_CanBeEstablished()
	{
		// Arrange: Get the telnet port from the ConnectionServer
		var connectionServerOptions = ConnectionServerFactory.Services.GetRequiredService<SharpMUSH.ConnectionServer.Configuration.ConnectionServerOptions>();
		var telnetPort = connectionServerOptions.TelnetPort;

		using var client = new TcpClient();

		// Act: Connect to the telnet port
		await client.ConnectAsync("localhost", telnetPort);

		// Assert: Connection should be established
		await Assert.That(client.Connected).IsTrue();

		// Cleanup
		client.Close();
	}

	/// <summary>
	/// Verifies that when a telnet message is sent to ConnectionServer, it publishes a TelnetInputMessage to Kafka.
	/// </summary>
	[Test]
	public async ValueTask TelnetInput_PublishesToKafka()
	{
		// Arrange: Get the telnet port and Kafka bootstrap address
		var connectionServerOptions = ConnectionServerFactory.Services.GetRequiredService<SharpMUSH.ConnectionServer.Configuration.ConnectionServerOptions>();
		var telnetPort = connectionServerOptions.TelnetPort;
		var kafkaBootstrap = RedPandaTestServer.Instance.GetBootstrapAddress();

		// Clean the bootstrap address
		var cleanedAddress = kafkaBootstrap;
		if (cleanedAddress.Contains("://"))
		{
			cleanedAddress = cleanedAddress.Substring(cleanedAddress.IndexOf("://") + 3);
		}
		if (cleanedAddress.EndsWith("/"))
		{
			cleanedAddress = cleanedAddress[..^1];
		}

		// Create a Kafka consumer to listen for messages
		var consumerConfig = new ConsumerConfig
		{
			BootstrapServers = cleanedAddress,
			GroupId = "test-consumer-group-" + Guid.NewGuid().ToString(),
			AutoOffsetReset = AutoOffsetReset.Earliest,
			EnableAutoCommit = false
		};

		using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
		
		// Subscribe to the telnet-input topic BEFORE making the connection
		consumer.Subscribe("telnet-input");
		
		// Give the consumer a moment to establish subscription
		await Task.Delay(500);

		// Act: Connect to telnet server
		using var client = new TcpClient();
		await client.ConnectAsync("localhost", telnetPort);
		await Assert.That(client.Connected).IsTrue();

		var stream = client.GetStream();

		// Wait a moment for connection to be fully established
		await Task.Delay(500);

		// Send a test message to the ConnectionServer
		var testMessage = "test command\r\n";
		var messageBytes = Encoding.UTF8.GetBytes(testMessage);
		await stream.WriteAsync(messageBytes);
		await stream.FlushAsync();

		// Wait for message to be processed and published to Kafka
		await Task.Delay(1000);

		// Assert: Poll for the message from Kafka
		var consumeResult = consumer.Consume(TimeSpan.FromSeconds(5));
		
		await Assert.That(consumeResult).IsNotNull();
		await Assert.That(consumeResult!.Message).IsNotNull();
		await Assert.That(consumeResult.Message.Value).Contains("test command");

		// Cleanup
		consumer.Close();
		client.Close();
	}

	/// <summary>
	/// Verifies that ConnectionEstablishedMessage is published to Kafka when a telnet connection is made.
	/// </summary>
	[Test]
	public async ValueTask TelnetConnection_PublishesConnectionEstablishedMessage()
	{
		// Arrange: Get the telnet port and Kafka bootstrap address
		var connectionServerOptions = ConnectionServerFactory.Services.GetRequiredService<SharpMUSH.ConnectionServer.Configuration.ConnectionServerOptions>();
		var telnetPort = connectionServerOptions.TelnetPort;
		var kafkaBootstrap = RedPandaTestServer.Instance.GetBootstrapAddress();

		// Clean the bootstrap address
		var cleanedAddress = kafkaBootstrap;
		if (cleanedAddress.Contains("://"))
		{
			cleanedAddress = cleanedAddress.Substring(cleanedAddress.IndexOf("://") + 3);
		}
		if (cleanedAddress.EndsWith("/"))
		{
			cleanedAddress = cleanedAddress[..^1];
		}

		// Create a Kafka consumer to listen for connection-established messages
		var consumerConfig = new ConsumerConfig
		{
			BootstrapServers = cleanedAddress,
			GroupId = "test-consumer-group-" + Guid.NewGuid().ToString(),
			AutoOffsetReset = AutoOffsetReset.Earliest,
			EnableAutoCommit = false
		};

		using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
		
		// Subscribe to the connection-established topic BEFORE making the connection
		consumer.Subscribe("connection-established");

		// Give the consumer a moment to establish subscription
		await Task.Delay(500);

		// Act: Connect to telnet server
		using var client = new TcpClient();
		await client.ConnectAsync("localhost", telnetPort);
		await Assert.That(client.Connected).IsTrue();

		// Wait for connection established message to be published
		await Task.Delay(1000);

		// Assert: Poll for the connection established message from Kafka
		var consumeResult = consumer.Consume(TimeSpan.FromSeconds(5));
		
		await Assert.That(consumeResult).IsNotNull();
		await Assert.That(consumeResult!.Message).IsNotNull();
		await Assert.That(consumeResult.Message.Value).Contains("telnet");
		// Connection from localhost appears as ::1 (IPv6 localhost)
		await Assert.That(consumeResult.Message.Value).Contains("::1");

		// Cleanup
		consumer.Close();
		client.Close();
	}

	/// <summary>
	/// Verifies that ConnectionClosedMessage is published to Kafka when a telnet connection is closed.
	/// </summary>
	[Test]
	public async ValueTask TelnetDisconnect_PublishesConnectionClosedMessage()
	{
		// Arrange: Get the telnet port and Kafka bootstrap address
		var connectionServerOptions = ConnectionServerFactory.Services.GetRequiredService<SharpMUSH.ConnectionServer.Configuration.ConnectionServerOptions>();
		var telnetPort = connectionServerOptions.TelnetPort;
		var kafkaBootstrap = RedPandaTestServer.Instance.GetBootstrapAddress();

		// Clean the bootstrap address
		var cleanedAddress = kafkaBootstrap;
		if (cleanedAddress.Contains("://"))
		{
			cleanedAddress = cleanedAddress.Substring(cleanedAddress.IndexOf("://") + 3);
		}
		if (cleanedAddress.EndsWith("/"))
		{
			cleanedAddress = cleanedAddress[..^1];
		}

		// Create a Kafka consumer to listen for connection-closed messages
		var consumerConfig = new ConsumerConfig
		{
			BootstrapServers = cleanedAddress,
			GroupId = "test-consumer-group-" + Guid.NewGuid().ToString(),
			AutoOffsetReset = AutoOffsetReset.Earliest,
			EnableAutoCommit = false
		};

		using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
		
		// Subscribe to the connection-closed topic BEFORE making the connection
		consumer.Subscribe("connection-closed");

		// Give the consumer a moment to establish subscription
		await Task.Delay(500);

		// Act: Connect and then disconnect from telnet server
		using var client = new TcpClient();
		await client.ConnectAsync("localhost", telnetPort);
		await Assert.That(client.Connected).IsTrue();

		// Wait for connection to be established
		await Task.Delay(200);

		// Close the connection
		client.Close();

		// Wait for connection closed message to be published
		await Task.Delay(1000);

		// Assert: Poll for the connection closed message from Kafka
		var consumeResult = consumer.Consume(TimeSpan.FromSeconds(5));
		
		await Assert.That(consumeResult).IsNotNull();
		await Assert.That(consumeResult!.Message).IsNotNull();
		// Connection closed message should contain a timestamp
		await Assert.That(consumeResult.Message.Value).IsNotNull();

		// Cleanup
		consumer.Close();
	}
}
