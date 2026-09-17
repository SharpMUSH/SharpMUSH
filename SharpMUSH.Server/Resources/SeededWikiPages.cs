namespace SharpMUSH.Server.Resources;

/// <summary>
/// The bodies of the help pages a fresh game's wiki is seeded with. They are Markdown files
/// embedded in the assembly rather than <c>const string</c> literals: 410 lines of prose inside
/// <c>StartupHandler</c> made a 661-line file out of a 250-line one, and prose in a `.md` is
/// editable, diffable and renderable as what it is.
/// </summary>
/// <remarks>
/// Both pages are seeded once and are freely editable in-game afterwards, so these are the initial
/// bodies, not the live ones. Read eagerly at first use and cached — a missing resource is a
/// packaging fault, and failing at startup beats seeding an empty help page.
/// </remarks>
public static class SeededWikiPages
{
	/// <summary>
	/// <c>Help:Markdown Guide</c> — the CommonMark subset and SharpMUSH extensions the wiki
	/// pipeline supports. Lives at <c>/wiki/help/markdown_guide</c>.
	/// </summary>
	public static string MarkdownGuide { get; } = Read("markdown-guide.md");

	/// <summary>
	/// <c>Help:Application Schema Guide</c> — the Portal Schema Document, its field and display
	/// elements, the application registry and a worked example. Lives at
	/// <c>/wiki/help/application_schema_guide</c>, and is kept in sync with
	/// <c>docs/design/dynamic-applications.md</c> and the chargen example package.
	/// </summary>
	public static string ApplicationSchemaGuide { get; } = Read("application-schema-guide.md");

	private static string Read(string name)
	{
		var resource = $"SharpMUSH.Server.Resources.Wiki.{name}";
		using var stream = typeof(SeededWikiPages).Assembly.GetManifestResourceStream(resource)
			?? throw new InvalidOperationException(
				$"Seed page '{resource}' is not embedded in the assembly. Check the EmbeddedResource "
				+ "item and its LogicalName in SharpMUSH.Server.csproj.");
		using var reader = new StreamReader(stream);

		// The literals these replaced ended at their closing delimiter, with no trailing newline.
		return reader.ReadToEnd().TrimEnd('\r', '\n');
	}
}
