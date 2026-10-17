using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Tests.Database.Lightning;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@storage</c> through the command path: the wizard lock, and that each form answers with what it
/// promises — the capacity figures, the history counts with their policy, and a purge that under the
/// default policy keeps everything.
/// </summary>
[NotInParallel]
public class StorageCommandTests : ServerTestBase
{
	[ClassDataSource<StorageInventoryServerFactory>(Shared = SharedType.PerTestSession)]
	public required StorageInventoryServerFactory InventoryFactory { get; init; }

	private async Task<IReadOnlyList<string>> HeardAfterAsync(string command)
	{
		// The recorder accumulates for the whole session, so read only what this command added.
		var before = Notifications.DeliveryCountFor(WebAppFactoryArg.ExecutorDBRef);
		await Cmd(command);
		return Notifications.DeliveriesFor(WebAppFactoryArg.ExecutorDBRef)
			.Skip(before)
			.Select(delivery => delivery.Message)
			.ToList();
	}

	[Test]
	public async Task BackupViewsUseTheSamePaginatedInventory()
	{
		// Compare one stable inventory without other fixtures teleporting the shared God or taking backups.
		async Task<string> Inventory(string command)
		{
			var before = InventoryFactory.Notifications.DeliveryCountFor(InventoryFactory.ExecutorDBRef);
			await InventoryFactory.CommandParser.CommandParse(1,
				InventoryFactory.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(), MString.Plain(command));
			return string.Join("\n", InventoryFactory.Notifications.DeliveriesFor(InventoryFactory.ExecutorDBRef)
				.Skip(before).Select(delivery => delivery.Message));
		}

		await Inventory("@backup");
		var storage = await Inventory("@storage/backups");
		var backup = await Inventory("@backup/list");
		await Assert.That(storage).IsEqualTo(backup);
		await Assert.That(storage).Contains("Created:").And.Contains("Page 1/");
		var invalid = await Inventory("@storage/backups 0");
		await Assert.That(invalid).Contains("positive page number");
	}

