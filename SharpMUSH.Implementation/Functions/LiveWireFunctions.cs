using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// RhostMUSH's <c>livewire(funceval, &lt;object&gt;[, &lt;owner too&gt;])</c> (<c>fun_livewire</c>,
	/// <c>src/functions.c</c>): the game's function invocation limit, then the object's <c>@livewire</c> limit and
	/// override, then, when asked, its owner's.
	/// </summary>
	[SharpFunction(Name = "livewire", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["livewire", "object", "owner-too"])]
	public async ValueTask<CallState> LiveWireFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		var kind = args["0"].Message.ToPlainText().Trim();
		if (kind.Length == 0)
		{
			return new CallState("Valid livewires: funceval");
		}

		var name = args.TryGetValue("1", out var target) ? target.Message.ToPlainText().Trim() : "";
		if (name.Length == 0)
		{
			return new CallState("#-1 TARGET NOT FOUND");
		}

		var withOwner = args.TryGetValue("2", out var toggle)
			&& int.TryParse(toggle.Message.ToPlainText().Trim(), out var number) && number != 0;

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await LocateService.Locate(parser, executor, executor, name, LocateFlags.All) is not AnySharpObject found)
		{
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		if (!await PermissionService.Controls(executor, found))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!"funceval".StartsWith(kind, StringComparison.OrdinalIgnoreCase))
		{
			return new CallState("#-1 INVALID LIVEWIRE ARGUMENT");
		}

		var token = ExecutionBudget.CurrentToken;
		var gameLimit = Configuration.CurrentValue.Limit.FunctionInvocationLimit;
		var own = await LiveWire.ReadAsync(Mediator, found.Object().DBRef, token);
		if (!withOwner)
		{
			return new CallState($"{gameLimit} {own}");
		}

		var owner = (await found.Object().Owner.WithCancellation(token)).Object.DBRef;
		var owners = owner.Number == found.Object().DBRef.Number ? own : await LiveWire.ReadAsync(Mediator, owner, token);
		return new CallState($"{gameLimit} {own} {owners}");
	}
}
