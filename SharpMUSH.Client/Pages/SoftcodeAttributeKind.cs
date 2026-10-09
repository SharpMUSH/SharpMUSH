using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Pages;

/// <summary>
/// What an attribute looks like it holds, as the Softcode Editor's badge marks it: a $-command, a function,
/// a ^-listen, data or text.
/// </summary>
public static class SoftcodeAttributeKind
{
	private static readonly HashSet<string> FunctionBranches =
		new(["FN", "FUN", "FUNC", "FUNCS", "FUNCTION", "FUNCTIONS", "UFUN"], StringComparer.OrdinalIgnoreCase);

	public static string Badge(MushAttribute attr)
	{
		if (attr.Value.TrimStart().StartsWith('$')) return "$";
		if (attr.Value.TrimStart().StartsWith('^')) return "^";
		// The tree already shows that an attribute is nested, so nesting alone no longer marks a
		// function: a branch named for functions does (FN`ROW, FUN`HEADER), as SAFE does.
		if (attr.AttributeFlags.Contains("SAFE", StringComparer.OrdinalIgnoreCase)
			|| attr.Name.Split(SoftcodeAttributeTree.Separator).SkipLast(1).Any(FunctionBranches.Contains))
			return "ƒ";
		return attr.Value.Length > 0 && (char.IsDigit(attr.Value[0]) || attr.Value == "0" || attr.Value.StartsWith("#-"))
			? "•"
			: "¶";
	}

	/// <summary>The resource key naming the kind, for the badge's tooltip.</summary>
	public static string LabelKey(MushAttribute attr) => Badge(attr) switch
	{
		"$" => "TermAttrKindCommand",
		"ƒ" => "TermAttrKindFunction",
		"^" => "TermAttrKindListen",
		"•" => "TermAttrKindData",
		_ => "TermAttrKindText",
	};

	/// <summary>The badge's tint, border and colour for its kind.</summary>
	public static string BadgeStyle(MushAttribute attr)
		=> $"background:rgba({Glow(attr)},0.14);border:1px solid rgba({Glow(attr)},0.3);color:{Color(attr)};";

	private static string Color(MushAttribute attr) => Badge(attr) switch
	{
		"$" => "var(--accent)",
		"ƒ" => "#5aa9ff",
		"^" => "#b39cff",
		"•" => "#d9a23a",
		_ => "var(--text-faint)",
	};

	private static string Glow(MushAttribute attr) => Badge(attr) switch
	{
		"$" => "var(--glow)",
		"ƒ" => "90,169,255",
		"^" => "179,156,255",
		"•" => "217,162,58",
		_ => "95,104,112",
	};
}
