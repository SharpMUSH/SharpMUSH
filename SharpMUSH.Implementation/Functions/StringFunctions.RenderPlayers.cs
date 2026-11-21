using System.Globalization;
using MarkupString;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// Replaces the player names written in <c>&lt;text&gt;</c> with what <c>&lt;obj&gt;/&lt;attr&gt;</c>
	/// makes of each: the attribute gets the player's dbref as %0, the name as written as %1 and the whole
	/// match, prefix included, as %2. Names match whole words, ignoring case, longest first; the rest of
	/// the text and its markup are kept as they are.
	/// </summary>
	[SharpFunction(Name = "renderplayers", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular,
		ParameterNames = ["object/attribute", "text", "count", "players", "options"])]
	public async ValueTask<CallState> RenderPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		var text = args["1"].Message;

		return RenderPlayersCount(args) switch
		{
			CallState refusal => refusal,
			// A negative count turns the function off: the text comes back with nothing called.
			RenderPlayersLimit { Times: < 0 } => new CallState(text),
			RenderPlayersLimit limit => RenderPlayersOptionsFrom(args) switch
			{
				CallState refusal => refusal,
				RenderPlayersOptions options => await RenderPlayersWithAsync(parser, text, limit.Times, options)
			}
		};
	}

	/// <summary>How many times each unique match (a name or an alias, in any case) is linked: 1 when left out, 0 for every time.</summary>
	private union RenderPlayersCountOrRefusal(RenderPlayersLimit, CallState);

	/// <summary>How many times each unique match is replaced: 0 for every time, negative for none at all.</summary>
	private readonly record struct RenderPlayersLimit(int Times);

	/// <summary>renderplayers()'s options, or the error it returns for one it does not know.</summary>
	private union RenderPlayersOptionsOrRefusal(RenderPlayersOptions, CallState);

	/// <summary>Which names a player answers to, and the text that must come right before one.</summary>
	private sealed record RenderPlayersOptions(bool Aliases, bool FirstNames, string Prefix);

	/// <summary>The player a name stands for, or none when two players answer to it.</summary>
	private sealed record RenderPlayersCandidate(DBRef? Player, int Tier);

	private static RenderPlayersCountOrRefusal RenderPlayersCount(IReadOnlyDictionary<string, CallState> args)
	{
		if (!args.TryGetValue("2", out var argument)) return new RenderPlayersLimit(1);

		var plain = argument.Message.ToPlainText().Trim();
		if (plain.Length == 0) return new RenderPlayersLimit(1);

		return int.TryParse(plain, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var count)
			? new RenderPlayersLimit(count)
			: new CallState(ErrorMessages.Returns.Integer);
	}

	private static RenderPlayersOptionsOrRefusal RenderPlayersOptionsFrom(IReadOnlyDictionary<string, CallState> args)
	{
		var options = new RenderPlayersOptions(Aliases: true, FirstNames: false, Prefix: string.Empty);
		if (!args.TryGetValue("4", out var argument)) return options;

		foreach (var word in argument.Message.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var lower = word.ToLowerInvariant();
			RenderPlayersOptions? next = lower switch
			{
				"noalias" or "noaliases" => options with { Aliases = false },
				"first" or "firstname" => options with { FirstNames = true },
				_ when lower.StartsWith("prefix:", StringComparison.Ordinal) && word.Length > "prefix:".Length
					=> options with { Prefix = word["prefix:".Length..] },
				_ => null
			};

			if (next is null) return new CallState($"{ErrorMessages.Returns.InvalidOption} {word.ToUpperInvariant()}");
			options = next;
		}

		return options;
	}

	private async ValueTask<CallState> RenderPlayersWithAsync(IMUSHCodeParser parser, MString text, int count,
		RenderPlayersOptions options)
	{
		var args = parser.CurrentState.ArgumentList;
		var rawAttrArg = args["0"].Message;
		var rawAttrStr = rawAttrArg.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			return await RenderPlayersInAsync(parser, text, count, options, environment =>
				AttributeService.EvaluateAttributeFunctionResultAsync(parser, executor, rawAttrArg, environment));
		}

		return await AttributeService.FetchAttributeFunctionAsync(parser, executor, rawAttrStr) switch
		{
			AttributeFunction function => await RenderPlayersInAsync(parser, text, count, options, environment =>
				AttributeService.CallAttributeFunctionAsync(parser, function, environment, environment)),
			CallState refusal => refusal
		};
	}

	private async ValueTask<CallState> RenderPlayersInAsync(IMUSHCodeParser parser, MString text, int count,
		RenderPlayersOptions options, Func<Dictionary<string, CallState>, ValueTask<CallState>> call)
	{
		var index = await RenderPlayersIndexAsync(parser.CurrentState.ArgumentList, options);
		if (index.IsEmpty) return new CallState(text);

		var plain = text.ToPlainText();
		// Where each plain character sits in the text. A match runs from its first character to just after its
		// last, so a sound or other point before or after a name stays in the text around it.
		var offsets = PlainOffsets(text, plain);
		// Each name as matched (any case) has its own count: a player's name and each alias are counted apart.
		var linked = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var errors = new ListEvaluationErrors();
		var pieces = new List<MString>();
		var copiedTo = 0;
		var replaced = false;
		var prefix = options.Prefix;

		for (var position = 0; position < plain.Length;)
		{
			if (position > 0 && PlayerNameIndex.IsNameCharacter(plain[position - 1])
				|| !plain.AsSpan(position).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				position++;
				continue;
			}

			var nameStart = position + prefix.Length;
			var (player, length) = index.MatchAt(plain, nameStart);
			if (length == 0)
			{
				position++;
				continue;
			}

			var matchEnd = nameStart + length;
			var matched = plain.Substring(nameStart, length);
			var times = linked.GetValueOrDefault(matched);
			if (count == 0 || times < count)
			{
				var start = offsets[position];
				var end = offsets[matchEnd - 1] + 1;
				linked[matched] = times + 1;
				pieces.Add(text.Substring(copiedTo, start - copiedTo));
				pieces.Add(errors.Record(await call(new Dictionary<string, CallState>
				{
					["0"] = new CallState($"#{player.Number}"),
					["1"] = new CallState(text.Substring(offsets[nameStart], end - offsets[nameStart])),
					["2"] = new CallState(text.Substring(start, end - start))
				})));
				copiedTo = end;
				replaced = true;
			}

			// A name past its count is still taken whole, so a shorter name inside it is not matched instead.
			position = matchEnd;
		}

		if (!replaced) return new CallState(text);
		pieces.Add(text.Substring(copiedTo, text.Length - copiedTo));
		return errors.Complete(new CallState(MarkupText.Concat(pieces)));
	}

	/// <summary>
	/// The names to look for: every player's, from the cached index, or only the players the
	/// <c>&lt;players&gt;</c> list names by dbref or objid, skipping anything that is not a player.
	/// </summary>
	private async ValueTask<PlayerNameIndex> RenderPlayersIndexAsync(IReadOnlyDictionary<string, CallState> args,
		RenderPlayersOptions options)
	{
		var list = args.TryGetValue("3", out var argument) ? argument.Message.ToPlainText().Trim() : string.Empty;
		if (list.Length == 0)
		{
			return await Mediator.Send(new GetPlayerNameIndexQuery(options.Aliases, options.FirstNames));
		}

		var players = new Dictionary<int, PlayerNameEntry>();
		foreach (var entry in list.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (DBRef.TryParse(entry, out var dbref) && dbref is { } parsed
				&& await Mediator.Send(new GetObjectNodeQuery(parsed)) is AnySharpObject and SharpPlayer player)
			{
				players.TryAdd(player.Object.DBRef.Number,
					new PlayerNameEntry(player.Object.DBRef, player.Object.Name, player.Aliases ?? []));
			}
		}

		return PlayerNameIndex.Build(players.Values, options.Aliases, options.FirstNames);
	}
}
