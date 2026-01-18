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

	private IContainer? _instance;

	public IContainer Instance => _instance ?? throw new InvalidOperationException("Container not initialized. Call InitializeAsync first.");

	public async Task InitializeAsync()
	{
		_instance = new ContainerBuilder("prom/prometheus:latest")
			.WithName("sharpmush-test-prometheus")
			.WithLabel("reuse-hash", "sharpmush-prometheus-v1")
			.WithPortBinding(9090, 9090)
			.WithCommand("--config.file=/etc/prometheus/prometheus.yml", "--storage.tsdb.path=/prometheus")
			.WithResourceMapping(
				Encoding.UTF8.GetBytes(PrometheusConfig),
				"/etc/prometheus/prometheus.yml")
			.WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9090).ForPath("/-/ready")))
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
