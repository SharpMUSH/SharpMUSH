using SharpMUSH.Library.Definitions;
using TUnit.Core;

namespace SharpMUSH.Tests.Services;

public class WarningLockChecksTests
{
	[Test]
	public async Task LockChecks_FlagValue_IsCorrect()
	{
		var lockProbs = WarningType.LockProbs;
		await Assert.That((uint)lockProbs).IsEqualTo(0x100000u);
	}

	[Test]
	public async Task LockChecks_FlagName_ParsesCorrectly()
	{
		// Test that "lock-checks" parses to LockProbs flag
		var parsed = WarningTypeHelper.ParseWarnings("lock-checks");
		await Assert.That(parsed).IsEqualTo(WarningType.LockProbs);
	}

	[Test]
	public async Task LockChecks_InSeriousGroup()
	{
		var serious = WarningType.Serious;
		await Assert.That(serious.HasFlag(WarningType.LockProbs)).IsEqualTo(true);
	}

	[Test]
	public async Task LockChecks_InNormalGroup()
	{
		var normal = WarningType.Normal;
		await Assert.That(normal.HasFlag(WarningType.LockProbs)).IsEqualTo(true);
	}

	[Test]
	public async Task LockChecks_InExtraGroup()
	{
		var extra = WarningType.Extra;
		await Assert.That(extra.HasFlag(WarningType.LockProbs)).IsEqualTo(true);
	}

	[Test]
	public async Task LockChecks_InAllGroup()
	{
		var all = WarningType.All;
		await Assert.That(all.HasFlag(WarningType.LockProbs)).IsEqualTo(true);
	}

	[Test]
	public async Task LockChecks_Unparsing_ReturnsLockChecks()
	{
		// Test that LockProbs unparsed returns "lock-checks"
		var unparsed = WarningTypeHelper.UnparseWarnings(WarningType.LockProbs);
		await Assert.That(unparsed).Contains("lock-checks");
	}

	[Test]
	public async Task LockChecks_Negation_RemovesFlag()
	{
		// Test that "all !lock-checks" removes LockProbs from All
		var parsed = WarningTypeHelper.ParseWarnings("all !lock-checks");
		await Assert.That(parsed.HasFlag(WarningType.LockProbs)).IsEqualTo(false);
		
		// But should still have other flags from All
		await Assert.That(parsed.HasFlag(WarningType.ExitUnlinked)).IsEqualTo(true);
	}

	[Test]
	public async Task LockChecks_Multiple_WithOtherFlags()
	{
		// Test combining lock-checks with other warnings
		var parsed = WarningTypeHelper.ParseWarnings("lock-checks room-desc");
		await Assert.That(parsed.HasFlag(WarningType.LockProbs)).IsEqualTo(true);
		await Assert.That(parsed.HasFlag(WarningType.RoomDesc)).IsEqualTo(true);
	}

	[Test]
	public async Task LockChecks_RoundTrip_PreservesValue()
	{
		// Test that parse -> unparse -> parse preserves LockProbs
		var original = WarningType.LockProbs;
		var unparsed = WarningTypeHelper.UnparseWarnings(original);
		var reparsed = WarningTypeHelper.ParseWarnings(unparsed);
		
		await Assert.That(reparsed).IsEqualTo(original);
	}

	// Integration tests (skipped - require full DB and lock service setup)

	[Test]
	[Skip("Requires database and lock service setup")]
	public async Task LockChecks_Integration_ValidLock_NoWarnings()
	{
		// Test that a valid lock doesn't trigger warnings
		// This would require:
		// - Creating a test object with a valid lock
		// - Running CheckObjectAsync on it
		// - Verifying no lock-checks warnings were issued
		await Task.CompletedTask;
	}

	[Test]
	[Skip("Requires database and lock service setup")]
	public async Task LockChecks_Integration_InvalidLock_TriggersWarning()
	{
		// Test that an invalid lock (e.g., reference to non-existent object) triggers warning
		// This would require:
		// - Creating a test object with an invalid lock
		// - Running CheckObjectAsync on it
		// - Verifying lock-checks warning was issued
		await Task.CompletedTask;
	}

	[Test]
	[Skip("Requires database and lock service setup")]
	public async Task LockChecks_Integration_MultipleLocks_ChecksAll()
	{
		// Test that all locks on an object are checked
		// This would require:
		// - Creating an object with multiple locks (some valid, some invalid)
		// - Running CheckObjectAsync on it
		// - Verifying warnings for all invalid locks
		await Task.CompletedTask;
	}

	[Test]
	[Skip("Requires database and lock service setup")]
	public async Task LockChecks_Integration_EmptyLock_Skipped()
	{
		// Test that empty/whitespace locks are skipped
		// This would require:
		// - Creating an object with empty lock
		// - Running CheckObjectAsync on it
		// - Verifying no warnings issued
		await Task.CompletedTask;
	}

	[Test]
	[Skip("Requires database and lock service setup")]
	public async Task LockChecks_Integration_GoingObjectReference_TriggersWarning()
	{
		// Test that lock referencing GOING object triggers warning
		// This would require:
		// - Creating an object with lock referencing a GOING object
		// - Running CheckObjectAsync on it
		// - Verifying lock-checks warning was issued
		await Task.CompletedTask;
	}
}
