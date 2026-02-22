using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Services;

public class WarningTypeTests
{
	[Test]
	public async Task ParseWarnings_None_ReturnsNone()
	{
		// Act
		var result = WarningTypeHelper.ParseWarnings("none");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.None);
	}

	[Test]
	public async Task ParseWarnings_EmptyString_ReturnsNone()
	{
		// Act
		var result = WarningTypeHelper.ParseWarnings("");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.None);
	}

	[Test]
	public async Task ParseWarnings_Normal_ReturnsNormal()
	{
		// Act
		var result = WarningTypeHelper.ParseWarnings("normal");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.Normal);
	}

	[Test]
	public async Task ParseWarnings_All_ReturnsAll()
	{
		// Act
		var result = WarningTypeHelper.ParseWarnings("all");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.All);
	}

	[Test]
	public async Task ParseWarnings_Serious_ReturnsSerious()
	{
		// Act
		var result = WarningTypeHelper.ParseWarnings("serious");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.Serious);
	}

	[Test]
	public async Task ParseWarnings_Extra_ReturnsExtra()
	{
		// Act
		var result = WarningTypeHelper.ParseWarnings("extra");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.Extra);
	}

	[Test]
	public async Task ParseWarnings_ExitUnlinked_ReturnsExitUnlinked()
	{
		// Arrange - use unique name "warn-exit-unlinked-test"

		// Act
		var result = WarningTypeHelper.ParseWarnings("exit-unlinked");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ExitUnlinked);
	}

	[Test]
	public async Task ParseWarnings_ThingDesc_ReturnsThingDesc()
	{
		// Arrange - use unique name "warn-thing-desc-test"

		// Act
		var result = WarningTypeHelper.ParseWarnings("thing-desc");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ThingDesc);
	}

	[Test]
	public async Task ParseWarnings_RoomDesc_ReturnsRoomDesc()
	{
		// Arrange - use unique name "warn-room-desc-test"

		// Act
		var result = WarningTypeHelper.ParseWarnings("room-desc");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.RoomDesc);
	}

	[Test]
	public async Task ParseWarnings_MyDesc_ReturnsPlayerDesc()
	{
		// Arrange - use unique name "warn-player-desc-test"

		// Act
		var result = WarningTypeHelper.ParseWarnings("my-desc");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.PlayerDesc);
	}

	[Test]
	public async Task ParseWarnings_ExitOneway_ReturnsExitOneway()
	{
		// Arrange - use unique name "warn-exit-oneway-test"

		// Act
		var result = WarningTypeHelper.ParseWarnings("exit-oneway");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ExitOneway);
	}

	[Test]
	public async Task ParseWarnings_ExitMultiple_ReturnsExitMultiple()
	{
		// Arrange - use unique name "warn-exit-multiple-test"

		// Act
		var result = WarningTypeHelper.ParseWarnings("exit-multiple");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ExitMultiple);
	}

	[Test]
	public async Task ParseWarnings_ExitMsgs_ReturnsExitMsgs()
	{
		// Arrange - use unique name "warn-exit-msgs-test"

		// Act
		var result = WarningTypeHelper.ParseWarnings("exit-msgs");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ExitMsgs);
	}

	[Test]
	public async Task ParseWarnings_ThingMsgs_ReturnsThingMsgs()
	{
		// Arrange - use unique name "warn-thing-msgs-test"

		// Act
		var result = WarningTypeHelper.ParseWarnings("thing-msgs");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ThingMsgs);
	}

	[Test]
	public async Task ParseWarnings_ExitDesc_ReturnsExitDesc()
	{
		// Arrange - use unique name "warn-exit-desc-test"

		// Act
		var result = WarningTypeHelper.ParseWarnings("exit-desc");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ExitDesc);
	}

	[Test]
	public async Task ParseWarnings_LockChecks_ReturnsLockProbs()
	{
		// Arrange - use unique name "warn-lock-checks-test"

		// Act
		var result = WarningTypeHelper.ParseWarnings("lock-checks");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.LockProbs);
	}

	[Test]
	public async Task ParseWarnings_MultipleWarnings_ReturnsCombined()
	{
		// Arrange - use unique name "warn-multiple-combined"

		// Act
		var result = WarningTypeHelper.ParseWarnings("exit-unlinked thing-desc");

		// Assert
		await Assert.That(result.HasFlag(WarningType.ExitUnlinked)).IsTrue();
		await Assert.That(result.HasFlag(WarningType.ThingDesc)).IsTrue();
	}

	[Test]
	public async Task ParseWarnings_NegatedWarning_RemovesFromAll()
	{
		// Arrange - use unique name "warn-negated-test"

		// Act
		var result = WarningTypeHelper.ParseWarnings("all !exit-desc");

		// Assert
		await Assert.That(result.HasFlag(WarningType.ExitDesc)).IsFalse();
		await Assert.That(result.HasFlag(WarningType.ExitUnlinked)).IsTrue();
		await Assert.That(result.HasFlag(WarningType.ThingDesc)).IsTrue();
	}

	[Test]
	public async Task ParseWarnings_UnknownWarning_CollectsUnknown()
	{
		// Arrange - use unique name "warn-unknown-test"
		var unknownList = new List<string>();

		// Act
		var result = WarningTypeHelper.ParseWarnings("exit-unlinked unknown-warning", unknownList);

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ExitUnlinked);
		await Assert.That(unknownList.Count).IsEqualTo(1);
		await Assert.That(unknownList[0]).IsEqualTo("unknown-warning");
	}

	[Test]
	public async Task UnparseWarnings_None_ReturnsNone()
	{
		// Act
		var result = WarningTypeHelper.UnparseWarnings(WarningType.None);

		// Assert
		await Assert.That(result).IsEqualTo("none");
	}

	[Test]
	public async Task UnparseWarnings_Normal_ReturnsNormal()
	{
		// Act
		var result = WarningTypeHelper.UnparseWarnings(WarningType.Normal);

		// Assert
		await Assert.That(result).IsEqualTo("normal");
	}

	[Test]
	public async Task UnparseWarnings_All_ReturnsAll()
	{
		// Act
		var result = WarningTypeHelper.UnparseWarnings(WarningType.All);

		// Assert
		await Assert.That(result).IsEqualTo("all");
	}

	[Test]
	public async Task UnparseWarnings_ExitUnlinked_ReturnsExitUnlinked()
	{
		// Act
		var result = WarningTypeHelper.UnparseWarnings(WarningType.ExitUnlinked);

		// Assert
		await Assert.That(result).IsEqualTo("exit-unlinked");
	}

	[Test]
	public async Task UnparseWarnings_MultipleFlags_ReturnsSpaceSeparated()
	{
		// Arrange - use unique name "warn-unparse-multiple"
		var flags = WarningType.ExitUnlinked | WarningType.ThingDesc;

		// Act
		var result = WarningTypeHelper.UnparseWarnings(flags);

		// Assert
		await Assert.That(result).Contains("exit-unlinked");
		await Assert.That(result).Contains("thing-desc");
	}

	[Test]
	public async Task UnparseWarnings_PrefersGroupNames_OverIndividual()
	{
		// Arrange - when all flags of a group are set, should show group name

		// Act
		var result = WarningTypeHelper.UnparseWarnings(WarningType.Normal);

		// Assert
		await Assert.That(result).IsEqualTo("normal");
		// Should not contain individual flag names
		await Assert.That(result).DoesNotContain("exit-unlinked");
	}

	[Test]
	public async Task ParseAndUnparse_RoundTrip_PreservesValue()
	{
		// Arrange - use unique names for round-trip test
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
			// Act
			var parsed = WarningTypeHelper.ParseWarnings(testCase);
			var unparsed = WarningTypeHelper.UnparseWarnings(parsed);
			var reparsed = WarningTypeHelper.ParseWarnings(unparsed);

			// Assert
			await Assert.That(parsed).IsEqualTo(reparsed);
		}
	}
}
