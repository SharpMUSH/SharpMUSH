using System.Text.RegularExpressions;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Sends structured MUSH query commands via <see cref="ITerminalService"/> and parses the
/// results into typed models.  Uses a <c>think</c>-based approach with embedded format markers
/// so output is unambiguous and permission-safe (the server enforces all MUSH permissions).
/// When <see cref="ITerminalService.MyPort"/> is known, all structured output is routed to
/// that specific connection via <c>pemit()</c> so it never appears on other sessions.
/// </summary>
public partial class MushQueryService(ITerminalService terminal, ILogger<MushQueryService> logger)
{
	private readonly ILogger<MushQueryService> _logger = logger;

	// ──────────────────────────────────────────────────────────────────────────────
	// Routing helpers
	// ──────────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Build a <c>think</c> command whose output is routed only to the editor port when known.
	/// <paramref name="mushExpr"/> is a softcode expression (no outer brackets needed).
	/// </summary>
	private string RouteExpr(string mushExpr)
	{
		var port = terminal.MyPort;
		return port.HasValue
			? $"think [pemit({port.Value}, {mushExpr})]"
			: $"think [{mushExpr}]";
	}

	/// <summary>
	/// Build a <c>think</c> command for literal text with embedded <c>[func()]</c> calls.
	/// Routes output to the editor port when known.
	/// </summary>
	private string RouteLiteral(string mushText)
	{
		var port = terminal.MyPort;
		return port.HasValue
			? $"think [pemit({port.Value}, {mushText})]"
			: $"think {mushText}";
	}

	// ──────────────────────────────────────────────────────────────────────────────
	// Object info
	// ──────────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Retrieve basic details (name, type, owner) and the full attribute list for a single object.
	/// </summary>
	public async Task<MushObject?> GetObjectAsync(string dbref)
	{
		_logger.LogDebug("GetObjectAsync {Dbref}", dbref);
		var infoCmd = RouteLiteral($"SHARP_INFO:{dbref}:[name({dbref})]:[type({dbref})]:[owner({dbref})]");
		var attrCmd = RouteExpr($"iter(lattr({dbref}),SHARP_ATTR:##::[get({dbref}/##)],%b,%r)");

		var infoLines = await terminal.SendCommandAsync(infoCmd);
		var attrLines = await terminal.SendCommandAsync(attrCmd);

		var obj = ParseInfo(infoLines);
		if (obj is null) return null;

		obj.Attributes = ParseAttributes(attrLines);
		return obj;
	}

	/// <summary>Get only the attribute list for an object (faster than full GetObjectAsync).</summary>
	public async Task<List<MushAttribute>> GetAttributesAsync(string dbref)
	{
		var cmd = RouteExpr($"iter(lattr({dbref}),SHARP_ATTR:##::[get({dbref}/##)],%b,%r)");
		var lines = await terminal.SendCommandAsync(cmd);
		return ParseAttributes(lines);
	}

	/// <summary>Get a single attribute value.</summary>
	public async Task<string?> GetAttributeAsync(string dbref, string attrName)
	{
		var lines = await terminal.SendCommandAsync(RouteExpr($"get({dbref}/{attrName})"));
		return lines.Length > 0 ? string.Join("\n", lines) : null;
	}

	/// <summary>Set (or clear) an attribute via the standard &amp;ATTR command.</summary>
	public Task SetAttributeAsync(string dbref, string attrName, string value)
		=> terminal.SendAsync($"&{attrName} {dbref}={value}");

	/// <summary>Delete an attribute by setting it to empty.</summary>
	public Task DeleteAttributeAsync(string dbref, string attrName)
		=> terminal.SendAsync($"&{attrName} {dbref}=");

