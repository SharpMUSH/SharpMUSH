# Jaeger OpenTelemetry Setup for SharpMUSH

This guide explains how to use Jaeger for collecting and visualizing OpenTelemetry metrics from SharpMUSH.

## Overview

Jaeger is integrated into the SharpMUSH stack to provide observability for:
- Function and command invocation times
- Notification delivery speed
- Connection metrics and health
- Server and ConnectionServer health status

## Quick Start with Docker Compose

The easiest way to get started is using the included docker-compose.yml:

```bash
docker-compose up -d
```

This will start:
- **Jaeger UI**: http://localhost:16686
- **SharpMUSH Server**: with OpenTelemetry configured
- **ConnectionServer**: with OpenTelemetry configured
- Supporting services (ArangoDB, Redpanda)

### Access Jaeger UI

Once the stack is running, open http://localhost:16686 in your browser to:
- View service metrics
- Analyze performance traces
- Monitor health status
- Search and filter telemetry data

## Standalone Jaeger (Without Docker Compose)

To run just Jaeger for local development:

```bash
docker run -d --name jaeger \
  -p 16686:16686 \
  -p 4317:4317 \
  -p 4318:4318 \
  jaegertracing/all-in-one:latest
```

Then configure your SharpMUSH services to point to it:
```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
dotnet run --project SharpMUSH.Server
```

## Using Jaeger in Tests

The `JaegerTestServer` class provides Testcontainers support for integration tests:

```csharp
using SharpMUSH.Tests;

public class MyTests
{
    [Test]
    public async Task TestWithJaeger()
    {
        // Jaeger container will be automatically started
        var jaeger = new JaegerTestServer();
        await jaeger.InitializeAsync();
        
        // Configure your test to use the Jaeger endpoint
        Environment.SetEnvironmentVariable(
            "OTEL_EXPORTER_OTLP_ENDPOINT", 
            $"http://localhost:{jaeger.Instance.GetMappedPublicPort(4317)}"
        );
        
        // Run your tests...
        
        await jaeger.DisposeAsync();
    }
}
```

## Available Metrics

SharpMUSH exports the following metrics to Jaeger:

### Execution Performance
- `sharpmush.function.invocation.duration` - Function execution time (ms)
  - Labels: `function.name`, `success`
- `sharpmush.command.invocation.duration` - Command execution time (ms)
  - Labels: `command.name`, `success`
- `sharpmush.notification.speed` - Notification delivery time (ms)
  - Labels: `notification.type`, `recipient.count`

### Connection Metrics
- `sharpmush.connection.events` - Connection lifecycle events
  - Labels: `event.type` (connected, disconnected, logged_in)
- `sharpmush.connections.active` - Current active connections
- `sharpmush.players.logged_in` - Current logged-in players

### Health Status
- `sharpmush.server.health` - Server health (1=healthy, 0=unhealthy)
- `sharpmush.connectionserver.health` - ConnectionServer health (1=healthy, 0=unhealthy)

## Configuration Options

### Environment Variables

- `OTEL_EXPORTER_OTLP_ENDPOINT` - Jaeger OTLP endpoint (default: http://localhost:4317)

### Docker Compose Service Ports

- `16686` - Jaeger UI
- `4317` - OTLP gRPC receiver (used by SharpMUSH)
- `4318` - OTLP HTTP receiver

## Troubleshooting

### Metrics not appearing in Jaeger

1. Check that Jaeger is running: `docker ps | grep jaeger`
2. Verify the OTLP endpoint is accessible: `curl http://localhost:4317`
3. Check SharpMUSH logs for OpenTelemetry connection errors
4. Ensure the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable is set correctly

### Jaeger UI not loading

1. Verify the container is healthy: `docker ps`
2. Check logs: `docker logs sharpmush-jaeger`
3. Ensure port 16686 is not already in use

## Advanced Configuration

For production deployments, consider:
- Using a persistent storage backend (Elasticsearch, Cassandra)
- Configuring sampling rates
- Setting up authentication
- Enabling HTTPS for the UI

See the [Jaeger documentation](https://www.jaegertracing.io/docs/) for more details.

## Alternative Collectors

While this setup uses Jaeger, the OpenTelemetry implementation supports any OTLP-compatible collector:
- Grafana + Prometheus
- SigNoz
- New Relic
- Datadog
- Honeycomb

Simply change the `OTEL_EXPORTER_OTLP_ENDPOINT` to point to your preferred collector.
