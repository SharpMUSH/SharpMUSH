using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class PostgreSqlTestServer : IAsyncInitializer, IAsyncDisposable
{
	public PostgreSqlContainer Instance { get; } = new PostgreSqlBuilder("postgres:latest")
		.WithDatabase("sharpmush_test")
		.WithUsername("testuser")
		.WithPassword("testpass")
		.WithReuse(false)
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}
