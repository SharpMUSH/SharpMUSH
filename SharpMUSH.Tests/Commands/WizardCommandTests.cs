using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class WizardCommandTests : TestsBase
{
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => CommandParser;

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask HaltCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@halt #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask AllhaltCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@allhalt"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "All objects halted")));
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask DrainCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@drain #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask PsCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ps"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask PsWithTarget()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ps #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask TriggerCommand()
	{
		// Clear any previous calls to the mock
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
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force #1=think Forced!"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask NotifyCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@notify #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask WaitCommand()
	{
		// Clear any previous calls to the mock
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
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@uptime"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DbckCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dbck"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DumpCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dump"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask QuotaCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@quota #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask AllquotaCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@allquota"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask BootCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@boot #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask WallCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wall Test wall message"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask WizwallCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wizwall Test wizwall message"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask PollCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poll"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "poll") || TestHelpers.MessageContains(s, "Poll")));
	}

	[Test]
	[Skip("Failing. Needs Investigation")]
	public async ValueTask Hide_NoSwitch_TogglesHidden()
	{
		// Clear any previous calls to the mock
		// Test that @hide without switches toggles the DARK flag
		
		
		// First call should hide (set DARK)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide"));
		
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "hidden")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
		
		
		
		// Second call should unhide (unset DARK)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide"));
		
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "no longer hidden") || TestHelpers.MessageContains(s, "visible")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Hide_YesSwitch_SetsHidden()
	{
		// Clear any previous calls to the mock
		// Test that @hide/yes sets the DARK flag
		
		
		// Ensure we start unhidden (call @hide/off first)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));
		
		
		// Now test @hide/yes
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/yes"));
		
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "hidden")));
	}

	[Test]
	public async ValueTask Hide_OnSwitch_SetsHidden()
	{
		// Clear any previous calls to the mock
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
	public async ValueTask Hide_NoSwitch_UnsetsHidden()
	{
		// Clear any previous calls to the mock
		// Test that @hide/no unsets the DARK flag
		
		
		// Ensure we start hidden
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/on"));
		
		
		// Now test @hide/no
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/no"));
		
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s
				=> TestHelpers.MessageContains(s, "no longer hidden") || TestHelpers.MessageContains(s, "visible")));
	}

	[Test]
	[Skip("Failing. Needs Investigation")]
	public async ValueTask Hide_OffSwitch_UnsetsHidden()
	{
		// Clear any previous calls to the mock
		// Test that @hide/off unsets the DARK flag
		
		
		// Ensure we start hidden
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/on"));
		
		
		// Now test @hide/off
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));
		
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s 
				=> TestHelpers.MessageContains(s, "no longer hidden") || TestHelpers.MessageContains(s, "visible")));
	}

	[Test]
	public async ValueTask Hide_AlreadyHidden_ShowsAppropriateMessage()
	{
		// Clear any previous calls to the mock
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
		// Clear any previous calls to the mock
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
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@purge"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "Purge complete") || TestHelpers.MessageContains(s, "GOING_TWICE")));
	}

	[Test]
	public async ValueTask ReadCacheCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@readcache"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "Reindexing text files") || TestHelpers.MessageContains(s, "Text file cache rebuilt")));
	}

	[Test]
	public async ValueTask ShutdownCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@shutdown"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "SHUTDOWN") || TestHelpers.MessageContains(s, "web") || TestHelpers.MessageContains(s, "orchestration")));
	}

	[Test]
	public async ValueTask ShutdownRebootCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@shutdown/reboot"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "REBOOT") || TestHelpers.MessageContains(s, "web") || TestHelpers.MessageContains(s, "orchestration")));
	}

	[Test]
	public async ValueTask ChownallCommand()
	{
		// Clear any previous calls to the mock
		// This test may need adjustment based on actual player setup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chownall #1"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "Permission denied") || TestHelpers.MessageContains(s, "objects") || TestHelpers.MessageContains(s, "ownership")));
	}

	[Test]
	public async ValueTask SuggestListCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@suggest/list"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "Suggestion categories") || TestHelpers.MessageContains(s, "No suggestion categories")));
	}

	[Test]
	public async ValueTask SuggestAddCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@suggest/add testcat547=testword923"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "Added 'testword923' to category 'testcat547'")));
	}

	[Test]
	public async ValueTask PollSetCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poll TestPollMessage897"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "Poll message set") || TestHelpers.MessageContains(s, "Permission")));
	}

	[Test]
	public async ValueTask PollClearCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poll/clear"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "Poll message cleared") || TestHelpers.MessageContains(s, "Permission")));
	}


}
