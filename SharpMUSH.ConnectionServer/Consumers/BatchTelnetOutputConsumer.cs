using System.Collections.Concurrent;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes telnet output messages in batches and sends them efficiently to connections.
/// This solves the @dolist performance issue by batching multiple sequential messages
/// before sending them over TCP.
/// </summary>
public class BatchTelnetOutputConsumer(
	IConnectionServerService connectionService,
	ILogger<BatchTelnetOutputConsumer> logger)
	: IConsumer<Batch<TelnetOutputMessage>>
{
	public async Task Consume(ConsumeContext<Batch<TelnetOutputMessage>> context)
	{
		var batch = context.Message;
		logger.LogDebug("Processing batch of {Count} telnet output messages", batch.Length);

		// Group messages by connection handle for efficient batching
		var messagesByHandle = new ConcurrentDictionary<long, List<byte[]>>();

		foreach (var message in batch)
		{
			messagesByHandle.AddOrUpdate(
				message.Message.Handle,
				_ => new List<byte[]> { message.Message.Data },
				(_, list) =>
				{
					list.Add(message.Message.Data);
					return list;
				});
		}

		// Send batched messages to each connection
		var tasks = messagesByHandle.Select(async kvp =>
		{
			var handle = kvp.Key;
			var dataList = kvp.Value;
			var connection = connectionService.Get(handle);

			if (connection == null)
			{
				logger.LogWarning("Received {Count} output messages for unknown connection handle: {Handle}",
					dataList.Count, handle);
				return;
			}

			try
			{
				// Combine all data into a single buffer to send in one TCP write
				var totalLength = dataList.Sum(d => d.Length);
				var combined = new byte[totalLength];
				var offset = 0;

				foreach (var data in dataList)
				{
					Buffer.BlockCopy(data, 0, combined, offset, data.Length);
					offset += data.Length;
				}

				// Single TCP write for all batched messages
				await connection.OutputFunction(combined);

				logger.LogDebug("Sent {Count} batched messages ({Bytes} bytes) to connection {Handle}",
					dataList.Count, totalLength, handle);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error sending {Count} batched messages to connection {Handle}",
					dataList.Count, handle);
			}
		});

		await Task.WhenAll(tasks);
	}
}
