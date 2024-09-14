using OneOf.Types;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services;

public partial class LocateService : ILocateService
{
	private static readonly Regex NthRegex = Nth();

	public enum ControlFlow { Break, Continue, Return, None };

	public async ValueTask<AnyOptionalSharpObjectOrError> LocateAndNotifyIfInvalid(IMUSHCodeParser parser, AnySharpObject looker, AnySharpObject executor, string name, LocateFlags flags)
	{
		var loc = await Locate(parser, looker, executor, name, flags);
		if (!loc.IsValid())
		{
			await parser.NotifyService.Notify(executor, loc.IsError ? loc.AsError.Value : "I can't see that here");
		}
		return loc;
	}

	public async ValueTask<AnyOptionalSharpObjectOrError> Locate(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		LocateFlags flags)
	{
		if ((flags &
		     ~(LocateFlags.PreferLockPass
		       | LocateFlags.FailIfNotPreferred
		       | LocateFlags.NoPartialMatches
		       | LocateFlags.OnlyMatchLookerControlledObjects)) != 0)
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
			return new Error<string>("#-1 NOT PERMITTED TO EVALUATE ON LOOKER");
		}

		var match = await LocateMatch(parser, executor, looker, flags, name, (flags & LocateFlags.UseLastIfAmbiguous) != 0);
		if (match.IsT5) return match.AsT5;
		if (match.IsT4) return new None();

		var result = match.WithoutError().WithoutNone();
		var location = FriendlyWhereIs(result);

		if (parser.PermissionService.CanExamine(executor, location.WithExitOption()) ||
		    ((!result.IsDarkLegal() || location.WithExitOption().IsLight() || result.IsLight()) && parser.PermissionService.CanInteract(result, executor, Library.Services.IPermissionService.InteractType.See)))
		{
			return result.WithNoneOption().WithErrorOption();
		}

