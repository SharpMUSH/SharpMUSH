using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
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

	/// <summary>Evaluates <paramref name="expression"/> once the events queued so far have run.</summary>
	private async Task<string> Eval(string expression)
	{
		await WebAppFactoryArg.QueueBarrierAsync();
		return (await WebAppFactoryArg.FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();
	}

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

	[Test]
	public async Task TriggeringRunsTheEventsOwnAttributeAndEachChild()
	{
		var name = EventName();
		var marker = TestIsolationHelpers.GenerateUniqueName("PresenceRun");
		await Cmd($"&{name} #9=&RAN_OWN_[secure(%0)] #9=own");
		await Cmd($"&{name}`JOBS #9=&RAN_JOBS_[secure(%0)] #9=jobs");

		await EventService.TriggerEventAsync(name, new DBRef(1), marker);

		await Assert.That(await Eval($"get(#9/RAN_OWN_{marker})")).IsEqualTo("own");
		await Assert.That(await Eval($"get(#9/RAN_JOBS_{marker})")).IsEqualTo("jobs");
		await Cmd($"&{name}`JOBS #9");
		await Cmd($"&{name} #9");
		await Cmd($"&RAN_OWN_{marker} #9");
		await Cmd($"&RAN_JOBS_{marker} #9");
	}
}
