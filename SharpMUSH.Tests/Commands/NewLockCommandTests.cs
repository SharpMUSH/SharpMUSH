using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Tests for newly implemented PennMUSH lock commands
/// </summary>
public class NewLockCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask ELOCK_CommandExecutes()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create ELockTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@elock #{newDb.Number}=#TRUE"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask EUNLOCK_CommandExecutes()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create EUnlockTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@eunlock #{newDb.Number}"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask ULOCK_CommandExecutes()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create ULockTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@ulock #{newDb.Number}=#TRUE"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask UUNLOCK_CommandExecutes()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create UUnlockTestObject"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@uunlock #{newDb.Number}"));

		await Assert.That(result).IsNotNull();
	}
}
