using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Snapshots;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services.Snapshots;

namespace SharpMUSH.Tests.Database.Lightning;

public class ObjectSnapshotBackupTests
{
	[Test]
	public async Task SnapshotHistoryAndPendingRecoverySurviveWorldCopy()
	{
		var root = Path.Join(Path.GetTempPath(), "snapshot-backup-" + Guid.NewGuid().ToString("N"));
		var sourcePath = Path.Join(root, "source");
		var copyPath = Path.Join(root, "copy");
		try
		{
			await using var source = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
				new LightningStoreOptions { Path = sourcePath, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
			await source.Migrate();
			var room = (await source.GetObjectNodeAsync(new DBRef(0))).AsRoom.Object;
			var snapshot = new ObjectSnapshot("pending-image", 1, room.DBRef.ToString(), room.Type, "account", "#1:1", 1, "before restore", 10, room.Name,
				[new SnapshotAttribute("DESC", MarkupTextSerializer.Serialize(MarkupText.Plain("backup text")), [], "#1:1")], new(), [], "digest");
			var history = new SnapshotHistory([snapshot], "pending-image", "injected stop");
			await source.SetExpandedObjectData(room.Id!, ObjectSnapshotService.StorageKey, new SnapshotStorageRecord(history));
			await source.CopyToAsync(copyPath);
			await using var restored = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
				new LightningStoreOptions { Path = copyPath, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
			var recovered = await restored.GetExpandedObjectData<SnapshotStorageRecord>(room.Id!, ObjectSnapshotService.StorageKey);
			await Assert.That(recovered!.History.PendingRecoveryId).IsEqualTo("pending-image");
			await Assert.That(recovered.History.Snapshots.Single().Attributes.Single().Markup).IsEqualTo(snapshot.Attributes.Single().Markup);
			await restored.SetExpandedObjectData(room.Id!, ObjectSnapshotService.StorageKey, new SnapshotStorageRecord(history with { PendingRecoveryId = null }));
			await Assert.That((await restored.GetExpandedObjectData<SnapshotStorageRecord>(room.Id!, ObjectSnapshotService.StorageKey))!.History.PendingRecoveryId).IsNull();
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}
}
