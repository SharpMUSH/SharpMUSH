using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class FlagAndPowerCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async ValueTask Flag_List_DisplaysAllFlags()
	{
		// Execute @flag/list
		await Parser.CommandParse(1, ConnectionService, MModule.single("@flag/list"));

		// Verify that a notification was sent with the flag list
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Object Flags:")));
	}

	[Test]
	public async ValueTask Flag_Add_CreatesNewFlag()
	{
		// Create a unique flag name for this test
		var flagName = $"TEST_FLAG_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		// Execute @flag/add
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));

		// Verify the flag was created by querying the database
		var createdFlag = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(createdFlag).IsNotNull();
		await Assert.That(createdFlag!.Name).IsEqualTo(flagName);
		await Assert.That(createdFlag.Symbol).IsEqualTo(symbol);
		await Assert.That(createdFlag.System).IsFalse();

		// Verify notification was sent
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains($"Flag '{flagName}' created")));

		// Cleanup - delete the flag
		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	[Test]
	public async ValueTask Flag_Add_PreventsSystemFlagCreation()
	{
		// Create a unique flag name
		var flagName = $"TEST_FLAG_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		// Execute @flag/add
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));

		// Verify the created flag is NOT a system flag
		var createdFlag = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(createdFlag).IsNotNull();
		await Assert.That(createdFlag!.System).IsFalse();

		// Cleanup
		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	[Test]
	public async ValueTask Flag_Add_PreventsDuplicateFlags()
	{
		// Create a unique flag name
		var flagName = $"TEST_FLAG_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		// Create the flag first time
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));
		
		// Clear received calls to reset NSubstitute tracking
		NotifyService.ClearReceivedCalls();

		// Try to create it again
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));

		// Verify error notification was sent
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("already exists")));

		// Cleanup
		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	[Test]
	public async ValueTask Flag_Delete_RemovesNonSystemFlag()
	{
		// Create a test flag first
		var flagName = $"TEST_FLAG_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		var createdFlag = await Mediator.Send(new CreateObjectFlagCommand(
			flagName, null, symbol, false,
			["FLAG^WIZARD"], ["FLAG^WIZARD"], ["PLAYER", "THING", "ROOM", "EXIT"]
		));
		await Assert.That(createdFlag).IsNotNull();

		// Execute @flag/delete
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/delete {flagName}"));

		// Verify the flag was deleted
		var deletedFlag = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(deletedFlag).IsNull();

		// Verify notification was sent
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains($"Flag '{flagName}' deleted")));
	}

	[Test]
	public async ValueTask Flag_Delete_PreventsSystemFlagDeletion()
	{
		// Try to delete a system flag (e.g., WIZARD)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@flag/delete WIZARD"));

		// Verify error notification was sent
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Cannot delete system flag")));
	}

	[Test]
	public async ValueTask Flag_Delete_HandlesNonExistentFlag()
	{
		// Try to delete a non-existent flag
		var flagName = "NONEXISTENT_FLAG_XYZ123";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/delete {flagName}"));

		// Verify error notification was sent
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("not found")));
	}

	[Test]
	public async ValueTask Power_List_DisplaysAllPowers()
	{
		// Execute @power/list
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power/list"));

		// Verify that a notification was sent with the power list
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Object Powers:")));
	}

	[Test]
	public async ValueTask Power_Add_CreatesNewPower()
	{
		// Create a unique power name for this test
		var powerName = $"TEST_POWER_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var alias = "TPOW";

		// Execute @power/add
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/add {powerName}={alias}"));

		// Verify the power was created by querying the database
		var createdPower = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(createdPower).IsNotNull();
		await Assert.That(createdPower!.Name).IsEqualTo(powerName);
		await Assert.That(createdPower.Alias).IsEqualTo(alias);
		await Assert.That(createdPower.System).IsFalse();

		// Verify notification was sent
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains($"Power '{powerName}' created")));

		// Cleanup - delete the power
		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	[Test]
	public async ValueTask Power_Add_PreventsSystemPowerCreation()
	{
		// Create a unique power name
		var powerName = $"TEST_POWER_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var alias = "TPOW";

		// Execute @power/add
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/add {powerName}={alias}"));

		// Verify the created power is NOT a system power
		var createdPower = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(createdPower).IsNotNull();
		await Assert.That(createdPower!.System).IsFalse();

		// Cleanup
		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	[Test]
	public async ValueTask Power_Delete_RemovesNonSystemPower()
	{
		// Create a test power first
		var powerName = $"TEST_POWER_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var alias = "TPOW";

		var createdPower = await Mediator.Send(new CreatePowerCommand(
			powerName, alias, false,
			["FLAG^WIZARD"], ["FLAG^WIZARD"], ["PLAYER"]
		));
		await Assert.That(createdPower).IsNotNull();

		// Execute @power/delete
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/delete {powerName}"));

		// Verify the power was deleted
		var deletedPower = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(deletedPower).IsNull();

		// Verify notification was sent
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains($"Power '{powerName}' deleted")));
	}

	[Test]
	public async ValueTask Power_Delete_PreventsSystemPowerDeletion()
	{
		// Try to delete a system power (e.g., BUILDER if it exists)
		// First check if BUILDER exists and is a system power
		var builderPower = await Mediator.Send(new GetPowerQuery("BUILDER"));
		if (builderPower != null && builderPower.System)
		{
			await Parser.CommandParse(1, ConnectionService, MModule.single("@power/delete BUILDER"));

			// Verify error notification was sent
			await NotifyService
				.Received(Quantity.Exactly(1))
				.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Cannot delete system power")));
		}
	}

	[Test]
	public async ValueTask Power_Delete_HandlesNonExistentPower()
	{
		// Try to delete a non-existent power
		var powerName = "NONEXISTENT_POWER_XYZ123";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/delete {powerName}"));

		// Verify error notification was sent
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("not found")));
	}

	[Test]
	public async ValueTask Flag_Add_RequiresBothArguments()
	{
		// Try to create a flag without symbol
		await Parser.CommandParse(1, ConnectionService, MModule.single("@flag/add TESTFLAG"));

		// Verify error notification was sent
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("requires flag name and symbol")));
	}

	[Test]
	public async ValueTask Power_Add_RequiresBothArguments()
	{
		// Try to create a power without alias
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power/add TESTPOWER"));

		// Verify error notification was sent
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("requires power name and alias")));
	}
}
