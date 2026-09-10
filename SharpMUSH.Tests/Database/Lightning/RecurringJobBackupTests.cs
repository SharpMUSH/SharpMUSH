using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.RecurringJobs;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services.RecurringJobs;

namespace SharpMUSH.Tests.Database.Lightning;

public class RecurringJobBackupTests
{
	[Test]
	public async Task DefinitionsAndFiringClaimsSurviveWorldCopy()
	{
		var root = Path.Join(Path.GetTempPath(), "jobs-backup-" + Guid.NewGuid().ToString("N"));
		try
		{
			await using var source = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
				new LightningStoreOptions { Path = Path.Join(root, "source"), MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
			await source.Migrate();
			var job = new RecurringJob("job", "account", "#1:1", "#0:1", "RUN", "0 9 * * *", "UTC", "daily event", true, 2, 1000, 500, null, "queued", "claim");
			await source.SetExpandedServerData(RecurringJobService.StorageKey, new RecurringJobDocument([job]));
			await source.CopyToAsync(Path.Join(root, "copy"));
			await using var restored = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
				new LightningStoreOptions { Path = Path.Join(root, "copy"), MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
			var document = await restored.GetExpandedServerData<RecurringJobDocument>(RecurringJobService.StorageKey);
			await Assert.That(document!.Jobs.Single()).IsEqualTo(job);
			await restored.SetExpandedServerData(RecurringJobService.StorageKey, new RecurringJobDocument([job with { RunToken = null, LastError = "skipped on restart" }]));
			await Assert.That((await restored.GetExpandedServerData<RecurringJobDocument>(RecurringJobService.StorageKey))!.Jobs.Single().RunToken).IsNull();
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}
}
