using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Tests for newly implemented PennMUSH lock commands
/// </summary>
[NotInParallel]
public class NewLockCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

	[Test]
	public async ValueTask ELOCK_CommandExecutes()
	{
		var testPlayer = await CreateTestPlayerAsync("ELock");

		// Create a test object
		var createResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create ELockTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Execute @elock command - should not throw
		var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@elock #{newDb.Number}=#TRUE"));

		// Command should execute without error
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask EUNLOCK_CommandExecutes()
	{
		var testPlayer = await CreateTestPlayerAsync("EUnlock");

		// Create a test object
		var createResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create EUnlockTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Execute @eunlock command - should not throw
		var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@eunlock #{newDb.Number}"));

		// Command should execute without error
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask ULOCK_CommandExecutes()
	{
		var testPlayer = await CreateTestPlayerAsync("ULock");

		// Create a test object
		var createResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create ULockTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Execute @ulock command - should not throw
		var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@ulock #{newDb.Number}=#TRUE"));

		// Command should execute without error
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask UUNLOCK_CommandExecutes()
	{
		var testPlayer = await CreateTestPlayerAsync("UUnlock");

		// Create a test object
		var createResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create UUnlockTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Execute @uunlock command - should not throw
		var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@uunlock #{newDb.Number}"));

		// Command should execute without error
		await Assert.That(result).IsNotNull();
	}
}
