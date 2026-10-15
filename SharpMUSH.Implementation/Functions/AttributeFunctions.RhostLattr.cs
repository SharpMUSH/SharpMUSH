using System.Text.RegularExpressions;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// How many names one page of <c>lattr(&lt;obj&gt;,,&lt;page&gt;)</c> holds with <c>rhost_lattr</c> on: the page
	/// size of a default RhostMUSH build, so a page number written for one names the same attributes.
	/// </summary>
	public const int RhostLattrPageSize = 243;

	/// <summary>
	/// lattr() and lattrp() for a call with more than two arguments: RhostMUSH's layout when
	/// <c>rhost_lattr</c> is on, and the arity error PennMUSH gives otherwise.
	/// </summary>
	private ValueTask<CallState> ListAttributesWithLayoutAsync(IMUSHCodeParser parser, string calledAs, bool checkParents)
	{
		var count = parser.CurrentState.ArgumentsOrdered.Count;
		if (Configuration.CurrentValue.Compatibility.RhostLattr)
		{
			return RhostListAttributesAsync(parser, calledAs, checkParents);
		}

		return count > 2
			? ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.TooManyArguments,
				calledAs.ToUpperInvariant(), 2, count)))
			: AttributePatternAsync(parser, calledAs, checkParents, IAttributeService.AttributePatternMode.Wildcard,
				matched => string.Join(AttributeListSeparator(parser, 1), matched));
	}

	/// <summary>
	/// <c>lattr(&lt;obj&gt;[/&lt;pattern&gt;][, &lt;target&gt;[, &lt;type&gt;[, &lt;flags&gt;[, &lt;regexp&gt;[, &lt;tree&gt;]]]]])</c>
	/// as RhostMUSH reads it. The names are those <c>&lt;target&gt;</c> may see, when the caller controls
	/// that player, and are always separated by a space. <c>&lt;type&gt;</c>'s first character narrows or
	/// summarises the list; see <c>help compatibility config</c>.
	/// </summary>
	private async ValueTask<CallState> RhostListAttributesAsync(IMUSHCodeParser parser, string calledAs, bool checkParents)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		string Arg(int position) => args.TryGetValue(position.ToString(), out var value) ? value.Message.ToPlainText() : string.Empty;

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var viewer = await RhostLattrViewerAsync(parser, executor, Arg(1));
		var type = Arg(2);
		var lookup = type.Length > 0 ? type[0] : ' ';
		var flagLetters = Arg(3);
		var regex = SearchSpecEngine.LeadingInteger(Arg(4)) != 0;
		var tree = SearchSpecEngine.LeadingInteger(Arg(5)) != 0;

		if (lookup is '&' or '|' && flagLetters.Length == 0)
		{
			return CallState.Empty;
		}

		var split = HelperFunctions.SplitDbRefAndOptionalAttr(Arg(0));
		if (split is not { Object: var obj, Attribute: var attributePattern })
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, calledAs.ToUpperInvariant()));
		}

		var mode = regex ? IAttributeService.AttributePatternMode.Regex : IAttributeService.AttributePatternMode.Wildcard;
		var pattern = attributePattern ?? (regex ? ".*" : "*");
		if (!regex && !tree)
		{
			// RhostMUSH has no attribute trees, so its * matches a backtick like any other character.
			pattern = Regex.Replace(pattern, @"\*+", "**");
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, obj, LocateFlags.All,
			async found => await AttributeService.LazilyGetAttributePatternAsync(viewer, found, pattern, checkParents, mode) switch
			{
				Error<string> error => error,
				IAsyncEnumerable<LazySharpAttribute> matched => await RhostLattrSelectAsync(matched, lookup, type, flagLetters)
			});
	}

	/// <summary>Whose view <c>&lt;target&gt;</c> names: that player when the caller controls them, else the caller.</summary>
	private async ValueTask<AnySharpObject> RhostLattrViewerAsync(IMUSHCodeParser parser, AnySharpObject executor, string target)
	{
		target = target.TrimStart('*');
		if (target.Length == 0)
		{
			return executor;
		}

		return await LocateService.LocatePlayer(parser, executor, executor, target) is AnySharpObject player
					 && await PermissionService.Controls(executor, player)
			? player
			: executor;
	}

	/// <summary>The names RhostMUSH's <c>&lt;type&gt;</c> keeps, or the summary it asks for.</summary>
	private static async ValueTask<CallState> RhostLattrSelectAsync(IAsyncEnumerable<LazySharpAttribute> matched,
		char lookup, string type, string flagLetters)
	{
		try
		{
			var attributes = await matched.ToArrayAsync(ExecutionBudget.CurrentToken);

			if (lookup is '>' or '<')
			{
				var names = attributes.Select(attribute => attribute.LongName.ToUpperInvariant()).ToArray();
				if (names.Length == 0)
				{
					return CallState.Empty;
				}

				return lookup == '>'
					? names.Max(StringComparer.Ordinal)!
					: names.Min(StringComparer.Ordinal)!;
			}

			var (low, high) = lookup == '~' && type.IndexOf('-') is var dash and > 0
				? (SearchSpecEngine.LeadingInteger(type[1..dash]),
					SearchSpecEngine.LeadingInteger(type[1..dash]) + SearchSpecEngine.LeadingInteger(type[(dash + 1)..]) - 1)
				: (0, 0);
			var page = char.IsAsciiDigit(lookup) ? SearchSpecEngine.LeadingInteger(type) : 0;

			var kept = new List<string>();
			var counted = 0;
			foreach (var attribute in attributes)
			{
				var symbols = string.Concat(attribute.Flags.Select(flag => flag.Symbol));
				switch (lookup)
				{
					case '&' when !flagLetters.All(letter => symbols.Contains(letter)):
					case '|' when !flagLetters.Any(letter => symbols.Contains(letter)):
					// Locks are not attributes here, so no attribute is one.
					case '+':
						continue;
				}

				counted++;
				if (lookup == 'l'
						|| (page > 0 && (counted <= RhostLattrPageSize * (page - 1) || counted > RhostLattrPageSize * page))
						|| (high != 0 && (counted < low || counted > high)))
				{
					continue;
				}

				if (lookup is '$' or '^')
				{
					var value = (await attribute.Value.WithCancellation(ExecutionBudget.CurrentToken)).ToPlainText();
					if (value.Length == 0 || value[0] != lookup || !value.Contains(':'))
					{
						continue;
					}
				}

				kept.Add(attribute.LongName);
			}

			return lookup == 'l'
				? $"{counted} {counted / RhostLattrPageSize + 1}"
				: string.Join(" ", kept);
		}
		catch (RegexParseException)
		{
			return new CallState(ErrorMessages.Returns.RegexpInvalid);
		}
		catch (RegexMatchTimeoutException)
		{
			return new CallState(ErrorMessages.Returns.RegexpTimeout);
		}
	}
}
