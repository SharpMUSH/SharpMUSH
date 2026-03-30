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
	[Skip("Requires configuring event_handler attribute on an object and asserting the handler executes (fires NotifyService or produces side effects)")]
	public async ValueTask TriggerEventWithHandler()
	{
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