	[Test]
	[Arguments("@storage")]
	[Arguments("@storage/history")]
	[Arguments("@storage/purge")]
	public async Task AMortalCannotRunIt(string command)
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services,
			WebAppFactoryArg.Services.GetRequiredService<IMediator>(),
			ConnectionService,
			"StorageMortal");

		var answer = await CmdAs(mortal.DbRef, mortal.Handle, command);

		await Assert.That(answer).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	[Test]
	public async Task TheReportTellsTheMapTheFileTheDiskAndTheLiveDataApart()
	{
		var heard = await HeardAfterAsync("@storage");

		var report = string.Join("\n", heard);
		await Assert.That(report).Contains("limit").And.Contains("live,").And.Contains("reusable");
		await Assert.That(report).Contains("File size:").And.Contains("Inside file:");
		await Assert.That(report).DoesNotContain("File:");
		await Assert.That(report).Contains("On disk:").And.Contains("Disk free:");
		await Assert.That(report).Contains("Only a compacted copy shrinks it.");
		await Assert.That(report).Contains("at the peak").And.Contains("Next run:");
	}

	private async Task<MString> CapacityReportAsync(long fileBytes, long liveBytes, long allocatedBytes,
		long mapBytes = 64L << 30)
	{
		var capacity = Substitute.For<IStorageCapacityService>();
		capacity.Measure().Returns(new StorageCapacityReport
		{
			WorldPath = "/data/world",
			MapSizeBytes = mapBytes,
			PageSize = 4096,
			FileBytes = fileBytes,
			AllocatedBytes = allocatedBytes,
			FilePages = Math.Max(0, fileBytes) / 4096,
			UsedPages = liveBytes / 4096,
			WorldDiskFreeBytes = 100L << 30,
			Backup = new BackupCapacity("", 0, 0, 0, 0, 0, -1, false),
			Leftovers = []
		});
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(
			WebAppFactoryArg.Services, capacity);
		var parser = WebAppFactoryArg.CommandParserWith(commands.Get(), WebAppFactoryArg.ExecutorDBRef, 1);
		var before = Notifications.RawCountFor(WebAppFactoryArg.ExecutorDBRef);
		await parser.CommandParse(1, ConnectionService, MarkupText.Plain("@storage"));
		return MarkupText.Join(MarkupText.Plain("\n"), Notifications.RawFor(WebAppFactoryArg.ExecutorDBRef)
			.Skip(before).Select(message => message switch
			{
				MString markup => markup,
				string text => MarkupText.Plain(text),
			}));
	}

	private static string HtmlField(MString report, string label)
		=> Regex.Match(report.Render(MarkupFormat.Html), $"<dt>{label}:</dt><dd[^>]*>(.*?)</dd>",
			RegexOptions.Singleline).Groups[1].Value;

	[Test]
	[Arguments(16384L, 16384L, 16384L, "16.0 KB", "16.0 KB live, 0 B reusable", "16.0 KB")]
	[Arguments(32L << 30, 24L << 30, 32L << 30, "32.0 GB", "24.0 GB live, 8.0 GB reusable", "32.0 GB")]
	[Arguments(32L << 30, 1L << 20, 1L << 20, "32.0 GB", "1.0 MB live, 32.0 GB reusable", "1.0 MB")]
	[Arguments(0L, 0L, 0L, "0 B", "0 B live, 0 B reusable", "0 B")]
	[Arguments(-1L, 0L, -1L, "unknown", "0 B live, unknown reusable", "unknown")]
	[Arguments(16384L, -4096L, 16384L, "16.0 KB", "unknown live, unknown reusable", "16.0 KB")]
	public async Task FileFacts_HaveNoPackingGauge(long fileBytes, long liveBytes, long allocatedBytes,
		string fileSize, string insideFile, string allocated)
	{
		var shown = await CapacityReportAsync(fileBytes, liveBytes, allocatedBytes);
		var text = shown.ToPlainText();
		await Assert.That(text).Contains("File size:").And.Contains("Inside file:");
		await Assert.That(HtmlField(shown, "File size")).Contains(fileSize).And.DoesNotContain("ms-gauge");
		await Assert.That(HtmlField(shown, "Inside file")).Contains(insideFile).And.DoesNotContain("ms-gauge");
		await Assert.That(HtmlField(shown, "On disk")).Contains(allocated);
		await Assert.That(text).DoesNotContain("NaN").And.DoesNotContain("Infinity");
		if (fileBytes < 0)
		{
			await Assert.That(HtmlField(shown, "Map")).Contains("unknown").And.DoesNotContain("ms-gauge");
		}
	}

	[Test]
	public async Task ANearlyFullMap_StillShowsItsActualCapacityGauge()
	{
		var shown = await CapacityReportAsync(99L << 20, 99L << 20, 99L << 20, 100L << 20);
		await Assert.That(HtmlField(shown, "Map")).Contains("<meter min=\"0\" max=\"104857600\" value=\"103809024\"")
			.And.Contains(">99%</span>").And.Contains("99.0 MB of 100.0 MB limit");
		await Assert.That(HtmlField(shown, "File size")).Contains("99.0 MB").And.DoesNotContain("ms-gauge");
		await Assert.That(shown.ToPlainText()).Contains("99%").And.Contains("99.0 MB live, 0 B reusable");
	}

	[Test]
	public async Task TheReportListsEachAttributeIndex()
	{
		var name = ("STOREIDX_" + Guid.NewGuid().ToString("N"))[..20].ToUpperInvariant();
		await Cmd($"@attribute/access {name}=none");
		await Cmd($"@attribute/index {name}=numeric");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "StoreIdx");
		await Cmd($"&{name} {thing}=42");

		var heard = string.Join("\n", await HeardAfterAsync("@storage"));

		await Assert.That(heard).Contains("Attribute indexes").And.Contains($"{name} (numeric)");
		var usage = WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IStorageCapacityService>()
			.Measure().AttributeIndexes.Single(index => index.Name == name);
		await Assert.That(usage.Rows).IsEqualTo(1);
		await Assert.That(usage.Ready).IsTrue();
		await Assert.That(usage.Bytes).IsGreaterThan(0);
	}

	[Test]
	public async Task TheReportCountsReaderSlotsADeadProcessLeftBehind()
	{
		// The session's store also checks on a timer, which can free the slot before this test does; the
		// count the report reads goes up whichever check freed it.
		var store = ((LightningDatabase)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>()).Store;
		var clearedBefore = store.StaleReadersCleared;
		await DeadReader.LeaveAsync(store.Path);
		store.CheckStaleReaders();
		await Assert.That(store.StaleReadersCleared).IsEqualTo(clearedBefore + 1);

		var heard = await HeardAfterAsync("@storage");

		await Assert.That(heard.Any(m => m.Contains("dead reader(s) cleared since startup"))).IsTrue();
	}

	[Test]
	public async Task HistoryListsEveryKindWithItsPolicy()
	{
		var heard = await HeardAfterAsync("@storage/history");

		var report = string.Join("\n", heard);
		await Assert.That(heard.Any(m => m.Split('\n').Any(line => line.Contains("wiki ") && line.Contains("keep everything")))).IsTrue();
		await Assert.That(report).Contains("No archive:");
	}

	/// <summary>Nothing is configured in the test host, so a pass purges nothing — the default is archival.</summary>
	[Test]
	public async Task PurgeUnderTheDefaultPolicyKeepsEverything()
	{
		var heard = await HeardAfterAsync("@storage/purge");

		await Assert.That(heard.Any(m => m.Contains("wiki: keeps everything; nothing purged."))).IsTrue();
	}
}

/// <summary>Inventory comparisons own their world and notification recorder.</summary>
public class StorageInventoryServerFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
}
