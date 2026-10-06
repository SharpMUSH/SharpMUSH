using MudBlazor;

namespace SharpMUSH.Client.Models.Configuration;

/// <summary>One section of the configuration: its route, the schema category it edits, its icon and title key.</summary>
public sealed record ConfigSection(string Route, string SchemaCategory, string Icon, string TitleKey);

/// <summary>A group of sections as the sidebar tree and the home cards present them.</summary>
public sealed record ConfigGroup(
	string Key,
	string TitleKey,
	string DescKey,
	string Icon,
	bool Important,
	IReadOnlyList<ConfigSection> Sections)
{
	public string FirstRoute => Sections[0].Route;
}

/// <summary>
/// The configuration's group and section table, shared by the sidebar tree and the home cards so
/// the two can never disagree: the Content card used to count 45 settings while the nav listed
/// Wiki, whose one setting made it 46.
/// </summary>
public static class ConfigSections
{
	public static readonly IReadOnlyList<ConfigGroup> Groups =
	[
		new("Server", "Server", "ServerCategoryDescription", Icons.Material.Outlined.Dns, false,
		[
			new("/admin/config/net", "Net", Icons.Material.Outlined.NetworkCheck, "Network"),
			new("/admin/config/database", "Database", Icons.Material.Outlined.Storage, "Database"),
			new("/admin/config/mssp", "Mssp", Icons.Material.Outlined.Radar, "AdmMssp"),
		]),
		new("Performance", "Performance", "PerformanceCategoryDescription", Icons.Material.Outlined.Speed, false,
		[
			new("/admin/config/limit", "Limit", Icons.Material.Outlined.Timer, "Limits"),
			new("/admin/config/command", "Command", Icons.Material.Outlined.Terminal, "Commands"),
		]),
		new("Security", "Security", "SecurityCategoryDescription", Icons.Material.Outlined.Shield, true,
		[
			new("/admin/config/sitelock", "SitelockRules", Icons.Material.Outlined.Lock, "Sitelock"),
			new("/admin/config/bannednames", "BannedNames", Icons.Material.Outlined.Block, "BannedNames"),
			new("/admin/config/restrictions", "Restriction", Icons.Material.Outlined.GppBad, "Restrictions"),
		]),
		new("Content", "Content", "ContentCategoryDescription", Icons.Material.Outlined.Article, false,
		[
			new("/admin/config/cosmetic", "Cosmetic", Icons.Material.Outlined.Palette, "Cosmetic"),
			new("/admin/config/chat", "Chat", Icons.Material.Outlined.Chat, "Chat"),
			new("/admin/config/wiki", "Wiki", Icons.Material.Outlined.MenuBook, "Wiki"),
		]),
		new("Logs", "LogsAndFiles", "LogsCategoryDescription", Icons.Material.Outlined.FolderOpen, false,
		[
			new("/admin/config/log", "Log", Icons.Material.Outlined.Description, "Logging"),
			new("/admin/config/file", "File", Icons.Material.Outlined.Folder, "Files"),
			new("/admin/config/textfile", "TextFile", Icons.Material.Outlined.TextSnippet, "TextFiles"),
			new("/admin/config/dump", "Dump", Icons.Material.Outlined.Save, "DatabaseDumps"),
		]),
		new("Advanced", "Advanced", "AdvancedCategoryDescription", Icons.Material.Outlined.Tune, false,
		[
			new("/admin/config/attribute", "Attribute", Icons.Material.Outlined.Label, "Attributes"),
			new("/admin/config/flag", "Flag", Icons.Material.Outlined.Flag, "Flags"),
			new("/admin/config/cost", "Cost", Icons.Material.Outlined.MonetizationOn, "Costs"),
			new("/admin/config/compatibility", "Compatibility", Icons.Material.Outlined.Verified, "Compatibility"),
			new("/admin/config/alias", "Alias", Icons.Material.Outlined.Link, "Aliases"),
			new("/admin/config/debug", "Debug", Icons.Material.Outlined.BugReport, "Debug"),
			new("/admin/config/function", "Function", Icons.Material.Outlined.Functions, "Functions"),
			new("/admin/config/warning", "Warning", Icons.Material.Outlined.Warning, "Warnings"),
		]),
	];

	/// <summary>
	/// The section whose route the path is; the <c>/admin/config/sitelockrules</c> alias resolves to
	/// Sitelock. Null on the home page and on Maintenance routes.
	/// </summary>
	public static ConfigSection? SectionForPath(string path)
	{
		var p = path.TrimEnd('/');
		if (p.Equals("/admin/config/sitelockrules", StringComparison.OrdinalIgnoreCase))
		{
			p = "/admin/config/sitelock";
		}

		return Groups.SelectMany(g => g.Sections)
			.FirstOrDefault(s => s.Route.Equals(p, StringComparison.OrdinalIgnoreCase));
	}

	public static ConfigGroup? GroupForPath(string path)
	{
		var section = SectionForPath(path);
		return section is null ? null : Groups.First(g => g.Sections.Contains(section));
	}
}
