using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using StackExchange.Redis;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Tests for Redis shared state store functionality.
/// Verifies that connection state is properly stored, retrieved, and shared across processes.
/// </summary>
public class RedisConnectionStateTests
{
	[ClassDataSource<RedisTestServer>(Shared = SharedType.PerTestSession)]
	public required RedisTestServer RedisTestServer { get; init; }

	private IConnectionStateStore CreateStateStore()
	{
		var port = RedisTestServer.Instance.GetMappedPublicPort(6379);
		var connectionString = $"localhost:{port}";
		var configuration = ConfigurationOptions.Parse(connectionString);
		configuration.AbortOnConnectFail = false;
		configuration.ConnectRetry = 3;
		configuration.ConnectTimeout = 5000;

		var redis = ConnectionMultiplexer.Connect(configuration);
		var logger = new LoggerFactory().CreateLogger<RedisConnectionStateStore>();

		return new RedisConnectionStateStore(redis, logger);
	}

	[Test]
	public async Task CanStoreAndRetrieveConnectionState()
	{
		// Arrange
		var store = CreateStateStore();
		var handle = 12345L;
		var connectionData = new ConnectionStateData
		{
			Handle = handle,
			PlayerRef = new DBRef(100),
			State = "LoggedIn",
			IpAddress = "192.168.1.1",
			Hostname = "test.local",
			ConnectionType = "telnet",
			ConnectedAt = DateTimeOffset.UtcNow,
			LastSeen = DateTimeOffset.UtcNow,
			Metadata = new Dictionary<string, string>
			{
				{ "TestKey", "TestValue" }
			}
		};

		// Act
		await store.SetConnectionAsync(handle, connectionData);
		var retrieved = await store.GetConnectionAsync(handle);

		// Assert
		await Assert.That(retrieved).IsNotNull();
		await Assert.That(retrieved!.Handle).IsEqualTo(handle);
		await Assert.That(retrieved.PlayerRef).IsEqualTo(new DBRef(100));
		await Assert.That(retrieved.State).IsEqualTo("LoggedIn");
		await Assert.That(retrieved.IpAddress).IsEqualTo("192.168.1.1");
		await Assert.That(retrieved.Hostname).IsEqualTo("test.local");
		await Assert.That(retrieved.ConnectionType).IsEqualTo("telnet");
		await Assert.That(retrieved.Metadata["TestKey"]).IsEqualTo("TestValue");
	}

	[Test]
	public async Task CanRemoveConnectionState()
	{
		// Arrange
		var store = CreateStateStore();
		var handle = 23456L;
		var connectionData = new ConnectionStateData
		{
			Handle = handle,
			PlayerRef = null,
			State = "Connected",
			IpAddress = "10.0.0.1",
			Hostname = "client.test",
			ConnectionType = "telnet",
			ConnectedAt = DateTimeOffset.UtcNow,
			LastSeen = DateTimeOffset.UtcNow,
			Metadata = new Dictionary<string, string>()
		};

		await store.SetConnectionAsync(handle, connectionData);

		// Act
		await store.RemoveConnectionAsync(handle);
		var retrieved = await store.GetConnectionAsync(handle);

		// Assert
		await Assert.That(retrieved).IsNull();
	}

	[Test]
	public async Task CanGetAllConnectionHandles()
	{
		// Arrange
		var store = CreateStateStore();
		var handle1 = 34567L;
		var handle2 = 34568L;
		var handle3 = 34569L;

		var data1 = new ConnectionStateData
		{
			Handle = handle1,
			PlayerRef = null,
			State = "Connected",
			IpAddress = "192.168.1.10",
			Hostname = "host1",
			ConnectionType = "telnet",
			ConnectedAt = DateTimeOffset.UtcNow,
			LastSeen = DateTimeOffset.UtcNow,
			Metadata = new Dictionary<string, string>()
		};

		var data2 = new ConnectionStateData
		{
			Handle = handle2,
			PlayerRef = new DBRef(200),
			State = "LoggedIn",
			IpAddress = "192.168.1.11",
			Hostname = "host2",
			ConnectionType = "telnet",
			ConnectedAt = DateTimeOffset.UtcNow,
			LastSeen = DateTimeOffset.UtcNow,
			Metadata = new Dictionary<string, string>()
		};

		var data3 = new ConnectionStateData
		{
			Handle = handle3,
			PlayerRef = null,
			State = "Connected",
			IpAddress = "192.168.1.12",
			Hostname = "host3",
			ConnectionType = "telnet",
			ConnectedAt = DateTimeOffset.UtcNow,
			LastSeen = DateTimeOffset.UtcNow,
			Metadata = new Dictionary<string, string>()
		};

		await store.SetConnectionAsync(handle1, data1);
		await store.SetConnectionAsync(handle2, data2);
		await store.SetConnectionAsync(handle3, data3);

		// Act
		var handles = await store.GetAllHandlesAsync();
		var handlesList = handles.ToList();

		// Assert
		await Assert.That(handlesList).Contains(handle1);
		await Assert.That(handlesList).Contains(handle2);
		await Assert.That(handlesList).Contains(handle3);
	}

