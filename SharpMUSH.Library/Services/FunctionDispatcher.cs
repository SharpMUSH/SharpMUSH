using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>Policy shared by parsed calls and #apply's already-evaluated arguments.</summary>
public static class FunctionDispatcher
{
	public static async ValueTask<CallState> InvokeAsync(IMUSHCodeParser parser, FunctionDefinition definition,
		AnySharpObject executor, bool sideEffects, INotifyService notify, ILogger logger, int? argumentCount = null, bool permissionsChecked = false, bool deferredArguments = false)
	{
		var attribute = definition.Attribute;
		var flags = attribute.Flags;
		var name = attribute.Name.ToUpperInvariant();
		if (!permissionsChecked)
		{
			var permissionError = await CheckPermissionAsync(attribute, executor);
			if (permissionError is not null) return new CallState(permissionError);
		}
		var count = argumentCount ?? parser.CurrentState.Arguments.Count;

		if (count < attribute.MinArgs)
			return new CallState(string.Format(ErrorMessages.Returns.TooFewArguments, name, attribute.MinArgs, count));
		if (count > attribute.MaxArgs)
			return new CallState(string.Format(ErrorMessages.Returns.TooManyArguments, name, attribute.MaxArgs, count));
		if (flags.HasFlag(FunctionFlags.EvenArgsOnly) && count % 2 != 0)
			return new CallState(string.Format(ErrorMessages.Returns.GotUnEvenArgs, name));
		if (flags.HasFlag(FunctionFlags.UnEvenArgsOnly) && count % 2 == 0)
			return new CallState(string.Format(ErrorMessages.Returns.GotEvenArgs, name));
		if (flags.HasFlag(FunctionFlags.HasSideFX) && count >= attribute.SideEffectMinArgs && !sideEffects)
			return new CallState(ErrorMessages.Returns.FunctionDisabled);
		var error = ValidateNumericArguments(attribute, parser.CurrentState.ArgumentsOrdered.Values);
		if (error is not null) return new CallState(error);

		if (flags.HasFlag(FunctionFlags.Deprecated))
		{
			var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);
			await notify.Notify(owner.Object.DBRef, $"Deprecated function {name} being used on object {executor.Object().DBRef}.");
		}
		if (flags.HasFlag(FunctionFlags.LogArgs))
			logger.LogInformation("Function {Function}({Arguments}) executed by {Executor}", name,
				string.Join(",", parser.CurrentState.ArgumentsOrdered.Values.Select(arg => arg.Message?.ToPlainText())), executor.Object().DBRef);
		else if (flags.HasFlag(FunctionFlags.LogName))
			logger.LogInformation("Function {Function} executed by {Executor}", name, executor.Object().DBRef);

		if (!flags.HasFlag(FunctionFlags.Localize)) return await definition.Function(parser);
		var localized = parser.Push(parser.CurrentState with
		{
			Registers = new(parser.CurrentState.Registers.Reverse()
				.Select(frame => new Dictionary<string, MString>(frame, frame.Comparer)))
		});
		if (deferredArguments && flags.HasFlag(FunctionFlags.NoParse))
		{
			// Rebind lazy argument evaluation to this scope instead of the caller's visitor.
			localized = localized.Push(localized.CurrentState with
			{
				Arguments = localized.CurrentState.Arguments.ToDictionary(pair => pair.Key, pair => pair.Value with
				{
					ParsedMessage = async () =>
					{
						var message = (await localized.FunctionParse(pair.Value.Message ?? MarkupText.Empty))?.Message;
						return flags.HasFlag(FunctionFlags.StripAnsi)
							? MarkupText.Plain(message?.ToPlainText() ?? "") : message;
					}
				})
			});
		}
		return await definition.Function(localized);
	}

	public static async ValueTask<string?> CheckPermissionAsync(SharpFunctionAttribute attribute, AnySharpObject executor)
	{
		var flags = attribute.Flags;
		if (flags.HasFlag(FunctionFlags.Disabled)) return ErrorMessages.Returns.FunctionDisabled;

		if ((flags.HasFlag(FunctionFlags.GodOnly) && !executor.IsGod())
			|| (flags.HasFlag(FunctionFlags.WizardOnly) && !await executor.IsWizard())
			|| (flags.HasFlag(FunctionFlags.AdminOnly) && !await executor.IsPriv())
			|| (flags.HasFlag(FunctionFlags.NoGuest) && await executor.IsGuest()))
			return ErrorMessages.Returns.PermissionDenied;
		if ((flags & (FunctionFlags.NoGagged | FunctionFlags.NoFixed)) != 0)
		{
			AnySharpObject owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);
			if ((flags.HasFlag(FunctionFlags.NoGagged) && await owner.HasFlag("GAGGED"))
				|| (flags.HasFlag(FunctionFlags.NoFixed) && await owner.HasFlag("FIXED")))
				return ErrorMessages.Returns.PermissionDenied;
		}
		if (attribute.Restrict.Length > 0
			&& !await executor.SatisfiesFunctionRestriction(string.Join(' ', attribute.Restrict)))
			return ErrorMessages.Returns.PermissionDenied;

		return null;
	}

	private static string? ValidateNumericArguments(SharpFunctionAttribute attribute, IEnumerable<CallState> arguments)
	{
		const FunctionFlags numeric = FunctionFlags.IntegersOnly | FunctionFlags.PositiveIntegersOnly
			| FunctionFlags.DecimalsOnly | FunctionFlags.NumbersOnly;
		if ((attribute.Flags & numeric) == 0) return null;

		foreach (var argument in arguments)
		{
			var text = argument.Message?.ToPlainText();
			if (string.IsNullOrEmpty(text)) text = "0";
			if (attribute.Flags.HasFlag(FunctionFlags.IntegersOnly) && !long.TryParse(text, out _))
				return attribute.MaxArgs == 1 ? ErrorMessages.Returns.Integer : ErrorMessages.Returns.Integers;
			if (attribute.Flags.HasFlag(FunctionFlags.PositiveIntegersOnly) && !ulong.TryParse(text, out _))
				return attribute.MaxArgs == 1 ? ErrorMessages.Returns.UInteger : ErrorMessages.Returns.UIntegers;
			if ((attribute.Flags.HasFlag(FunctionFlags.DecimalsOnly) && !decimal.TryParse(text, out _))
				|| (attribute.Flags.HasFlag(FunctionFlags.NumbersOnly) && !double.TryParse(text, out _)))
				return attribute.MaxArgs == 1 ? ErrorMessages.Returns.Number : ErrorMessages.Returns.Numbers;
		}
		return null;
	}
}
