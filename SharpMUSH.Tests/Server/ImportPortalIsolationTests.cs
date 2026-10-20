using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpMUSH.Server.Services;
using Quartz;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Server;

public class ImportPortalServerFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;

	protected override Action<IServiceCollection>? ConfigureTestServices => services =>
	{
		// TUnit wraps IHostedService registrations. Retain the exact underlying singleton so this
		// fixture can join it, preserving its original position in hosted-service startup order.
		var descriptor = services.Single(item => item.ServiceType == typeof(IHostedService)
			&& item.ImplementationType == typeof(RemoteBackupScheduleService));
		var index = services.IndexOf(descriptor);
		services.RemoveAt(index);
		services.AddSingleton<RemoteBackupScheduleService>();
		services.Insert(index, ServiceDescriptor.Singleton<IHostedService>(provider => provider.GetRequiredService<RemoteBackupScheduleService>()));
	};

	public override async Task InitializeAsync()
	{
		await base.InitializeAsync();
		// These independent maintenance writers can change the live file during preview. Join them
		// before the baseline, retaining ordinary configuration writes and the strict file assertion.
		var scheduler = await Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
		await scheduler.Shutdown(waitForJobsToComplete: true);
		var maintenance = Services.GetRequiredService<RemoteBackupScheduleService>();
		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await maintenance.StopAsync(deadline.Token);
		var execution = maintenance.ExecuteTask ?? throw new InvalidOperationException("Remote backup maintenance never started.");
		try { await execution.WaitAsync(deadline.Token); }
		catch (OperationCanceledException) when (execution.IsCanceled) { }
		await QueueBarrierAsync();
	}
}

[NotInParallel]
public class ImportPortalIsolationTests
{
	[ClassDataSource<ImportPortalServerFactory>(Shared = SharedType.PerTestSession)]
	public required ImportPortalServerFactory Factory { get; init; }

