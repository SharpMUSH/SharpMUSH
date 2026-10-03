using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;

namespace SharpMUSH.Tests.Integration.Packages;

/// <summary>
/// A portal package operation is preceded by an automatic backup of the world (#1333), on by
/// default, into the pre-package directory with its own retention.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class PackageOperationApiTests(ServerWebAppFactory factory)
{
	/// <summary>Pinned to https: following the http→https redirect drops the request body and headers.</summary>
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	[Test]
	public async Task APortalRollback_IsPrecededByAPrePackageBackup()
	{
		var backups = factory.Services.GetRequiredService<IWorldBackupService>();
		await Assert.That(backups.IsSupported).IsTrue();
		await Assert.That(backups.PackageOperationKeep).IsGreaterThan(0).Because("the automatic backup is on by default");
		var scheduledBefore = backups.List().Select(b => b.Name).ToArray();
		// Backup names carry the millisecond, so allow for the stamp rounding down.
		var start = DateTimeOffset.UtcNow.AddSeconds(-1);
		using var http = CreateClient();

		using var response = await http.PostAsync(
			$"api/packages/{TestIsolationHelpers.GenerateUniqueName("not-installed").ToLowerInvariant()}/rollback/1", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(backups.ListPackageOperationBackups().Any(b => b.CreatedAt >= start)).IsTrue();
		await Assert.That(backups.List().Select(b => b.Name).ToArray()).IsEquivalentTo(scheduledBefore)
			.Because("a pre-package copy never evicts or joins the scheduled ones");
	}
}
