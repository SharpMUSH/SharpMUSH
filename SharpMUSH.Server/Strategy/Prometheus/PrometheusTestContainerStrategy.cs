using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using System.Text;

namespace SharpMUSH.Server.Strategy.Prometheus;

/// <summary>
/// Strategy for running Prometheus in a TestContainer for local development.
/// Automatically starts a Prometheus container when no external instance is configured.
/// </summary>
public class PrometheusTestContainerStrategy : PrometheusStrategy
{
	private IContainer? _container;
	private const string PrometheusConfig = @"
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: 'sharpmush-server'
    static_configs:
      - targets: ['host.docker.internal:9092']

  - job_name: 'connectionserver'
    static_configs:
      - targets: ['host.docker.internal:9091']
";

	public override string GetPrometheusUrl()
	{
		if (_container == null)
		{
			throw new InvalidOperationException("Prometheus container not initialized. Call InitializeAsync first.");
		}

		var port = _container.GetMappedPublicPort(9090);
		return $"http://localhost:{port}";
	}

	public override async ValueTask InitializeAsync()
	{
		_container = new ContainerBuilder()
			.WithImage("prom/prometheus:latest")
			.WithPortBinding(9090, true) // Random host port
			.WithCommand("--config.file=/etc/prometheus/prometheus.yml", "--storage.tsdb.path=/prometheus")
			.WithResourceMapping(
				Encoding.UTF8.GetBytes(PrometheusConfig),
				"/etc/prometheus/prometheus.yml")
			.WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9090).ForPath("/-/ready")))
			.Build();

		await _container.StartAsync();
	}

	public override async ValueTask DisposeAsync()
	{
		if (_container != null)
		{
			await _container.DisposeAsync();
			_container = null;
		}
	}
}
