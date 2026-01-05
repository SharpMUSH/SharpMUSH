# Kafka/RedPanda Setup for SharpMUSH

## Overview

SharpMUSH now supports Kafka/RedPanda as an alternative message broker to RabbitMQ. Kafka/RedPanda provides streaming-optimized message delivery with better throughput for high-volume scenarios like @dolist operations.

## Configuration

### Enable Kafka/RedPanda

In your configuration (appsettings.json or Program.cs), set:

```csharp
services.AddConnectionServerMessaging(options =>
{
    options.BrokerType = MessageBrokerType.Kafka;
    options.Host = "localhost";  // RedPanda/Kafka host
    options.Port = 9092;          // Default Kafka port
    options.TelnetOutputTopic = "telnet-output";
    options.ConsumerGroupId = "sharpmush-consumer-group";
});
```

### Key Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `BrokerType` | `RabbitMQ` | Set to `Kafka` to use Kafka/RedPanda |
| `Host` | `localhost` | Kafka/RedPanda broker host |
| `Port` | `5672` | Port (9092 for Kafka, 19092 for RedPanda default) |
| `TelnetOutputTopic` | `telnet-output` | Topic name for telnet messages |
| `ConsumerGroupId` | `sharpmush-consumer-group` | Consumer group ID |
| `EnableIdempotence` | `true` | Ensures exactly-once message delivery |
| `CompressionType` | `lz4` | Compression (none, gzip, snappy, lz4, zstd) |
| `BatchSize` | `32768` | Batch size in bytes (32KB) |
| `LingerMs` | `5` | How long to wait for batching (milliseconds) |
| `MaxMessageBytes` | `6291456` | Maximum message size (6MB for production) |

## RedPanda Setup

### Using Docker

```bash
# Start RedPanda (Kafka-compatible)
docker run -d \
  --name redpanda \
  -p 9092:9092 \
  -p 19092:19092 \
  docker.redpanda.com/vectorized/redpanda:latest \
  redpanda start \
  --overprovisioned \
  --smp 1 \
  --memory 1G \
  --reserve-memory 0M \
  --node-id 0 \
  --check=false \
  --kafka-addr 0.0.0.0:9092 \
  --advertise-kafka-addr localhost:9092
```

### Using RedPanda Console

```bash
# Start RedPanda Console for monitoring
docker run -d \
  --name redpanda-console \
  -p 8080:8080 \
  -e KAFKA_BROKERS=host.docker.internal:9092 \
  docker.redpanda.com/vectorized/console:latest
```

Access at: http://localhost:8080

## Kafka Setup

### Using Docker

```bash
# Start Zookeeper
docker run -d \
  --name zookeeper \
  -p 2181:2181 \
  -e ZOOKEEPER_CLIENT_PORT=2181 \
  confluentinc/cp-zookeeper:latest

# Start Kafka
docker run -d \
  --name kafka \
  -p 9092:9092 \
  -e KAFKA_ZOOKEEPER_CONNECT=host.docker.internal:2181 \
  -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://localhost:9092 \
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 \
  confluentinc/cp-kafka:latest
```

## Message Size Configuration

SharpMUSH is configured for **6MB maximum message size** to handle production workloads. This is set via:

- `MaxMessageBytes` in MessageQueueOptions (6MB)
- Applied to producer `max.request.size` and `message.max.bytes`
- Applied to consumer `fetch.max.bytes` and `max.partition.fetch.bytes`

### RedPanda Configuration

RedPanda automatically handles large messages up to 1MB by default. For 6MB messages, configure:

```yaml
# redpanda.yaml
redpanda:
  kafka_api:
    - address: 0.0.0.0
      port: 9092
  
  # Increase max message size to 6MB
  max_message_bytes: 6291456
```

Or via command line:

```bash
rpk cluster config set max_message_size 6291456
```

### Kafka Configuration

For Kafka, configure `server.properties`:

```properties
# Maximum message size: 6MB
message.max.bytes=6291456
replica.fetch.max.bytes=6291456
```

## Performance Expectations

### Expected Improvements

- **2-5x improvement** over baseline RabbitMQ due to:
  - Streaming-optimized architecture
  - Better batching (5ms linger time)
  - Native compression (lz4)
  - Lower per-message overhead

### Why Not 600x?

The architectural limitation remains: @dolist makes 1000 sequential `await Publish()` calls. Kafka/RedPanda can't batch messages that are published individually. To achieve iter()-level performance (600x improvement), application-level accumulation is still required.

## Monitoring

### RedPanda Console

- View topics, messages, consumer groups
- Monitor throughput and lag
- Access at http://localhost:8080

### Kafka Tools

```bash
# List topics
kafka-topics --bootstrap-server localhost:9092 --list

# Describe topic
kafka-topics --bootstrap-server localhost:9092 --describe --topic telnet-output

# Monitor consumer group
kafka-consumer-groups --bootstrap-server localhost:9092 --describe --group sharpmush-consumer-group
```

## Troubleshooting

### Message Too Large Error

If you see "Message too large" errors:

1. Check RedPanda/Kafka `max_message_bytes` configuration
2. Verify `MaxMessageBytes` in MessageQueueOptions is set to 6MB
3. Ensure both producer and consumer configs are applied

### Connection Refused

1. Verify RedPanda/Kafka is running: `docker ps`
2. Check port is accessible: `telnet localhost 9092`
3. Verify firewall settings

### High Latency

1. Reduce `LingerMs` if latency is critical (trade-off: less batching)
2. Increase `BatchSize` for better throughput
3. Monitor consumer lag in RedPanda Console

## Rollback to RabbitMQ

To switch back to RabbitMQ:

```csharp
services.AddConnectionServerMessaging(options =>
{
    options.BrokerType = MessageBrokerType.RabbitMQ;
    options.Host = "localhost";
    options.Port = 5672;
    options.Username = "guest";
    options.Password = "guest";
});
```

No data migration needed - messages are transient.

## Next Steps

1. Start RedPanda: `docker run -d -p 9092:9092 ...`
2. Update configuration to use Kafka
3. Run performance tests to measure improvement
4. Monitor with RedPanda Console

For application-level batching to achieve 600x improvement, see `PERFORMANCE_ANALYSIS.md`.
