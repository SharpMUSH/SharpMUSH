using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class SystemCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask FlagCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@flag/list"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask PowerCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power/list"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask HookCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hook/list"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask FunctionCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@function/list"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask CommandCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@command/list"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask HideCommand()
	{
		var testDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SystemTestHide");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hide {testDbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask KickCommand()
	{
		var testDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SystemTestKick");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@kick {testDbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask AttributeCommand()
	{
		var uniqueAttr = TestIsolationHelpers.GenerateUniqueName("SYSCMD_ATTRTEST").ToUpperInvariant();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@attribute/access {uniqueAttr}=wizard"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask AtrlockCommand()
	{
		var testDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SystemTestAtrlock");
		var uniqueAttr = TestIsolationHelpers.GenerateUniqueName("SYSCMD_ATRLOCKATTR").ToUpperInvariant();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@atrlock {testDbRef}/{uniqueAttr}=me"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask AtrchownCommand()
	{
		var sourceDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SystemTestAtrchownSrc");
		var targetPlayerDbRef = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "SystemTestAtrchownPly");
		var uniqueAttr = TestIsolationHelpers.GenerateUniqueName("SYSCMD_ATRCHOWNATTR").ToUpperInvariant();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@atrchown {sourceDbRef}/{uniqueAttr}={targetPlayerDbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask FirstexitCommand()
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName("SystemTestFirstexitRoom");
		var roomResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));
		var roomMessage = roomResult.Message?.ToPlainText()
			?? throw new InvalidOperationException($"@dig {roomName} returned a null message.");
		var roomDbRef = DBRef.Parse(roomMessage);

		var exitName = TestIsolationHelpers.GenerateUniqueName("SystemTestFirstexitExit");
		var exitResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@open {exitName}"));
		var exitMessage = exitResult.Message?.ToPlainText()
			?? throw new InvalidOperationException($"@open {exitName} returned a null message.");
		var exitDbRef = DBRef.Parse(exitMessage);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@firstexit {roomDbRef}={exitDbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
