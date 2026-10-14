using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <see cref="IEventService.HasHandlerAsync"/>: whether the event handler (#9) answers an event, through
/// the event's own attribute or a package's child of it. The portal's Report button asks it. Each test
/// uses an event name of its own on the shared #9.
/// </summary>
public class EventHandlerPresenceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IEventService EventService => WebAppFactoryArg.Services.GetRequiredService<IEventService>();

	private Task Cmd(string command) =>
		WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private static string EventName() => TestIsolationHelpers.GenerateUniqueName("PRESENCE").ToUpperInvariant();

	[Test]
	public async Task NoAttribute_IsNoHandler()
	{
		await Assert.That(await EventService.HasHandlerAsync(EventName())).IsFalse();
	}

	[Test]
	public async Task TheEventsOwnAttribute_IsAHandler()
	{
		var name = EventName();
		await Cmd($"&{name} #9=think handled");

		await Assert.That(await EventService.HasHandlerAsync(name)).IsTrue();
		await Cmd($"&{name} #9");
	}

	[Test]
	public async Task APackagesChild_IsAHandler()
	{
		var name = EventName();
		await Cmd($"&{name}`JOBS #9=think handled");

		await Assert.That(await EventService.HasHandlerAsync(name)).IsTrue();
		await Cmd($"&{name}`JOBS #9");
	}

	[Test]
	public async Task TheEmptyBranchLeftByAClearedChild_IsNoHandler()
	{
		var name = EventName();
		await Cmd($"&{name}`JOBS #9=think handled");
		await Cmd($"&{name}`JOBS #9");

		await Assert.That(await EventService.HasHandlerAsync(name)).IsFalse();
	}
}