	[Test]
	public async Task CanGetAllConnections()
	{
		// Arrange
		var store = CreateStateStore();
		var handle1 = 45678L;
		var handle2 = 45679L;

		var data1 = new ConnectionStateData
		{
			Handle = handle1,
			PlayerRef = new DBRef(300),
			State = "LoggedIn",
			IpAddress = "172.16.0.1",
			Hostname = "user1.test",
			ConnectionType = "telnet",
			ConnectedAt = DateTimeOffset.UtcNow,
			LastSeen = DateTimeOffset.UtcNow,
			Metadata = new Dictionary<string, string> { { "User", "Alice" } }
		};

		var data2 = new ConnectionStateData
		{
			Handle = handle2,
			PlayerRef = new DBRef(301),
			State = "LoggedIn",
			IpAddress = "172.16.0.2",
			Hostname = "user2.test",
			ConnectionType = "telnet",
			ConnectedAt = DateTimeOffset.UtcNow,
			LastSeen = DateTimeOffset.UtcNow,
			Metadata = new Dictionary<string, string> { { "User", "Bob" } }
		};

		await store.SetConnectionAsync(handle1, data1);
		await store.SetConnectionAsync(handle2, data2);

		// Act
		var connections = await store.GetAllConnectionsAsync();
		var connectionsList = connections.ToList();

		// Assert
		await Assert.That(connectionsList.Count).IsGreaterThanOrEqualTo(2);

		var conn1 = connectionsList.FirstOrDefault(c => c.Handle == handle1);
		var conn2 = connectionsList.FirstOrDefault(c => c.Handle == handle2);

		await Assert.That(conn1.Data).IsNotNull();
		await Assert.That(conn1.Data.PlayerRef).IsEqualTo(new DBRef(300));
		await Assert.That(conn1.Data.Metadata["User"]).IsEqualTo("Alice");

		await Assert.That(conn2.Data).IsNotNull();
		await Assert.That(conn2.Data.PlayerRef).IsEqualTo(new DBRef(301));
		await Assert.That(conn2.Data.Metadata["User"]).IsEqualTo("Bob");
	}

	[Test]
	public async Task CanUpdatePlayerBinding()
	{
		// Arrange
		var store = CreateStateStore();
		var handle = 56789L;
		var initialData = new ConnectionStateData
		{
			Handle = handle,
			PlayerRef = null,
			State = "Connected",
			IpAddress = "10.1.1.1",
			Hostname = "guest.test",
			ConnectionType = "telnet",
			ConnectedAt = DateTimeOffset.UtcNow,
			LastSeen = DateTimeOffset.UtcNow,
			Metadata = new Dictionary<string, string>()
		};

		await store.SetConnectionAsync(handle, initialData);

		// Act
		var playerRef = new DBRef(400);
		await store.SetPlayerBindingAsync(handle, playerRef);
		var updated = await store.GetConnectionAsync(handle);

		// Assert
		await Assert.That(updated).IsNotNull();
		await Assert.That(updated!.PlayerRef).IsEqualTo(playerRef);
		await Assert.That(updated.State).IsEqualTo("LoggedIn");
	}

	[Test]
	public async Task CanUpdateMetadata()
	{
		// Arrange
		var store = CreateStateStore();
		var handle = 67890L;
		var initialData = new ConnectionStateData
		{
			Handle = handle,
			PlayerRef = null,
			State = "Connected",
			IpAddress = "10.2.2.2",
			Hostname = "client.test",
			ConnectionType = "telnet",
			ConnectedAt = DateTimeOffset.UtcNow,
			LastSeen = DateTimeOffset.UtcNow,
			Metadata = new Dictionary<string, string> { { "Initial", "Value" } }
		};

		await store.SetConnectionAsync(handle, initialData);

		// Act
		await store.UpdateMetadataAsync(handle, "NewKey", "NewValue");
		var updated = await store.GetConnectionAsync(handle);

		// Assert
		await Assert.That(updated).IsNotNull();
		await Assert.That(updated!.Metadata["Initial"]).IsEqualTo("Value");
		await Assert.That(updated.Metadata["NewKey"]).IsEqualTo("NewValue");
	}

