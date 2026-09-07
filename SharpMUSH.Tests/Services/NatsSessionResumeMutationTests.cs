using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using NSubstitute;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class NatsSessionResumeMutationTests
{
	[Test]
	public async Task LogoutRevocationDuringTransportRetryCannotBeOverwritten()
	{
		var kv = Substitute.For<INatsKVStore>();
		var revoked = Data();
		revoked.Metadata["ResumeRevoked"] = "1";
		Reads(kv, Entry(Data(), 1), Entry(revoked, 2));
		var writes = Writes(kv, Conflict());
		await using var store = Create(kv);
		await Assert.That(await store.TryUpdateTransportAsync(42, "session", "#5:1234", "LoggedIn", "new-ip", "new-host", true)).IsFalse();
		await Assert.That(writes.Count).IsEqualTo(1);
	}

	[Test]
	[Arguments("0", true)]
	[Arguments("-1", false)]
	[Arguments("1", false)]
	[Arguments("invalid", false)]
	public async Task TransportUpdateHonorsDetachedDeadlineConvention(string expiry, bool expected)
	{
		var kv = Substitute.For<INatsKVStore>();
		var data = Data();
		data.Metadata["ResumeExpiresAt"] = expiry;
		Reads(kv, Entry(data, 1));
		var writes = Writes(kv, new NatsResult<ulong>(2));
		await using var store = Create(kv);
		await Assert.That(await store.TryUpdateTransportAsync(42, "session", "#5:1234", "LoggedIn", "new-ip", "new-host", true)).IsEqualTo(expected);
		await Assert.That(writes.Count).IsEqualTo(expected ? 1 : 0);
	}

	[Test]
	public async Task ConcurrentRevocationPreventsTransportMutationRetry()
	{
		var kv = Substitute.For<INatsKVStore>();
		var revoked = Data();
		revoked.State = "Connected";
		revoked.PlayerObjid = null;
		Reads(kv, Entry(Data(), 1), Entry(revoked, 2));
		var writes = Writes(kv, Conflict());
		await using var store = Create(kv);
		var accepted = await store.TryUpdateTransportAsync(42, "session", "#5:1234", "LoggedIn", "new-ip", "new-host", true);
		await Assert.That(accepted).IsFalse();
		await Assert.That(writes.Count).IsEqualTo(1);
	}

	[Test]
	public async Task ConcurrentSessionReplacementPreventsTransportMutationRetry()
	{
		var kv = Substitute.For<INatsKVStore>();
		var replacement = Data();
		replacement.Metadata["SessionId"] = "replacement";
		Reads(kv, Entry(Data(), 1), Entry(replacement, 2));
		var writes = Writes(kv, Conflict());
		await using var store = Create(kv);
		await Assert.That(await store.TryUpdateTransportAsync(42, "session", "#5:1234", "LoggedIn", "new-ip", "new-host", true)).IsFalse();
		await Assert.That(writes.Count).IsEqualTo(1);
	}

	[Test]
	public async Task ConcurrentDeletionDoesNotResurrectSession()
	{
		var kv = Substitute.For<INatsKVStore>();
		Reads(kv, Entry(Data(), 1), new NatsResult<NatsKVEntry<string>>(new NatsKVKeyDeletedException(2)));
		var writes = Writes(kv, Conflict());
		await using var store = Create(kv);
		await Assert.That(await store.TryUpdateTransportAsync(42, "session", "#5:1234", "LoggedIn", "new-ip", "new-host", true)).IsFalse();
		await Assert.That(writes.Count).IsEqualTo(1);
		await Assert.That(kv.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "PutAsync")).IsFalse();
	}

	[Test]
	public async Task SuccessfulRetryPreservesConcurrentMetadataAndUpdatesTransportAtomically()
	{
		var kv = Substitute.For<INatsKVStore>();
		var concurrent = Data();
		concurrent.Metadata["Width"] = "120";
		Reads(kv, Entry(Data(), 1), Entry(concurrent, 2));
		var writes = Writes(kv, Conflict(), new NatsResult<ulong>(3));
		await using var store = Create(kv);
		await Assert.That(await store.TryUpdateTransportAsync(42, "session", "#5:1234", "LoggedIn", "new-ip", "new-host", true)).IsTrue();
		await Assert.That(writes.Count).IsEqualTo(2);
		await Assert.That(writes[1].Revision).IsEqualTo(2UL);
		await Assert.That(writes[1].Data.Metadata["Width"]).IsEqualTo("120");
		await Assert.That(writes[1].Data.Metadata["InternetProtocolAddress"]).IsEqualTo("new-ip");
		await Assert.That(writes[1].Data.Metadata["HostName"]).IsEqualTo("new-host");
		await Assert.That(writes[1].Data.Metadata["SSL"]).IsEqualTo("1");
	}

	private static NatsConnectionStateStore Create(INatsKVStore kv) =>
		new(new NatsConnection(), kv, NullLogger<NatsConnectionStateStore>.Instance);

	private static ConnectionStateData Data(DateTimeOffset? connectedAt = null) => new()
	{
		Handle = 42,
		State = "LoggedIn",
		PlayerObjid = "#5:1234",
		IpAddress = "127.0.0.1",
		Hostname = "localhost",
		ConnectionType = "websocket",
		ConnectedAt = connectedAt ?? DateTimeOffset.UnixEpoch,
		Metadata = new Dictionary<string, string> { ["SessionId"] = "session" }
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
