using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// The managed remote backup endpoints through the real MVC pipeline: model binding and validation of
/// <see cref="RemoteBackupConfigure"/> and <see cref="RemoteBackupReceipt"/>, which a direct controller call skips.
/// </summary>
public class RemoteBackupsHttpTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	public async Task Configure_SavesTheSettingsItIsSent()
	{
		using var client = Factory.CreateHttpClient();
		var data = Factory.Services.GetRequiredService<IExpandedObjectDataService>();
		var original = await data.GetExpandedServerDataAsync<RemoteBackupData>();
		var repository = $"rest:https://backup.example.test/{TestIsolationHelpers.GenerateUniqueName("world")}";

		try
		{
			// Left disabled, so no credentials are needed and nothing is scheduled.
			using var response = await client.PutAsJsonAsync("api/admin/backups/remote",
				new RemoteBackupConfigure(new RemoteBackupSettings(false, 12, repository), null, null, null));

			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
				.Because(await response.Content.ReadAsStringAsync());
			var status = (await client.GetFromJsonAsync<RemoteBackupStatus>("api/admin/backups/remote"))!;
			await Assert.That(status.Settings).IsEqualTo(new RemoteBackupSettings(false, 12, repository));
		}
		finally
		{
			// The world is shared; put back what was stored before.
			var state = await data.GetExpandedServerDataAsync<RemoteBackupData>() ?? new RemoteBackupData();
			state.Settings = original?.Settings ?? new RemoteBackupData().Settings;
			await data.SetExpandedServerDataAsync(state);
		}
	}

	[Test]
	public async Task Configure_RefusesSettingsWithoutARepository()
	{
		using var client = Factory.CreateHttpClient();

		using var response = await client.PutAsJsonAsync("api/admin/backups/remote",
			new { Settings = new { Enabled = false, IntervalHours = 24 } });

		var body = await response.Content.ReadAsStringAsync();
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest).Because(body);
		// Refused by the [Required] model validation, before the coordinator's own check sees it.
		await Assert.That(body).Contains("\"Settings.Repository\"");
	}

	[Test]
	public async Task Receipt_IsBoundAndThenRefusedWithoutTheAgentToken()
	{
		using var client = Factory.CreateHttpClient();

		using var response = await client.PostAsJsonAsync("api/backup-agent/receipt",
			new RemoteBackupReceipt(Guid.NewGuid(), "nonce", 0, null, null));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized)
			.Because(await response.Content.ReadAsStringAsync());
	}
}
