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

	/// <summary>How many times each player is linked: 1 when left out, 0 for every time.</summary>
	private union RenderPlayersCountOrRefusal(RenderPlayersLimit, CallState);

	/// <summary>How many times each player is replaced: 0 for every time, negative for none at all.</summary>
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
		var players = await RenderPlayersCandidatesAsync(parser.CurrentState.ArgumentList, options);
		if (players.Count == 0) return new CallState(text);

		var plain = text.ToPlainText();
		var offsets = PlainOffsets(text, plain);
		// From plain offset a to b; from the start, any point before the first character comes too.
		MString Slice(int from, int to)
		{
			var start = from == 0 ? 0 : offsets[from];
			return text.Substring(start, offsets[to] - start);
		}

		var lookup = players.GetAlternateLookup<ReadOnlySpan<char>>();
		var lengths = players.Keys.Select(key => key.Length).Distinct().OrderDescending().ToArray();
		var linked = new Dictionary<int, int>();
		var errors = new ListEvaluationErrors();
		var pieces = new List<MString>();
		var copiedTo = 0;
		var prefix = options.Prefix;

		for (var position = 0; position < plain.Length;)
		{
			if (position > 0 && IsNameCharacter(plain[position - 1])
				|| !plain.AsSpan(position).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				position++;
				continue;
			}

			var nameStart = position + prefix.Length;
			var match = RenderPlayersMatchAt(plain, nameStart, lengths, lookup);
			if (match is not ({ } player, var length))
			{
				position++;
				continue;
			}

			var matchEnd = nameStart + length;
			var times = linked.GetValueOrDefault(player.Number);
			if (count == 0 || times < count)
			{
				linked[player.Number] = times + 1;
				pieces.Add(Slice(copiedTo, position));
				pieces.Add(errors.Record(await call(new Dictionary<string, CallState>
				{
					["0"] = new CallState($"#{player.Number}"),
					["1"] = new CallState(text.Substring(offsets[nameStart], offsets[matchEnd] - offsets[nameStart])),
					["2"] = new CallState(text.Substring(offsets[position], offsets[matchEnd] - offsets[position]))
				})));
				copiedTo = matchEnd;
			}

			// A name past its count is still taken whole, so a shorter name inside it is not matched instead.
			position = matchEnd;
		}

		if (copiedTo == 0) return new CallState(text);
		pieces.Add(Slice(copiedTo, plain.Length));
		return errors.Complete(new CallState(MarkupText.Concat(pieces)));
	}

	/// <summary>
	/// The longest name starting at <paramref name="start"/> that ends on a word boundary, and the player
	/// it stands for; nothing when no name is there, or when the name found belongs to two players.
	/// </summary>
	private static (DBRef? Player, int Length) RenderPlayersMatchAt(string plain, int start, int[] lengths,
		Dictionary<string, RenderPlayersCandidate>.AlternateLookup<ReadOnlySpan<char>> lookup)
	{
		foreach (var length in lengths)
		{
			var end = start + length;
			if (end > plain.Length || end < plain.Length && IsNameCharacter(plain[end])) continue;
			if (lookup.TryGetValue(plain.AsSpan(start, length), out var candidate))
			{
				return (candidate.Player, length);
			}
		}

		return (null, 0);
	}

	/// <summary>A character that continues a word, so a name may not start right after it or end right before it.</summary>
	private static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

	/// <summary>
	/// Every name renderplayers() looks for, keyed without regard to case: full names and, unless turned off,
	/// aliases first; with <c>first</c>, the first word of a name with spaces where nothing else holds it.
	/// A name two players answer to at the same rank stands for neither.
	/// </summary>
	private async ValueTask<Dictionary<string, RenderPlayersCandidate>> RenderPlayersCandidatesAsync(
		IReadOnlyDictionary<string, CallState> args, RenderPlayersOptions options)
	{
		var names = new Dictionary<string, RenderPlayersCandidate>(StringComparer.OrdinalIgnoreCase);

		void Add(string name, DBRef player, int tier)
		{
			name = name.Trim();
			if (name.Length == 0) return;

			if (!names.TryGetValue(name, out var held) || held.Tier > tier)
			{
				names[name] = new RenderPlayersCandidate(player, tier);
			}
			else if (held.Tier == tier && held.Player is { } other && other.Number != player.Number)
			{
				names[name] = held with { Player = null };
			}
		}

		var players = await RenderPlayersListAsync(args).ToListAsync();
		foreach (var player in players)
		{
			Add(player.Name, player.DBRef, 0);
			if (!options.Aliases) continue;
			foreach (var alias in player.Aliases) Add(alias, player.DBRef, 0);
		}

		if (options.FirstNames)
		{
			foreach (var player in players)
			{
				var space = player.Name.Trim().IndexOf(' ');
				if (space > 0) Add(player.Name.Trim()[..space], player.DBRef, 1);
			}
		}

		return names;
	}

	/// <summary>
	/// The players the <c>&lt;players&gt;</c> list names, by dbref or objid, skipping anything that is not a
	/// player; every player when the list is left out or empty.
	/// </summary>
	private async IAsyncEnumerable<PlayerNameEntry> RenderPlayersListAsync(IReadOnlyDictionary<string, CallState> args)
	{
		var list = args.TryGetValue("3", out var argument) ? argument.Message.ToPlainText().Trim() : string.Empty;
		if (list.Length == 0)
		{
			await foreach (var player in Mediator.CreateStream(new GetPlayerNamesQuery())) yield return player;
			yield break;
		}

		var seen = new HashSet<int>();
		foreach (var entry in list.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (!DBRef.TryParse(entry, out var dbref) || dbref is not { } parsed) continue;
			if (await Mediator.Send(new GetObjectNodeQuery(parsed)) is AnySharpObject and SharpPlayer player
				&& seen.Add(player.Object.DBRef.Number))
			{
				yield return new PlayerNameEntry(player.Object.DBRef, player.Object.Name, player.Aliases ?? []);
			}
		}
	}
}
