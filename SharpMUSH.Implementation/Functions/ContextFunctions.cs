using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>The most <c>context()</c> returns.</summary>
	private const int MaxContextItems = 20;

	/// <summary>
	/// <c>context(&lt;player&gt;[, &lt;count&gt;[, &lt;osep&gt;[, &lt;obj&gt;/&lt;attr&gt;]]])</c>: the commands the
	/// player's context stack puts first, in the order the command palette's Now and Here groups show
	/// them. Without an attribute, the ready ones (every slot filled) as command links, the info or the
	/// line as each link's hover text, joined by <c> · </c>; a link needs what <c>cmdlink()</c> needs, and
	/// without it each is its label. With one, every entry is passed through it with named arguments
	/// (<c>%&lt;label&gt;</c>, <c>%&lt;line&gt;</c>, <c>%&lt;ready&gt;</c>, <c>%&lt;urgent&gt;</c>,
	/// <c>%&lt;contexts&gt;</c>, <c>%&lt;id&gt;</c>, <c>%&lt;system&gt;</c>, <c>%&lt;info&gt;</c>). Code may ask
	/// for the player whose command it runs, or a player its object controls.
	/// </summary>
	[SharpFunction(Name = "context", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular,
		ParameterNames = ["player", "count", "osep", "object/attribute"])]
	public async ValueTask<CallState> ContextSuggestions(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var count = 5;
		if (args.TryGetValue("1", out var countArg) && countArg.Message.ToPlainText().Trim() is { Length: > 0 } countText
			&& (!int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1))
		{
			return new CallState(ErrorMessages.Returns.PositiveInteger);
		}

		count = Math.Min(count, MaxContextItems);

		var separator = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MString.Plain(" · "));
		var through = args.TryGetValue("3", out var attributeArg) && attributeArg.Message.ToPlainText().Trim().Length > 0
			? attributeArg.Message
			: null;

		return await ContextAuthority.ResolveAsync(parser, LocateService, PermissionService, executor,
				args["0"].Message.ToPlainText()) switch
		{
			SharpPlayer player => await ContextItemsAsync(parser, executor, player, count, separator, through),
			Error<string> error => new CallState(error.Value)
		};
	}

	private async ValueTask<CallState> ContextItemsAsync(IMUSHCodeParser parser, AnySharpObject executor,
		SharpPlayer player, int count, MString separator, MString? through)
	{
		var suggestions = await parser.ServiceProvider.GetRequiredService<IPaletteService>()
			.SuggestAsync(player, ExecutionBudget.CurrentToken);

		if (through is null)
		{
			var links = await executor.IsWizard() || await executor.HasPower("Send_OOB");
			return new CallState(MarkupText.Join(separator, suggestions
				.Where(suggestion => suggestion.Ready)
				.Take(count)
				.Select(suggestion => links ? Link(suggestion) : MString.Plain(suggestion.Entry.Label))));
		}

		var items = new List<MString>();
		foreach (var suggestion in suggestions.Take(count))
		{
			var result = await AttributeService.EvaluateAttributeFunctionResultAsync(parser, executor, through,
				new Dictionary<string, CallState>
				{
					["label"] = new(suggestion.Entry.Label),
					["line"] = new(suggestion.Line),
					["ready"] = new(suggestion.Ready ? "1" : "0"),
					["urgent"] = new(suggestion.Urgent ? "1" : "0"),
					["contexts"] = new(string.Join(' ', suggestion.Contexts)),
					["id"] = new(suggestion.Entry.Id),
					["system"] = new(suggestion.Entry.System),
					["info"] = new(suggestion.Info ?? string.Empty)
				},
				ignoreLambda: true);
			items.Add(result.Message ?? MString.Empty);
		}

		return new CallState(MarkupText.Join(separator, items));
	}

	private static MString Link(PaletteSuggestion suggestion)
		=> MarkupText.Wrap(
			Ansi.Create(linkUrl: suggestion.Line, linkKind: LinkKind.Command, linkText: suggestion.Info ?? suggestion.Line),
			MString.Plain(suggestion.Entry.Label));
}