	[Test]
	public async Task QuiescingMaintenanceDoesNotSuppressLiveConfigurationWrites()
	{
		var livePath = Factory.Services.GetRequiredService<LightningWorldPath>().Value;
		var options = Factory.Services.GetRequiredService<IConfigOptionWriter>();
		var original = await options.CurrentAsync();
		var before = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(livePath, "data.mdb")));
		try
		{
			await options.UpdateAsync(current => current with { Net = current.Net with { MudName = "Live isolation sentinel" } });
			await Assert.That((await options.CurrentAsync()).Net.MudName).IsEqualTo("Live isolation sentinel");
			await Assert.That(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(livePath, "data.mdb"))).SequenceEqual(before)).IsFalse();
		}
		finally
		{
			await options.UpdateAsync(_ => original);
		}
	}

	[Test]
	public async Task IsolationBaselineJoinsRemoteBackupMaintenance()
	{
		var maintenance = Factory.Services.GetRequiredService<RemoteBackupScheduleService>();
		await Assert.That(maintenance.ExecuteTask is not null).IsTrue().Because("the host must have started this exact singleton");
		await Assert.That(maintenance.ExecuteTask is { IsCompleted: true }).IsTrue()
			.Because("neither startup recovery nor a later timer tick may write during the isolation baseline");
	}

	[Test]
	public async Task UploadOnlyPreviewsConfigurationAndPromotionRequiresASeparateExplicitConfirmation()
	{
		using var client = Factory.CreateHttpClient();
		var livePath = Factory.Services.GetRequiredService<LightningWorldPath>().Value;
		var options = Factory.Services.GetRequiredService<IConfigOptionWriter>();
		var beforeName = (await options.CurrentAsync()).Net.MudName;
		var beforeState = CaptureCoreState();
		var before = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(livePath, "data.mdb")));
		var baselineEnd = ReadSnapshotId();
		using var upload = new MultipartFormDataContent();
		upload.Add(new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Services", "TestData", "rhost-sample.flat"))), "file", "world.flat");
		upload.Add(new StringContent("rhostmush"), "format");
		upload.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("mud_name Portal Private Preview\nsql_password synthetic-portal-import-secret")), "configFile", "mush.cnf");
		using var response = await client.PostAsync("/api/databaseconversion/upload", upload);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		using var started = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		var id = started.RootElement.GetProperty("sessionId").GetString()!;
		Exception? failure = null;
		try
		{
			var afterPost = CaptureCoreState();
			JsonDocument? completed = null;
			for (var attempt = 0; attempt < 300; attempt++)
			{
				using var result = await client.GetAsync("/api/databaseconversion/result/" + id);
				if (result.IsSuccessStatusCode)
				{
					completed = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
					break;
				}
				await Task.Delay(100);
			}
			using var report = completed ?? throw new TimeoutException("Import preview did not finish.");
			await Assert.That((await options.CurrentAsync()).Net.MudName).IsEqualTo(beforeName);
			var readyState = CaptureCoreState();
			var after = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(livePath, "data.mdb")));
			var finalId = ReadSnapshotId();
			await Assert.That(after.SequenceEqual(before)).IsTrue()
				.Because(DescribeCoreChanges(beforeState, baselineEnd, afterPost, readyState, finalId));
			await Assert.That(report.RootElement.GetProperty("requiresPromotion").GetBoolean()).IsTrue();
			using var refused = await client.PostAsJsonAsync("/api/databaseconversion/promote/" + id, new { confirmRestart = false });
			await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		}
		catch (Exception error) { failure = error; throw; }
		finally
		{
			try
			{
				using var aborted = await client.PostAsync("/api/databaseconversion/cancel/" + id, null);
				await Assert.That(aborted.StatusCode).IsEqualTo(HttpStatusCode.OK);
			}
			catch (Exception cleanup) when (failure is not null)
			{
				throw new AggregateException("Import assertions and session cleanup both failed.", failure, cleanup);
			}
		}
	}
	// Failure diagnostics only: keep the strict file assertion and identify concurrent native writes
	// without logging account/configuration values. All reads use the already-open live provider.
	private sealed record CoreState(ulong TransactionId, IReadOnlyDictionary<string, string[]> Rows);

	private CoreState CaptureCoreState() => Factory.Services.GetRequiredService<LightningDatabase>().Store.Read(tx =>
	{
		var id = ((IReadSnapshotTx)tx).ReadTransactionId;
		var rows = new Dictionary<string, string[]>(StringComparer.Ordinal);
		foreach (var table in Tables.All)
		{
			var entries = tx.Range(table, []).Take(10_001).Select(row =>
				Convert.ToHexString(SHA256.HashData(row.Key)) + ":" + Convert.ToHexString(SHA256.HashData(row.Value))).ToArray();
			if (entries.Length > 10_000) throw new InvalidOperationException("Import isolation diagnostic exceeds its per-table fixture bound.");
			rows.Add(table.Name, entries);
		}
		return new CoreState(id, rows);
	});

	private ulong ReadSnapshotId() => Factory.Services.GetRequiredService<LightningDatabase>().Store.Read(tx => ((IReadSnapshotTx)tx).ReadTransactionId);

	private static string DescribeCoreChanges(CoreState before, ulong baselineEnd, CoreState post, CoreState ready, ulong finalId)
	{
		object[] Changes(CoreState after) => before.Rows.Keys.Where(table => !before.Rows[table].SequenceEqual(after.Rows[table]))
			.Select(table => (object)new
			{
				Table = table,
				BeforeCount = before.Rows[table].Length,
				AfterCount = after.Rows[table].Length,
				RemovedDigests = before.Rows[table].Except(after.Rows[table]).Take(16).ToArray(),
				AddedDigests = after.Rows[table].Except(before.Rows[table]).Take(16).ToArray()
			}).ToArray();
		return "Live native isolation trace (core tables only; values and keys are SHA256 digests): " + JsonSerializer.Serialize(new
		{
			Before = before.TransactionId,
			BaselineHashEnd = baselineEnd,
			AfterPost = post.TransactionId,
			Ready = ready.TransactionId,
			FinalHashEnd = finalId,
			PostChanges = Changes(post),
			ReadyChanges = Changes(ready)
		});
	}

}
