using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class AttributeCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	[Test]
	public async ValueTask SetAttributeBasic()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TEST #1=Test Value"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());

		// Verify attribute was set
		var obj = await Mediator.Send(new GetObjectNodeQuery(new(1)));
		var attr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "TEST",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue();
	}

	[Test]
	public async ValueTask SetAttributeEmpty()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTCLEAR #1="));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask SetAttributeComplexValue()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&COMPLEX #1=This is a [add(1,2)] test"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask CopyAttribute()
	{
		// First set an attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single("&SOURCE #1=Source Value"));
		
		// Copy it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@cpattr #1/SOURCE=#1/DEST"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask MoveAttribute()
	{
		// First set an attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single("&MOVESOURCE #1=Move Value"));
		
		// Move it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mvattr #1/MOVESOURCE=#1/MOVEDEST"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask WipeAttributes()
	{
		// Set some attributes
		await Parser.CommandParse(1, ConnectionService, MModule.single("&WIPE1 #1=Value1"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&WIPE2 #1=Value2"));
		
		// Wipe them
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wipe #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
