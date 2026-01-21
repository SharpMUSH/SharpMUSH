using Testcontainers.MySql;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests.ClassDataSources;

public class MySqlTestServer : IAsyncInitializer, IAsyncDisposable
{
	public MySqlContainer Instance { get; } = new MySqlBuilder("mysql:latest")
		.WithName("sharpmush-test-mysql")
		.WithLabel("reuse-hash", "sharpmush-mysql-v1")
		.WithDatabase("sharpmush_test")
		.WithUsername("testuser")
		.WithPassword("testpass")
		.WithReuse(true)
		.Build();

	public async Task InitializeAsync()
	{
		await Instance.StartAsync();
	}

	public async ValueTask DisposeAsync()
	{
		await Instance.DisposeAsync();
	}
}