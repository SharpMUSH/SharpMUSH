using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services.DatabaseConversion.Softcode;

/// <summary>
/// TinyMUX's and RhostMUSH's <c>@hook</c>, written as SharpMUSH's. Those servers keep the hook's code on one global
/// object, <c>hook_obj</c>, in an attribute named for the hook and the command (<c>B_LOOK</c> for a before hook on
/// <c>look</c>), so <c>@hook/before look</c> only turns the hook on. SharpMUSH's <c>@hook</c> names the object and
/// attribute (<c>@hook/before look=#12,B_LOOK</c>), and the bare form clears a hook, as in PennMUSH. The import
/// writes each such command out in full.
/// </summary>
public static partial class HookCommandRewriter
{
	/// <summary>A hook type of TinyMUX's <c>@hook</c>: SharpMUSH's switch for it, and its attribute's prefix.</summary>
	private sealed record HookKind(string Switch, string? Prefix);

	/// <summary>
	/// TinyMUX's hook switches. <c>igswitch</c> has no attribute: SharpMUSH's form for it is <c>/extend</c> naming
	/// no object (<c>=*</c>).
	/// </summary>
	private static readonly Dictionary<string, HookKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
	{
		["before"] = new("before", "B"),
		["after"] = new("after", "A"),
		["ignore"] = new("ignore", "I"),
		["permit"] = new("permit", "P"),
		["fail"] = new("fail", "AF"),
		["args"] = new("args", "R"),
		["igswitch"] = new("extend", null)
	};

	/// <summary>The command a one-character command hooks, as TinyMUX's <c>hook_name</c> names it.</summary>
	public static string CommandName(string command) => command switch
	{
		"\"" => "say",
		":" or ";" => "pose",
		"\\" => "@emit",
		"#" => "@force",
		"&" => "@set",
		"-" or "~" => "@mail",
		_ => command
	};

	/// <summary>
	/// The SharpMUSH <c>@hook</c> commands that turn on <paramref name="hooks"/> for <paramref name="command"/>, with
	/// their code on <paramref name="hookObject"/> (as <c>#12</c> or <c>me</c>). An unknown hook type is left out.
	/// </summary>
	public static IEnumerable<string> Lines(string command, IEnumerable<string> hooks, string hookObject)
	{
		var name = CommandName(command);
		foreach (var hook in hooks)
		{
			if (!Kinds.TryGetValue(hook, out var kind))
			{
				continue;
			}

			yield return kind.Prefix is null
				? $"@hook/{kind.Switch} {name}=*"
				: $"@hook/{kind.Switch} {name}={hookObject},{kind.Prefix}_{name.ToUpperInvariant()}";
		}
	}

	/// <summary>Whether <paramref name="hook"/> is one of TinyMUX's hook types.</summary>
	public static bool IsHook(string hook) => Kinds.ContainsKey(hook);

	/// <summary>
	/// Rewrites every TinyMUX-style <c>@hook</c> command in <paramref name="softcode"/>, with the hook code on
	/// <paramref name="hookObject"/>, or <see langword="null"/> when the game named none: then a command that would
	/// need it is left as written and noted.
	/// </summary>
	public static SoftcodeRewriteResult Rewrite(string softcode, string? hookObject)
	{
		if (softcode.IndexOf("@hook", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return new SoftcodeRewriteResult(softcode, []);
		}

		var notes = new List<string>();
		var text = HookCommand().Replace(softcode, match => RewriteOne(match, hookObject, notes));
		return new SoftcodeRewriteResult(text, notes) { Changed = text != softcode };
	}

	private static string RewriteOne(Match match, string? hookObject, List<string> notes)
	{
		var switches = match.Groups["switches"].Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
		var command = match.Groups["command"].Value;
		var lead = match.Groups["lead"].Value;

		// TinyMUX's bare @hook, and @hook/list, list every hook or one command's.
		if (switches.Length == 0 || switches.Any(s => s.Equals("list", StringComparison.OrdinalIgnoreCase)))
		{
			return command.Length == 0 ? $"{lead}@hook/list" : $"{lead}@hook/list {CommandName(command)}";
		}

		if (command.Length == 0 || switches.FirstOrDefault(s => !IsHook(s) && !s.Equals("clear", StringComparison.OrdinalIgnoreCase)) is not null)
		{
			notes.Add($"check by hand: {match.Value.Trim()} is not a TinyMUX @hook SharpMUSH can write out");
			return match.Value;
		}

		var kinds = switches.Where(IsHook).ToList();
		if (switches.Any(s => s.Equals("clear", StringComparison.OrdinalIgnoreCase)))
		{
			// SharpMUSH clears a hook with the bare form, as PennMUSH does.
			var name = CommandName(command);
			return lead + string.Join(";", kinds.Select(kind => $"@hook/{Kinds[kind].Switch} {name}"));
		}

		if (hookObject is null && kinds.Any(kind => Kinds[kind].Prefix is not null))
		{
			notes.Add($"check by hand: {match.Value.Trim()} runs code on hook_obj, which the import was not given");
			return match.Value;
		}

		notes.Add($"{match.Value.Trim()} written with its hook object and attribute");
		return lead + string.Join(";", Lines(command, kinds, hookObject ?? "*"));
	}

	/// <summary>
	/// <c>@hook</c> at the start of a command, with its switches and the command it hooks, up to the end of that
	/// command. A form with an <c>=</c> is already SharpMUSH's and does not match.
	/// </summary>
	[GeneratedRegex(@"(?<lead>(?:^|[;{])\s*)@hook(?<switches>(?:/[A-Za-z]+)*)(?:[ \t]+(?<command>[^\s;{}=]+))?[ \t]*(?=$|[;}])",
		RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex HookCommand();
}
