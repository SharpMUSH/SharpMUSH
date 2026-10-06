using System.Collections.Frozen;
using System.Reflection;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The icon an application names. A package writes a Material icon name (<c>support_agent</c>,
/// <c>assignment_ind</c>, <c>Badge</c>); <see cref="MudBlazor.MudIcon"/> draws SVG markup, so the name is
/// looked up among MudBlazor's Material icons. SVG markup passes through as it is; an unknown name or
/// none is the fallback.
/// </summary>
public static class AppIcons
{
	private static readonly FrozenDictionary<string, string> Outlined = Table(typeof(MudBlazor.Icons.Material.Outlined));
	private static readonly FrozenDictionary<string, string> Filled = Table(typeof(MudBlazor.Icons.Material.Filled));

	/// <summary>The SVG for <paramref name="icon"/> in the outlined set (filled when outlined lacks it), else <paramref name="fallback"/>.</summary>
	public static string Outline(string? icon, string fallback) => Resolve(icon, fallback, Outlined, Filled);

	/// <summary>The SVG for <paramref name="icon"/> in the filled set (outlined when filled lacks it), else <paramref name="fallback"/>.</summary>
	public static string Fill(string? icon, string fallback) => Resolve(icon, fallback, Filled, Outlined);

	private static string Resolve(string? icon, string fallback, FrozenDictionary<string, string> first, FrozenDictionary<string, string> second)
	{
		if (string.IsNullOrWhiteSpace(icon))
		{
			return fallback;
		}

		var trimmed = icon.Trim();
		if (trimmed.StartsWith('<'))
		{
			return trimmed;
		}

		var key = Key(trimmed);
		return first.TryGetValue(key, out var svg) || second.TryGetValue(key, out svg) ? svg : fallback;
	}

	// support_agent, support-agent, SupportAgent and supportagent all name the same icon.
	private static string Key(string name) =>
		string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();

	private static FrozenDictionary<string, string> Table(Type set) =>
		set.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.FieldType == typeof(string))
			.GroupBy(f => Key(f.Name))
			.ToFrozenDictionary(g => g.Key, g => (string)g.First().GetValue(null)!);
}
