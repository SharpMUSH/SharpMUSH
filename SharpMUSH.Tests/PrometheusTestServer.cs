using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Testcontainer for Prometheus instance for OpenTelemetry metrics collection.
/// Exposes Prometheus HTTP API (9090) and metrics scraping endpoint.
/// </summary>
public class PrometheusTestServer : IAsyncInitializer, IAsyncDisposable
{
	private const string PrometheusConfig = @"
global:
  scrape_interval: 5s
  evaluation_interval: 5s

scrape_configs:
  - job_name: 'test-server'
    static_configs:
      - targets: ['host.docker.internal:9092']
";

	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	public IContainer Instance => field ??= new ContainerBuilder("prom/prometheus:latest")
		.WithNetwork(DockerNetwork.Instance)
		.WithPortBinding(9090, 9090)
		.WithCommand("--config.file=/etc/prometheus/prometheus.yml", "--storage.tsdb.path=/prometheus")
		.WithResourceMapping(
			Encoding.UTF8.GetBytes(PrometheusConfig),
			"/etc/prometheus/prometheus.yml")
		.WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9090).ForPath("/-/ready")))
		.WithReuse(false)
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}
