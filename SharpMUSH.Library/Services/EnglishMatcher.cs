using SharpMUSH.Library.Services.Interfaces;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// PennMUSH <c>match.c</c>'s <c>parse_english</c>: the <c>this</c>/<c>here</c>/<c>my</c>/<c>toward</c>
/// adjectives that narrow a search's scope, and the <c>Nth</c> count adjective that picks among the
/// matches. Pure string and flag arithmetic over the name as typed.
/// </summary>
internal static partial class EnglishMatcher
{
	private static readonly Regex NthRegex = Nth();

	internal static (string RemainingString, LocateFlags NewFlags, int Count) Parse(
		string oldName,
		LocateFlags oldFlags)
	{
		var flags = oldFlags;
		var saveFlags = flags;
		var name = oldName;
		var saveName = name;
		var count = 0;

		// Each adjective narrows the search to the scope it names by clearing the others. The masks are
		// match.c's parse_english, member for member — they had drifted, and because they are cleared
		// rather than set, a wrong bit does not fail loudly: it silently searches somewhere the adjective
		// said not to, or refuses to search the one place it said to.
		if ((flags & LocateFlags.MatchObjectsInLookerLocation) != 0)
		{
			if (name.StartsWith("this here ", StringComparison.OrdinalIgnoreCase))
			{
				// MAT_POSSESSION | MAT_EXIT
				name = name[10..];
				flags &= ~(LocateFlags.MatchObjectsInLookerInventory | LocateFlags.ExitsInTheRoomOfLooker);
			}
			else if (name.StartsWith("here ", StringComparison.OrdinalIgnoreCase) ||
							 name.StartsWith("this ", StringComparison.OrdinalIgnoreCase))
			{
				// MAT_POSSESSION | MAT_EXIT | MAT_REMOTE_CONTENTS | MAT_CONTAINER
				name = name[5..];
				flags &= ~(LocateFlags.MatchObjectsInLookerInventory | LocateFlags.ExitsInTheRoomOfLooker |
									 LocateFlags.MatchRemoteContents | LocateFlags.MatchAgainstLookerLocationName);
			}
		}

		if (((flags & LocateFlags.MatchObjectsInLookerInventory) != 0) &&
				(name.StartsWith("my ", StringComparison.OrdinalIgnoreCase) ||
				 name.StartsWith("me ", StringComparison.OrdinalIgnoreCase)))
		{
			// MAT_NEIGHBOR | MAT_EXIT | MAT_CONTAINER | MAT_REMOTE_CONTENTS. MatchObjectsInLookerLocation
			// is the one that matters and the one that was missing — the exit bit appeared twice in its
			// place, so "my sword" went on searching the room.
			name = name[3..];
			flags &= ~(LocateFlags.MatchObjectsInLookerLocation | LocateFlags.ExitsInTheRoomOfLooker |
								 LocateFlags.MatchAgainstLookerLocationName | LocateFlags.MatchRemoteContents);
		}

		if (((flags & (LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.ExitsInsideOfLooker)) != 0) &&
				(name.StartsWith("toward ", StringComparison.OrdinalIgnoreCase)))
		{
			// MAT_NEIGHBOR | MAT_POSSESSION | MAT_CONTAINER | MAT_REMOTE_CONTENTS. Note which is absent:
			// the exit scope is the one "toward" selects, and clearing it — as this did — left the
			// adjective unable to match any exit at all.
			name = name[7..];
			flags &= ~(LocateFlags.MatchObjectsInLookerLocation | LocateFlags.MatchObjectsInLookerInventory |
								 LocateFlags.MatchAgainstLookerLocationName | LocateFlags.MatchRemoteContents);
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

		// match.c:560 — `mname = strchr(*name, ' '); if (!mname) return 0;`. A count with no noun after it
		// is not a count adjective at all, and the name stands as typed. Split(' ').FirstOrDefault()
		// hands back the *whole string* when there is no space, so a search for "2nd" became an ordinal
		// search for the empty string instead of a search for an object named "2nd".
		var space = name.IndexOf(' ');
		if (space < 0)
		{
			return (name, flags, 0);
		}

		var mName = name[..space];
		var ordinalMatch = NthRegex.Match(mName);

		// match.c:590 — an error like '0th' or '12nd' "wasn't really a count adjective. Reset and press
		// on", restoring the name rather than consuming the token. Falling through to the shared return
		// stripped it, so a thing named "5 Swords" was searched for as "Swords" and never found.
		if (!ordinalMatch.Success)
		{
			return (name, flags, 0);
		}

		// `\d` matches every Unicode decimal digit and caps no length, so this group can hold "\u0663" or
		// twenty nines — int.Parse answers those with FormatException and OverflowException, out of a
		// path any player reaches by typing `get 99999999999999999999th thing`. match.c runs strtoul,
		// which saturates and then fails the suffix test, so declining to read it as a count is the same
		// answer: the name stands as typed. NumberStyles.None also refuses a sign, which `\d+` cannot
		// produce but which int.Parse would otherwise accept.
		if (!int.TryParse(ordinalMatch.Groups["Number"].ValueSpan, NumberStyles.None,
					CultureInfo.InvariantCulture, out count))
		{
			return (name, flags, 0);
		}

		// Validate the ordinal suffix, following PennMUSH parse_english() rules:
		//   11th, 12th, 13th  → always "th"  (teen exception – not st/nd/rd)
		//   *1  (excl. 11)    → "st"
		//   *2  (excl. 12)    → "nd"
		//   *3  (excl. 13)    → "rd"
		//   everything else   → "th"
		var mod100 = count % 100;
		var isTeen = mod100 >= 11 && mod100 <= 13;
		var mod10 = count % 10;

		string expectedSuffix = (isTeen || mod10 == 0 || mod10 > 3) ? "th"
			: mod10 == 1 ? "st"
			: mod10 == 2 ? "nd"
			: "rd";

		if (count < 1
				|| !ordinalMatch.Groups["Ordinal"].ValueSpan.Equals(expectedSuffix, StringComparison.CurrentCultureIgnoreCase))
		{
			return (name, flags, 0);
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
