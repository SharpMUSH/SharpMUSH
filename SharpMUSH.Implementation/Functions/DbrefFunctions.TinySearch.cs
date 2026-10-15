using SharpMUSH.Library;
using SharpMUSH.Implementation.Common;
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
	/// The RhostMUSH and TinyMUSH classes of <c>search([&lt;player&gt;] &lt;class&gt;=&lt;restriction&gt;[,&lt;low&gt;[,&lt;high&gt;]])</c>,
	/// in the order they are tried: a class written short matches the first one it begins.
	/// </summary>
	private static readonly (string Name, string Class)[] TinySearchClasses =
	[
		("exits", "EXITS"),
		("evaluate", "EVAL"),
		("eplayer", "EPLAYER"),
		("eroom", "EROOM"),
		("eobject", "ETHING"),
		("ething", "ETHING"),
		("eexit", "EEXIT"),
		("flags", "FLAGS"),
		("name", "NAME"),
		("objects", "OBJECTS"),
		("players", "PLAYERS"),
		("parent", "PARENT"),
		("rooms", "ROOMS"),
		("type", "TYPE"),
		("things", "THINGS"),
	];

	/// <summary>The object types <c>type=</c> takes, each matched by any prefix.</summary>
	private static readonly (string Name, string Type)[] TinySearchTypes =
	[
		("rooms", "ROOM"),
		("exits", "EXIT"),
		("objects", "THING"),
		("things", "THING"),
		("players", "PLAYER"),
	];

	/// <summary>
	/// lsearch() and search() given RhostMUSH's single argument, <c>[&lt;player&gt;] &lt;class&gt;=&lt;restriction&gt;</c>,
	/// with an optional lowest and highest dbref after it. The commas that separate those reach us as
	/// further arguments. The class is the last word before the <c>=</c> and the player, when there is
	/// one, the words before it. Without a player the search covers every owner the searcher may
	/// search, as lsearch(all) does; a player search or a parent search covers every owner whatever
	/// the searcher.
	/// </summary>
	private async ValueTask<CallState> TinySearchAsync(IMUSHCodeParser parser, AnySharpObject executor)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var spec = args["0"].Message.ToPlainText();
		var equals = spec.IndexOf('=');
		var before = spec[..equals].Trim();
		var restriction = spec[(equals + 1)..].Trim();

		var lastSpace = before.LastIndexOf(' ');
		var className = (lastSpace < 0 ? before : before[(lastSpace + 1)..]).ToLowerInvariant();
		var playerName = lastSpace < 0 ? string.Empty : before[..lastSpace].Trim();
		if (playerName.Length > 1 && playerName[0] == '"' && playerName[^1] == '"')
		{
			playerName = playerName[1..^1];
		}

		var pairs = new List<SearchSpecEngine.SearchPair>();
		var everyOwner = false;
		if (className.Length > 0)
		{
			var match = TinySearchClasses.FirstOrDefault(known => known.Name.StartsWith(className, StringComparison.Ordinal));
			if (match.Class is null)
			{
				return new CallState(ErrorMessages.Returns.InvalidSearchType);
			}

			if (match.Class == "TYPE")
			{
				if (restriction.Length > 0)
				{
					var type = TinySearchTypes.FirstOrDefault(known =>
						known.Name.StartsWith(restriction.ToLowerInvariant(), StringComparison.Ordinal));
					if (type.Type is null)
					{
						return new CallState(ErrorMessages.Returns.InvalidSearchType);
					}

					pairs.Add(new SearchSpecEngine.SearchPair("TYPE", type.Type));
					everyOwner = type.Type == "PLAYER";
				}
			}
			else
			{
				pairs.Add(new SearchSpecEngine.SearchPair(match.Class, restriction));
				everyOwner = match.Class is "PLAYERS" or "PARENT";
			}
		}

		if (TinySearchBound(args, 1) is int low)
		{
			pairs.Add(new SearchSpecEngine.SearchPair("MINDB", low.ToString()));
		}

		if (TinySearchBound(args, 2) is int high)
		{
			pairs.Add(new SearchSpecEngine.SearchPair("MAXDB", high.ToString()));
		}

		return await TinySearchOwnerAsync(parser, executor, playerName, everyOwner) switch
		{
			DBRef owner => await RunListSearchAsync(parser, executor, owner, pairs, useRegex: false),
			None => await RunListSearchAsync(parser, executor, null, pairs, useRegex: false),
			NotFound => await UnknownOwnerAsync()
		};

		async ValueTask<CallState> UnknownOwnerAsync()
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchUnknownOwner), executor);
			return new CallState(ErrorMessages.Returns.Nothing);
		}
	}

	/// <summary>A lowest or highest dbref, written with or without its <c>#</c>; none when the argument is absent or empty.</summary>
	private static int? TinySearchBound(IReadOnlyDictionary<string, CallState> args, int position)
	{
		if (!args.TryGetValue(position.ToString(), out var argument))
		{
			return null;
		}

		var text = argument.Message.ToPlainText().Trim().TrimStart('#');
		return text.Length == 0 ? null : Math.Max(0, SearchSpecEngine.LeadingInteger(text));
	}

	/// <summary>Whose objects a Tiny-form search covers: one player, everyone the searcher may search (none), or no such player.</summary>
	private union TinySearchOwner(DBRef, None, NotFound);

	private async ValueTask<TinySearchOwner> TinySearchOwnerAsync(IMUSHCodeParser parser, AnySharpObject executor,
		string playerName, bool everyOwner)
	{
		if (playerName.Length == 0)
		{
			return everyOwner || await executor.IsSee_All() || await executor.HasPower("Search")
				? new None()
				: (await executor.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;
		}

		if (playerName.Equals("me", StringComparison.OrdinalIgnoreCase))
		{
			return (await executor.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;
		}

		return await LocateService.LocatePlayer(parser, executor, executor, playerName) is AnySharpObject and SharpPlayer player
			? player.Object.DBRef
			: new NotFound();
	}
}
