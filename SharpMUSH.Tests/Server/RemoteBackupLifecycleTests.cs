using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Secrets;

namespace SharpMUSH.Tests.Server;

public class RemoteBackupLifecycleTests
{
	private sealed class World : IExpandedObjectDataService
	{
		public string? Serialized;
		public ValueTask<T?> GetExpandedServerDataAsync<T>() where T : class => ValueTask.FromResult(Serialized is null ? null : JsonSerializer.Deserialize<T>(Serialized));
		public ValueTask SetExpandedServerDataAsync<T>(T value, bool ignoreNull = false) where T : class { Serialized = JsonSerializer.Serialize(value); return ValueTask.CompletedTask; }
		public ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj) where T : class => throw new NotSupportedException();
		public ValueTask SetExpandedDataAsync<T>(T value, SharpObject obj, bool ignoreNull = false) where T : class => throw new NotSupportedException();
	}
	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
		public override DateTimeOffset GetUtcNow() => Now;
	}
	private sealed class Host : IDisposable
	{
		public readonly World Data = new();
		public readonly Clock Time = new();
		public readonly IWorldBackupService Backups = Substitute.For<IWorldBackupService>();
		public readonly string Root = Path.Join(Path.GetTempPath(), "remote-backup-" + Guid.NewGuid().ToString("N"));
		public readonly SecretStore Secrets;
		public readonly IDataProtectionProvider Protection = new EphemeralDataProtectionProvider();
		public RemoteBackupCoordinator Coordinator;
		public Host()
		{
			Backups.IsSupported.Returns(true);
			Backups.Root.Returns(Root);
			Backups.CreateRemoteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
			{
				var path = Path.Join(Root + ".remote-jobs", call.Arg<Guid>().ToString("N"), "20261016");
				Directory.CreateDirectory(path);
				File.WriteAllText(Path.Join(path, "data.mdb"), "safe copy");
				return ValueTask.FromResult<Result<WorldBackup>>(new WorldBackup("20261016", path, Time.Now, 9));
			});
			Secrets = new(new SecretStoreTests.InMemoryWorld(), Protection,
				[RemoteBackupCoordinator.Password, RemoteBackupCoordinator.AccountId, RemoteBackupCoordinator.AccountKey],
				new SecretStoreOptions(Path.Join(Root, "secrets"), _ => null), Time, NullLogger<SecretStore>.Instance);
			Coordinator = new(Data, Secrets, Protection, Backups, Time);
		}
		public async Task EnableAsync(string repository = "b2:bucket:path", string password = "test-password")
			=> await Assert.That(await Coordinator.ConfigureAsync(new(new(true, 24, repository), password, "account", "key"), new("account", "Admin")) is Success).IsTrue();
		public void Dispose()
		{
			if (Directory.Exists(Root)) Directory.Delete(Root, true);
			if (Directory.Exists(Root + ".remote-jobs")) Directory.Delete(Root + ".remote-jobs", true);
		}
	}

	[Test]
	public async Task CompletedCopyIsNotRemoteSuccessAndSecretsAreMasked()
	{
		using var host = new Host();
		await host.EnableAsync();
		await host.Coordinator.QueueAsync("manual");
		await host.Coordinator.TickAsync(default);
		var status = await host.Coordinator.StatusAsync();
		await Assert.That(status.Runs.Single().State).IsEqualTo("ready");
		await Assert.That(status.LastSuccessAt).IsNull();
		await Assert.That(JsonSerializer.Serialize(status).Contains("test-password")).IsFalse();
		await Assert.That(host.Data.Serialized!.Contains("test-password")).IsFalse();
	}

	[Test]
	public async Task SuccessReceiptIsAuthenticatedIdempotentAndCleansOnlyOwnedCopy()
	{
		using var host = new Host();
		Directory.CreateDirectory(host.Root);
		File.WriteAllText(Path.Join(host.Root, "keep"), "ordinary local copy");
		await host.EnableAsync();
		await host.Coordinator.QueueAsync("manual");
		await host.Coordinator.TickAsync(default);
		var claim = (await host.Coordinator.ClaimAsync("worker")).Expect<RemoteBackupClaim>();
		var receipt = new RemoteBackupReceipt(claim.Id, claim.Nonce, 0, new string('a', 64), null);
		await Assert.That(await host.Coordinator.ReceiptAsync(receipt with { Nonce = "wrong" })).IsFalse();
		await Assert.That(await host.Coordinator.ReceiptAsync(receipt)).IsTrue();
		await Assert.That(await host.Coordinator.ReceiptAsync(receipt)).IsTrue();
		await Assert.That((await host.Coordinator.StatusAsync()).Runs.Single().State).IsEqualTo("succeeded");
		await Assert.That(Directory.Exists(claim.CopyPath)).IsFalse();
		await Assert.That(File.Exists(Path.Join(host.Root, "keep"))).IsTrue();
		var saved = JsonSerializer.Deserialize<RemoteBackupData>(host.Data.Serialized!)!;
		saved.Jobs.Clear();
		await host.Data.SetExpandedServerDataAsync(saved);
		await Assert.That((await host.Coordinator.StatusAsync()).LastSuccessAt).IsEqualTo(host.Time.Now);
	}

	[Test]
	public async Task ConfigEditPinsExistingJobAndInvalidEditChangesNoCredentials()
	{
		using var host = new Host();
		await host.EnableAsync();
		await host.Coordinator.QueueAsync("manual");
		await host.EnableAsync("b2:other:prefix", "new-password");
		await host.Coordinator.TickAsync(default);
		var claim = (await host.Coordinator.ClaimAsync("worker")).Expect<RemoteBackupClaim>();
		await Assert.That(claim.Repository).IsEqualTo("b2:bucket:path");
		await Assert.That(claim.Password).IsEqualTo("test-password");
		var rejected = await host.Coordinator.ConfigureAsync(new(new(true, 24, "b2:other:prefix"), "bad-replacement", "id", new string('x', SecretStore.MaxValueLength + 1)), new("admin", "Admin"));
		await Assert.That(rejected is Error<string>).IsTrue();
		await Assert.That((await host.Secrets.GetAsync(RemoteBackupCoordinator.Password.Name)).Expect<string>()).IsEqualTo("new-password");
	}

	[Test]
	public async Task ExpiryKeepsCopyAndOnlyOwnerCanReconcile()
	{
		using var host = new Host();
		await host.EnableAsync();
		await host.Coordinator.QueueAsync("manual");
		await host.Coordinator.TickAsync(default);
		var claim = (await host.Coordinator.ClaimAsync("owner")).Expect<RemoteBackupClaim>();
		host.Time.Now += TimeSpan.FromHours(3);
		await host.Coordinator.TickAsync(default);
		await Assert.That((await host.Coordinator.StatusAsync()).Runs.Single().State).IsEqualTo("unknown");
		await Assert.That(Directory.Exists(claim.CopyPath)).IsTrue();
		await Assert.That(await host.Coordinator.ClaimAsync("other") is NotFound).IsTrue();
		await Assert.That((await host.Coordinator.ClaimAsync("owner")).Expect<RemoteBackupClaim>().ReconcileOnly).IsTrue();
		await Assert.That(await host.Coordinator.QueueAsync("manual") is Error<string>).IsTrue();
	}

	[Test]
	public async Task PartialExitCannotAdvanceLastSuccess()
	{
		using var host = new Host();
		await host.EnableAsync();
		await host.Coordinator.QueueAsync("manual");
		await host.Coordinator.TickAsync(default);
		var claim = (await host.Coordinator.ClaimAsync("owner")).Expect<RemoteBackupClaim>();
		await host.Coordinator.ReceiptAsync(new(claim.Id, claim.Nonce, 3, new string('a', 64), "password=secret"));
		var status = await host.Coordinator.StatusAsync();
		await Assert.That(status.LastSuccessAt).IsNull();
		await Assert.That(status.Runs.Single().ErrorCode).IsEqualTo("partial-backup");
		await Assert.That(JsonSerializer.Serialize(status).Contains("password=secret")).IsFalse();
	}

	[Test]
	public async Task UnsafeProviderCopyIsRejected()
	{
		using var host = new Host();
		host.Backups.CreateRemoteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Result<WorldBackup>>(new WorldBackup("live", host.Root, host.Time.Now, 1)));
		await host.EnableAsync();
		await host.Coordinator.QueueAsync("manual");
		await host.Coordinator.TickAsync(default);
		await Assert.That((await host.Coordinator.StatusAsync()).Runs.Single().ErrorCode).IsEqualTo("local-copy-failed");
		await Assert.That(await host.Coordinator.ClaimAsync("owner") is NotFound).IsTrue();
	}
	[Test]
	public async Task AbandonRetainsCopyUnblocksNewJobsAndAcceptsLateReceipt()
	{
		using var host = new Host();
		await host.EnableAsync();
		await host.Coordinator.QueueAsync("manual");
		await host.Coordinator.TickAsync(default);
		var claim = (await host.Coordinator.ClaimAsync("lost-worker")).Expect<RemoteBackupClaim>();
		host.Time.Now += TimeSpan.FromHours(3);
		await host.Coordinator.TickAsync(default);
		await Assert.That(await host.Coordinator.AbandonAsync(claim.Id)).IsTrue();
		await Assert.That(Directory.Exists(claim.CopyPath)).IsTrue();
		await Assert.That(await host.Coordinator.QueueAsync("manual") is RemoteBackupRun).IsTrue();
		await Assert.That(await host.Coordinator.ReceiptAsync(new(claim.Id, claim.Nonce, 0, new string('a', 64), null))).IsTrue();
		await Assert.That(Directory.Exists(claim.CopyPath)).IsFalse();
	}

	[Test]
	public async Task RestartDuringPreparationCannotReportSuccess()
	{
		using var host = new Host();
		await host.EnableAsync();
		await host.Coordinator.QueueAsync("manual");
		var persisted = JsonSerializer.Deserialize<RemoteBackupData>(host.Data.Serialized!)!;
		persisted.Jobs.Single().Run = persisted.Jobs.Single().Run with { State = "preparing" };
		await host.Data.SetExpandedServerDataAsync(persisted);
		host.Coordinator = new(host.Data, host.Secrets, host.Protection, host.Backups, host.Time);
		await host.Coordinator.RecoverAsync();
		await Assert.That((await host.Coordinator.StatusAsync()).Runs.Single().ErrorCode).IsEqualTo("local-copy-interrupted");
		await Assert.That((await host.Coordinator.StatusAsync()).LastSuccessAt).IsNull();
	}

	[Test]
	public async Task ProviderCopySurvivesOrdinaryRetentionAndHasOwnedLayout()
	{
		var root = Path.Join(Path.GetTempPath(), "remote-provider-" + Guid.NewGuid().ToString("N"));
		var accessor = Substitute.For<ILightningStorageAccessor>();
		accessor.CopyToAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			File.WriteAllText(Path.Join(call.Arg<string>(), "data.mdb"), "provider-safe");
			return ValueTask.CompletedTask;
		});
		var backups = new LightningWorldBackupService(accessor, new WorldBackupOptions { Root = root, Keep = 1 }, true, NullLogger<LightningWorldBackupService>.Instance);
		var id = Guid.NewGuid();
		try
		{
			var copy = (await backups.CreateRemoteAsync(id)).Expect<WorldBackup>();
			await backups.CreateAsync();
			await backups.CreateAsync();
			await Assert.That(Path.GetDirectoryName(copy.Path)).IsEqualTo(Path.Join(root + ".remote-jobs", id.ToString("N")));
			await Assert.That(File.Exists(Path.Join(copy.Path, "data.mdb"))).IsTrue();
			await Assert.That(backups.List().Count).IsEqualTo(1);
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
			if (Directory.Exists(root + ".remote-jobs")) Directory.Delete(root + ".remote-jobs", true);
		}
	}

	[Test]
	public async Task ClearingStorageCredentialsAffectsOnlyFutureJobsAndReturnsNoSecret()
	{
		using var host = new Host();
		await host.EnableAsync();
		await host.Coordinator.QueueAsync("manual");
		await Assert.That(await host.Coordinator.ConfigureAsync(new(new(true, 24, "rest:https://backup.example.com/repo"), null, null, null, true, true), new("admin", "Admin")) is Success).IsTrue();
		var status = await host.Coordinator.StatusAsync();
		await Assert.That(status.AccountIdSet).IsFalse();
		await Assert.That(status.AccountKeySet).IsFalse();
		await host.Coordinator.TickAsync(default);
		var old = (await host.Coordinator.ClaimAsync("worker")).Expect<RemoteBackupClaim>();
		await Assert.That(old.AccountId).IsEqualTo("account");
		await Assert.That(old.AccountKey).IsEqualTo("key");
		await host.Coordinator.ReceiptAsync(new(old.Id, old.Nonce, 0, new string('a', 64), null));
		await host.Coordinator.QueueAsync("manual");
		await host.Coordinator.TickAsync(default);
		var next = (await host.Coordinator.ClaimAsync("worker")).Expect<RemoteBackupClaim>();
		await Assert.That(next.AccountId).IsNull();
		await Assert.That(next.AccountKey).IsNull();
		await Assert.That(next.Repository).IsEqualTo("rest:https://backup.example.com/repo");
	}

}
