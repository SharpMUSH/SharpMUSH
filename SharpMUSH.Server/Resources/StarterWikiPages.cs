using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Server.Resources;

/// <summary>
/// The bodies of the starter wiki pages (<see cref="Services.StarterWikiService"/>): Markdown files under
/// <c>Resources/Wiki/Starter</c>, embedded in the assembly. Lines opening <c>&gt; **Fill in:**</c> are prompts for
/// the game's administrators, which <c>wikisearch(Fill in)</c> finds. Categories are the page's own field, not
/// part of its text.
/// </summary>
public static class StarterWikiPages
{
	/// <param name="Title">The page's title; its slug follows from it.</param>
	/// <param name="Namespace">Main for the hubs, Category for the category pages.</param>
	/// <param name="Markdown">The page's initial body.</param>
	/// <param name="Categories">
	/// The categories the page is filed in. A category page filed in another is its subcategory.
	/// </param>
	/// <param name="Protect">Whether only wiki administrators may edit it.</param>
	public sealed record Page(string Title, WikiNamespace Namespace, string Markdown, IReadOnlyList<string> Categories,
		bool Protect);

	/// <summary>The Home page that links to the four hubs. Replaces the boot-seeded Home only while it is unchanged.</summary>
	public static string Home { get; } = Read("home.md");

	/// <summary>The hubs, then the category tree that files them: a subcategory is filed in its parent.</summary>
	public static IReadOnlyList<Page> All { get; } =
	[
		new("Getting Started", WikiNamespace.Main, Read("getting-started.md"), ["Getting Started"], Protect: false),
		new("Theme", WikiNamespace.Main, Read("theme.md"), ["Theme"], Protect: true),
		new("Setting", WikiNamespace.Main, Read("setting.md"), ["Setting"], Protect: true),
		new("Policies", WikiNamespace.Main, Read("policies.md"), ["Policies"], Protect: true),
		new("Getting Started", WikiNamespace.Category, Read("category-getting-started.md"), [], Protect: false),
		new("Theme", WikiNamespace.Category, Read("category-theme.md"), [], Protect: false),
		new("Setting", WikiNamespace.Category, Read("category-setting.md"), [], Protect: false),
		new("Places", WikiNamespace.Category, Read("category-places.md"), ["Setting"], Protect: false),
		new("History", WikiNamespace.Category, Read("category-history.md"), ["Setting"], Protect: false),
		new("Factions", WikiNamespace.Category, Read("category-factions.md"), ["Setting"], Protect: false),
		new("Peoples", WikiNamespace.Category, Read("category-peoples.md"), ["Setting"], Protect: false),
		new("Policies", WikiNamespace.Category, Read("category-policies.md"), [], Protect: false),
		new("House Rules", WikiNamespace.Category, Read("category-house-rules.md"), ["Policies"], Protect: false),
	];

	private static string Read(string name)
	{
		var resource = $"SharpMUSH.Server.Resources.Wiki.Starter.{name}";
		using var stream = typeof(StarterWikiPages).Assembly.GetManifestResourceStream(resource)
			?? throw new InvalidOperationException(
				$"Starter wiki page '{resource}' is not embedded in the assembly. Check the EmbeddedResource "
				+ "item for Resources\\Wiki\\Starter in SharpMUSH.Server.csproj.");
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd().TrimEnd('\r', '\n');
	}
}
