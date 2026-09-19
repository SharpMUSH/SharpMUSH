using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "grep", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public ValueTask<CallState> Grep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return GrepInternal(parser, false, false);
	}

	[SharpFunction(Name = "pgrep", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern", "flags"])]
	public ValueTask<CallState> ParentGrep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return GrepInternal(parser, false, true);
	}

	[SharpFunction(Name = "grepi", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public ValueTask<CallState> GrepCaseInsensitive(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return GrepInternal(parser, true, false);
	}

	/// <summary>
	/// Internal helper for grep, grepi, pgrep.
	/// </summary>
	private async ValueTask<CallState> GrepInternal(IMUSHCodeParser parser, bool caseInsensitive, bool checkParents)
	{
		var args = parser.CurrentState.Arguments;
		var objectStr = args["0"].Message!.ToPlainText();
		var attrsPattern = args["1"].Message!.ToPlainText();
		var substring = args["2"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, objectStr!, LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService.GetAttributePatternAsync(executor, found,
					attrsPattern ?? "*", checkParents,
					IAttributeService.AttributePatternMode.Wildcard);

				var comparison = caseInsensitive
					? StringComparison.OrdinalIgnoreCase
					: StringComparison.Ordinal;

				return attributes switch
				{
					Error<string> error => error,
					SharpAttribute[] matched => string.Join(" ", matched
						.Where(attr => attr.Value.ToPlainText().Contains(substring, comparison))
						.Select(attr => attr.LongName))
				};
			});
	}

	[SharpFunction(Name = "regrep", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "attribute", "pattern"])]
	public ValueTask<CallState> RegularExpressionGrep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegGrepInternal(parser, false);
	}

	[SharpFunction(Name = "regrepi", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "attribute", "pattern"])]
	public ValueTask<CallState> RegularExpressionGrepCaseInsensitive(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		return RegGrepInternal(parser, true);
	}

	/// <summary>
	/// Internal helper for regrep, regrepi - searches attributes for pattern matches.
	/// </summary>
	private async ValueTask<CallState> RegGrepInternal(IMUSHCodeParser parser, bool caseInsensitive)
	{
		var args = parser.CurrentState.Arguments;
		var objectStr = args["0"].Message!.ToPlainText();
		var attrsPattern = args["1"].Message!.ToPlainText();
		var regexpPattern = args["2"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		try
		{
			var options = RegexOptions.None;
			if (caseInsensitive)
			{
				options |= RegexOptions.IgnoreCase;
			}

			var regex = SoftcodeRegex.Create(regexpPattern, options);

			return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, executor, executor, objectStr, LocateFlags.All,
				async found =>
				{
					var attributes = await AttributeService.GetAttributePatternAsync(
						executor,
						found,
						attrsPattern,
						false,
						IAttributeService.AttributePatternMode.Wildcard);

					if (attributes is not SharpAttribute[] matched)
					{
						return CallState.Empty;
					}

					var matchingAttributes = matched
						.Where(attr => attr.Value.ToPlainText() is { Length: > 0 } value && regex.IsMatch(value))
						.Select(attr => attr.Name);

					return new CallState(string.Join(" ", matchingAttributes));
				});
		}
		catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
		{
			// A player's pattern that cannot finish within SoftcodeRegex.MatchTimeout: an answer, not a crash.
			return new CallState(ErrorMessages.Returns.RegexpTimeout);
		}
		catch (ArgumentException)
		{
			return new CallState(ErrorMessages.Returns.RegexpInvalid);
		}
	}

	[SharpFunction(Name = "wildgrep", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "attribute", "pattern"])]
	public ValueTask<CallState> WildcardGrep(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return WildGrepInternal(parser, false);
	}

	[SharpFunction(Name = "wildgrepi", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["object", "attribute", "pattern"])]
	public ValueTask<CallState> WildcardGrepCaseInsensitive(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return WildGrepInternal(parser, true);
	}

	/// <summary>
	/// Internal helper for wildgrep, wildgrepi.
	/// </summary>
	private async ValueTask<CallState> WildGrepInternal(IMUSHCodeParser parser, bool caseInsensitive)
	{
		var args = parser.CurrentState.Arguments;
		var objectStr = args["0"].Message!.ToPlainText();
		var attrsPattern = args["1"].Message!.ToPlainText();
		var valuePattern = args["2"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// Compiled once for every attribute it is held against; grep_util asks for a case-sensitive
		// glob unless the caller used the "i" spelling.
		var regex = SoftcodeRegex.Wildcard(valuePattern, caseSensitive: !caseInsensitive);

		try
		{
			return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
				executor, executor, objectStr!, LocateFlags.All,
				async found =>
				{
					var attributes = await AttributeService.GetAttributePatternAsync(executor, found,
						attrsPattern ?? "*", false,
						IAttributeService.AttributePatternMode.Wildcard);

					return attributes switch
					{
						Error<string> error => error,
						SharpAttribute[] matched => string.Join(" ", matched
							.Where(attr => regex.IsMatch(attr.Value.ToPlainText()))
							.Select(attr => attr.LongName))
					};
				});
		}
		catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
		{
			// A player's pattern that cannot finish within SoftcodeRegex.MatchTimeout: an answer, not a crash.
			return new CallState(ErrorMessages.Returns.RegexpTimeout);
		}
	}

	[SharpFunction(Name = "reglattr", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public ValueTask<CallState> RegularExpressionListAttribute(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> AttributePatternAsync(parser, attribute.Name, false, IAttributeService.AttributePatternMode.Regex,
			matched => string.Join(AttributeListSeparator(parser, "1"), matched.Select(x => x.LongName)));

	[SharpFunction(Name = "reglattrp", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public ValueTask<CallState> RegularExpressionListAttributeParent(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> AttributePatternAsync(parser, attribute.Name, true, IAttributeService.AttributePatternMode.Regex,
			matched => string.Join(AttributeListSeparator(parser, "1"), matched.Select(x => x.LongName)));

	[SharpFunction(Name = "regnattr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["object"])]
	public ValueTask<CallState> RegularExpressionNumberAttributes(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> AttributePatternAsync(parser, attribute.Name, false, IAttributeService.AttributePatternMode.Regex,
			matched => matched.Length);

	[SharpFunction(Name = "regnattrp", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["object"])]
	public ValueTask<CallState> RegularExpressionNumberAttributesParent(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> AttributePatternAsync(parser, attribute.Name, true, IAttributeService.AttributePatternMode.Regex,
			matched => matched.Length);

	[SharpFunction(Name = "regxattr", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public ValueTask<CallState> RegularExpressionNumberRangeAttributes(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> AttributeRangeAsync(parser, attribute.Name, false, IAttributeService.AttributePatternMode.Regex);

	[SharpFunction(Name = "regxattrp", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["object", "pattern"])]
	public ValueTask<CallState> RegularExpressionNumberRangeParent(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> AttributeRangeAsync(parser, attribute.Name, true, IAttributeService.AttributePatternMode.Regex);
}
