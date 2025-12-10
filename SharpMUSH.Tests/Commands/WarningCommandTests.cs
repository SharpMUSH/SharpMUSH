using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class WarningCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IWarningService WarningService => WebAppFactoryArg.Services.GetRequiredService<IWarningService>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task WarningsCommand_SetToNormal()
	{
		// Arrange - set warnings to normal on object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@warnings #1=normal"));

		// Assert - should notify user
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Warnings set to")),
				Arg.Any<AnySharpObject?>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WarningsCommand_SetToAll()
	{
		// Arrange - set warnings to all on object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@warnings #1=all"));

		// Assert - should notify user
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Warnings set to")),
				Arg.Any<AnySharpObject?>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WarningsCommand_SetToNone()
	{
		// Arrange - clear warnings on object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@warnings #1=none"));

		// Assert - should notify user about clearing
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("cleared") || s.Value.ToString()!.Contains("none")),
				Arg.Any<AnySharpObject?>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WarningsCommand_WithNegation()
	{
		// Arrange - set warnings with negation
		await Parser.CommandParse(1, ConnectionService, MModule.single("@warnings #1=all !exit-desc"));

		// Assert - should notify user
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Warnings set to")),
				Arg.Any<AnySharpObject?>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WarningsCommand_WithUnknownWarning()
	{
		// Arrange - try to set unknown warning
		await Parser.CommandParse(1, ConnectionService, MModule.single("@warnings #1=unknown-warning"));

		// Assert - should notify about unknown warning
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Unknown warning")),
				Arg.Any<AnySharpObject?>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WarningsCommand_NoArguments_ShowsUsage()
	{
		// Arrange - call without arguments
		await Parser.CommandParse(1, ConnectionService, MModule.single("@warnings"));

		// Assert - should show usage
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Usage")),
				Arg.Any<AnySharpObject?>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WCheckCommand_SpecificObject()
	{
		// Arrange - check warnings on specific object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wcheck #1"));

		// Assert - should complete check
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("@wcheck complete") || s.Value.ToString()!.Contains("Warning")),
				Arg.Any<AnySharpObject?>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WCheckCommand_NoArguments_ShowsUsage()
	{
		// Arrange - call without arguments
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wcheck"));

		// Assert - should show usage
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Usage")),
				Arg.Any<AnySharpObject?>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("Integration test - requires proper object setup")]
	public async Task WCheckCommand_WithMe_ChecksOwnedObjects()
	{
		// Arrange - check owned objects
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wcheck/me"));

		// Assert - should complete check
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Checking objects")),
				Arg.Any<AnySharpObject?>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("Integration test - requires wizard permissions")]
	public async Task WCheckCommand_WithAll_RequiresWizard()
	{
		// This test would need to set up a wizard player
		await ValueTask.CompletedTask;
	}
}
