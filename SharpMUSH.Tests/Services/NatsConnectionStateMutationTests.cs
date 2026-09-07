using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using NSubstitute;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class NatsConnectionStateMutationTests
{
	[Test]
	public async Task InfrastructureReadFailureIsNotAnAbsentConnection()
	{
		var kv = Substitute.For<INatsKVStore>();
		Reads(kv, new NatsResult<NatsKVEntry<string>>(new IOException("NATS unavailable")));
		await using var store = Create(kv);

		await Assert.That(async () => await store.GetConnectionAsync(42)).Throws<IOException>();
		await Assert.That(async () => await store.SetPlayerBindingAsync(42, "#5:1234")).Throws<IOException>();
		await Assert.That(kv.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "TryUpdateAsync")).IsFalse();
	}

	[Test]
	public async Task BindingRetryPreservesConcurrentMetadataAndUsesFreshRevision()
	{
		var kv = Substitute.For<INatsKVStore>();
		var before = Data();
		var concurrent = Data();
		concurrent.Metadata["Width"] = "120";
		Reads(kv, Entry(before, 1), Entry(concurrent, 2));
		var writes = Writes(kv, Conflict(), new NatsResult<ulong>(3));
		await using var store = Create(kv);

		await store.SetPlayerBindingAsync(42, "#5:1234");

		await Assert.That(writes.Count).IsEqualTo(2);
		await Assert.That(writes[0].Revision).IsEqualTo(1UL);
		await Assert.That(writes[1].Revision).IsEqualTo(2UL);
		await Assert.That(writes[1].Data.Metadata["Width"]).IsEqualTo("120");
		await Assert.That(writes[1].Data.PlayerObjid).IsEqualTo("#5:1234");
		await Assert.That(writes[1].Data.State).IsEqualTo("LoggedIn");
	}

	[Test]
	public async Task DeletionDuringBindingUpdateDoesNotResurrectConnection()
	{
		var kv = Substitute.For<INatsKVStore>();
		Reads(kv, Entry(Data(), 1), new NatsResult<NatsKVEntry<string>>(new NatsKVKeyDeletedException(2)));
		var writes = Writes(kv, Conflict());
		await using var store = Create(kv);

		await store.SetPlayerBindingAsync(42, "#5:1234");

		await Assert.That(writes.Count).IsEqualTo(1);
		await Assert.That(kv.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "PutAsync")).IsFalse();
	}

	[Test]
	public async Task BindingRetryDoesNotModifyReplacementSession()
	{
		var kv = Substitute.For<INatsKVStore>();
		Reads(kv, Entry(Data(), 1), Entry(Data(DateTimeOffset.UnixEpoch.AddSeconds(1)), 3));
		var writes = Writes(kv, Conflict());
		await using var store = Create(kv);

		await store.SetPlayerBindingAsync(42, "#5:1234");

		await Assert.That(writes.Count).IsEqualTo(1);
		await Assert.That(kv.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "PutAsync")).IsFalse();
	}

	[Test]
	public async Task AccountModeMetadataUpdatesPersistedState()
	{
		var kv = Substitute.For<INatsKVStore>();
		Reads(kv, Entry(Data(), 1));
		var writes = Writes(kv, new NatsResult<ulong>(2));
		await using var store = Create(kv);

		await store.UpdateMetadataAsync(42, "State", "AccountMode");

		await Assert.That(writes.Count).IsEqualTo(1);
		await Assert.That(writes[0].Data.State).IsEqualTo("AccountMode");
		await Assert.That(writes[0].Data.Metadata["State"]).IsEqualTo("AccountMode");
		await Assert.That(writes[0].Data.Metadata["PresenceClass"]).IsEqualTo("portal");
	}

	private static NatsConnectionStateStore Create(INatsKVStore kv) =>
		new(new NatsConnection(), kv, NullLogger<NatsConnectionStateStore>.Instance);

	private static ConnectionStateData Data(DateTimeOffset? connectedAt = null) => new()
	{
		Handle = 42,
		State = "Connected",
		IpAddress = "127.0.0.1",
		Hostname = "localhost",
		ConnectionType = "websocket",
		ConnectedAt = connectedAt ?? DateTimeOffset.UnixEpoch,
		Metadata = new Dictionary<string, string> { ["PresenceClass"] = "portal" }
	};

	private static NatsResult<NatsKVEntry<string>> Entry(ConnectionStateData data, ulong revision) =>
		new(new NatsKVEntry<string>("sharpmush-connections", "conn.42")
		{
			Value = JsonSerializer.Serialize(data),
			Revision = revision
		});

	private static NatsResult<ulong> Conflict() =>
		new(new NatsKVWrongLastRevisionException(new ApiError { Code = 400, ErrCode = 10071 }));

	private static void Reads(INatsKVStore kv, params NatsResult<NatsKVEntry<string>>[] results)
	{
		var index = 0;
		kv.TryGetEntryAsync<string>("conn.42", Arg.Any<ulong>(), Arg.Any<INatsDeserialize<string>>(), Arg.Any<CancellationToken>())
			.Returns(_ => ValueTask.FromResult(results[Math.Min(index++, results.Length - 1)]));
	}

	private static List<(ulong Revision, ConnectionStateData Data)> Writes(INatsKVStore kv, params NatsResult<ulong>[] results)
	{
		var writes = new List<(ulong Revision, ConnectionStateData Data)>();
		kv.TryUpdateAsync("conn.42", Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<INatsSerialize<string>>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				var result = results[Math.Min(writes.Count, results.Length - 1)];
				writes.Add((call.ArgAt<ulong>(2), JsonSerializer.Deserialize<ConnectionStateData>(call.ArgAt<string>(1))!));
				return ValueTask.FromResult(result);
			});
		return writes;
	}
}
