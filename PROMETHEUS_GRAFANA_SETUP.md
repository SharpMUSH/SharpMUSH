# Prometheus and Grafana Setup for SharpMUSH

This guide explains how to use Prometheus and Grafana for collecting and visualizing OpenTelemetry metrics from SharpMUSH.

## Overview

Prometheus and Grafana are integrated into the SharpMUSH stack to provide observability for:
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
- **Prometheus**: http://localhost:9090 - Metrics collection and storage
- **Grafana**: http://localhost:3000 - Visualization dashboard (admin/admin)
- **SharpMUSH Server**: with Prometheus metrics endpoint on port 9092
- **ConnectionServer**: with Prometheus metrics endpoint on port 9091
- Supporting services (ArangoDB, Redpanda)

### Access Grafana Dashboard

Once the stack is running:
1. Open http://localhost:3000 in your browser
2. Log in with username: `admin`, password: `admin`
3. The SharpMUSH dashboard will be automatically provisioned
4. View real-time metrics including:
   - Function and command performance
   - Most frequently called functions/commands
   - Active connections and logged-in players
   - System health status

### Access Prometheus

To query metrics directly:
1. Open http://localhost:9090
2. Use PromQL to query metrics
3. Explore available metrics in the metrics explorer

## Standalone Setup (Without Docker Compose)

### Running Prometheus

Create a `prometheus.yml` configuration file (see repository for full example), then:

```bash
docker run -d --name prometheus \
  -p 9090:9090 \
  -v $(pwd)/prometheus.yml:/etc/prometheus/prometheus.yml \
  prom/prometheus:latest
```

### Running Grafana

```bash
docker run -d --name grafana \
  -p 3000:3000 \
  -e "GF_SECURITY_ADMIN_PASSWORD=admin" \
  grafana/grafana:latest
```

Then:
1. Add Prometheus as a data source in Grafana
2. Import the SharpMUSH dashboard from `grafana/provisioning/dashboards/sharpmush-dashboard.json`

### Configure SharpMUSH Services

```bash
export PROMETHEUS_PORT=9092
dotnet run --project SharpMUSH.Server
```

## Available Metrics

SharpMUSH exports the following metrics to Prometheus:

### Execution Performance
- `sharpmush_function_invocation_duration` - Function execution time histogram (ms)
  - Labels: `function_name`, `success`
- `sharpmush_command_invocation_duration` - Command execution time histogram (ms)
  - Labels: `command_name`, `success`
- `sharpmush_notification_speed` - Notification delivery time histogram (ms)
  - Labels: `notification_type`, `recipient_count`

### Connection Metrics
- `sharpmush_connection_events` - Connection lifecycle event counter
  - Labels: `event_type` (connected, disconnected, logged_in)
- `sharpmush_connections_active` - Current active connections (gauge)
- `sharpmush_players_logged_in` - Current logged-in players (gauge)

### Health Status
- `sharpmush_server_health` - Server health state (1=healthy, 0=unhealthy)
- `sharpmush_connectionserver_health` - ConnectionServer health state (1=healthy, 0=unhealthy)

## Querying Metrics from SharpMUSH

SharpMUSH includes built-in commands to query Prometheus metrics directly from the game:

### @metrics/slowest <time-range>
Shows the slowest functions in the specified time range.

Example:
```
@metrics/slowest 1h
```

### @metrics/popular <time-range>
Shows the most frequently called commands in the specified time range.

Example:
```
@metrics/popular 5m
```

### @metrics/query <promql>
Executes a custom PromQL query and returns results.

Example:
```
@metrics/query rate(sharpmush_function_invocation_duration_count[5m])
```

## Configuration Options

### Environment Variables

- `PROMETHEUS_PORT` - Port to expose Prometheus metrics endpoint (default: 9092 for Server, 9091 for ConnectionServer)

### Prometheus Configuration

The `prometheus.yml` file configures:
- Scrape intervals (how often to collect metrics)
- Target services to monitor
- Data retention policies

### Grafana Configuration

Grafana is configured via:
- `grafana/provisioning/datasources/` - Data source configuration (Prometheus)
- `grafana/provisioning/dashboards/` - Dashboard definitions

## Example PromQL Queries

### Top 10 slowest functions (average duration)
```promql
topk(10, avg by (function_name) (rate(sharpmush_function_invocation_duration_sum[5m]) / rate(sharpmush_function_invocation_duration_count[5m])))
```

### Top 10 most called commands
```promql
topk(10, rate(sharpmush_command_invocation_duration_count[5m]))
```

### Function success rate
```promql
rate(sharpmush_function_invocation_duration_count{success="true"}[5m]) / rate(sharpmush_function_invocation_duration_count[5m])
```

### Command p95 latency
```promql
histogram_quantile(0.95, rate(sharpmush_command_invocation_duration_bucket[5m]))
```

## Troubleshooting

### Metrics not appearing in Prometheus

1. Check that Prometheus is running: `docker ps | grep prometheus`
2. Verify the metrics endpoint is accessible: `curl http://localhost:9092/metrics`
3. Check SharpMUSH logs for OpenTelemetry export errors
4. Verify Prometheus scrape configuration in `prometheus.yml`

### Grafana not showing data

1. Verify Prometheus data source is configured correctly in Grafana
2. Check that Prometheus is successfully scraping metrics (Status > Targets in Prometheus UI)
3. Ensure time range in Grafana matches when metrics were generated
4. Check Grafana logs: `docker logs sharpmush-grafana`

### Dashboard not loading

1. Verify dashboard provisioning files are mounted correctly
2. Check Grafana logs for provisioning errors
3. Manually import the dashboard JSON if auto-provisioning fails

## Advanced Configuration

For production deployments, consider:
- Setting up persistent storage for Prometheus data
- Configuring Prometheus recording rules for complex queries
- Setting up alerting rules for important metrics
- Enabling authentication and HTTPS for Grafana
- Configuring longer retention periods for metrics
- Using remote storage backends (e.g., Thanos, Cortex)

## Architecture Notes

SharpMUSH uses:
- **OpenTelemetry Metrics API** for instrumentation
- **Prometheus Exporter** to expose metrics in Prometheus format
- **Prometheus** for metrics collection, storage, and querying
- **Grafana** for visualization and dashboards

The metrics flow:
1. SharpMUSH instruments code using OpenTelemetry Meters
2. Metrics are exposed via HTTP endpoints (/metrics)
3. Prometheus scrapes these endpoints periodically
4. Data is stored in Prometheus TSDB
5. Grafana queries Prometheus for visualization
6. SharpMUSH can query Prometheus via HTTP API for in-game metrics commands

## Migration from Jaeger

If you're migrating from Jaeger:
- Jaeger was used for distributed tracing with OTLP
- Prometheus is used for metrics collection and storage
- Both use OpenTelemetry, but with different exporters
- No changes to instrumentation code are needed
- Only configuration and deployment changes are required
