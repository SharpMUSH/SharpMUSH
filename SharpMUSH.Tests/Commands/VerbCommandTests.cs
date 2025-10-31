using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class VerbCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	[Skip("Requires object and attribute setup")]
	public async ValueTask VerbBasic()
	{
		Console.WriteLine("Testing basic @verb command");
		
		// @verb <victim>=<actor>,<what>,<whatd>,<owhat>,<owhatd>,<awhat>
		// This test would require:
		// 1. Creating a victim object
		// 2. Setting attributes WHAT, OWHAT, AWHAT on it
		// 3. Having proper permissions
		
		await ValueTask.CompletedTask;
	}

	[Test]
	public async ValueTask VerbInsufficientArgs()
	{
		Console.WriteLine("Testing @verb with insufficient arguments");
		
		// Should fail with too few arguments - but command will still parse
		// The implementation will send a usage notification
		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=#2"));

		// Just verify it completes without crashing
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires proper permission setup")]
	public async ValueTask VerbPermissionDenied()
	{
		Console.WriteLine("Testing @verb permission denied");
		
		// This would require setting up objects where executor doesn't have control
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires attribute setup")]
	public async ValueTask VerbWithDefaultMessages()
	{
		Console.WriteLine("Testing @verb with default messages when attributes don't exist");
		
		// When victim doesn't have WHAT, should use WHATD default
		// When victim doesn't have OWHAT, should use OWHATD default
		
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires stack argument support")]
	public async ValueTask VerbWithStackArgs()
	{
		Console.WriteLine("Testing @verb with stack arguments");
		
		// @verb <victim>=<actor>,<what>,<whatd>,<owhat>,<owhatd>,<awhat>,arg1,arg2,arg3
		// Arguments should be available as %0, %1, %2 in the attribute evaluation
		
		await ValueTask.CompletedTask;
	}
}
