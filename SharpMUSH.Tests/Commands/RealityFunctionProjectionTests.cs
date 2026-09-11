using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class RealityFunctionProjectionTests
{
	[ClassDataSource<RealityGameServerFactory>(Shared = SharedType.PerTestSession)]
	public required RealityGameServerFactory Factory { get; init; }
	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();

	[Test, NotInParallel]
	[Arguments("room", true)]
	[Arguments("room", false)]
	[Arguments("inventory", true)]
	[Arguments("inventory", false)]
	[Arguments("globals", true)]
	[Arguments("globals", false)]
	public async Task ScanDoesNotExposeHiddenCommandReferences(string scope, bool enabled)
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var owner = (await objects.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var room = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("function scan room", owner)))).Expect<SharpRoom>();
		var actor = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("function scanner", room, owner, room)))).Expect<SharpThing>();
		AnySharpContainer container = scope == "inventory" ? actor : scope == "globals"
			? (await objects.GetObjectNodeAsync(new DBRef(0))).Expect<SharpRoom>() : room;
		var visible = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("visible command", container, owner, room)))).Expect<SharpThing>();
		var hidden = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("hidden command", container, owner, room)))).Expect<SharpThing>();
		var command = "scanprojection" + Guid.NewGuid().ToString("N");
		try
		{
			await policy.SaveConfigurationAsync(new(1, false, ["normal", "ghost"]), default);
			var setup = Factory.CommandParser.FromState(ParserState.RootFor(owner.Object.DBRef));
			foreach (var target in new[] { visible, hidden })
			{
				await setup.CommandListParse(MarkupText.Plain($"@set {target.Object.DBRef}=!NO_COMMAND"));
				await setup.CommandListParse(MarkupText.Plain($"&CMD {target.Object.DBRef}=${command}:@pemit me=matched"));
			}
			await policy.SaveObjectAsync(hidden.Object.Id!, ObjectReality.Default(hidden.Object.DBRef) with { Transmit = ["ghost"] }, default);
			await policy.SaveConfigurationAsync(new(1, enabled, ["normal", "ghost"]), default);
			var result = await Factory.FunctionParser.FromState(ParserState.RootFor(actor.Object.DBRef))
				.FunctionParse(MarkupText.Plain($"scan(me,{command},{scope})"));
			var output = result!.Message!.ToPlainText();
			await Assert.That(output.Contains($"{visible.Object.DBRef}/CMD")).IsTrue();
			await Assert.That(output.Contains($"{hidden.Object.DBRef}/CMD")).IsEqualTo(!enabled);
		}
		finally { await policy.SaveConfigurationAsync(original, default); }
	}

	[Test, NotInParallel]
	[Arguments(false, true)]
	[Arguments(false, false)]
	[Arguments(true, true)]
	[Arguments(true, false)]
	public async Task RNumExcludesHiddenMatchesBeforeResolvingAmbiguity(bool ambiguous, bool enabled)
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var owner = (await objects.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var room = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("rnum projection room", owner)))).Expect<SharpRoom>();
		var actor = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("rnum observer", room, owner, room)))).Expect<SharpThing>();
		var visible = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("projection visible", room, owner, room)))).Expect<SharpThing>();
		var hidden = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("projection hidden", room, owner, room)))).Expect<SharpThing>();
		try
		{
			await policy.SaveObjectAsync(hidden.Object.Id!, ObjectReality.Default(hidden.Object.DBRef) with { Transmit = ["ghost"] }, default);
			await policy.SaveConfigurationAsync(new(1, enabled, ["normal", "ghost"]), default);
			var result = await Factory.FunctionParser.FromState(ParserState.RootFor(actor.Object.DBRef))
				.FunctionParse(MarkupText.Plain($"rnum({room.Object.DBRef},{(ambiguous ? "projection" : hidden.Object.Name)})"));
			var expected = ambiguous ? enabled ? $"#{visible.Object.DBRef.Number}" : "#-2"
				: enabled ? "#-1" : $"#{hidden.Object.DBRef.Number}";
			await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
		}
		finally { await policy.SaveConfigurationAsync(original, default); }
	}
}
