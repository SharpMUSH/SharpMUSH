using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class WarningCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IWarningService WarningService => WebAppFactoryArg.Services.GetRequiredService<IWarningService>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task WarningsCommand_SetToNormal()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - set warnings to normal on object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@warnings #1=normal"));

		// Assert - should notify user
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Warnings set to")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WarningsCommand_SetToAll()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - set warnings to all on object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@warnings #1=all"));

		// Assert - should notify user
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Warnings set to")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WarningsCommand_SetToNone()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - clear warnings on object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@warnings #1=none"));

		// Assert - should notify user about clearing
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "cleared") || TestHelpers.MessageContains(s, "none")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WarningsCommand_WithNegation()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - set warnings with negation
		await Parser.CommandParse(1, ConnectionService, MModule.single("@warnings #1=all !exit-desc"));

		// Assert - should notify user
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Warnings set to")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WarningsCommand_WithUnknownWarning()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - try to set unknown warning
		await Parser.CommandParse(1, ConnectionService, MModule.single("@warnings #1=unknown-warning"));

		// Assert - should notify about unknown warning
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Unknown warning")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WarningsCommand_NoArguments_ShowsUsage()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - call without arguments
		await Parser.CommandParse(1, ConnectionService, MModule.single("@warnings"));

		// Assert - should show usage
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Usage")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WCheckCommand_SpecificObject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - check warnings on specific object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wcheck #1"));

		// Assert - should complete check
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "@wcheck complete") || TestHelpers.MessageContains(s, "Warning")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task WCheckCommand_NoArguments_ShowsUsage()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - call without arguments
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wcheck"));

		// Assert - should show usage
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Usage")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Integration test - requires proper object setup")]
	public async Task WCheckCommand_WithMe_ChecksOwnedObjects()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - check owned objects
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wcheck/me"));

		// Assert - should complete check
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Checking objects")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Integration test - requires wizard permissions")]
	public async Task WCheckCommand_WithAll_RequiresWizard()
	{
		// This test would need to set up a wizard player
		await ValueTask.CompletedTask;
	}
}
