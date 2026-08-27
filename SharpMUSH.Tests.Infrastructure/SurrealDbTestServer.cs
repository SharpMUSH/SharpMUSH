using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Testcontainer for SurrealDB, as an alternative to the default embedded <c>mem://</c> engine
/// (see <see cref="ServerWebAppFactory"/>). Only starts when
/// <c>SHARPMUSH_SURREALDB_USE_TESTCONTAINER</c> is set (in addition to
/// <c>SHARPMUSH_DATABASE_PROVIDER=surrealdb</c>) - the embedded engine remains the default for
/// local dev and CI unless this is explicitly opted into, pending a measured comparison of the
/// two under CI's resource constraints.
/// </summary>
public class SurrealDbTestServer : IAsyncInitializer, IAsyncDisposable
{
	private const int HttpPort = 8000;

	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	private IContainer? _instance;

	// Unauthenticated: keeps this a pure test-infrastructure change. The production connection
	// string built in Startup.cs only carries Endpoint/Namespace/Database, no credentials - adding
	// Username/Password there for a test-only container would touch shipped code for something
	// that only needs to exist in CI/local test runs.
	public IContainer Instance => _instance ??= new ContainerBuilder("surrealdb/surrealdb:v2")
		.WithNetwork(DockerNetwork.Instance)
		.WithPortBinding(HttpPort, true)
		.WithCommand("start", "--unauthenticated", "memory")
		.WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Started web server on"))
		.WithReuse(false)
		.Build();

	public string Endpoint => $"ws://localhost:{Instance.GetMappedPublicPort(HttpPort)}/rpc";

	public static bool IsEnabled =>
		string.Equals(
			Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER"),
			"surrealdb",
			StringComparison.OrdinalIgnoreCase)
		&& !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SHARPMUSH_SURREALDB_USE_TESTCONTAINER"));

	public async Task InitializeAsync()
	{
		if (IsEnabled)
		{
			await Instance.StartAsync();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_instance is not null)
		{
			try
			{
				await _instance.StopAsync();
			}
			catch
			{
				// Podman may fail if the network was already removed
			}

			try
			{
				await _instance.DisposeAsync();
			}
			catch
			{
				// Podman may fail if the network was already removed
			}
		}
	}
}
