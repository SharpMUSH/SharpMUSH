using SharpMUSH.Client.Models.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Mssp;

namespace SharpMUSH.Client.Pages.Admin.Config;

/// <summary>How the MSSP page labels a variable and links a server-reported one to where it is set.</summary>
public static class MsspNaming
{
	private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
	{
		["ANSI"] = "ANSI", ["GMCP"] = "GMCP", ["MSDP"] = "MSDP", ["MCCP"] = "MCCP", ["MXP"] = "MXP", ["SSL"] = "SSL",
		["IP"] = "IP", ["IPV6"] = "IPv6", ["UTF-8"] = "UTF-8", ["VT100"] = "VT100", ["XTERM"] = "xterm"
	};

	/// <summary>"MINIMUM AGE" reads "Minimum age", "XTERM 256 COLORS" reads "xterm 256 colors", "UTF-8" stays.</summary>
	public static string Title(string name)
	{
		var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		return string.Join(' ', words.Select((word, index) =>
			Words.TryGetValue(word, out var kept) ? kept
			: index == 0 ? word[..1] + word[1..].ToLowerInvariant()
			: word.ToLowerInvariant()));
	}

	/// <summary>
	/// The configuration section that sets <paramref name="option"/> (a <c>mush.cnf</c> name such as
	/// <c>mud_name</c>), for a link from the row it feeds; null when no section shows it.
	/// </summary>
	public static ConfigSection? SectionFor(string option)
	{
		if (!ConfigMetadata.AttributeToPropertyName.TryGetValue(option, out var property)
			|| !ConfigMetadata.PropertyMetadata.TryGetValue(property, out var metadata))
		{
			return null;
		}

		return ConfigSections.Groups.SelectMany(group => group.Sections)
			.FirstOrDefault(section => section.SchemaCategory == metadata.Category);
	}

	/// <summary>The groups, in the specification's order.</summary>
	public static IReadOnlyList<MsspGroup> Groups { get; } = Enum.GetValues<MsspGroup>();

	public static string Anchor(MsspGroup group) => "mssp-" + group.ToString().ToLowerInvariant();

	public const string OtherAnchor = "mssp-other";
}
