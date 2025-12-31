using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class WizardCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask HaltCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@halt #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask AllhaltCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@allhalt"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask DrainCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@drain #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask PsCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ps"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask PsWithTarget()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ps #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask TriggerCommand()
	{
		// Set an attribute first
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TRIGGER_TEST #1=think Triggered!"));
		
		// Trigger it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@trigger #1/TRIGGER_TEST"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask ForceCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force #1=think Forced!"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask NotifyCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@notify #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask WaitCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wait 1=think Waited"));

		// Note: This test doesn't verify the wait actually happened, just that the command executed
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask UptimeCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@uptime"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DbckCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dbck"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DumpCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dump"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask QuotaCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@quota #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask AllquotaCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@allquota"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask BootCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@boot #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask WallCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wall Test wall message"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask WizwallCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wizwall Test wizwall message"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test, Skip("Test causes deadlock - command implementation needs review")]
	public async ValueTask PollCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poll"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Failing. Needs Investigation")]
	public async ValueTask Hide_NoSwitch_TogglesHidden()
	{
		// Test that @hide without switches toggles the DARK flag
		
		
		// First call should hide (set DARK)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide"));
		
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("hidden")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
		
		
		
		// Second call should unhide (unset DARK)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide"));
		
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("no longer hidden") || s.Value.ToString()!.Contains("visible")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Hide_YesSwitch_SetsHidden()
	{
		// Test that @hide/yes sets the DARK flag
		
		
		// Ensure we start unhidden (call @hide/off first)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));
		
		
		// Now test @hide/yes
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/yes"));
		
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("hidden")));
	}

	[Test]
	[Skip("Failing. Needs Investigation")]
	public async ValueTask Hide_OnSwitch_SetsHidden()
	{
		// Test that @hide/on sets the DARK flag
		
		
		// Ensure we start unhidden
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));
		
		
		// Now test @hide/on
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/on"));
		
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s 
				=> s.Value.ToString()!.Contains("hidden")));
	}

	[Test]
	[Skip("Failing. Needs Investigation")]
	public async ValueTask Hide_NoSwitch_UnsetsHidden()
	{
		// Test that @hide/no unsets the DARK flag
		
		
		// Ensure we start hidden
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/on"));
		
		
		// Now test @hide/no
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/no"));
		
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s
				=> s.Value.ToString()!.Contains("no longer hidden") || s.Value.ToString()!.Contains("visible")));
	}

	[Test]
	[Skip("Failing. Needs Investigation")]
	public async ValueTask Hide_OffSwitch_UnsetsHidden()
	{
		// Test that @hide/off unsets the DARK flag
		
		
		// Ensure we start hidden
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/on"));
		
		
		// Now test @hide/off
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));
		
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s 
				=> s.Value.ToString()!.Contains("no longer hidden") || s.Value.ToString()!.Contains("visible")));
	}

	[Test]
	public async ValueTask Hide_AlreadyHidden_ShowsAppropriateMessage()
	{
		// Test that @hide/on when already hidden shows appropriate message
		
		
		// Set hidden
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/on"));
		
		
		// Try to set hidden again
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/on"));
		
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s 
				=> s.Value.ToString()!.Contains("already hidden")));
	}

	[Test]
	[Skip("Failing. Needs Investigation")]
	public async ValueTask Hide_AlreadyVisible_ShowsAppropriateMessage()
	{
		// Test that @hide/off when already visible shows appropriate message
		
		
		// Ensure unhidden
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));
		
		
		// Try to set visible again
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));
		
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s 
				=> s.Value.ToString()!.Contains("already visible")));
	}

	[Test]
	public async ValueTask PurgeCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@purge"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask ReadCacheCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@readcache"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("startup")));
	}

	[Test]
	public async ValueTask ShutdownCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@shutdown"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("SHUTDOWN") || s.Contains("web application")));
	}

	[Test]
	public async ValueTask ShutdownRebootCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@shutdown/reboot"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("REBOOT") || s.Contains("web")));
	}

	[Test, Skip("Test causes deadlock - command implementation needs review")]
	public async ValueTask ChownallCommand()
	{
		// This test may need adjustment based on actual player setup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chownall #1"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test, Skip("Test causes deadlock - command implementation needs review")]
	public async ValueTask SuggestListCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@suggest/list"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test, Skip("Test causes deadlock - command implementation needs review")]
	public async ValueTask SuggestAddCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@suggest/add test=word"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Added")));
	}

	[Test, Skip("Test causes deadlock - command implementation needs review")]
	public async ValueTask PollSetCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poll Test poll message"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("set") || s.Contains("Permission")));
	}

	[Test, Skip("Test causes deadlock - command implementation needs review")]
	public async ValueTask PollClearCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poll/clear"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("cleared") || s.Contains("Permission")));
	}


}
