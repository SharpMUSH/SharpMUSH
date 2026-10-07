using SharpMUSH.Library.Definitions;

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
		var parsed = WarningTypeHelper.ParseWarnings("lock-checks");
		await Assert.That(parsed).IsEqualTo(WarningType.LockProbs);
	}

	[Test]
	public async Task LockChecks_InSeriousGroup()
	{
		var serious = WarningType.Serious;
		await Assert.That(serious.HasFlag(WarningType.LockProbs)).IsTrue();
	}

	[Test]
	public async Task LockChecks_InNormalGroup()
	{
		var normal = WarningType.Normal;
		await Assert.That(normal.HasFlag(WarningType.LockProbs)).IsTrue();
	}

	[Test]
	public async Task LockChecks_InExtraGroup()
	{
		var extra = WarningType.Extra;
		await Assert.That(extra.HasFlag(WarningType.LockProbs)).IsTrue();
	}

	[Test]
	public async Task LockChecks_InAllGroup()
	{
		var all = WarningType.All;
		await Assert.That(all.HasFlag(WarningType.LockProbs)).IsTrue();
	}

	[Test]
	public async Task LockChecks_Unparsing_ReturnsLockChecks()
	{
		var unparsed = WarningTypeHelper.UnparseWarnings(WarningType.LockProbs);
		await Assert.That(unparsed).Contains("lock-checks");
	}

	[Test]
	public async Task LockChecks_Negation_RemovesFlag()
	{
		var parsed = WarningTypeHelper.ParseWarnings("all !lock-checks");
		await Assert.That(parsed.HasFlag(WarningType.LockProbs)).IsFalse();

		await Assert.That(parsed.HasFlag(WarningType.ExitUnlinked)).IsTrue();
	}

	[Test]
	public async Task LockChecks_Multiple_WithOtherFlags()
	{
		var parsed = WarningTypeHelper.ParseWarnings("lock-checks room-desc");
		await Assert.That(parsed.HasFlag(WarningType.LockProbs)).IsTrue();
		await Assert.That(parsed.HasFlag(WarningType.RoomDesc)).IsTrue();
	}

	[Test]
	public async Task LockChecks_RoundTrip_PreservesValue()
	{
		var original = WarningType.LockProbs;
		var unparsed = WarningTypeHelper.UnparseWarnings(original);
		var reparsed = WarningTypeHelper.ParseWarnings(unparsed);

		await Assert.That(reparsed).IsEqualTo(original);
	}
}