		return new None();
	}

	private static async ValueTask<AnyOptionalSharpObjectOrError> LocateMatch(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject where,
		LocateFlags flags,
		string name,
		bool lastMatch)
	{
		AnyOptionalSharpObjectOrError match;
		AnyOptionalSharpObjectOrError bestMatch = new None();
		AnySharpContainer location;
		ControlFlow c;
		int final = 0;
		int curr = 0;
		bool exact = false;
		int right_type = 0;

		if (where.IsRoom)
		{
			location = where.MinusExit();
		}
		if (where.IsExit)
		{
			location = where.MinusRoom().Home();
		}
		else
		{
			location = FriendlyWhereIs(where);
		}

		if (true // !flags.HasFlag(LocateFlags.NoTypePreference) // TODO: Incorrect check.
		    && flags.HasFlag(LocateFlags.MatchMeForLooker)
		    && !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerInventory)
		    && name.Equals("me", StringComparison.InvariantCultureIgnoreCase))
		{
			if (!flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
			    && parser.PermissionService.Controls(looker, where))
			{
				return where.WithNoneOption().WithErrorOption();
			}
			return new Error<string>(Errors.ErrorPerm);
		}

		if (flags.HasFlag(LocateFlags.MatchHereForLookerLocation)
		    && !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerInventory)
		    && name.Equals("here", StringComparison.InvariantCultureIgnoreCase))
		{
			if (!flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
			    && parser.PermissionService.Controls(looker, where))
			{
				return FriendlyWhereIs(where).WithExitOption().WithNoneOption().WithErrorOption();
			}
			return new Error<string>(Errors.ErrorPerm);
		}

		if ((flags.HasFlag(LocateFlags.MatchOptionalWildCardForPlayerName) || flags.HasFlag(LocateFlags.PlayersPreference)
			    && name.StartsWith('*'))
		    && (flags.HasFlag(LocateFlags.PlayersPreference) || flags.HasFlag(LocateFlags.NoTypePreference)))
		{
			// TODO: Fix Async
			var maybeMatch = (await parser.Database.GetPlayerByNameAsync(name)).FirstOrDefault();
			match = maybeMatch == null
				? new None()
				: maybeMatch;
			if (maybeMatch != null && flags.HasFlag(LocateFlags.MatchObjectsInLookerInventory))
			{
				if (!flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerLocation)
				    || looker.HasLongFingers()
				    || Nearby(looker, match.WithoutError().WithoutNone())
				    || parser.PermissionService.Controls(looker, match.WithoutError().WithoutNone()))
				{
					// TODO: This doesn't look right.
					if (!flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
					    && parser.PermissionService.Controls(looker, where))
					{
						return match;
					}
					return new Error<string>(Errors.ErrorPerm);
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
			match = absObject.WithErrorOption();
			if (!match.IsT4 && (flags & LocateFlags.AbsoluteMatch) != 0)
			{
				if (!flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerLocation)
				    || looker.HasLongFingers()
				    || (Nearby(looker, match.WithoutError().WithoutNone())
				        || parser.PermissionService.Controls(looker, match.WithoutError().WithoutNone())))
				{
					if (!(flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
					      && !parser.PermissionService.Controls(looker, where)))
					{
						return match;
					}

					return new Error<string>(Errors.ErrorPerm);
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
				var contents = parser.Database
					.GetContentsAsync(where.WithNoneOption())
					.GetAwaiter().GetResult()!
					.Select(x => x.WithRoomOption());
				(bestMatch, final, curr, right_type, exact, c) =
					Match_List(parser, contents, looker, where, bestMatch, exact, final, curr, right_type, flags, name);
				if (c == ControlFlow.Break) break;
				if (c == ControlFlow.Return) break;
			}

			if (flags.HasFlag(LocateFlags.MatchAgainstLookerLocationName)
			    && !flags.HasFlag(LocateFlags.MatchRemoteContents)
			    && location.Object().DBRef != where.Object().DBRef)
			{
				var contents = parser.Database
					.GetContentsAsync(location.WithExitOption().WithNoneOption())
					.GetAwaiter().GetResult()!
					.Select(x => x.WithRoomOption());
				(bestMatch, final, curr, right_type, exact, c) =
					Match_List(parser, contents, looker, where, bestMatch, exact, final, curr, right_type, flags, name);
				if (c == ControlFlow.Break) break;
				if (c == ControlFlow.Return) break;
			}

			if (flags.HasFlag(LocateFlags.ExitsPreference) || flags.HasFlag(LocateFlags.NoTypePreference))
			{
				if (location.IsRoom && flags.HasFlag(LocateFlags.ExitsPreference))
				{
					if (flags.HasFlag(LocateFlags.MatchRemoteContents)
					    && !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerLocation | LocateFlags.OnlyMatchObjectsInLookerInventory))
						/* TODO: && IsRoom(Zone(loc) */
					{
						/* TODO: MATCH_LIST(Exits(Zone(loc))); */
						throw new NotImplementedException();
					}
					if (flags.HasFlag(LocateFlags.All)
					    && !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerLocation | LocateFlags.OnlyMatchObjectsInLookerInventory))
					{
						var exits = parser.Database
							.GetContentsAsync(Library.Definitions.Configurable.MasterRoom)
							.GetAwaiter().GetResult()!
							.Where(x => x.IsT1)!
							.Select(x => x.WithRoomOption());

						(bestMatch, final, curr, right_type, exact, c) = Match_List(parser, exits, looker, where, bestMatch, exact, final, curr, right_type, flags, name);
						if (c == ControlFlow.Break) break;
						if (c == ControlFlow.Return) break;
					}
					if (location.IsRoom)
					{
						var exits = parser.Database
							.GetContentsAsync(location.WithExitOption().WithNoneOption())
							.GetAwaiter().GetResult()!
							.Where(x => x.IsT1)
							.Select(x => x.WithRoomOption());
						(bestMatch, final, curr, right_type, exact, c) = Match_List(parser, exits, looker, where, bestMatch, exact, final, curr, right_type, flags, name);
						if (c == ControlFlow.Break) break;
						if (c == ControlFlow.Return) break;
					}
				}
			}
			if (flags.HasFlag(LocateFlags.MatchObjectsInLookerInventory))
			{
				(bestMatch, final, curr, right_type, exact, c) = Match_List(parser, [location.WithExitOption()], looker, where, bestMatch, exact, final, curr, right_type, flags, name);
				if (c == ControlFlow.Break) break;
				if (c == ControlFlow.Return) break;
			}
			if (flags.HasFlag(LocateFlags.ExitsPreference) || flags.HasFlag(LocateFlags.NoTypePreference))
			{
				if (flags.HasFlag(LocateFlags.ExitsInsideOfLooker)
				    && where.IsRoom
				    && ((location.Object().DBRef != where.Object().DBRef) || !flags.HasFlag(LocateFlags.ExitsPreference)))
				{
					var exits = parser.Database
						.GetContentsAsync(where.WithNoneOption())
						.GetAwaiter().GetResult()!
						.Where(x => x.IsT1)
						.Select(x => x.WithRoomOption());

					(bestMatch, final, curr, right_type, exact, c) = Match_List(parser, exits, looker, where, bestMatch, exact, final, curr, right_type, flags, name);
				}
			}
			break;
		}

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
			return bestMatch;
		}

		return new None();
	}

	public static (AnyOptionalSharpObjectOrError BestMatch, int Final, int Curr, int RightType, bool Exact, ControlFlow c) Match_List(
		IMUSHCodeParser parser,
		IEnumerable<AnySharpObject> list,
		AnySharpObject looker,
		AnySharpObject where,
		AnyOptionalSharpObjectOrError bestMatch,
		bool exact,
		int final,
		int curr,
		int rightType,
		LocateFlags flags,
		string name)
	{
		ControlFlow flow;

		foreach (var item in list)
		{
			var cur = item;
			if (flags.HasFlag(LocateFlags.PlayersPreference) && !cur.IsPlayer
			    || flags.HasFlag(LocateFlags.RoomsPreference) && !cur.IsRoom
			    || flags.HasFlag(LocateFlags.ExitsPreference) && !cur.IsExit
			    || flags.HasFlag(LocateFlags.ThingsPreference) && !cur.IsThing)
			{
				continue;
			}
			var abs = HelperFunctions.ParseDBRef(name);
			if (abs.IsSome() && cur.Object().DBRef == abs.Value())
			{
				(bestMatch, final, curr, rightType, exact, flow) =
					Matched(parser, true, exact, final, curr, rightType, looker, where, cur, bestMatch, flags);

				if (flow == ControlFlow.Break) break;
				else if (flow == ControlFlow.Continue) continue;
				else if (flow == ControlFlow.Return) return (bestMatch, final, curr, rightType, exact, ControlFlow.Return);
			}
			else if (!parser.PermissionService.CanInteract(cur, looker, Library.Services.IPermissionService.InteractType.Match))
			{
				continue;
			}
			else if (cur.IsPlayer && cur.AsPlayer.Aliases!.Contains(name)
			         || (!cur.IsExit
			             && !string.Equals(cur.Object().Name, name, StringComparison.OrdinalIgnoreCase)))
			{
				(bestMatch, final, curr, rightType, exact, flow) =
					Matched(parser, true, exact, final, curr, rightType, looker, where, cur, bestMatch, flags);

				if (flow == ControlFlow.Break) break;
				else if (flow == ControlFlow.Continue) continue;
				else if (flow == ControlFlow.Return) return (bestMatch, final, curr, rightType, exact, ControlFlow.Return);
			}
			else if (!flags.HasFlag(LocateFlags.NoPartialMatches)
			         && !cur.IsExit
			         && cur.Object().Name.Equals(name, StringComparison.OrdinalIgnoreCase))
			{
				(bestMatch, final, curr, rightType, exact, flow) =
					Matched(parser, false, exact, final, curr, rightType, looker, where, cur, bestMatch, flags);

				if (flow == ControlFlow.Break) break;
				else if (flow == ControlFlow.Continue) continue;
				else if (flow == ControlFlow.Return) return (bestMatch, final, curr, rightType, exact, ControlFlow.Return);
			}
		}

		return (bestMatch, final, curr, rightType, exact, ControlFlow.Break);
	}

	public static AnyOptionalSharpObject ChooseThing(IMUSHCodeParser parser, AnySharpObject who, LocateFlags flags, AnyOptionalSharpObject thing1, AnyOptionalSharpObject thing2)
	{
		if (thing1.IsNone() && thing2.IsNone()) return thing1.IsNone() ? thing2 : thing1;
		else if (thing1.IsNone()) return thing2;
		else if (thing2.IsNone()) return thing1;

		if (TypePreferences(flags).Contains(thing1.Object()!.Type) && !TypePreferences(flags).Contains(thing2.Object()!.Type)) return thing1;
		else if (TypePreferences(flags).Contains(thing2.Object()!.Type)) return thing2;

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

	public static (AnyOptionalSharpObjectOrError BestMatch, int Final, int Curr, int RightType, bool Exact, ControlFlow c) Matched(
		IMUSHCodeParser parser,
		bool full,
		bool exact,
		int final,
		int curr,
		int right_type,
		AnySharpObject looker,
		AnySharpObject where,
		AnySharpObject cur,
		AnyOptionalSharpObjectOrError bestMatch,
		LocateFlags flags)
	{
		if (!(!flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
		      && parser.PermissionService.Controls(looker, where)))
		{
			return (new Error<string>(Errors.ErrorPerm), final, curr, right_type, exact, ControlFlow.Continue);
		}
		if (final != 0)
		{
			AnyOptionalSharpObject bm = ChooseThing(parser, looker, flags, bestMatch.WithoutError(), cur.WithNoneOption());
			if (bm.WithoutNone().Object().DBRef != cur.Object().DBRef)
			{
				return (new None(), final, curr, right_type, exact, ControlFlow.Continue);
			}

			if (full)
			{
				if (exact)
				{
					//  Another exact match 
					curr++;
				}
				else
				{
					//  Ignore any previous partial matches now we have an exact match 
					exact = true;
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
			    && bestMatch.IsValid()
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
				return (cur.WithNoneOption().WithErrorOption(), final, curr, right_type, exact, ControlFlow.Return);
			}
		}
		return (cur.WithNoneOption().WithErrorOption(), final, curr, right_type, exact, ControlFlow.None);
	}



	public static DBRef? WhereIs(AnySharpObject thing)
	{
		if (thing.IsRoom) return null;
		var minusRoom = thing.MinusRoom();
		if (thing.IsExit) return OneOfExtensions.Home(minusRoom).Object()?.DBRef;
		else return OneOfExtensions.Location(minusRoom).Object()?.DBRef;
	}

	public static AnySharpContainer FriendlyWhereIs(AnySharpObject thing)
	{
		if (thing.IsRoom) return thing.AsT1;
		var minusRoom = thing.MinusRoom();
		if (thing.IsExit) return minusRoom.Home();
		else return minusRoom.Location();
	}

	public static bool Nearby(
		AnySharpObject obj1,
		AnySharpObject obj2)
	{
		if (obj1.IsRoom && obj2.IsRoom) return false;

		var loc1 = FriendlyWhereIs(obj1).Object().DBRef;

		if (loc1 == obj2.Object().DBRef) return true;

		var loc2 = FriendlyWhereIs(obj2).Object().DBRef;

		return (loc2 == obj1.Object()!.DBRef) || (loc2 == loc1);
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

	private static (string RemainingString, LocateFlags NewFlags, int Count) ParseEnglish(
		string oldName,
		LocateFlags oldFlags)
	{
		var flags = oldFlags;
		var saveFlags = flags;
		var name = oldName;
		var saveName = name;
		var count = 0;

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

	/// <summary>
	/// A regular expression that checks if a string is a number followed by an ordinal indicator.
	/// </summary>
	/// <returns>A regex that has a Named Group for Number and Ordinal.</returns>
	[GeneratedRegex(@"^(?<Number>\d+)(?<Ordinal>rd|th|nd|st)$")]
	private static partial Regex Nth();
}