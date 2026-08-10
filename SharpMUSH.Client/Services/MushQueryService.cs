using System.Text.RegularExpressions;
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
/// expressions and <c>u()</c>. Object info, attribute CRUD and object creation moved to
/// <see cref="ObjectApiService"/>, because this channel is line-delimited and so could not carry
/// an attribute value containing a newline without encoding it.
/// </summary>
public partial class MushQueryService(ITerminalService terminal, ILogger<MushQueryService> logger)
{
	private readonly ILogger<MushQueryService> _logger = logger;

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
		var expr = $"iter({expression},SHARP_OBJ:[before(%i0,:)]:[type(%i0)]:[name(%i0)],,%r)";
		var lines = await terminal.SendCommandAsync(expr);
		return ParseSearchResults(lines);
	}

	/// <summary>
	/// Evaluate the attribute on <paramref name="dbref"/> using <c>u()</c> so MUSH evaluates
	/// the attribute in object context. The result is returned over the out-of-band channel.
	/// </summary>
	public Task<string[]> EvalAsync(string dbref, string attrName)
		=> terminal.SendCommandAsync($"u({dbref}/{attrName})");

	private static List<MushSearchResult> ParseSearchResults(string[] lines)
	{
		var results = new List<MushSearchResult>();

		foreach (var line in lines)
		{
			if (!line.StartsWith("SHARP_OBJ:")) continue;

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

	/// <summary>
	/// Also handles raw examine output as a fallback for when think-based commands aren't
	/// available.  Parses PennMUSH <c>examine #dbref</c> output line-by-line.
	/// </summary>
	public static MushObject? ParseExamineOutput(string[] lines)
	{
		if (lines.Length == 0) return null;

		var obj = new MushObject();
		var inAttributes = false;

		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i];

			if (i == 0)
			{
				// First line: Name(#DbrefFlags)
				var m = HeaderRegex().Match(line);
				if (m.Success)
				{
					obj.Name = m.Groups[1].Value.Trim();
					if (int.TryParse(m.Groups[2].Value, out var dbref)) obj.Dbref = dbref;
					obj.Type = ParseTypeChar(m.Groups[3].Value);
					obj.Flags = m.Groups[4].Value;
				}
				continue;
			}

			if (line.TrimStart().StartsWith("Owner:", StringComparison.OrdinalIgnoreCase))
			{
				var ownerMatch = OwnerRegex().Match(line);
				if (ownerMatch.Success) obj.Owner = $"{ownerMatch.Groups[1].Value}(#{ownerMatch.Groups[2].Value})";
				continue;
			}

			if (line.Equals("Attributes:", StringComparison.OrdinalIgnoreCase))
			{
				inAttributes = true;
				continue;
			}

			if (!inAttributes) continue;

			// Attribute line: "  ATTRNAME[/FLAGS]: value"
			var attrMatch = AttributeRegex().Match(line);
			if (!attrMatch.Success) continue;

			obj.Attributes.Add(new MushAttribute
			{
				Name = attrMatch.Groups[1].Value,
				AttributeFlags = string.IsNullOrEmpty(attrMatch.Groups[2].Value)
					? []
					: [.. attrMatch.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)],
				Value = attrMatch.Groups[3].Value,
			});
		}

		return obj.Dbref == 0 ? null : obj;
	}

	private static MushObjectType ParseType(string typeStr) => typeStr.ToUpperInvariant() switch
	{
		"THING" => MushObjectType.Thing,
		"ROOM" => MushObjectType.Room,
		"EXIT" => MushObjectType.Exit,
		"PLAYER" => MushObjectType.Player,
		_ => MushObjectType.Unknown,
	};

	private static MushObjectType ParseTypeChar(string typeChar) => typeChar.ToUpperInvariant() switch
	{
		"T" => MushObjectType.Thing,
		"R" => MushObjectType.Room,
		"E" => MushObjectType.Exit,
		"P" => MushObjectType.Player,
		_ => MushObjectType.Unknown,
	};

	[GeneratedRegex(@"^(.+)\(#(\d+)([TREPtrep]?)([A-Z]*)\)\s*$")]
	private static partial Regex HeaderRegex();

	[GeneratedRegex(@"Owner:\s+(.+?)\(#(\d+)")]
	private static partial Regex OwnerRegex();

	[GeneratedRegex(@"^\s{2,}([A-Z_][A-Z0-9_\-]*)(?:/([A-Z][A-Z ]*))?:\s?(.*)$")]
	private static partial Regex AttributeRegex();
}
