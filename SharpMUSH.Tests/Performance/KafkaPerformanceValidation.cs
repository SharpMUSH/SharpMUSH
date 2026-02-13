using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Tests.Performance;

/// <summary>
/// Validates that the Kafka migration maintains or improves messaging performance
/// </summary>
[NotInParallel]
public class KafkaPerformanceValidation
{
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public required ServerWebAppFactory WebAppFactoryArg { get; init; }

private IMessageBus MessageBus => WebAppFactoryArg.Services.GetRequiredService<IMessageBus>();

[Test, Explicit]  // Explicit - only run when specifically requested
public async Task Kafka_ProducerThroughput_ShouldHandleHighVolume()
{
// Arrange
const int messageCount = 1000;
var messages = Enumerable.Range(0, messageCount)
.Select(i => new TelnetOutputMessage(1, Encoding.UTF8.GetBytes($"Message {i}")))
.ToList();

// Act - Measure throughput for 1000 messages
var sw = Stopwatch.StartNew();

foreach (var message in messages)
{
await MessageBus.Publish(message);
}

sw.Stop();

// Assert - Should complete within reasonable time
// Direct Kafka should be faster than MassTransit (which took ~18s for 1000 messages)
// Target: < 15 seconds (17% improvement)
// With batching: < 10 seconds (45% improvement)
var elapsedMs = sw.ElapsedMilliseconds;
Console.WriteLine($"Published {messageCount} messages in {elapsedMs}ms");
Console.WriteLine($"Throughput: {messageCount * 1000.0 / elapsedMs:F2} msg/sec");
Console.WriteLine($"Average latency: {(double)elapsedMs / messageCount:F2}ms per message");

// Performance expectations:
// - Worst case (no batching): ~18ms per message = 15,000ms total
// - With producer batching: ~8-10ms per message = 8,000-10,000ms total
// - Optimal: ~5-7ms per message = 5,000-7,000ms total
await Assert.That(elapsedMs).IsLessThan(15000)
.Because("Kafka producer should be faster than MassTransit baseline (18s)");
}

[Test, Explicit]  // Explicit - requires Kafka to be running
public async Task Kafka_ProducerLatency_ShouldBeLowForSingleMessage()
{
// Arrange
var message = new TelnetOutputMessage(1, Encoding.UTF8.GetBytes("Test message"));

// Warm up
await MessageBus.Publish(message);
await Task.Delay(100);

// Act - Measure latency for single message
var sw = Stopwatch.StartNew();
await MessageBus.Publish(message);
sw.Stop();

// Assert - Single message should have low latency
var elapsedMs = sw.ElapsedMilliseconds;
Console.WriteLine($"Single message latency: {elapsedMs}ms");

// Direct Kafka should have lower latency than MassTransit
// Expected: < 50ms (including producer batching linger time of 8ms)
await Assert.That(elapsedMs).IsLessThan(100)
.Because("Single message latency should be low (<100ms)");
}

[Test, Explicit]  // Explicit - requires Kafka to be running
public async Task Kafka_BatchedProducer_ShouldImprovePerformance()
{
// Arrange
const int messageCount = 100;
var messages = Enumerable.Range(0, messageCount)
.Select(i => new TelnetOutputMessage(1, Encoding.UTF8.GetBytes($"Batch message {i}")))
.ToList();

// Act - Send messages rapidly to trigger batching
var sw = Stopwatch.StartNew();

var tasks = messages.Select(m => MessageBus.Publish(m)).ToList();
await Task.WhenAll(tasks);

sw.Stop();

// Assert - Batching should significantly improve throughput
var elapsedMs = sw.ElapsedMilliseconds;
Console.WriteLine($"Published {messageCount} messages (batched) in {elapsedMs}ms");
Console.WriteLine($"Throughput: {messageCount * 1000.0 / elapsedMs:F2} msg/sec");
Console.WriteLine($"Average latency: {(double)elapsedMs / messageCount:F2}ms per message");

// With batching, should be much faster than sequential sends
// Expected: ~5-10ms total for 100 messages with LingerMs=5
await Assert.That(elapsedMs).IsLessThan(1000)
.Because("Batched messages should complete quickly (<1s for 100 messages)");
}

[Test]
public async Task Kafka_Configuration_ShouldHavePerformanceOptimizations()
{
// Arrange
var options = WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Messaging.Configuration.MessageQueueOptions>();

// Assert - Verify performance settings are configured
await Assert.That(options.CompressionType).IsEqualTo("lz4")
.Because("Should use LZ4 compression");
await Assert.That(options.LingerMs).IsGreaterThan(0)
.Because("Should have producer batching enabled");
await Assert.That(options.BatchSize).IsGreaterThan(16384)
.Because("Should have reasonable batch size (>16KB)");
await Assert.That(options.BatchMaxSize).IsGreaterThan(0)
.Because("Should have consumer batching enabled");
        // EnableIdempotence disabled for performance (acks=1 instead of acks=all)
        // MaxInFlight=1 still ensures ordering within partitions
        await Assert.That(options.EnableIdempotence).IsFalse()
            .Because("Idempotence disabled for lower latency (acks=1), ordering via MaxInFlight=1");

Console.WriteLine($"Kafka Configuration:");
Console.WriteLine($"  Compression: {options.CompressionType}");
Console.WriteLine($"  Producer Batch Size: {options.BatchSize} bytes");
Console.WriteLine($"  Producer Linger: {options.LingerMs}ms");
Console.WriteLine($"  Consumer Batch Size: {options.BatchMaxSize} messages");
Console.WriteLine($"  Consumer Batch Time: {options.BatchTimeLimit.TotalMilliseconds}ms");
Console.WriteLine($"  Idempotence: {options.EnableIdempotence}");
}

[Test]
public async Task Kafka_Configuration_ShouldBeOptimizedForDoListScenario()
{
// Arrange
var options = WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Messaging.Configuration.MessageQueueOptions>();

// Assert - Verify settings optimized for @dolist (rapid sequential messages)
        // LingerMs optimized at 16ms for better throughput while maintaining reasonable latency
        await Assert.That(options.LingerMs).IsGreaterThanOrEqualTo(10)
            .And.IsLessThanOrEqualTo(20)
            .Because("LingerMs should balance batching and latency (10-20ms for optimal throughput)");

await Assert.That(options.BatchMaxSize).IsGreaterThanOrEqualTo(100)
.Because("Consumer batch size should handle @dolist iterations (>=100)");

await Assert.That(options.BatchTimeLimit.TotalMilliseconds).IsLessThanOrEqualTo(50)
.Because("Batch time limit should be low for responsiveness (<=50ms)");

Console.WriteLine($"@dolist Optimization Settings:");
Console.WriteLine($"  Producer batching window: {options.LingerMs}ms");
Console.WriteLine($"  Consumer batch size: {options.BatchMaxSize} messages");
Console.WriteLine($"  Consumer batch timeout: {options.BatchTimeLimit.TotalMilliseconds}ms");
}
}
