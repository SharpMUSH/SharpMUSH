using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Server;

public class ImportPortalServerFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;

	public override async Task InitializeAsync()
	{
		await base.InitializeAsync();
		// The byte-for-byte assertion needs a quiescent world: maintenance jobs can write uptime
		// data independently of preview. Await in-flight jobs, then drain startup connection work.
		var scheduler = await Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
		await scheduler.Shutdown(waitForJobsToComplete: true);
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
	public async Task UploadOnlyPreviewsConfigurationAndPromotionRequiresASeparateExplicitConfirmation()
	{
		using var client = Factory.CreateHttpClient();
		var livePath = Factory.Services.GetRequiredService<LightningWorldPath>().Value;
		var options = Factory.Services.GetRequiredService<IConfigOptionWriter>();
		var beforeName = (await options.CurrentAsync()).Net.MudName;
		var before = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(livePath, "data.mdb")));
		using var upload = new MultipartFormDataContent();
		upload.Add(new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Services", "TestData", "rhost-sample.flat"))), "file", "world.flat");
		upload.Add(new StringContent("rhostmush"), "format");
		upload.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("mud_name Portal Private Preview\nsql_password synthetic-portal-import-secret")), "configFile", "mush.cnf");
		using var response = await client.PostAsync("/api/databaseconversion/upload", upload);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		using var started = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		var id = started.RootElement.GetProperty("sessionId").GetString()!;
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
		await Assert.That(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(livePath, "data.mdb"))).SequenceEqual(before)).IsTrue();
		await Assert.That(report.RootElement.GetProperty("requiresPromotion").GetBoolean()).IsTrue();
		using var refused = await client.PostAsJsonAsync("/api/databaseconversion/promote/" + id, new { confirmRestart = false });
		await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		using var aborted = await client.PostAsync("/api/databaseconversion/cancel/" + id, null);
		await Assert.That(aborted.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}
}
