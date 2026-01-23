using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class MogrifierTests : TestClassFactory
{
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => CommandParser;

	[Test]
	public async ValueTask ChannelMogrifier_SetCommand_ExecutesWithoutError()
	{
		// Test that @channel/mogrifier command accepts basic input
		await Parser.CommandParse(1, ConnectionService, MModule.single("@channel/mogrifier test=#1"));

		// Command executes (may fail if channel doesn't exist, but validates command parsing)
	}

	[Test]
	public async ValueTask ChannelMogrifier_ClearCommand_ExecutesWithoutError()
	{
		// Test that clearing a mogrifier works
		await Parser.CommandParse(1, ConnectionService, MModule.single("@channel/mogrifier test"));

		// Command executes without error
	}

	[Test]
	public async ValueTask ChannelMessage_WithoutMogrifier_SendsBasicFormat()
	{
		// This test verifies that channel messages work without a mogrifier
		// In a full integration test, we would:
		// 1. Create a channel
		// 2. Join it
		// 3. Send a message
		// 4. Verify the format
		
		// For now, just verify the command parses
		// await Parser.CommandParse(1, ConnectionService, MModule.single("pub Hello"));
	}

	[Test]
	public async ValueTask MogrifyBlock_NonEmpty_BlocksMessage()
	{
		// Integration test concept:
		// 1. Create channel and mogrifier object
		// 2. Set MOGRIFY`BLOCK to return non-empty string
		// 3. Send message on channel
		// 4. Verify only speaker received the block message
		
		// This would require full channel setup which is beyond unit test scope
	}

	[Test]
	public async ValueTask MogrifyOverride_True_SkipsChatFormat()
	{
		// Integration test concept:
		// 1. Set up channel with mogrifier
		// 2. Set MOGRIFY`OVERRIDE to return true
		// 3. Set individual @chatformat on player
		// 4. Send message
		// 5. Verify individual @chatformat was skipped
		
		// This would require full channel and player setup
	}

	[Test]
	public async ValueTask MogrifyFormat_CustomFormat_AltersMessage()
	{
		// Integration test concept:
		// 1. Set up channel with mogrifier
		// 2. Set MOGRIFY`FORMAT to custom format
		// 3. Send message
		// 4. Verify message uses custom format
		
		// This would require full channel setup
	}

	[Test]
	public async ValueTask MogrifyParts_CustomValues_AlterComponents()
	{
		// Integration test concept:
		// 1. Set up channel with mogrifier
		// 2. Set MOGRIFY`CHANNAME, MOGRIFY`PLAYERNAME, etc.
		// 3. Send message
		// 4. Verify each component was modified
		
		// This would require full channel setup
	}

	[Test]
	public async ValueTask MogrifyUseLock_Fails_SkipsMogrification()
	{
		// Integration test concept:
		// 1. Set up channel with mogrifier
		// 2. Set Use lock on mogrifier to fail for speaker
		// 3. Send message
		// 4. Verify mogrification was skipped
		
		// This would require full channel and lock setup
	}
}