	/// <summary>
	/// Create a new in-game object using the appropriate building command.
	/// Returns the new object's dbref if the server confirms creation, otherwise null.
	/// </summary>
	public async Task<int?> CreateObjectAsync(string name, MushObjectType type)
	{
		var cmd = type switch
		{
			MushObjectType.Room => $"@dig {name}",
			MushObjectType.Exit => $"@open {name}",
			_                   => $"@create {name}",
		};

		var lines = await terminal.SendCommandAsync(cmd);
		return ParseCreatedDbref(lines, type);
	}

	/// <summary>Parse the newly-created dbref from server creation output.</summary>
	private static int? ParseCreatedDbref(string[] lines, MushObjectType type)
	{
		foreach (var line in lines)
		{
			int? found = type switch
			{
				// "Created Name (#5)."  — any (#N) in the line
				MushObjectType.Thing  => TryParseDbrefInParens(line),
				// "Name created with room number 5."
				MushObjectType.Room   => TryParseTrailingNumber(line, "room number"),
				// "Opened exit Name" — server doesn't echo the dbref
				MushObjectType.Exit   => null,
				_                     => TryParseDbrefInParens(line),
			};
			if (found.HasValue) return found;
		}
		return null;
	}

	private static int? TryParseDbrefInParens(string line)
	{
		var m = DbrefParensRegex().Match(line);
		return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
	}

	private static int? TryParseTrailingNumber(string line, string afterText)
	{
		var idx = line.IndexOf(afterText, StringComparison.OrdinalIgnoreCase);
		if (idx < 0) return null;
		var rest = line[(idx + afterText.Length)..].Trim().TrimEnd('.');
		return int.TryParse(rest, out var n) ? n : null;
	}

	[GeneratedRegex(@"\(#(\d+)\)")]
	private static partial Regex DbrefParensRegex();

	// ──────────────────────────────────────────────────────────────────────────────
	// Object search
	// ──────────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Returns true if the currently connected player has the WIZARD flag.
	/// Used to decide whether to run lsearch(all) or lsearch(me).
	/// </summary>
	public async Task<bool> IsWizardAsync()
	{
		var lines = await terminal.SendCommandAsync(RouteExpr("hasflag(me,WIZARD)"));
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
		// before(##,:) strips the :creationTime suffix that DBRef.ToString() appends
		var cmd = RouteExpr("iter(lcon(loc(me)) lexits(loc(me)),SHARP_OBJ:[before(##,:)]:[type(##)]:[name(##)],,%r)");
		var lines = await terminal.SendCommandAsync(cmd);
		return ParseSearchResults(lines);
	}

	/// <summary>
	/// Execute a free-form softcode expression whose result is a space-separated
	/// list of dbrefs, and return typed search results.
	/// Examples: lsearch(me)  ·  lsearch(me, type, room)  ·  lcon(loc(me))
	/// </summary>
	public async Task<List<MushSearchResult>> SearchAsync(string expression)
	{
		// before(##,:) strips the :creationTime suffix that DBRef.ToString() appends
		// ,,%r uses default space iSep and newline oSep (3rd arg = iSep, 4th = oSep)
		var cmd = RouteExpr($"iter({expression},SHARP_OBJ:[before(##,:)]:[type(##)]:[name(##)],,%r)");
		var lines = await terminal.SendCommandAsync(cmd);
		return ParseSearchResults(lines);
	}

	// ──────────────────────────────────────────────────────────────────────────────
	// Eval / test
	// ──────────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Evaluate a MUSHcode expression and capture the result.
	/// Wraps in <c>think</c> so output goes only to the sender and returns the captured lines.
	/// </summary>
	public Task<string[]> EvalAsync(string code)
	{
		var cmd = RouteExpr($"think [{code}]");
		return terminal.SendCommandAsync(cmd);
	}

	// ──────────────────────────────────────────────────────────────────────────────
	// Parsing helpers
	// ──────────────────────────────────────────────────────────────────────────────

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

			attrs.Add(new MushAttribute
			{
				Name = parts[1],
				AttributeFlags = string.IsNullOrEmpty(parts[2])
					? []
					: [.. parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries)],
				Value = parts[3],
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
