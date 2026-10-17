using SharpMUSH.Library.Models.Portal.Applications;
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
			new(ConfigurationOptionRoutes.ForCategory("Net"), "Net", Icons.Material.Outlined.NetworkCheck, "Network"),
			new(ConfigurationOptionRoutes.ForCategory("Database"), "Database", Icons.Material.Outlined.Storage, "Database"),
			new(ConfigurationOptionRoutes.ForCategory("Mssp"), "Mssp", Icons.Material.Outlined.Radar, "AdmMssp"),
		]),
		new("Performance", "Performance", "PerformanceCategoryDescription", Icons.Material.Outlined.Speed, false,
		[
			new(ConfigurationOptionRoutes.ForCategory("Limit"), "Limit", Icons.Material.Outlined.Timer, "Limits"),
			new(ConfigurationOptionRoutes.ForCategory("Command"), "Command", Icons.Material.Outlined.Terminal, "Commands"),
		]),
		new("Security", "Security", "SecurityCategoryDescription", Icons.Material.Outlined.Shield, true,
		[
			new(ConfigurationOptionRoutes.ForCategory("SitelockRules"), "SitelockRules", Icons.Material.Outlined.Lock, "Sitelock"),
			new(ConfigurationOptionRoutes.ForCategory("BannedNames"), "BannedNames", Icons.Material.Outlined.Block, "BannedNames"),
			new(ConfigurationOptionRoutes.ForCategory("Restriction"), "Restriction", Icons.Material.Outlined.GppBad, "Restrictions"),
			new(ConfigurationOptionRoutes.ForCategory("Secrets"), "Secrets", Icons.Material.Outlined.Key, "AdmSecrets"),
			new(ConfigurationOptionRoutes.ForCategory("Apps"), "Apps", Icons.Material.Outlined.Extension, "OAuthAdmApps"),
		]),
		new("Content", "Content", "ContentCategoryDescription", Icons.Material.Outlined.Article, false,
		[
			new(ConfigurationOptionRoutes.ForCategory("Cosmetic"), "Cosmetic", Icons.Material.Outlined.Palette, "Cosmetic"),
			new(ConfigurationOptionRoutes.ForCategory("AsciiTranslations"), "AsciiTranslations", Icons.Material.Outlined.Translate, "AsciiTranslations"),
			new(ConfigurationOptionRoutes.ForCategory("Chat"), "Chat", Icons.Material.Outlined.Chat, "Chat"),
			new(ConfigurationOptionRoutes.ForCategory("Wiki"), "Wiki", Icons.Material.Outlined.MenuBook, "Wiki"),
		]),
		new("Logs", "LogsAndFiles", "LogsCategoryDescription", Icons.Material.Outlined.FolderOpen, false,
		[
			new(ConfigurationOptionRoutes.ForCategory("Log"), "Log", Icons.Material.Outlined.Description, "Logging"),
			new(ConfigurationOptionRoutes.ForCategory("File"), "File", Icons.Material.Outlined.Folder, "Files"),
			new(ConfigurationOptionRoutes.ForCategory("TextFile"), "TextFile", Icons.Material.Outlined.TextSnippet, "TextFiles"),
			new(ConfigurationOptionRoutes.ForCategory("Dump"), "Dump", Icons.Material.Outlined.Save, "DatabaseDumps"),
		]),
		new("Advanced", "Advanced", "AdvancedCategoryDescription", Icons.Material.Outlined.Tune, false,
		[
			new(ConfigurationOptionRoutes.ForCategory("Attribute"), "Attribute", Icons.Material.Outlined.Label, "Attributes"),
			new(ConfigurationOptionRoutes.ForCategory("Flag"), "Flag", Icons.Material.Outlined.Flag, "Flags"),
			new(ConfigurationOptionRoutes.ForCategory("Cost"), "Cost", Icons.Material.Outlined.MonetizationOn, "Costs"),
			new(ConfigurationOptionRoutes.ForCategory("Compatibility"), "Compatibility", Icons.Material.Outlined.Verified, "Compatibility"),
			new(ConfigurationOptionRoutes.ForCategory("Alias"), "Alias", Icons.Material.Outlined.Link, "Aliases"),
			new(ConfigurationOptionRoutes.ForCategory("Debug"), "Debug", Icons.Material.Outlined.BugReport, "Debug"),
			new(ConfigurationOptionRoutes.ForCategory("Function"), "Function", Icons.Material.Outlined.Functions, "Functions"),
			new(ConfigurationOptionRoutes.ForCategory("Warning"), "Warning", Icons.Material.Outlined.Warning, "Warnings"),
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
