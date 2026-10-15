using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json.Nodes;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>PennMUSH's <c>AMBIGUOUS</c>, which <c>loc()</c> answers for a variable exit as <c>#-2</c>.</summary>
	private const int VariableDestinationNumber = -2;

	/// <summary>PennMUSH's <c>NOTHING</c>: an exit that leads nowhere.</summary>
	private const int UnlinkedDestinationNumber = -1;

	/// <summary>
	/// <c>exitmap(&lt;room&gt;[, &lt;viewer&gt;])</c>: a JSON object of each exit the viewer can see in the
	/// room, by name, to the number of the room it leads to — what a client's mapper wants, and what the
	/// GMCP Room.Info package's <c>exits</c> holds.
	/// </summary>
	/// <remarks>
	/// The exits are the ones <c>look</c> lists to the viewer, decided by the same
	/// <see cref="WorldVisibility"/> scan (DARK, LIGHT and reality layers), so a package never works
	/// visibility out again. The room has to pass the gate <c>lexits()</c> has, and seeing it as someone
	/// else needs the executor to control that viewer or to pass the gate itself.
	/// </remarks>
	[SharpFunction(Name = "exitmap", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["room", "viewer"])]
	public async ValueTask<CallState> ExitMap(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var (executor, enactor, room, failure) = await WalkTarget(parser, Arg(parser, "0"));
		if (room is null) return failure;
		if (!room.IsRoom) return new CallState(ErrorMessages.Returns.NotARoom);

		var mayWalk = await WalkGate(executor, enactor, room, WalkType.Exit);

		if (!parser.CurrentState.Arguments.ContainsKey("1"))
		{
			return mayWalk ? await ExitMapAsync(parser, executor, room) : Refused;
		}

		var maybeViewer = await LocateService.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, Arg(parser, "1"), LocateFlags.All);

		return maybeViewer switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject viewer when mayWalk || await PermissionService.Controls(executor, viewer)
				=> await ExitMapAsync(parser, viewer, room),
			_ => Refused
		};
	}

	private async ValueTask<CallState> ExitMapAsync(IMUSHCodeParser parser, AnySharpObject viewer, AnySharpObject room)
	{
		if (room.AsOptionalContainer is not AnySharpContainer container) return new CallState(ErrorMessages.Returns.NotARoom);

		var canSee = await WorldVisibility.CreateScanAsync(viewer, room,
			parser.ServiceProvider.GetRequiredService<IRealityPolicy>(), ConnectionService, ExecutionBudget.CurrentToken);
		var god = await HelperFunctions.GetGod(Mediator);
		var map = new JsonObject();

		await foreach (var item in container.Content(Mediator).WithCancellation(ExecutionBudget.CurrentToken))
		{
			if (!item.IsExit || item is not SharpExit exit || !await canSee(item, ExecutionBudget.CurrentToken)) continue;

			// An exit's name carries its aliases after the first ';'; the first is the one look shows.
			var name = exit.Object.Name.Split(';')[0];

			// Two exits of one name: the first, as typing the name would take it.
			if (map.ContainsKey(name)) continue;

			map[name] = await ExitDestinationNumberAsync(god, viewer, exit);
		}

		return new CallState(map.ToJsonString());
	}

	/// <summary>
	/// The number of the room an exit leads the viewer to: the viewer's own home for an exit linked to
	/// HOME, as look sees through one; -2 for a variable exit, whose destination is only known when
	/// someone moves through it; -1 for an unlinked exit.
	/// </summary>
	private async ValueTask<int> ExitDestinationNumberAsync(AnySharpObject god, AnySharpObject viewer, SharpExit exit)
	{
		var linkType = await AttributeService.GetAttributeAsync(god, new AnySharpObject(exit), TeleportHelpers.AttrLinkType,
			IAttributeService.AttributeMode.Read, false) is SharpAttribute[] { Length: > 0 } chain
			? chain[0].Value.ToPlainText().Trim()
			: string.Empty;

		if (linkType.Equals(TeleportHelpers.LinkTypeVariable, StringComparison.OrdinalIgnoreCase))
		{
			return VariableDestinationNumber;
		}

		var destination = linkType.Equals(TeleportHelpers.LinkTypeHome, StringComparison.OrdinalIgnoreCase)
			? await HomeOfAsync(viewer)
			: await exit.Home.WithCancellation(CancellationToken.None);

		return destination is AnySharpContainer found ? found.Object().DBRef.Number : UnlinkedDestinationNumber;
	}

	private static async ValueTask<AnyOptionalSharpContainer> HomeOfAsync(AnySharpObject viewer)
	{
		if (viewer.AsOptionalContent is not AnySharpContent content) return new None();
		return await content.Home();
	}
}
