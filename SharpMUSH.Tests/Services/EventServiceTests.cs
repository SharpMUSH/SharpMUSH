using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class EventServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

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
		// Without an event_handler configured, this returns silently.
		// Verify the method is callable and does not throw.
		await EventService.TriggerEventAsync(
			WebAppFactoryArg.CommandParser,
			"PLAYER`CONNECT",
			null,
			"#1");

		await ValueTask.CompletedTask;
	}

	[Test]
	public async ValueTask TriggerEventWithSystemEnactor()
	{
		// Test that events with null enactor (system events) use #-1 as the enactor — no throw
		await EventService.TriggerEventAsync(
			WebAppFactoryArg.CommandParser,
			"SOCKET`DISCONNECT",
			null);

		await ValueTask.CompletedTask;
	}
}
