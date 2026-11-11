namespace SharpMUSH.Configuration;

/// <summary>The servers a compatibility option emulates, or whose database import sets it.</summary>
[Flags]
public enum CompatibilityServer
{
	None = 0,
	PennMUSH = 1,
	TinyMUSH = 2,
	TinyMUX = 4,
	RhostMUSH = 8
}

/// <summary>How a compatibility option's servers read in a description, a listing and the import report.</summary>
public static class CompatibilityServers
{
	/// <summary>Each server, in the order they are listed.</summary>
	public static readonly CompatibilityServer[] All =
		[CompatibilityServer.PennMUSH, CompatibilityServer.TinyMUSH, CompatibilityServer.TinyMUX, CompatibilityServer.RhostMUSH];

	/// <summary>The servers in <paramref name="servers"/>, by name, in <see cref="All"/>'s order.</summary>
	public static IReadOnlyList<string> Names(CompatibilityServer servers)
		=> [.. All.Where(server => servers.HasFlag(server)).Select(server => server.ToString())];

	/// <summary><c>TinyMUX</c>, <c>TinyMUX or RhostMUSH</c>, <c>PennMUSH, TinyMUX or RhostMUSH</c>.</summary>
	public static string Join(CompatibilityServer servers, string conjunction)
		=> Names(servers) switch
		{
			[] => "",
			[var one] => one,
			var names => string.Join(", ", names.Take(names.Count - 1)) + $" {conjunction} " + names[^1]
		};

	/// <summary>
	/// The sentence saying which database imports set <paramref name="option"/>, made from its
	/// <see cref="SharpConfigAttribute.SetBy"/>, or null when none does.
	/// </summary>
	public static string? ImportSentence(SharpConfigAttribute option, bool isBoolean)
		=> option.SetBy == CompatibilityServer.None
			? null
			: $"Importing a {Join(option.SetBy, "or")} database {(isBoolean ? "turns it on" : "sets it")}.";

	/// <summary>
	/// <paramref name="option"/>'s description, followed for a compatibility option by the servers it
	/// emulates and the imports that set it, as the configuration page shows it.
	/// </summary>
	public static string Describe(SharpConfigAttribute option, bool isBoolean)
	{
		if (option.Emulates == CompatibilityServer.None) return option.Description;

		var description = option.Description.TrimEnd();
		var sentences = new List<string> { description.EndsWith('.') ? description : description + "." };
		sentences.Add($"Emulates {Join(option.Emulates, "and")}.");
		if (ImportSentence(option, isBoolean) is { } imported) sentences.Add(imported);
		return string.Join(" ", sentences);
	}
}
