using Testcontainers.MySql;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests.ClassDataSources;

public class MySqlTestServer : IAsyncInitializer
{
	private static readonly Lazy<MySqlContainer> _container = new(() => 
		new MySqlBuilder("mysql:latest")
			.WithName("sharpmush-test-mysql")
			.WithLabel("reuse-id", "SharpMUSH")
			.WithLabel("reuse-hash", "sharpmush-mysql-v1")
			.WithDatabase("sharpmush_test")
			.WithUsername("testuser")
			.WithPassword("testpass")
			.WithReuse(true)
			.Build());
	
	private static bool _initialized;
	private static readonly object _lock = new();

	public MySqlContainer Instance => _container.Value;

	public async Task InitializeAsync()
	{
		// Ensure container is only started once across all test sessions
		lock (_lock)
		{
			if (_initialized) return;
			_initialized = true;
		}
		
		await Instance.StartAsync();
	}
}