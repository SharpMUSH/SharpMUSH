using System.Globalization;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using NSubstitute;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class DurableDescriptorGeneratorTests
{
	[Test]
	public async Task WaitingAllocationsAreAsynchronousAndIndividuallyCancelable()
	{
		var backend = new AllocatorStore();
		await using var generator = await DurableDescriptorGeneratorService.CreateAsync(backend.Store, 1, 0);
		await generator.GetNextTelnetDescriptorAsync();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		backend.Store.TryGetEntryAsync<string>(DurableDescriptorGeneratorService.HighWaterKey, Arg.Any<ulong>(),
			Arg.Any<INatsDeserialize<string>>(), Arg.Any<CancellationToken>())
			.Returns(call => StallReadAsync(call.ArgAt<CancellationToken>(3)));
		async ValueTask<NatsResult<NatsKVEntry<string>>> StallReadAsync(CancellationToken ct)
		{
			entered.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, ct);
			return default;
		}
		using var reservationCancellation = new CancellationTokenSource();
		var reservation = generator.GetNextTelnetDescriptorAsync(reservationCancellation.Token).AsTask();
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
		using var waiterCancellation = new CancellationTokenSource();
		var waiter = generator.GetNextWebSocketDescriptorAsync(waiterCancellation.Token).AsTask();
		waiterCancellation.Cancel();
		await Assert.That(async () => await waiter.WaitAsync(TimeSpan.FromSeconds(2))).Throws<OperationCanceledException>();
		await Assert.That(reservation.IsCompleted).IsFalse();
		reservationCancellation.Cancel();
		await Assert.That(async () => await reservation.WaitAsync(TimeSpan.FromSeconds(2))).Throws<OperationCanceledException>();
	}

	[Test]
	public async Task RecreatedOwnerNeverReusesAnyPreviouslyReservedHandle()
	{
		var backend = new AllocatorStore();
		await using var first = await DurableDescriptorGeneratorService.CreateAsync(backend.Store, 4);
		var original = first.GetNextTelnetDescriptor();
		first.ReleaseTelnetDescriptor(original);
		first.ReserveWebSocketDescriptor(original);
		var browser = first.GetNextWebSocketDescriptor();
		await using var replacement = await DurableDescriptorGeneratorService.CreateAsync(backend.Store, 4);

		await Assert.That(original).IsEqualTo((long)int.MaxValue + 1);
		await Assert.That(browser).IsEqualTo(original + 1);
		await Assert.That(replacement.GetNextWebSocketDescriptor()).IsEqualTo(original + 4);
		await Assert.That(first.GetNextTelnetDescriptor()).IsEqualTo(original + 2);
	}

	[Test]
	public async Task AllocationWritesOnlyOncePerBlock()
	{
		var backend = new AllocatorStore();
		await using var generator = await DurableDescriptorGeneratorService.CreateAsync(backend.Store, 4, 0);
		var handles = Enumerable.Range(0, 4).Select(_ => generator.GetNextTelnetDescriptor()).ToArray();
		await Assert.That(handles.SequenceEqual([1L, 2L, 3L, 4L])).IsTrue();
		await Assert.That(backend.Writes).IsEqualTo(1);
		await Assert.That(generator.GetNextWebSocketDescriptor()).IsEqualTo(5L);
		await Assert.That(backend.Writes).IsEqualTo(2);
	}

	[Test]
	public async Task CasConflictRereadsAndSkipsTheOtherReservation()
	{
		var backend = new AllocatorStore { CompetingReservation = 4 };
		await using var generator = await DurableDescriptorGeneratorService.CreateAsync(backend.Store, 4, 0);

		await Assert.That(generator.GetNextTelnetDescriptor()).IsEqualTo(5L);
		await Assert.That(backend.Writes).IsEqualTo(2);
	}

	[Test]
	public async Task ExhaustedRangeFailsInsteadOfWrapping()
	{
		var backend = new AllocatorStore { HighWater = long.MaxValue - 2, Revision = 1 };
		await using var generator = await DurableDescriptorGeneratorService.CreateAsync(backend.Store, 2, 0);
		await Assert.That(generator.GetNextTelnetDescriptor()).IsEqualTo(long.MaxValue - 1);
		await Assert.That(generator.GetNextWebSocketDescriptor()).IsEqualTo(long.MaxValue);

		await Assert.That(() => generator.GetNextTelnetDescriptor()).Throws<OverflowException>();
		await Assert.That(backend.Writes).IsEqualTo(1);
	}

	[Test]
	public async Task ReservationFailureNeverReusesTheExhaustedBlock()
	{
		var backend = new AllocatorStore();
		await using var generator = await DurableDescriptorGeneratorService.CreateAsync(backend.Store, 1, 0);
		await Assert.That(generator.GetNextTelnetDescriptor()).IsEqualTo(1L);
		backend.WriteError = new IOException("NATS unavailable");
		await Assert.That(() => generator.GetNextTelnetDescriptor()).Throws<IOException>();
		backend.WriteError = null;
		await Assert.That(generator.GetNextWebSocketDescriptor()).IsEqualTo(2L);
	}

	[Test]
	public async Task DeletedHighWaterFailsClosed()
	{
		var backend = new AllocatorStore { ReadError = new NatsKVKeyDeletedException(2) };
		await Assert.That(async () => await DurableDescriptorGeneratorService.CreateAsync(backend.Store, 4, 0))
			.Throws<NatsKVKeyDeletedException>();
		await Assert.That(backend.Writes).IsEqualTo(0);
	}

	private sealed class AllocatorStore
	{
		public INatsKVStore Store { get; } = Substitute.For<INatsKVStore>();
		public long? HighWater { get; set; }
		public ulong Revision { get; set; }
		public int Writes { get; private set; }
		public long? CompetingReservation { get; init; }
		public Exception? WriteError { get; set; }
		public Exception? ReadError { get; init; }

		public AllocatorStore()
		{
			Store.TryGetEntryAsync<string>(DurableDescriptorGeneratorService.HighWaterKey, Arg.Any<ulong>(),
				Arg.Any<INatsDeserialize<string>>(), Arg.Any<CancellationToken>()).Returns(_ =>
				{
					NatsResult<NatsKVEntry<string>> entry = ReadError is { } error
						? new(error)
						: HighWater is { } highWater
							? new(new NatsKVEntry<string>("connection_descriptors", DurableDescriptorGeneratorService.HighWaterKey)
							{ Value = highWater.ToString(CultureInfo.InvariantCulture), Revision = Revision })
							: new(new NatsKVKeyNotFoundException());
					return ValueTask.FromResult(entry);
				});
			Store.TryUpdateAsync(DurableDescriptorGeneratorService.HighWaterKey, Arg.Any<string>(), Arg.Any<ulong>(),
				Arg.Any<INatsSerialize<string>>(), Arg.Any<CancellationToken>()).Returns(call =>
				{
					Writes++;
					if (WriteError is { } error) return ValueTask.FromResult(new NatsResult<ulong>(error));
					if (Writes == 1 && CompetingReservation is { } concurrent)
					{
						HighWater = concurrent;
						Revision++;
					}
					if (call.ArgAt<ulong>(2) != Revision)
						return ValueTask.FromResult(new NatsResult<ulong>(new NatsKVWrongLastRevisionException(new ApiError { Code = 400, ErrCode = 10071 })));
					HighWater = long.Parse(call.ArgAt<string>(1), CultureInfo.InvariantCulture);
					return ValueTask.FromResult(new NatsResult<ulong>(++Revision));
				});
		}
	}
}
