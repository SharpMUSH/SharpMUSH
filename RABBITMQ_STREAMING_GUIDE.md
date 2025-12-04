# RabbitMQ Streaming Implementation Guide

## Overview

This guide explains how to enable RabbitMQ Streams for SharpMUSH to improve @dolist performance from 609x slower than iter() to approximately 10-50x improvement.

## Current State (Phase 1 - IMPLEMENTED)

### Quick Wins Applied
The following optimizations are now active in `MassTransitExtensions.cs`:

1. **Disabled Publisher Confirms**: `h.PublisherConfirmation = false`
   - Removes wait for broker acknowledgment
   - Acceptable message loss risk for game telnet output
   - Expected 3-5x improvement

2. **Increased Channel Limit**: `h.RequestedChannelMax = 2047`
   - Allows more concurrent channels
   - Better for high-message-count scenarios

3. **Increased Prefetch Count**: `cfg.PrefetchCount = 100`
   - Consumers pull more messages at once
   - Improves consumer throughput

**Expected Improvement**: 5-10x (from 18,278ms to ~1,800-3,600ms for 1000 iterations)

## Phase 2 - RabbitMQ Streams (TO BE IMPLEMENTED)

### Prerequisites

1. **RabbitMQ 3.9+** (already using 3.11 in testcontainers)
2. **Streams plugin enabled** (usually enabled by default in 3.11+)
3. **MassTransit 8.0+** (currently using 8.3.5/8.5.5) ✅

### Implementation Steps

#### Step 1: Add Stream Package Reference

Add to `SharpMUSH.Messaging.csproj`:
```xml
<PackageReference Include="MassTransit.RabbitMQ.Stream" Version="8.3.5" />
```

#### Step 2: Update MassTransitExtensions

Add stream configuration to both `AddConnectionServerMessaging` and `AddMainProcessMessaging`:

```csharp
x.UsingRabbitMq((context, cfg) =>
{
    cfg.Host(options.Host, options.VirtualHost, h =>
    {
        h.Username(options.Username);
        h.Password(options.Password);
        h.PublisherConfirmation = false;
        h.RequestedChannelMax = 2047;
    });

    cfg.PrefetchCount = 100;

    // Enable stream for telnet output if configured
    if (options.UseStreams)
    {
        cfg.Send<TelnetOutputMessage>(s => 
        {
            s.UseStream(options.TelnetOutputStreamName);
        });
        
        cfg.Publish<TelnetOutputMessage>(p => 
        {
            p.UseStream(options.TelnetOutputStreamName);
        });
    }

    cfg.UseMessageRetry(r => r.Interval(options.RetryCount, TimeSpan.FromSeconds(options.RetryDelaySeconds)));
    cfg.ConfigureEndpoints(context);
});
```

#### Step 3: Configure Stream Consumer

Update consumer registration to use stream consumer when enabled:

```csharp
if (options.UseStreams)
{
    x.AddRider(rider =>
    {
        rider.AddConsumer<TelnetOutputConsumer>();
        
        rider.UsingRabbitMqStream((context, cfg) =>
        {
            cfg.Host(options.Host, options.VirtualHost, h =>
            {
                h.Username(options.Username);
                h.Password(options.Password);
            });
            
            cfg.Stream(options.TelnetOutputStreamName, s =>
            {
                s.ConfigureConsumer<TelnetOutputConsumer>(context);
            });
        });
    });
}
```

#### Step 4: Enable in Configuration

Add to appsettings or environment configuration:
```json
{
  "MessageQueue": {
    "UseStreams": true,
    "TelnetOutputStreamName": "telnet-output-stream",
    "StreamMaxAgeHours": 24
  }
}
```

### Testing Stream Implementation

1. **Start with streams disabled**: Verify Phase 1 improvements work
2. **Enable streams**: Set `UseStreams = true`
3. **Run performance tests**: Use `InProcessPerformanceMeasurement` test
4. **Monitor RabbitMQ**: Check stream metrics in management UI
5. **Verify ordering**: Ensure output order is maintained

### Expected Results

| Scenario | Before | Phase 1 (Config) | Phase 2 (Streams) |
|----------|--------|------------------|-------------------|
| @dolist 100 | 1,928ms | ~300-400ms | ~80-150ms |
| @dolist 1000 | 18,278ms | ~2,000-4,000ms | ~400-900ms |
| Improvement | 1x | 5-10x | 20-50x |

**Note**: Won't match iter() (30ms) due to architectural difference - @dolist executes commands sequentially while iter() is a single function accumulating results.

### Monitoring

After enabling streams, monitor:

1. **Stream Rate**: Messages/sec in RabbitMQ management UI
2. **Consumer Lag**: Offset difference between publisher and consumer
3. **Memory Usage**: Streams keep messages in memory
4. **Disk Usage**: Old stream segments on disk

### Rollback Plan

If streams cause issues:

1. Set `UseStreams = false` in configuration
2. Restart both ConnectionServer and Server
3. Falls back to Phase 1 optimizations (traditional queues)

## Alternative: Message Batching (Phase 3 - IF NEEDED)

If Phases 1-2 don't achieve acceptable performance, implement application-level batching:

```csharp
// Add to NotifyService.cs
private readonly Channel<(long handle, byte[] data)> _sendQueue;
private readonly CancellationTokenSource _cts = new();

public NotifyService(IBus publishEndpoint, IConnectionService connections)
{
    // ... existing code ...
    _sendQueue = Channel.CreateUnbounded<(long, byte[])>();
    _ = Task.Run(() => ProcessSendQueueAsync(_cts.Token));
}

private async Task ProcessSendQueueAsync(CancellationToken cancellationToken)
{
    var batch = new List<(long handle, byte[] data)>();
    
    while (!cancellationToken.IsCancellationRequested)
    {
        // Collect messages for 5ms or until we have 50
        var deadline = DateTime.UtcNow.AddMilliseconds(5);
        
        while (DateTime.UtcNow < deadline && batch.Count < 50)
        {
            if (_sendQueue.Reader.TryRead(out var item))
            {
                batch.Add(item);
            }
            else
            {
                await Task.Delay(1, cancellationToken);
            }
        }
        
        // Send batch
        if (batch.Count > 0)
        {
            var tasks = batch.GroupBy(x => x.handle)
                .Select(g => PublishBatch(g.Key, g.Select(x => x.data)));
            await Task.WhenAll(tasks);
            batch.Clear();
        }
    }
}
```

This adds 5ms latency but can batch 1000 messages into 20 publishes (50 messages each).

## Conclusion

**Current Status**: Phase 1 implemented (config optimizations)
**Next Step**: Implement Phase 2 (RabbitMQ Streams)
**Fallback**: Phase 3 (message batching) if needed

The streaming approach is the industry-standard solution for high-throughput ordered messaging and should significantly improve @dolist performance without introducing lag on every command.
