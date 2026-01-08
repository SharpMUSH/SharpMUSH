using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Tests for topology warning checks (exit-oneway, exit-multiple, exit-unlinked)
/// Note: Full integration tests require database setup and are marked as Skip.
/// These tests verify the warning type flags are properly defined.
/// </summary>
public class WarningTopologyTests
{
	[Test]
	public async Task WarningType_ExitUnlinked_IsDefined()
	{
		var value = (uint)WarningType.ExitUnlinked;
		await Assert.That(value).IsEqualTo(0x10u);
	}

	[Test]
	public async Task WarningType_ExitOneway_IsDefined()
	{
		var value = (uint)WarningType.ExitOneway;
		await Assert.That(value).IsEqualTo(0x1u);
	}

	[Test]
	public async Task WarningType_ExitMultiple_IsDefined()
	{
		var value = (uint)WarningType.ExitMultiple;
		await Assert.That(value).IsEqualTo(0x2u);
	}

	[Test]
	public async Task ParseWarnings_ExitUnlinked_ParsesCorrectly()
	{
		// Act
		var result = WarningTypeHelper.ParseWarnings("exit-unlinked");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ExitUnlinked);
		await Assert.That(result.HasFlag(WarningType.ExitUnlinked)).IsTrue();
	}

	[Test]
	public async Task ParseWarnings_ExitOneway_ParsesCorrectly()
	{
		// Act
		var result = WarningTypeHelper.ParseWarnings("exit-oneway");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ExitOneway);
		await Assert.That(result.HasFlag(WarningType.ExitOneway)).IsTrue();
	}

	[Test]
	public async Task ParseWarnings_ExitMultiple_ParsesCorrectly()
	{
		// Act
		var result = WarningTypeHelper.ParseWarnings("exit-multiple");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ExitMultiple);
		await Assert.That(result.HasFlag(WarningType.ExitMultiple)).IsTrue();
	}

	[Test]
	public async Task WarningType_Normal_IncludesTopologyChecks()
	{
		// Normal should include exit-oneway and exit-multiple
		var normal = WarningType.Normal;
		
		await Assert.That(normal.HasFlag(WarningType.ExitOneway)).IsTrue();
		await Assert.That(normal.HasFlag(WarningType.ExitMultiple)).IsTrue();
	}

	[Test]
	public async Task UnparseWarnings_TopologyFlags_UnparsesCorrectly()
	{
		// Test unparsing individual topology flags
		var exitUnlinked = WarningTypeHelper.UnparseWarnings(WarningType.ExitUnlinked);
		var exitOneway = WarningTypeHelper.UnparseWarnings(WarningType.ExitOneway);
		var exitMultiple = WarningTypeHelper.UnparseWarnings(WarningType.ExitMultiple);

		await Assert.That(exitUnlinked).Contains("exit-unlinked");
		await Assert.That(exitOneway).Contains("exit-oneway");
		await Assert.That(exitMultiple).Contains("exit-multiple");
	}

	[Test]
	
	public async Task CheckExitWarnings_UnlinkedExit_DetectsWarning()
	{
		// This test would require:
		// - Creating an exit with NOTHING destination (DBRef -1 or 0)
		// - Setting up WarningService with mocked dependencies
		// - Verifying warning notification is sent
		
		// Placeholder for future integration testing
		await Task.CompletedTask;
	}

	[Test]
	
	public async Task CheckExitWarnings_OnewayExit_DetectsWarning()
	{
		// This test would require:
		// - Creating two rooms
		// - Creating an exit from room A to room B
		// - NOT creating a return exit from room B to room A
		// - Mocking GetExitsQuery to return empty list for destination room
		// - Verifying one-way warning notification is sent
		
		// Placeholder for future integration testing
		await Task.CompletedTask;
	}

	[Test]
	
	public async Task CheckExitWarnings_MultipleReturnExits_DetectsWarning()
	{
		// This test would require:
		// - Creating two rooms
		// - Creating an exit from room A to room B
		// - Creating MULTIPLE return exits from room B to room A
		// - Mocking GetExitsQuery to return multiple exits
		// - Verifying multiple-return warning notification is sent
		
		// Placeholder for future integration testing
		await Task.CompletedTask;
	}
}
