using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Integration tests for the staging database workflow.
/// Verifies that:
/// 1. Creating a staging DB doesn't affect the live database
/// 2. Objects created in staging are isolated from live
/// 3. Promote swaps the handle so live now serves staging data
/// 4. The live database can continue operating after promotion without downtime
/// 5. Abort discards staging without affecting live
/// </summary>
public class StagingDatabaseTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	[NotInParallel]
	public async Task CreateStaging_DoesNotAffectLiveDatabase()
	{
		var db = Database;

		// Get the God player from live
		var playerOne = (await db.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		await Assert.That(playerOne.Object.Name).IsEqualTo("God");

		// Set a unique attribute on live to verify it stays
		var liveMarker = $"LIVE_MARKER_{Guid.NewGuid():N}";
		await db.SetAttributeAsync(new DBRef(1), ["STAGING_TEST_LIVE"], A.single(liveMarker), playerOne);

		// Create staging
		await using var staging = await db.CreateStagingAsync();

		// Verify live attribute is still readable through the live database
		var liveAttr = await db.GetAttributeAsync(new DBRef(1), ["STAGING_TEST_LIVE"])!.ToListAsync();
		await Assert.That(liveAttr.Last().Value.ToString()).IsEqualTo(liveMarker);

		// Abort staging (cleanup)
		await staging.AbortAsync();
	}

	[Test]
	[NotInParallel]
	public async Task StagingObjects_AreIsolatedFromLive()
	{
		var db = Database;

		// Get live state
		var playerOne = (await db.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		// Create staging
		await using var staging = await db.CreateStagingAsync();

		// Staging is a full ISharpDatabase — it was migrated, so it has Room Zero, God, Master Room
		var stagingPlayerOne = (await staging.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		await Assert.That(stagingPlayerOne.Object.Name).IsEqualTo("God");

		// Set a unique attribute ONLY in staging
		var stagingMarker = $"STAGING_ONLY_{Guid.NewGuid():N}";
		await staging.SetAttributeAsync(new DBRef(1), ["STAGING_ONLY_ATTR"], A.single(stagingMarker), stagingPlayerOne);

		// Verify the attribute exists in staging
		var stagingAttr = await staging.GetAttributeAsync(new DBRef(1), ["STAGING_ONLY_ATTR"])!.ToListAsync();
		await Assert.That(stagingAttr.Last().Value.ToString()).IsEqualTo(stagingMarker);

		// Verify the attribute does NOT exist in live
		var liveAttr = db.GetAttributeAsync(new DBRef(1), ["STAGING_ONLY_ATTR"]);
		if (liveAttr != null)
		{
			var liveAttrList = await liveAttr.ToListAsync();
			// Should be empty or not contain the staging marker
			if (liveAttrList.Count > 0)
			{
				await Assert.That(liveAttrList.Last().Value.ToString()).IsNotEqualTo(stagingMarker);
			}
		}

		await staging.AbortAsync();
	}

	[Test]
	[NotInParallel]
	public async Task PromoteStaging_SwapsLiveToStagingData()
	{
		var db = Database;

		// Get the current player
		var playerOne = (await db.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		// Set a unique marker in live that we'll verify disappears after promote
		var liveOnlyMarker = $"BEFORE_PROMOTE_{Guid.NewGuid():N}";
		await db.SetAttributeAsync(new DBRef(1), ["BEFORE_PROMOTE"], A.single(liveOnlyMarker), playerOne);

		// Verify it's there
		var prePromoteAttr = await db.GetAttributeAsync(new DBRef(1), ["BEFORE_PROMOTE"])!.ToListAsync();
		await Assert.That(prePromoteAttr.Last().Value.ToString()).IsEqualTo(liveOnlyMarker);

		// Create staging and set a different marker there
		await using var staging = await db.CreateStagingAsync();
		var stagingPlayer = (await staging.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		var stagingMarker = $"AFTER_PROMOTE_{Guid.NewGuid():N}";
		await staging.SetAttributeAsync(new DBRef(1), ["PROMOTED_MARKER"], A.single(stagingMarker), stagingPlayer);

		// PROMOTE — this is the critical operation
		await staging.PromoteToLiveAsync();

		// Now the live database handle points to what was the staging DB.
		// The live DB should have the PROMOTED_MARKER attribute
		var postPromoteAttr = db.GetAttributeAsync(new DBRef(1), ["PROMOTED_MARKER"]);
		await Assert.That(postPromoteAttr).IsNotNull();
		var postPromoteList = await postPromoteAttr!.ToListAsync();
		await Assert.That(postPromoteList.Last().Value.ToString()).IsEqualTo(stagingMarker);

		// The BEFORE_PROMOTE attribute should NOT exist anymore (old DB was dropped)
		var oldAttr = db.GetAttributeAsync(new DBRef(1), ["BEFORE_PROMOTE"]);
		if (oldAttr != null)
		{
			var oldAttrList = await oldAttr.ToListAsync();
			// If the attribute tree returns results, they shouldn't contain the old marker
			if (oldAttrList.Count > 0)
			{
				await Assert.That(oldAttrList.Last().Value.ToString()).IsNotEqualTo(liveOnlyMarker);
			}
		}
	}

	[Test]
	[DependsOn(nameof(PromoteStaging_SwapsLiveToStagingData))]
	public async Task AfterPromote_LiveDatabaseContinuesOperating()
	{
		var db = Database;

		// After a promote, the live database should be fully functional.
		// The handle now points to what was the staging DB.

		// Can we read existing objects?
		var roomZero = (await db.GetObjectNodeAsync(new DBRef(0))).AsRoom;
		await Assert.That(roomZero.Object.Name).IsEqualTo("Room Zero");

		var playerOne = (await db.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		await Assert.That(playerOne.Object.Name).IsEqualTo("God");

		// Can we create NEW objects in the now-live database?
		var newRoomRef = await db.CreateRoomAsync("PostPromoteRoom", playerOne);
		await Assert.That(newRoomRef.Number).IsGreaterThan(0);

		var newRoom = (await db.GetObjectNodeAsync(newRoomRef)).AsRoom;
		await Assert.That(newRoom.Object.Name).IsEqualTo("PostPromoteRoom");

		// Can we set and retrieve attributes?
		var continuityMarker = $"CONTINUITY_{Guid.NewGuid():N}";
		await db.SetAttributeAsync(new DBRef(1), ["CONTINUITY_CHECK"], A.single(continuityMarker), playerOne);
		var attr = await db.GetAttributeAsync(new DBRef(1), ["CONTINUITY_CHECK"])!.ToListAsync();
		await Assert.That(attr.Last().Value.ToString()).IsEqualTo(continuityMarker);
	}

	[Test]
	[DependsOn(nameof(AfterPromote_LiveDatabaseContinuesOperating))]
	public async Task AfterPromote_CanCreateAnotherStaging()
	{
		var db = Database;

		// Verify we can create yet another staging database after a previous promote
		await using var staging2 = await db.CreateStagingAsync();

		var stagingPlayer = (await staging2.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		await Assert.That(stagingPlayer.Object.Name).IsEqualTo("God");

		// Clean up without promoting
		await staging2.AbortAsync();
	}

	[Test]
	[NotInParallel]
	public async Task AbortStaging_LiveDatabaseUnchanged()
	{
		var db = Database;

		var playerOne = (await db.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		// Set a marker in live
		var liveMarker = $"ABORT_TEST_{Guid.NewGuid():N}";
		await db.SetAttributeAsync(new DBRef(1), ["ABORT_TEST_MARKER"], A.single(liveMarker), playerOne);

		// Create staging, set different data
		await using var staging = await db.CreateStagingAsync();
		var stagingPlayer = (await staging.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		await staging.SetAttributeAsync(new DBRef(1), ["ABORT_TEST_MARKER"], A.single("SHOULD_NOT_APPEAR"), stagingPlayer);

		// Abort staging
		await staging.AbortAsync();

		// Live should still have the original marker
		var liveAttr = await db.GetAttributeAsync(new DBRef(1), ["ABORT_TEST_MARKER"])!.ToListAsync();
		await Assert.That(liveAttr.Last().Value.ToString()).IsEqualTo(liveMarker);
	}

	[Test]
	[NotInParallel]
	public async Task Staging_CanCreateAndQueryObjects()
	{
		var db = Database;

		// Create staging
		await using var staging = await db.CreateStagingAsync();

		var stagingPlayer = (await staging.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		// Create a room in staging
		var roomRef = await staging.CreateRoomAsync("StagingTestRoom", stagingPlayer);
		await Assert.That(roomRef.Number).IsGreaterThan(0);

		// Query it back
		var room = (await staging.GetObjectNodeAsync(roomRef)).AsRoom;
		await Assert.That(room.Object.Name).IsEqualTo("StagingTestRoom");

		// Create a thing in staging
		var roomZero = (await staging.GetObjectNodeAsync(new DBRef(0))).AsRoom;
		var thingRef = await staging.CreateThingAsync("StagingWidget", roomZero, stagingPlayer, roomZero);
		await Assert.That(thingRef.Number).IsGreaterThan(0);

		var thing = (await staging.GetObjectNodeAsync(thingRef)).AsThing;
		await Assert.That(thing.Object.Name).IsEqualTo("StagingWidget");

		// Clean up
		await staging.AbortAsync();
	}

	[Test]
	[NotInParallel]
	public async Task Staging_DisposeWithoutExplicitAbort_CleansUp()
	{
		var db = Database;

		// Creating and disposing without explicit abort should auto-cleanup
		{
			await using var staging = await db.CreateStagingAsync();
			var stagingPlayer = (await staging.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
			await staging.SetAttributeAsync(new DBRef(1), ["DISPOSE_TEST"], A.single("dispose_test_value"), stagingPlayer);
			// Let DisposeAsync run naturally
		}

		// Live should not have the staging attribute
		var liveAttr = db.GetAttributeAsync(new DBRef(1), ["DISPOSE_TEST"]);
		if (liveAttr != null)
		{
			var list = await liveAttr.ToListAsync();
			if (list.Count > 0)
			{
				await Assert.That(list.Last().Value.ToString()).IsNotEqualTo("dispose_test_value");
			}
		}
	}
}