	[Test]
	public async Task MultipleProcessesCanShareState()
	{
		// Arrange - Simulate two different processes (ConnectionServer and Server)
		var storeProcess1 = CreateStateStore();
		var storeProcess2 = CreateStateStore();

		var handle = 78901L;
		var connectionData = new ConnectionStateData
		{
			Handle = handle,
			PlayerRef = null,
			State = "Connected",
			IpAddress = "192.168.100.1",
			Hostname = "shared.test",
			ConnectionType = "telnet",
			ConnectedAt = DateTimeOffset.UtcNow,
			LastSeen = DateTimeOffset.UtcNow,
			Metadata = new Dictionary<string, string> { { "Process", "1" } }
		};

		// Act - Process 1 writes
		await storeProcess1.SetConnectionAsync(handle, connectionData);

		var retrieved = await storeProcess2.GetConnectionAsync(handle);

		await storeProcess2.SetPlayerBindingAsync(handle, new DBRef(500));

		var updated = await storeProcess1.GetConnectionAsync(handle);

		// Assert
		await Assert.That(retrieved).IsNotNull();
		await Assert.That(retrieved!.Handle).IsEqualTo(handle);
		await Assert.That(retrieved.IpAddress).IsEqualTo("192.168.100.1");

		await Assert.That(updated).IsNotNull();
		await Assert.That(updated!.PlayerRef).IsEqualTo(new DBRef(500));
		await Assert.That(updated.State).IsEqualTo("LoggedIn");
	}

	[Test]
	public async Task StatePersiststAcrossReconnection()
	{
		// Arrange
		var store1 = CreateStateStore();
		var handle = 89012L;
		var connectionData = new ConnectionStateData
		{
			Handle = handle,
			PlayerRef = new DBRef(600),
			State = "LoggedIn",
			IpAddress = "10.10.10.10",
			Hostname = "persistent.test",
			ConnectionType = "telnet",
			ConnectedAt = DateTimeOffset.UtcNow,
			LastSeen = DateTimeOffset.UtcNow,
			Metadata = new Dictionary<string, string> { { "Session", "PersistentSession" } }
		};

		// Act - Store data with first connection
		await store1.SetConnectionAsync(handle, connectionData);

		// Simulate disconnect/reconnect by creating new store instance
		var store2 = CreateStateStore();
		var retrieved = await store2.GetConnectionAsync(handle);

		// Assert
		await Assert.That(retrieved).IsNotNull();
		await Assert.That(retrieved!.Handle).IsEqualTo(handle);
		await Assert.That(retrieved.PlayerRef).IsEqualTo(new DBRef(600));
		await Assert.That(retrieved.State).IsEqualTo("LoggedIn");
		await Assert.That(retrieved.Metadata["Session"]).IsEqualTo("PersistentSession");
	}

	[Test]
	public async Task NonExistentConnectionReturnsNull()
	{
		// Arrange
		var store = CreateStateStore();
		var nonExistentHandle = 99999L;

		// Act
		var result = await store.GetConnectionAsync(nonExistentHandle);

		// Assert
		await Assert.That(result).IsNull();
	}

	[Test]
	public async Task CanHandleConcurrentUpdates()
	{
		// Arrange
		var store = CreateStateStore();
		var handle = 11111L;
		var connectionData = new ConnectionStateData
		{
			Handle = handle,
			PlayerRef = null,
			State = "Connected",
			IpAddress = "192.168.50.1",
			Hostname = "concurrent.test",
			ConnectionType = "telnet",
			ConnectedAt = DateTimeOffset.UtcNow,
			LastSeen = DateTimeOffset.UtcNow,
			Metadata = new Dictionary<string, string>()
		};

		await store.SetConnectionAsync(handle, connectionData);

		// Act - Perform concurrent metadata updates
		var tasks = new List<Task>();
		for (int i = 0; i < 10; i++)
		{
			var index = i;
			tasks.Add(Task.Run(async () =>
			{
				await store.UpdateMetadataAsync(handle, $"Key{index}", $"Value{index}");
			}));
		}

		await Task.WhenAll(tasks);

		// Retrieve final state
		var result = await store.GetConnectionAsync(handle);

		// Assert - All updates should be present
		await Assert.That(result).IsNotNull();
		for (int i = 0; i < 10; i++)
		{
			await Assert.That(result!.Metadata[$"Key{i}"]).IsEqualTo($"Value{i}");
		}
	}
}
