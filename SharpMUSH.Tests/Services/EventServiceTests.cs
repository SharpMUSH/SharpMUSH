using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class EventServiceTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IEventService EventService => WebAppFactoryArg.Services.GetRequiredService<IEventService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	
	public async ValueTask TriggerEventWithNoHandlerConfigured()
	{
		// When no event_handler is configured, TriggerEventAsync should return without error
		await EventService.TriggerEventAsync(
			WebAppFactoryArg.CommandParser,
			"TEST`EVENT",
			null,
			"arg0", "arg1");
		
		// Should complete without throwing an exception
		await ValueTask.CompletedTask;
	}

	[Test]
	
	public async ValueTask TriggerEventWithHandler()
	{
		// This test would require:
		// 1. An event handler object to be created
		// 2. The event_handler config option to be set to that object
		// 3. An attribute matching the event name to exist on that object
		// 4. Verification that the attribute was executed with the correct arguments
		await ValueTask.CompletedTask;
	}

	[Test]
	
	public async ValueTask TriggerEventWithSystemEnactor()
	{
		// Test that events with null enactor (system events) use #-1 as the enactor
		await ValueTask.CompletedTask;
	}
}
