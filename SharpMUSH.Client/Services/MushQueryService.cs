using System.Text.RegularExpressions;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Sends structured MUSH query commands via <see cref="ITerminalService"/> and parses the
/// results into typed models.  Each query is a softcode expression with embedded format markers
/// so output is unambiguous and permission-safe (the server enforces all MUSH permissions).
/// <see cref="ITerminalService.SendCommandAsync"/> returns the result over the out-of-band channel,
/// so structured output never appears in the visible terminal (or on other sessions).
/// </summary>
public partial class MushQueryService(ITerminalService terminal, ILogger<MushQueryService> logger)
{
	private readonly ILogger<MushQueryService> _logger = logger;

	/// <summary>Retrieve basic details (name, type, owner) and the full attribute list for a single object.</summary>
	public async Task<MushObject?> GetObjectAsync(string dbref)
	{
		_logger.LogDebug("GetObjectAsync {Dbref}", dbref);
		var infoExpr = $"SHARP_INFO:{dbref}:[name({dbref})]:[type({dbref})]:[owner({dbref})]";
		// edit() collapses actual newlines in values to @@NL@@ so SHARP_ATTR stays single-line.
		var attrExpr = $"iter(lattr({dbref}/**),SHARP_ATTR:%i0::[edit(get({dbref}/%i0),%r,@@NL@@)],%b,%r)";

		var infoLines = await terminal.SendCommandAsync(infoExpr);
		var attrLines = await terminal.SendCommandAsync(attrExpr);

		var obj = ParseInfo(infoLines);
		if (obj is null) return null;

		obj.Attributes = ParseAttributes(attrLines);
		return obj;
	}

	/// <summary>Get only the attribute list for an object (faster than full GetObjectAsync).</summary>
	public async Task<List<MushAttribute>> GetAttributesAsync(string dbref)
	{
		// edit() replaces any actual newlines in the attribute value with @@NL@@ so the
		// SHARP_ATTR marker stays on a single line — safe even when attrs contain %r output.
		var expr = $"iter(lattr({dbref}/**),SHARP_ATTR:%i0::[edit(get({dbref}/%i0),%r,@@NL@@)],%b,%r)";
		var lines = await terminal.SendCommandAsync(expr);
		return ParseAttributes(lines);
	}

	/// <summary>Get a single attribute value.</summary>
	public async Task<string?> GetAttributeAsync(string dbref, string attrName)
	{
		var lines = await terminal.SendCommandAsync($"get({dbref}/{attrName})");
		return lines.Length > 0 ? string.Join("\n", lines) : null;
	}

	/// <summary>Set (or clear) an attribute via the standard &amp;ATTR command.
	/// Newlines in <paramref name="value"/> are converted to the MUSH <c>%r</c> substitution
	/// so the command is always a single-line WebSocket message.</summary>
	public Task SetAttributeAsync(string dbref, string attrName, string value)
	{
		// Replace actual newlines with %r (MUSH convention) so the &ATTR command
		// is a single WebSocket message — a multi-line message would be split by
		// the server into separate commands, truncating the attribute value.
		var safeValue = value.Replace("\r\n", "%r").Replace("\r", "%r").Replace("\n", "%r");
		return terminal.SendAsync($"&{attrName} {dbref}={safeValue}");
	}

	/// <summary>Delete an attribute by setting it to empty.</summary>
	public Task DeleteAttributeAsync(string dbref, string attrName)
		=> terminal.SendAsync($"&{attrName} {dbref}=");

	/// <summary>
	/// Create a new in-game object using the appropriate building command.
	/// Returns the new object's dbref if the server confirms creation, otherwise null.
	/// </summary>
	/// <summary>
	/// Create a new in-game object using the appropriate softcode function
	/// (<c>create()</c>, <c>dig()</c>, or <c>open()</c>) so the new dbref is
	/// returned directly in one round-trip — no text parsing needed.
	/// </summary>
	public async Task<int?> CreateObjectAsync(string name, MushObjectType type)
	{
		// Each function returns the new dbref (#N or #N:timestamp).
		// We wrap with before(…,:) to strip any creation-time suffix from dig()/open().
		var expr = type switch
		{
			MushObjectType.Room => $"before(dig({name}),:)",
			MushObjectType.Exit => $"before(open({name}),:)",
			_ => $"create({name})",   // create() already returns #N cleanly
		};

		var lines = await terminal.SendCommandAsync(expr);
		if (lines.Length == 0) return null;

		var dbrefStr = lines.FirstOrDefault(l => l.TrimStart().StartsWith('#'))?.Trim();
		return dbrefStr is not null && int.TryParse(dbrefStr.TrimStart('#'), out var n) ? n : null;
	}

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

	private static MushObject? ParseInfo(string[] lines)
	{
		foreach (var line in lines)
		{
			if (!line.StartsWith("SHARP_INFO:")) continue;

			// SHARP_INFO:<dbref>:<name>:<type>:<owner>
			var parts = line.Split(':', 5);
			if (parts.Length < 5) continue;

			if (!int.TryParse(parts[1].TrimStart('#'), out var dbref)) continue;

			return new MushObject
			{
				Dbref = dbref,
				Name = parts[2],
				Type = ParseType(parts[3]),
				Owner = parts[4],
			};
		}

		return null;
	}

	private static List<MushAttribute> ParseAttributes(string[] lines)
	{
		var attrs = new List<MushAttribute>();

		foreach (var line in lines)
		{
			if (!line.StartsWith("SHARP_ATTR:")) continue;

			// SHARP_ATTR:<name>:<flags>:<value…>
			var parts = line.Split(':', 4);
			if (parts.Length < 4) continue;

			// Decode @@NL@@ placeholders back to actual newlines (see edit() in iter expr).
			var value = parts[3].Replace("@@NL@@", "\n");

			attrs.Add(new MushAttribute
			{
				Name = parts[1],
				AttributeFlags = string.IsNullOrEmpty(parts[2])
					? []
					: [.. parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries)],
				Value = value,
			});
		}

		return attrs;
	}

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
