using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Services;

public class WarningTypeTests
{
	[Test]
	public async Task ParseWarnings_None_ReturnsNone()
	{
		var result = WarningTypeHelper.ParseWarnings("none");

		await Assert.That(result).IsEqualTo(WarningType.None);
	}

	[Test]
	public async Task ParseWarnings_EmptyString_ReturnsNone()
	{
		var result = WarningTypeHelper.ParseWarnings("");

		await Assert.That(result).IsEqualTo(WarningType.None);
	}

	[Test]
	public async Task ParseWarnings_Normal_ReturnsNormal()
	{
		var result = WarningTypeHelper.ParseWarnings("normal");

		await Assert.That(result).IsEqualTo(WarningType.Normal);
	}

	[Test]
	public async Task ParseWarnings_All_ReturnsAll()
	{
		var result = WarningTypeHelper.ParseWarnings("all");

		await Assert.That(result).IsEqualTo(WarningType.All);
	}

	[Test]
	public async Task ParseWarnings_Serious_ReturnsSerious()
	{
		var result = WarningTypeHelper.ParseWarnings("serious");

		await Assert.That(result).IsEqualTo(WarningType.Serious);
	}

	[Test]
	public async Task ParseWarnings_Extra_ReturnsExtra()
	{
		var result = WarningTypeHelper.ParseWarnings("extra");

		await Assert.That(result).IsEqualTo(WarningType.Extra);
	}

	[Test]
	public async Task ParseWarnings_ExitUnlinked_ReturnsExitUnlinked()
	{
		var result = WarningTypeHelper.ParseWarnings("exit-unlinked");

		await Assert.That(result).IsEqualTo(WarningType.ExitUnlinked);
	}

	[Test]
	public async Task ParseWarnings_ThingDesc_ReturnsThingDesc()
	{
		var result = WarningTypeHelper.ParseWarnings("thing-desc");

		await Assert.That(result).IsEqualTo(WarningType.ThingDesc);
	}

	[Test]
	public async Task ParseWarnings_RoomDesc_ReturnsRoomDesc()
	{
		var result = WarningTypeHelper.ParseWarnings("room-desc");

		await Assert.That(result).IsEqualTo(WarningType.RoomDesc);
	}

	[Test]
	public async Task ParseWarnings_MyDesc_ReturnsPlayerDesc()
	{
		var result = WarningTypeHelper.ParseWarnings("my-desc");

		await Assert.That(result).IsEqualTo(WarningType.PlayerDesc);
	}

	[Test]
	public async Task ParseWarnings_ExitOneway_ReturnsExitOneway()
	{
		var result = WarningTypeHelper.ParseWarnings("exit-oneway");

		await Assert.That(result).IsEqualTo(WarningType.ExitOneway);
	}

	[Test]
	public async Task ParseWarnings_ExitMultiple_ReturnsExitMultiple()
	{
		var result = WarningTypeHelper.ParseWarnings("exit-multiple");

		await Assert.That(result).IsEqualTo(WarningType.ExitMultiple);
	}

	[Test]
	public async Task ParseWarnings_ExitMsgs_ReturnsExitMsgs()
	{
		var result = WarningTypeHelper.ParseWarnings("exit-msgs");

		await Assert.That(result).IsEqualTo(WarningType.ExitMsgs);
	}

	[Test]
	public async Task ParseWarnings_ThingMsgs_ReturnsThingMsgs()
	{
		var result = WarningTypeHelper.ParseWarnings("thing-msgs");

		await Assert.That(result).IsEqualTo(WarningType.ThingMsgs);
	}

	[Test]
	public async Task ParseWarnings_ExitDesc_ReturnsExitDesc()
	{
		var result = WarningTypeHelper.ParseWarnings("exit-desc");

		await Assert.That(result).IsEqualTo(WarningType.ExitDesc);
	}

	[Test]
	public async Task ParseWarnings_LockChecks_ReturnsLockProbs()
	{
		var result = WarningTypeHelper.ParseWarnings("lock-checks");

		await Assert.That(result).IsEqualTo(WarningType.LockProbs);
	}

	[Test]
	public async Task ParseWarnings_MultipleWarnings_ReturnsCombined()
	{
		var result = WarningTypeHelper.ParseWarnings("exit-unlinked thing-desc");

		await Assert.That(result.HasFlag(WarningType.ExitUnlinked)).IsTrue();
		await Assert.That(result.HasFlag(WarningType.ThingDesc)).IsTrue();
	}

	[Test]
	public async Task ParseWarnings_NegatedWarning_RemovesFromAll()
	{
		var result = WarningTypeHelper.ParseWarnings("all !exit-desc");

		await Assert.That(result.HasFlag(WarningType.ExitDesc)).IsFalse();
		await Assert.That(result.HasFlag(WarningType.ExitUnlinked)).IsTrue();
		await Assert.That(result.HasFlag(WarningType.ThingDesc)).IsTrue();
	}

	[Test]
	public async Task ParseWarnings_UnknownWarning_CollectsUnknown()
	{
		var unknownList = new List<string>();

		var result = WarningTypeHelper.ParseWarnings("exit-unlinked unknown-warning", unknownList);

		await Assert.That(result).IsEqualTo(WarningType.ExitUnlinked);
		await Assert.That(unknownList.Count).IsEqualTo(1);
		await Assert.That(unknownList[0]).IsEqualTo("unknown-warning");
	}

	[Test]
	public async Task UnparseWarnings_None_ReturnsNone()
	{
		var result = WarningTypeHelper.UnparseWarnings(WarningType.None);

		await Assert.That(result).IsEqualTo("none");
	}

	[Test]
	public async Task UnparseWarnings_Normal_ReturnsNormal()
	{
		var result = WarningTypeHelper.UnparseWarnings(WarningType.Normal);

		await Assert.That(result).IsEqualTo("normal");
	}

	[Test]
	public async Task UnparseWarnings_All_ReturnsAll()
	{
		var result = WarningTypeHelper.UnparseWarnings(WarningType.All);

		await Assert.That(result).IsEqualTo("all");
	}

	[Test]
	public async Task UnparseWarnings_ExitUnlinked_ReturnsExitUnlinked()
	{
		var result = WarningTypeHelper.UnparseWarnings(WarningType.ExitUnlinked);

		await Assert.That(result).IsEqualTo("exit-unlinked");
	}

	[Test]
	public async Task UnparseWarnings_MultipleFlags_ReturnsSpaceSeparated()
	{
		var flags = WarningType.ExitUnlinked | WarningType.ThingDesc;

		var result = WarningTypeHelper.UnparseWarnings(flags);

		await Assert.That(result).Contains("exit-unlinked");
		await Assert.That(result).Contains("thing-desc");
	}

	[Test]
	public async Task UnparseWarnings_PrefersGroupNames_OverIndividual()
	{
		var result = WarningTypeHelper.UnparseWarnings(WarningType.Normal);

		await Assert.That(result).IsEqualTo("normal");
		await Assert.That(result).DoesNotContain("exit-unlinked");
	}

	[Test]
	public async Task ParseAndUnparse_RoundTrip_PreservesValue()
	{
		var testCases = new[]
		{
			"none",
			"serious",
			"normal",
			"extra",
			"all",
			"exit-unlinked",
			"thing-desc room-desc"
		};

		foreach (var testCase in testCases)
		{
			var parsed = WarningTypeHelper.ParseWarnings(testCase);
			var unparsed = WarningTypeHelper.UnparseWarnings(parsed);
			var reparsed = WarningTypeHelper.ParseWarnings(unparsed);

			await Assert.That(parsed).IsEqualTo(reparsed);
		}
	}
}
