using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class MessageCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask MessageBasic()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGBASIC #1=Formatted: %0"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=Default message,TESTFORMAT_MSGBASIC,TestArg"));
		await ValueTask.CompletedTask;
	}

	[Test]
	public async ValueTask MessageWithAttribute()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGATTR #1=Custom format: [add(%0,%1)]"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=Default,#1/TESTFORMAT_MSGATTR,5,10"));
		await ValueTask.CompletedTask;
	}

	[Test]
	public async ValueTask MessageSilentSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGSILENT #1=Silent: %0"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message/silent #1=Test,TESTFORMAT_MSGSILENT,TestValue"));
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires room setup")]
	public async ValueTask MessageRemitSwitch()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires multiple objects")]
	public async ValueTask MessageOemitSwitch()
	{
		Console.WriteLine("Testing @message/oemit");
		
		// This would require multiple objects in a room to test oemit
		await ValueTask.CompletedTask;
	}

	[Test]
	public async ValueTask MessageNospoofSwitch()
	{
		Console.WriteLine("Testing @message/nospoof");
		
		// Set up the attribute for this test with a unique name
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGNOSPOOF #1=Nospoof: %0"));
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message/nospoof #1=Test,TESTFORMAT_MSGNOSPOOF,TestValue"));

		await ValueTask.CompletedTask;
	}
}
