using System.Globalization;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// RhostMUSH's <c>@livewire</c> (<c>do_livewire</c>, <c>src/wiz.c</c>): a function invocation limit for one
/// object, or every object of one owner, in place of <c>function_invocation_limit</c> (<see cref="LiveWire"/>).
/// </summary>
public partial class Commands
{
	[SharpCommand(Name = "@LIVEWIRE", Switches = ["LIST", "FUNCEVAL", "FUNCOVER"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "value"])]
	public async ValueTask<Option<CallState>> LiveWireCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await PermissionService.Check(executor, Requires.Any(PortalPermission.ConfigAdmin)) is PermissionRefusal refused)
		{
			return await NotifyService.PermissionDenied(executor, refused);
		}

		var name = PlainArgument(parser, "0").Trim();
		if (name.Length == 0)
		{
			await NotifyService.Notify(executor, "@livewire: I don't see that target.");
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		var switches = parser.CurrentState.Switches;
		var operation = switches.Contains("LIST") ? "LIST"
			: switches.Contains("FUNCEVAL") ? "FUNCEVAL"
			: switches.Contains("FUNCOVER") ? "FUNCOVER"
			: null;
		if (operation is null)
		{
			await NotifyService.Notify(executor, "@livewire: switch expected.");
			return new CallState("#-1 SWITCH EXPECTED");
		}

		var located = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, LocateFlags.All);
		if (located is not AnySharpObject target)
		{
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		var value = parser.CurrentState.Arguments.ContainsKey("1") ? PlainArgument(parser, "1").Trim() : "";
		var token = ExecutionBudget.CurrentToken;
		return operation switch
		{
			"LIST" => await LiveWireListAsync(executor, target, token),
			"FUNCEVAL" => await LiveWireFuncEvalAsync(executor, target, value, token),
			_ => await LiveWireFuncOverAsync(executor, target, value, token)
		};
	}

	private async ValueTask<CallState> LiveWireListAsync(AnySharpObject executor, AnySharpObject target, CancellationToken token)
	{
		var wire = await LiveWire.ReadAsync(Mediator, target.Object().DBRef, token);
		var owner = (await target.Object().Owner.WithCancellation(token)).Object;
		var line = owner.DBRef.Number == target.Object().DBRef.Number
			? $"@livewire: Target {Described(target)} -- FuncEval {wire.Limit}, FuncEvalOverride {(wire.Override ? 1 : 0)}"
			: $"@livewire: Target {Described(target)} [Owner: {owner.Name}(#{owner.DBRef.Number})] -- FuncEval {wire.Limit}, FuncEvalOverride {(wire.Override ? 1 : 0)}";
		await NotifyService.Notify(executor, line);
		return new CallState(wire.ToString());
	}

	private async ValueTask<CallState> LiveWireFuncEvalAsync(AnySharpObject executor, AnySharpObject target, string value,
		CancellationToken token)
	{
		uint limit;
		if (value.Length == 0)
		{
			await NotifyService.Notify(executor, "@livewire: No value specified, resetting func evaluation to default value (0)");
			limit = 0;
		}
		else if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var asked))
		{
			await NotifyService.Notify(executor, "@livewire: The value must be a whole number.");
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}
		else if (asked < 0)
		{
			await NotifyService.Notify(executor, "@livewire: Warning -- Negative values not allowed. value reset to 0");
			limit = 0;
		}
		else if (asked > LiveWire.MaximumLimit)
		{
			await NotifyService.Notify(executor, "@livewire: Warning -- Value greater than 1,000,000,000. Reset to max");
			limit = LiveWire.MaximumLimit;
		}
		else
		{
			limit = (uint)asked;
		}

		var wire = await LiveWire.ReadAsync(Mediator, target.Object().DBRef, token);
		if (wire.Limit == limit)
		{
			await NotifyService.Notify(executor, "@livewire: Evaluation value is same as previous value. Ignoring.");
			return new CallState(wire.ToString());
		}

		await WriteLiveWireAsync(target, wire with { Limit = limit }, token);
		await NotifyService.Notify(executor, $"@livewire: Target {Described(target)} FuncEval changed [{wire.Limit} -> {limit}]");
		return new CallState((wire with { Limit = limit }).ToString());
	}

	private async ValueTask<CallState> LiveWireFuncOverAsync(AnySharpObject executor, AnySharpObject target, string value,
		CancellationToken token)
	{
		bool overriding;
		if (value.Length == 0)
		{
			await NotifyService.Notify(executor, "@livewire: No value specified, resetting func override to default value (0)");
			overriding = false;
		}
		else if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var asked))
		{
			await NotifyService.Notify(executor, "@livewire: The value must be a whole number.");
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}
		else if (asked < 0)
		{
			await NotifyService.Notify(executor, "@livewire: Warning -- Negative values not allowed. value reset to 0");
			overriding = false;
		}
		else
		{
			overriding = asked != 0;
		}

		var wire = await LiveWire.ReadAsync(Mediator, target.Object().DBRef, token);
		if (wire.Override == overriding)
		{
			await NotifyService.Notify(executor, "@livewire: Override value is same as previous value. Ignoring.");
			return new CallState(wire.ToString());
		}

		await WriteLiveWireAsync(target, wire with { Override = overriding }, token);
		await NotifyService.Notify(executor,
			$"@livewire: Target {Described(target)} FuncOverride changed [{(wire.Override ? 1 : 0)} -> {(overriding ? 1 : 0)}]");
		return new CallState((wire with { Override = overriding }).ToString());
	}

	/// <summary>Stores <paramref name="wire"/> on <paramref name="target"/>, or clears it once both numbers are 0.</summary>
	private async ValueTask WriteLiveWireAsync(AnySharpObject target, LiveWire wire, CancellationToken token)
	{
		// Cleared first, so the value is written with the world entry's flags even over a copy someone set by hand.
		var dbref = target.Object().DBRef;
		await Mediator.Send(new ClearAttributeCommand(dbref, [LiveWire.AttributeName]), token);
		if (wire == LiveWire.None)
		{
			return;
		}

		var owner = await target.Object().Owner.WithCancellation(token);
		await Mediator.Send(new SetAttributeCommand(dbref, [LiveWire.AttributeName], MarkupText.Plain(wire.ToString()), owner), token);
	}

	private static string Described(AnySharpObject target)
		=> $"{target.Object().Name}(#{target.Object().DBRef.Number})";
}
