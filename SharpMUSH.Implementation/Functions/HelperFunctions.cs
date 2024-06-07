using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Implementation.Tools;
using OneOf;
using SharpMUSH.Library.Models;
using System.Text.RegularExpressions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using OneOf.Types;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	private readonly static Regex TimeFormatMatchRegex = TimeFormatMatch();
	private readonly static Regex NthRegex = Nth();
	private readonly static Regex TimeSpanFormatMatchRegex = TimeSpanFormatMatch();
	private readonly static Regex NameListPatternRegex = NameListPattern();

	public enum ControlFlow { Break, Continue, None };

	private static CallState AggregateDecimals(List<CallState> args, Func<decimal, decimal, decimal> aggregateFunction) =>
		new(args
			.Select(x => decimal.Parse(MModule.plainText(x.Message)))
			.Aggregate(aggregateFunction).ToString());

	private static CallState AggregateIntegers(List<CallState> args, Func<int, int, int> aggregateFunction) =>
		new(args
			.Select(x => int.Parse(MModule.plainText(x.Message)))
			.Aggregate(aggregateFunction).ToString());

	private static CallState ValidateIntegerAndEvaluate(List<CallState> args, Func<IEnumerable<int>, MString> aggregateFunction)
		 => new(aggregateFunction(args.Select(x => int.Parse(MModule.plainText(x.Message!)))).ToString());

	private static CallState AggregateDecimalToInt(List<CallState> args, Func<decimal, decimal, decimal> aggregateFunction) =>
		new(Math.Floor(args
			.Select(x => decimal.Parse(string.Join(string.Empty, MModule.plainText(x.Message))))
			.Aggregate(aggregateFunction)).ToString());

	private static CallState EvaluateDecimal(List<CallState> args, Func<decimal, decimal> func)
		=> new(func(decimal.Parse(MModule.plainText(args[0].Message))).ToString());

	private static CallState EvaluateDouble(List<CallState> args, Func<double, double> func)
		=> new(func(double.Parse(MModule.plainText(args[0].Message))).ToString());

	private static CallState EvaluateInteger(List<CallState> args, Func<int, int> func)
		=> new(func(int.Parse(MModule.plainText(args[0].Message))).ToString());

	private static CallState ValidateDecimalAndEvaluatePairwise(this List<CallState> args, Func<(decimal, decimal), bool> func)
	{
		if (args.Count < 2)
		{
			return new CallState(Message: Errors.ErrorTooFewArguments);
		}

		var doubles = args.Select(x =>
			(
				IsDouble: decimal.TryParse(string.Join("", MModule.plainText(x.Message)), out var b),
				Double: b
			)).ToList();

		return doubles.Any(x => !x.IsDouble)
				? new CallState(Message: Errors.ErrorNumbers)
				: new CallState(Message: doubles.Select(x => x.Double).Pairwise().Skip(1).SkipWhile(func).Any().ToString());
	}

	private static (int, string)[] ExtractArray(TimeSpan span) =>
		[
			(span.Days > 6 ? span.Days / 7 : 0, "w"),
			(span.Days < 7 ? span.Days : span.Days % 7, "d"),
			(span.Hours, "h"),
			(span.Minutes, "m"),
			(span.Seconds, "s")
		];

	public static string TimeString(TimeSpan span, int pad = 0, char padding = '0', ushort accuracy = 1, bool ignoreZero = true) =>
		$"{string.Join(" ",
			ExtractArray(span)
			.SkipWhile((x, y) => ignoreZero ? x.Item1 == 0 : y < 5 - accuracy)
			.Take(accuracy)
			.DefaultIfEmpty((0, "s"))
			.Select(x => $"{x.Item1.ToString().PadRight(pad, padding)}{x.Item2}"))}";

	public static string TimeFormat(DateTimeOffset time, string format)
		=> TimeFormatMatchRegex.Replace(format, match =>
			match.Groups["Character"].Value switch
			{
				// Abbreviated weekday name 
				"a" => string.Empty,
				// Full weekday name
				"A" => string.Empty,
				// Abbreviated month name
				"b" => string.Empty,
				// Full month name  
				"B" => string.Empty,
				// Date and time 
				"c" => string.Empty,
				// Day of the month
				"d" => string.Empty,
				// Hour of the 24-hour day
				"H" => time.Hour.ToString(),
				// Hour of the 12-hour day
				"I" => time.Hour > 12 ? $"{time.Hour - 12}PM" : $"{time.Hour}AM",
				// Day of the year 
				"j" => time.DayOfYear.ToString(),
				// Month of the year
				"m" => string.Empty,
				// Minutes after the hour 
				"M" => string.Empty,
				"P" or "p" => string.Empty,
				// Seconds after the minute
				"S" => string.Empty,
				// Week of the year from 1rst Sunday
				"U" => string.Empty,
				// Day of the week. 0 = Sunday
				"w" => string.Empty,
				// Week of the year from 1rst Monday
				"W" => string.Empty,
				// Date 
				"x" => string.Empty,
				// Time
				"X" => time.DateTime.ToShortTimeString(),
				// Two-digit year
				"y" => time.Year.ToString("{0:2}"),
				// Four-digit year
				"Y" => time.Year.ToString(),
				// Time zone
				"Z" => time.Offset.ToString(),
				// $ character
				"$" => "$",
				_ => string.Empty,
			});

	public static string TimeSpanFormat(DateTimeOffset time, string format)
		=> TimeFormatMatchRegex.Replace(format, match =>
			{
				var character = match.Groups["Character"];
				var adjustment = match.Groups["Adjustment"].Success
					? match.Groups["Adjustment"].Value
					: null;
				var pad = adjustment?.Contains('x') ?? false;
				var append = adjustment?.Contains('z') ?? false;

				return character.Value switch
				{
					// The number of seconds
					"s" => string.Empty,
					// The number of seconds
					"S" => string.Empty,
					// The number of minutes
					"m" => string.Empty,
					// The number of minutes
					"M" => string.Empty,
					// The number of weeks
					"w" => string.Empty,
					// The number of weeks
					"W" => string.Empty,
					// The number of hours
					"h" => string.Empty,
					// The number of hours
					"H" => string.Empty,
					// The number of days
					"d" => string.Empty,
					// The number of days
					"D" => string.Empty,
					// The number of 365-day years 
					"y" => string.Empty,
					// The number of 365-day years
					"Y" => string.Empty,
					// $ character
					"$" => "$",
					_ => string.Empty,
				};
			});

	[Flags]
	public enum LocateFlags
	{
		NoTypePreference = 0,
		ExitsPreference,
		PreferLockPass,
		PlayersPreference,
		RoomsPreference,
		ThingsPreference,
		FailIfNotPreferred,
		UseLastIfAmbiguous,
		AbsoluteMatch,
		ExitsInTheRoomOfLooker,
		ExitsInsideOfLooker,
		MatchHereForLookerLocation,
		MatchObjectsInLookerInventory,
		MatchAgainstLookerLocationName,
		MatchRemoteContents,
		MatchMeForLooker,
		MatchObjectsInLookerLocation,
		MatchWildCardForPlayerName,
		MatchOptionalWildCardForPlayerName,
		EnglishStyleMatching,
		All,
		NoPartialMatches,
		MatchLookerControlledObjects
	}

	public static IEnumerable<string> TypePreferences(LocateFlags flags) =>
		((string[])["Player", "Thing", "Room", "Exit"]).Where(x =>
			x switch
			{
				"Player" => flags.HasFlag(LocateFlags.PlayersPreference),
				"Thing" => flags.HasFlag(LocateFlags.ThingsPreference),
				"Room" => flags.HasFlag(LocateFlags.RoomsPreference),
				"Exit" => flags.HasFlag(LocateFlags.ExitsPreference),
				_ => false
			});

	public static bool HasObjectFlags(SharpObject obj, SharpObjectFlag flag)
		=> obj.Flags!.Contains(flag);

	public static bool HasObjectPowers(SharpObject obj, string power) =>
		obj.Powers!.Any(x => x.Name == power || x.Alias == power);

	public static string Locate(
		IMUSHCodeParser parser,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> looker,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> executor,
		string name,
		LocateFlags flags)
	{
		if ((flags &
			~(LocateFlags.PreferLockPass
			| LocateFlags.FailIfNotPreferred
			| LocateFlags.NoPartialMatches
			| LocateFlags.MatchLookerControlledObjects)) != 0)
		{
			flags |= (LocateFlags.All | LocateFlags.MatchAgainstLookerLocationName | LocateFlags.ExitsInsideOfLooker);
		}

		if (((flags &
			(LocateFlags.MatchObjectsInLookerLocation
			| LocateFlags.MatchObjectsInLookerLocation
			| LocateFlags.MatchObjectsInLookerInventory
			| LocateFlags.MatchHereForLookerLocation
			| LocateFlags.ExitsPreference
			| LocateFlags.ExitsInsideOfLooker)) != 0) &&
			(!Nearby(executor, looker) && !executor.IsSee_All() && !parser.PermissionService.Controls(executor, looker))
			)
		{
			return "#-1 NOT PERMITTED TO EVALUATE ON LOOKER";
		}

		var match = LocateMatch(parser, executor, looker, flags, name, (flags & LocateFlags.UseLastIfAmbiguous) != 0);
		if (match.IsT5) return match.AsT5.Value;
		if (match.IsT4) return string.Empty;

		var result = match.WithoutError().WithoutNone();
		var location = FriendlyWhereIs(result);

		if (parser.PermissionService.CanExamine(executor, location.WithExitOption()) ||
			((!result.IsDarkLegal() || location.WithExitOption().IsLight() || result.IsLight()) && parser.PermissionService.CanInteract(result, executor, Library.Services.IPermissionService.InteractType.See)))
		{
			return result.Object().DBRef.ToString()!;
		}

		return string.Empty;
	}

	/* The real work. Here's the spec:
	 * str  --> "me"
	 *      --> "here"
	 *      --> "#dbref"
	 *      --> "*player"
	 *      --> adj-phrase name
	 *      --> name
	 * adj-phrase --> adj
	 *            --> adj count
	 *            --> count
	 * adj  --> "my", "me" (restrict match to inventory)
	 *      --> "here", "this", "this here" (restrict match to neighbor objects)
	 *      --> "toward" (restrict match to exits)
	 * count --> 1st, 21st, etc.
	 *       --> 2nd, 22nd, etc.
	 *       --> 3rd, 23rd, etc.
	 *       --> 4th, 10th, etc.
	 * name --> exit_alias
	 *      --> full_obj_name
	 *      --> partial_obj_name
	 *
	 * 1. Look for exact matches and return immediately:
	 *  a. "me" if requested
	 *  b. "here" if requested
	 *  c. #dbref, possibly with a control check
	 *  d. *player
	 * 2. Parse for adj-phrases and restrict further matching and/or
	 *    remember the object count
	 * 3. Look for matches (remote contents, neighbor, inventory, exits,
	 *    containers, carried exits)
	 *  a. If we don't have an object count, collect the number of exact
	 *     and partial matches and the best partial match.
	 *  b. If we do have an object count, collect the nth exact match
	 *     and the nth match (exact or partial). number of matches is always
	 *     0 or 1.
	 * 4. Make decisions
	 *  a. If we got a single exact match, return it
	 *  b. If we got multiple exact matches, complain
	 *  c. If we got no exact matches, but a single partial match, return it
	 *  d. If we got multiple partial matches, complain
	 *  e. If we got no matches, complain
	 */

	/* MATCHED() is called from inside the MATCH_LIST macro. full is 1 if the
		match was full/exact, and 0 if it was partial */
	private static OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None, OneOf.Types.Error<string>> LocateMatch(
		IMUSHCodeParser parser,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> looker,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> where,
		LocateFlags flags,
		string name,
		bool lastMatch)
	{
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None> match;
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None> bestMatch;
		OneOf<SharpPlayer, SharpRoom, SharpThing> location;
		int final = 0;
		int curr = 0;
		int right_type = 0;
		if (where.IsRoom())
		{
			location = where.MinusExit();
		}
		if (where.IsExit())
		{
			location = where.MinusRoom().Home();
		}
		else
		{
			location = FriendlyWhereIs(where);
		}

		if (!flags.HasFlag(LocateFlags.NoTypePreference)
			&& flags.HasFlag(LocateFlags.MatchMeForLooker)
			&& !flags.HasFlag(LocateFlags.MatchObjectsInLookerInventory)
			&& name.Equals("me", StringComparison.InvariantCultureIgnoreCase))
		{
			if (!flags.HasFlag(LocateFlags.MatchLookerControlledObjects)
				&& parser.PermissionService.Controls(looker, where))
			{
				return where.WithNoneOption().WithErrorOption();
			}
			return new OneOf.Types.Error<string>(Errors.ErrorPerm);
		}

		if (flags.HasFlag(LocateFlags.MatchHereForLookerLocation)
			&& !flags.HasFlag(LocateFlags.MatchObjectsInLookerInventory)
			&& name.Equals("here", StringComparison.InvariantCultureIgnoreCase))
		{
			if (!flags.HasFlag(LocateFlags.MatchLookerControlledObjects)
				&& parser.PermissionService.Controls(looker, where))
			{
				return FriendlyWhereIs(where).WithExitOption().WithNoneOption().WithErrorOption();
			}
			return new OneOf.Types.Error<string>(Errors.ErrorPerm);
		}

		if ((flags.HasFlag(LocateFlags.MatchOptionalWildCardForPlayerName) || flags.HasFlag(LocateFlags.PlayersPreference) && name.StartsWith('*'))
			&& (flags.HasFlag(LocateFlags.PlayersPreference) || flags.HasFlag(LocateFlags.NoTypePreference)))
		{
			// TODO: Fix Async
			var maybeMatch = parser.Database.GetPlayerByNameAsync(name).Result.FirstOrDefault();
			match = maybeMatch == null
				? OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None>.FromT4(new OneOf.Types.None())
				: OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None>.FromT0(maybeMatch);
			if (maybeMatch != null && (flags & LocateFlags.MatchObjectsInLookerInventory) != 0)
			{
				if (!flags.HasFlag(LocateFlags.MatchObjectsInLookerLocation)
					|| looker.HasLongFingers()
					|| Nearby(looker, match.WithoutNone())
					|| parser.PermissionService.Controls(looker, match.WithoutNone()))
				{
					if (!flags.HasFlag(LocateFlags.MatchLookerControlledObjects)
						&& parser.PermissionService.Controls(looker, where))
					{
						return match.WithErrorOption();
					}
					return new OneOf.Types.Error<string>(Errors.ErrorPerm);
				}
				else
				{
					bestMatch = match;
				}
			}
		}

		var abs = HelperFunctions.ParseDBRef(name);
		if (abs.IsSome())
		{
			var absObject = parser.Database.GetObjectNode(abs.Value());
			match = absObject;
			if (!match.IsT4 && (flags & LocateFlags.AbsoluteMatch) != 0)
			{
				if (!flags.HasFlag(LocateFlags.MatchObjectsInLookerLocation)
						|| looker.HasLongFingers()
						|| (Nearby(looker, match.WithoutNone())
						|| parser.PermissionService.Controls(looker, match.WithoutNone())))
				{
					if (!flags.HasFlag(LocateFlags.MatchLookerControlledObjects)
							&& parser.PermissionService.Controls(looker, where))
					{
						return match.WithErrorOption();
					}

					return new OneOf.Types.Error<string>(Errors.ErrorPerm);
				}
			}
		}

		if (flags.HasFlag(LocateFlags.EnglishStyleMatching))
		{
			(name, flags, final) = ParseEnglish(name, flags);
		}

		while (true)
		{
			if (flags.HasFlag(LocateFlags.MatchObjectsInLookerInventory | LocateFlags.MatchRemoteContents))
			{
				var contents = parser.Database.GetContentsAsync(where.WithNoneOption()).GetAwaiter().GetResult();
				// var a = Match_List(parser, contents, looker, where, flags, name);
			}

			if (flags.HasFlag(LocateFlags.MatchAgainstLookerLocationName)
					&& !flags.HasFlag(LocateFlags.MatchRemoteContents)
					&& location.Object().DBRef != where.Object().DBRef)
			{
				var contents = parser.Database.GetContentsAsync(location.WithExitOption().WithNoneOption()).GetAwaiter().GetResult();
				// MATCH_LIST(contents);
			}

			if (flags.HasFlag(LocateFlags.ExitsPreference) || flags.HasFlag(LocateFlags.NoTypePreference))
			{
				if (location.IsRoom() && flags.HasFlag(LocateFlags.ExitsPreference))
				{
					if (flags.HasFlag(LocateFlags.MatchRemoteContents)
						&& !flags.HasFlag(LocateFlags.MatchObjectsInLookerLocation | LocateFlags.MatchObjectsInLookerInventory))
					/* TODO: && IsRoom(Zone(loc) */
					{
						/* TODO: MATCH_LIST(Exits(Zone(loc))); */
					}
					if (flags.HasFlag(LocateFlags.All) && !flags.HasFlag(LocateFlags.MatchObjectsInLookerLocation | LocateFlags.MatchObjectsInLookerInventory))
					{
						var exits = parser.Database.GetContentsAsync(Library.Definitions.Configurable.MasterRoom).GetAwaiter().GetResult()!
							.Where(x => x.IsT1);

						// MATCH_LIST(exits)
					}
					if (location.IsRoom())
					{
						var exits = parser.Database.GetContentsAsync(location.WithExitOption().WithNoneOption()).GetAwaiter().GetResult()!
							.Where(x => x.IsT1);
						// MATCH_LIST(exits);
					}
				}
			}
			if (flags.HasFlag(LocateFlags.MatchObjectsInLookerInventory))
			{
				// MATCH_LIST(loc);
			}
			if (flags.HasFlag(LocateFlags.ExitsPreference) || flags.HasFlag(LocateFlags.NoTypePreference))
			{
				if (flags.HasFlag(LocateFlags.ExitsInsideOfLooker)
					&& where.IsRoom()
					&& ((location.Object().DBRef != where.Object().DBRef) || !flags.HasFlag(LocateFlags.ExitsPreference)))
				{
					var exits = parser.Database.GetContentsAsync(where.WithNoneOption()).GetAwaiter().GetResult()!
							.Where(x => x.IsT1);

					// MATCH_LIST(exits);
				}
			}
			break;
		}

		bestMatch = new None();

		if (bestMatch.IsT4 && final != 0)
		{
			return new None();
		}
		else if (final == 0 && curr > 1)
		{
			if (right_type != 1 && !flags.HasFlag(LocateFlags.UseLastIfAmbiguous))
			{
				return new Error<string>(Errors.ErrorAmbiguous);
			}
			return bestMatch.WithErrorOption();
		}

		return new None();
	}

	public static (OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None, Error<string>> BestMatch, int Final, int Curr, int RightType, int Exact, ControlFlow c) Match_List(IMUSHCodeParser parser,
		IEnumerable<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None>> list,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> looker,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> where,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None, Error<string>> bestMatch,
		int exact,
		int final, 
		int curr, 
		int rightType,
		LocateFlags flags,
		string name)
	{
		ControlFlow flow;

		foreach (var item in list)
		{
			if (item.IsT4) continue;

			var cur = item.WithoutNone();
			if (flags.HasFlag(LocateFlags.PlayersPreference) && !cur.IsPlayer()
				|| flags.HasFlag(LocateFlags.RoomsPreference) && !cur.IsRoom()
				|| flags.HasFlag(LocateFlags.ExitsPreference) && !cur.IsExit()
				|| flags.HasFlag(LocateFlags.ThingsPreference) && !cur.IsThing())
			{
				continue;
			}
			var abs = HelperFunctions.ParseDBRef(name);
			if (abs.IsSome() && cur.Object().DBRef == abs.Value())
			{
				(bestMatch,final, curr, rightType, exact, flow) = 
					Matched(parser, true, exact, final, curr, rightType, looker, where, cur, bestMatch, flags);
				
				if (flow == ControlFlow.Break) break;
				else if (flow == ControlFlow.Continue) continue;
			}
			else if (!parser.PermissionService.CanInteract(cur, looker, Library.Services.IPermissionService.InteractType.Match))
			{
				continue;
			}
			else if (cur.IsPlayer() && (cur.AsT0).Aliases!.Contains(name)
				|| (!cur.IsExit()
					&& !string.Equals(cur.Object().Name, name, StringComparison.OrdinalIgnoreCase)))
			{
				(bestMatch, final, curr, rightType, exact, flow) = 
					Matched(parser, true, exact, final, curr, rightType, looker, where, cur, bestMatch, flags);
				
				if (flow == ControlFlow.Break) break;
				else if (flow == ControlFlow.Continue) continue;
			}
			else if (!flags.HasFlag(LocateFlags.NoPartialMatches) && !cur.IsExit() && cur.Object().Name.Equals(name, StringComparison.OrdinalIgnoreCase))
			{
				(bestMatch, final, curr, rightType, exact, flow) = 
					Matched(parser, false, exact, final, curr, rightType, looker, where, cur, bestMatch, flags);
				
				if (flow == ControlFlow.Break) break;
				else if (flow == ControlFlow.Continue) continue;
			}
		}

		throw new NotImplementedException();
	}

	public static OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> ChooseThing(IMUSHCodeParser parser, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> who, int preferredType, LocateFlags flags, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> thing1, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> thing2)
	{
		if (thing1.IsT4 && thing2.IsT4)
		{
			if (thing1.IsT4)
			{
				return thing2;
			}
			else
			{
				return thing1;
			}
		}
		else if (thing1.IsT4)
		{
			return thing2;
		}
		else if (thing2.IsT4)
		{
			return thing1;
		}

		if (preferredType != 0)
		{
			if (TypePreferences(flags).Contains(thing1.Object()!.Type))
			{
				if (!TypePreferences(flags).Contains(thing2.Object()!.Type))
				{
					return thing1;
				}
			}
			else if (TypePreferences(flags).Contains(thing2.Object()!.Type))
			{
				return thing2;
			}
		}


		if (flags.HasFlag(LocateFlags.PreferLockPass))
		{
			var key = parser.PermissionService.CouldDoIt(who, thing1, null);
			if (!key && parser.PermissionService.CouldDoIt(who, thing2, null))
			{
				return thing2;
			}
			else if (key && !parser.PermissionService.CouldDoIt(who, thing2, null))
			{
				return thing1;
			}
		}
		return thing2;
	}

	public static (OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None, Error<string>> BestMatch, int Final, int Curr, int RightType, int Exact, ControlFlow c) Matched(
		IMUSHCodeParser parser,
		bool full,
		int exact,
		int final,
		int curr,
		int right_type,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> looker,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> where,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> cur,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None, Error<string>> bestMatch,
		LocateFlags flags)
	{
		if (!(!flags.HasFlag(LocateFlags.MatchLookerControlledObjects)
				&& parser.PermissionService.Controls(looker, where)))
		{
			return (new Error<string>(Errors.ErrorPerm), final, curr, right_type, exact, ControlFlow.Continue);
		}
		if (final != 0)
		{
			OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> bm = new None();
			// TODO: Make this call BEST_MATCH()
			if (bm.WithoutNone().Object().DBRef != cur.Object().DBRef)
			{
				return (new None(), final, curr, right_type, exact, ControlFlow.Continue);
			}

			if (full)
			{
				if (exact != 0)
				{
					//  Another exact match 
					curr++;
				}
				else
				{
					//  Ignore any previous partial matches now we have an exact match 
					exact = 1;
					curr = 1;
					right_type = 0;
				}
			}
			else
			{
				//  Another partial match 
				curr++;
			}

			if (!flags.HasFlag(LocateFlags.NoTypePreference) 
				&& !bestMatch.IsT5 && !bestMatch.IsT4 
				&& (bestMatch.WithoutError().WithoutNone().Object().Type == cur.Object().Type))
			{
				right_type++;
			}
		}
		else
		{
			curr++;
			if (curr == final)
			{
				return (cur.WithNoneOption().WithErrorOption(), final, curr, right_type, exact, ControlFlow.Break);
			}
		}
		return (cur.WithNoneOption().WithErrorOption(), final, curr, right_type, exact, ControlFlow.None);
	}


	public static DBRef? WhereIs(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> thing)
	{
		if (thing.IsT1) return null;
		var minusRoom = thing.MinusRoom();
		if (thing.IsT2) return OneOfExtensions.Home(minusRoom).Object()?.DBRef;
		else return OneOfExtensions.Location(minusRoom).Object()?.DBRef;
	}

	public static OneOf<SharpPlayer, SharpRoom, SharpThing> FriendlyWhereIs(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> thing)
	{
		if (thing.IsT1) return thing.AsT1;
		var minusRoom = thing.MinusRoom();
		if (thing.IsT2) return OneOfExtensions.Home(minusRoom);
		else return OneOfExtensions.Location(minusRoom);
	}

	public static bool Nearby(
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj1,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj2)
	{
		if (obj1.IsRoom() && obj2.IsRoom()) return false;

		var loc1 = FriendlyWhereIs(obj1).Object().DBRef;

		if (loc1 == obj2.Object().DBRef) return true;

		var loc2 = FriendlyWhereIs(obj2).Object().DBRef;

		return (loc2 == obj1.Object()!.DBRef) || (loc2 == loc1);
	}

	private static (string RemainingString, LocateFlags NewFlags, int Count) ParseEnglish(
		string oldName,
		LocateFlags oldFlags)
	{
		LocateFlags flags = oldFlags;
		LocateFlags saveFlags = flags;
		string name = oldName;
		string saveName = name;
		int count = 0;

		if ((flags & LocateFlags.MatchObjectsInLookerLocation) != 0)
		{
			if (name.StartsWith("this here ", StringComparison.OrdinalIgnoreCase))
			{
				name = name[10..];
				flags &= ~(LocateFlags.MatchObjectsInLookerInventory | LocateFlags.ExitsInTheRoomOfLooker);
			}
			else if (name.StartsWith("here ", StringComparison.OrdinalIgnoreCase) || name.StartsWith("this ", StringComparison.OrdinalIgnoreCase))
			{
				name = name[5..];
				flags &= ~(LocateFlags.MatchObjectsInLookerInventory | LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.MatchAgainstLookerLocationName);
			}
		}

		if (((flags & LocateFlags.MatchObjectsInLookerInventory) != 0) && (name.StartsWith("my ", StringComparison.OrdinalIgnoreCase) || name.StartsWith("me ", StringComparison.OrdinalIgnoreCase)))
		{
			name = name[3..];
			flags &= ~(LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.MatchAgainstLookerLocationName);
		}

		if (((flags & (LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.ExitsInsideOfLooker)) != 0) && (name.StartsWith("toward ", StringComparison.OrdinalIgnoreCase)))
		{
			name = name[7..];
			flags &= ~(LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.MatchObjectsInLookerInventory | LocateFlags.MatchAgainstLookerLocationName);
		}

		name = name.TrimStart();

		if (string.IsNullOrWhiteSpace(name))
		{
			return (saveName, saveFlags, 0);
		}

		if (!char.IsDigit(name[0]))
		{
			return (name, flags, 0);
		}

		var mName = name.Split(' ').FirstOrDefault();
		if (string.IsNullOrWhiteSpace(mName))
		{
			return (name, flags, 0);
		}

		var ordinalMatch = NthRegex.Match(mName);

		if (ordinalMatch.Success)
		{
			count = int.Parse(ordinalMatch.Groups["Number"].Value);
			var ordinal = ordinalMatch.Groups["Ordinal"].Value;

			// This is really only valid in English.
			if (count < 1
				|| Enumerable.Range(10, 14).Contains(count) && !ordinal.Equals("th", StringComparison.CurrentCultureIgnoreCase)
				|| count % 10 == 1 && !ordinal.Equals("st", StringComparison.CurrentCultureIgnoreCase)
				|| count % 10 == 2 && !ordinal.Equals("nd", StringComparison.CurrentCultureIgnoreCase)
				|| count % 10 == 3 && !ordinal.Equals("rd", StringComparison.CurrentCultureIgnoreCase)
				|| ordinal != "th")
			{
				return (name, flags, 0);
			}
		}

		return (name[mName.Length..].TrimStart(), flags, count);
	}

	public static IEnumerable<OneOf<DBRef, string>> NameList(string list)
		=> NameListPatternRegex.Matches(list).Cast<Match>().Select(x =>
			!string.IsNullOrWhiteSpace(x.Groups["DBRef"].Value)
				? OneOf<DBRef, string>.FromT0(HelperFunctions.ParseDBRef(x.Groups["DBRef"].Value).Value())
				: OneOf<DBRef, string>.FromT1(x.Groups["User"].Value));

	/// <summary>
	/// A regular expression that matches one or more names in a list format.
	/// </summary>
	/// <returns>A regex that has a named group for the match.</returns>
	[GeneratedRegex("(\"(?<User>.+?)\"|(?<DBRef>#\\d+(:\\d+)?)|(?<User>\\S+))(\\s+|$)")]
	private static partial Regex NameListPattern();

	/// <summary>
	/// A regular expression that puts in time formats, with the ability to escape $ with another $.
	/// </summary>
	/// <returns>A regex that has a match for each replacement.</returns>
	[GeneratedRegex(@"\$(?<Character>[aAbBcdHIjmMpSUwWxXyYZ\$])")]
	private static partial Regex TimeFormatMatch();

	/// <summary>
	/// A regular expression that puts in time formats, with the ability to escape $ with another $.
	/// </summary>
	/// <returns>A regex that has a match for each replacement.</returns>
	[GeneratedRegex(@"\$(?<Adjustment>z?x?|x?z?)(?<Character>[smwhdySMWHDY\$])")]
	private static partial Regex TimeSpanFormatMatch();

	/// <summary>
	/// A regular expression that checks if a string is a number followed by an ordinal indicator.
	/// </summary>
	/// <returns>A regex that has a Named Group for Number and Ordinal.</returns>
	[GeneratedRegex(@"^(?<Number>\d+)(?<Ordinal>rd|th|nd|st)$")]
	private static partial Regex Nth();
}
