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
	[Skip("Requires attribute setup to fully test")]
	public async ValueTask MessageBasic()
	{
		Console.WriteLine("Testing basic @message command");
		
		// Basic message with default message
		// This would work better with actual attribute setup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=Default message,TESTFORMAT"));

		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires attribute setup")]
	public async ValueTask MessageWithAttribute()
	{
		Console.WriteLine("Testing @message command with attribute evaluation");
		
		// This test would require setting up an attribute first
		// await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=Default,#0/TESTFORMAT,arg1,arg2"));
		
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires attribute setup to fully test")]
	public async ValueTask MessageSilentSwitch()
	{
		Console.WriteLine("Testing @message/silent");
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message/silent #1=Test,TESTFORMAT"));

		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires room setup")]
	public async ValueTask MessageRemitSwitch()
	{
		Console.WriteLine("Testing @message/remit");
		
		// This would require proper room setup to test room emission
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
	[Skip("Requires attribute setup to fully test")]
	public async ValueTask MessageNospoofSwitch()
	{
		Console.WriteLine("Testing @message/nospoof");
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message/nospoof #1=Test,TESTFORMAT"));

		await ValueTask.CompletedTask;
	}
}
