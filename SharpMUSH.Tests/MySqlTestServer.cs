using Testcontainers.MySql;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class MySqlTestServer : IAsyncInitializer, IAsyncDisposable
{
	private MySqlContainer? _instance;
	
	public MySqlContainer Instance => _instance ?? throw new InvalidOperationException("Container not initialized. Call InitializeAsync first.");

	public async Task InitializeAsync()
	{
		_instance = new MySqlBuilder("mysql:latest")
			.WithName("sharpmush-test-mysql")
			.WithLabel("reuse-hash", "sharpmush-mysql-v1")
			.WithDatabase("sharpmush_test")
			.WithUsername("testuser")
			.WithPassword("testpass")
			.WithReuse(true)
			.Build();
		await _instance.StartAsync();
	}
	
	public async ValueTask DisposeAsync()
	{
		if (_instance != null)
		{
			await _instance.DisposeAsync();
		}
	}
}
