using Testcontainers.MySql;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class MySqlTestServer : IAsyncInitializer, IAsyncDisposable
{
	public MySqlContainer Instance { get; } = new MySqlBuilder("mysql:latest")
		.WithDatabase("sharpmush_test")
		.WithUsername("testuser")
		.WithPassword("testpass")
		.WithReuse(true)
		.WithLabel("testcontainers.reuse.hash", "sharpmush-mysql-test")
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}
