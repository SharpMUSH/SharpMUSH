using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Sends structured MUSH query commands via <see cref="ITerminalService"/> and parses the
/// results into typed models.  Each query is a softcode expression with embedded format markers
/// so output is unambiguous and permission-safe (the server enforces all MUSH permissions).
/// <see cref="ITerminalService.SendCommandAsync"/> returns the result over the out-of-band channel,
/// so structured output never appears in the visible terminal (or on other sessions).
///
/// What remains here is what genuinely IS softcode evaluation: free-form <c>lsearch</c>
/// expressions. The Softcode Editor's console evaluates through <see cref="GameCommandService"/>. Object info, attribute CRUD and object creation moved to
/// <see cref="ObjectApiService"/>, because this channel is line-delimited and so could not carry
/// an attribute value containing a newline without encoding it.
/// </summary>
public class MushQueryService(ITerminalService terminal)
{
	/// <summary>
	/// Returns true if the currently connected player has the WIZARD flag.
	/// Used to decide whether to run lsearch(all) or lsearch(me).
	/// </summary>
	public async Task<bool> IsWizardAsync()
	{
		var lines = await terminal.SendCommandAsync("hasflag(me,WIZARD)");
		return lines.Length > 0 && lines[0].Trim() == "1";
	}

	/// <summary>
	/// Return all objects owned by the currently logged-in player.
	/// Wizards automatically get lsearch(all) to see the full database.
	/// </summary>
	public async Task<List<MushSearchResult>> SearchOwnedAsync()
	{
		var isWiz = await IsWizardAsync();
		// lsearch(all) — for wizards returns every object; for mortals returns
		// only objects they can examine (engine enforces this automatically).
		// lsearch(me)  — strictly only objects owned by me.
		var expr = isWiz ? "lsearch(all)" : "lsearch(me)";
		return await SearchAsync(expr);
	}

	/// <summary>Search objects in the current location using lcon/lexits.</summary>
	public async Task<List<MushSearchResult>> GetContentsAsync()
	{
		// %l = the executor's location; squish() collapses the gaps when any sub-list is
		// empty. Includes room contents, its exits, and the room object (%l) itself.
		// before(%i0,:) strips the :creationTime suffix that DBRef.ToString() appends.
		var expr = "iter(squish([lcon(%l)] [lexits(%l)] %l),SHARP_OBJ:[before(%i0,:)]:[type(%i0)]:[name(%i0)],,%r)";
		var lines = await terminal.SendCommandAsync(expr);
		return ParseSearchResults(lines);
	}

	/// <summary>
	/// Execute a free-form softcode expression whose result is a space-separated
	/// list of dbrefs, and return typed search results.
	/// Examples: lsearch(me)  ·  lsearch(me, type, room)  ·  lcon(loc(me))
	/// </summary>
	public async Task<List<MushSearchResult>> SearchAsync(string expression)
	{
		// %i0 = current iter element; before(%i0,:) strips the :creationTime suffix.
		// ,,%r uses default space iSep and newline oSep (3rd arg = iSep, 4th = oSep)
		// An account's holder (#acct:<key>) is no object, so type() and name() would not find it; it is listed by key.
		var expr = $"iter({expression},[if(strmatch(%i0,#acct:*),SHARP_ACCT:[after(%i0,:)],SHARP_OBJ:[before(%i0,:)]:[type(%i0)]:[name(%i0)])],,%r)";
		var lines = await terminal.SendCommandAsync(expr);
		return ParseSearchResults(lines);
	}

	private static List<MushSearchResult> ParseSearchResults(string[] lines)
	{
		var results = new List<MushSearchResult>();

		results.AddRange(lines
			.Where(line => line.StartsWith("SHARP_ACCT:"))
			.Select(line => int.TryParse(line["SHARP_ACCT:".Length..], System.Globalization.NumberStyles.None,
				System.Globalization.CultureInfo.InvariantCulture, out var key) ? MushRefs.Account(key) : 0)
			.Where(MushRefs.IsAccount)
			.Select(holder => new MushSearchResult
			{
				Dbref = holder,
				Type = MushObjectType.Account,
				Name = MushRefs.Format(holder),
			}));

		foreach (var line in lines.Where(line => line.StartsWith("SHARP_OBJ:")))
		{
			// SHARP_OBJ:<dbref>:<type>:<name…>
			var parts = line.Split(':', 4);
			if (parts.Length < 4) continue;

			if (!int.TryParse(parts[1].TrimStart('#'), out var dbref)) continue;

			results.Add(new MushSearchResult
			{
				Dbref = dbref,
				Type = ParseType(parts[2]),
				Name = parts[3],
			});
		}

		return results;
	}

	private static MushObjectType ParseType(string typeStr) => typeStr.ToUpperInvariant() switch
	{
		"THING" => MushObjectType.Thing,
		"ROOM" => MushObjectType.Room,
		"EXIT" => MushObjectType.Exit,
		"PLAYER" => MushObjectType.Player,
		"ACCOUNT" => MushObjectType.Account,
		_ => MushObjectType.Unknown,
	};
}
