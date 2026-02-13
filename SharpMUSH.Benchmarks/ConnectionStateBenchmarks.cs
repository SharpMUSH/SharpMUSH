using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using StackExchange.Redis;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Benchmarks to measure actual performance difference between in-memory and Redis operations
/// for connection state management.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[BenchmarkCategory("Connection State")]
public class ConnectionStateBenchmarks
{
	private ConcurrentDictionary<long, string>? _inMemoryCache;
	private IConnectionMultiplexer? _redis;
	private IDatabase? _redisDb;
	private RedisConnectionStateStore? _redisStore;
	private const long TestHandle = 12345;
	
	[GlobalSetup]
	public async Task Setup()
	{
		// Setup in-memory dictionary
		_inMemoryCache = new ConcurrentDictionary<long, string>();
		_inMemoryCache.TryAdd(TestHandle, "test data");
		
		// Setup Redis connection (if available)
		try
		{
			var redisHost = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
			_redis = await ConnectionMultiplexer.ConnectAsync(redisHost);
			_redisDb = _redis.GetDatabase();
			_redisStore = new RedisConnectionStateStore(_redis, NullLogger<RedisConnectionStateStore>.Instance);
			
			// Pre-populate Redis
			await _redisStore.SetConnectionAsync(TestHandle, new ConnectionStateData
			{
				Handle = TestHandle,
				PlayerRef = null,
				State = "Connected",
				IpAddress = "127.0.0.1",
				Hostname = "localhost",
				ConnectionType = "telnet",
				ConnectedAt = DateTimeOffset.UtcNow,
				LastSeen = DateTimeOffset.UtcNow,
				Metadata = new Dictionary<string, string> { { "test", "data" } }
			});
		}
		catch
		{
			// Redis not available - skip Redis benchmarks
		}
	}
	
	[GlobalCleanup]
	public async Task Cleanup()
	{
		if (_redisStore != null)
		{
			await _redisStore.RemoveConnectionAsync(TestHandle);
			await _redisStore.DisposeAsync();
		}
		if (_redis != null)
		{
			await _redis.CloseAsync();
			_redis.Dispose();
		}
	}
	
	[Benchmark(Baseline = true)]
	[BenchmarkCategory("Read")]
	public string? InMemory_Get()
	{
		return _inMemoryCache!.GetValueOrDefault(TestHandle);
	}
	
	[Benchmark]
	[BenchmarkCategory("Read")]
	public async Task<ConnectionStateData?> Redis_Get()
	{
		if (_redisStore == null) return null;
		return await _redisStore.GetConnectionAsync(TestHandle);
	}
	
	[Benchmark]
	[BenchmarkCategory("Write")]
	public void InMemory_Update()
	{
		_inMemoryCache!.AddOrUpdate(TestHandle, "updated", (_, _) => "updated");
	}
	
	[Benchmark]
	[BenchmarkCategory("Write")]
	public async Task Redis_Update()
	{
		if (_redisStore == null) return;
		await _redisStore.UpdateMetadataAsync(TestHandle, "test", "updated");
	}
	
	[Benchmark]
	[BenchmarkCategory("Mixed")]
	[Arguments(10)]
	[Arguments(100)]
	public void InMemory_GetAndUpdate_Sequence(int iterations)
	{
		for (int i = 0; i < iterations; i++)
		{
			var value = _inMemoryCache!.GetValueOrDefault(TestHandle);
			_inMemoryCache!.AddOrUpdate(TestHandle, "updated", (_, _) => "updated");
		}
	}
	
	[Benchmark]
	[BenchmarkCategory("Mixed")]
	[Arguments(10)]
	[Arguments(100)]
	public async Task Redis_GetAndUpdate_Sequence(int iterations)
	{
		if (_redisStore == null) return;
		
		for (int i = 0; i < iterations; i++)
		{
			var data = await _redisStore!.GetConnectionAsync(TestHandle);
			if (data != null)
			{
				await _redisStore!.UpdateMetadataAsync(TestHandle, "test", "updated");
			}
		}
	}
}
