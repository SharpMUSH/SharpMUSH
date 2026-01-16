using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Tests for newly implemented PennMUSH lock commands
/// </summary>
public class NewLockCommandTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private INotifyService NotifyService => Factory.NotifyService;
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.CommandParser;
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask ELOCK_CommandExecutes()
	{
		// Create a test object
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ELockTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Execute @elock command - should not throw
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@elock #{newDb.Number}=#TRUE"));
		
		// Command should execute without error
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask EUNLOCK_CommandExecutes()
	{
		// Create a test object
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create EUnlockTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Execute @eunlock command - should not throw
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@eunlock #{newDb.Number}"));
		
		// Command should execute without error
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask ULOCK_CommandExecutes()
	{
		// Create a test object
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ULockTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Execute @ulock command - should not throw
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@ulock #{newDb.Number}=#TRUE"));
		
		// Command should execute without error
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask UUNLOCK_CommandExecutes()
	{
		// Create a test object
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create UUnlockTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Execute @uunlock command - should not throw
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@uunlock #{newDb.Number}"));
		
		// Command should execute without error
		await Assert.That(result).IsNotNull();
	}
}
