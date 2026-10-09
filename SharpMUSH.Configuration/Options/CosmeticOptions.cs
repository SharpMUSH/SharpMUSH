namespace SharpMUSH.Configuration.Options;

public record CosmeticOptions(
	[property: SharpConfig(
		Name = "money_singular",
		Category = "Cosmetic",
		Description = "Singular form of money (e.g., 'penny')",
		Group = "Currency",
		Order = 1)]
	string MoneySingular,

	[property: SharpConfig(
		Name = "money_plural",
		Category = "Cosmetic",
		Description = "Plural form of money (e.g., 'pennies')",
		Group = "Currency",
		Order = 2)]
	string MoneyPlural,

	[property: SharpConfig(
		Name = "player_name_spaces",
		Category = "Cosmetic",
		Description = "Allow spaces in player names",
		Group = "Names",
		Order = 1)]
	bool PlayerNameSpaces,

	[property: SharpConfig(
		Name = "ansi_names",
		Category = "Cosmetic",
		Description = "Allow ANSI color codes in player names",
		Group = "Names",
		Order = 2)]
	bool AnsiNames,

	[property: SharpConfig(
		Name = "only_ascii_in_names",
		Category = "Cosmetic",
		Description = "Restrict names to ASCII characters only",
		Group = "Names",
		Order = 3)]
	bool OnlyAsciiInNames,

	[property: SharpConfig(
		Name = "monikers",
		Category = "Cosmetic",
		Description = "Enable moniker (nickname) system for players",
		Group = "Names",
		Order = 4)]
	bool Monikers,

	[property: SharpConfig(
		Name = "float_precision",
		Category = "Cosmetic",
		Description = "Number of decimal places for floating-point display",
		ValidationPattern = @"^\d+$",
		Group = "Display",
		Order = 1,
		Min = 0,
		Max = 15)]
	uint FloatPrecision,

	[property: SharpConfig(
		Name = "comma_exit_list",
		Category = "Cosmetic",
		Description = "Use commas to separate exit names in lists",
		Group = "Display",
		Order = 2)]
	bool CommaExitList,

	[property: SharpConfig(
		Name = "count_all",
		Category = "Cosmetic",
		Description = "Include all objects in @count command results",
		Group = "Display",
		Order = 3)]
	bool CountAll,

	[property: SharpConfig(
		Name = "page_aliases",
		Category = "Cosmetic",
		Description = "Allow @page command to use player aliases",
		Group = "Names",
		Order = 5)]
	bool PageAliases,

	[property: SharpConfig(
		Name = "flags_on_examine",
		Category = "Cosmetic",
		Description = "Show object flags when using examine command",
		Group = "Display",
		Order = 4)]
	bool FlagsOnExamine,

	// NOTE: accepted and configurable, but not yet honoured. @examine's attribute loop
	// (SharpMUSH.Implementation/Commands/GeneralCommands.cs, Commands.Examine) does not read this
	// option -- every attribute the permission check already allows is shown regardless of this
	// flag. PennMUSH gates non-public attributes here; wiring up the real semantics is its own task.
	[property: SharpConfig(
		Name = "ex_public_attribs",
		Category = "Cosmetic",
		Description = "Show public attributes in examine command",
		Group = "Display",
		Order = 5)]
	bool ExaminePublicAttributes,

	[property: SharpConfig(
		Name = "wizwall_prefix",
		Category = "Cosmetic",
		Description = "Prefix text for wizard wall messages",
		Group = "Announcements",
		Order = 1)]
	string WizardWallPrefix,

	[property: SharpConfig(
		Name = "rwall_prefix",
		Category = "Cosmetic",
		Description = "Prefix text for royalty wall messages",
		Group = "Announcements",
		Order = 2)]
	string RoyaltyWallPrefix,

	[property: SharpConfig(
		Name = "wall_prefix",
		Category = "Cosmetic",
		Description = "Prefix text for general wall messages",
		Group = "Announcements",
		Order = 3)]
	string WallPrefix,

	[property: SharpConfig(
		Name = "announce_connects",
		Category = "Cosmetic",
		Description = "Announce when players connect to the MUSH",
		Group = "Announcements",
		Order = 4)]
	bool AnnounceConnects,

	[property: SharpConfig(
		Name = "chat_strip_quote",
		Category = "Cosmetic",
		Description = "Remove quote marks from chat messages",
		Group = "Announcements",
		Order = 5)]
	bool ChatStripQuote,

	[property: SharpConfig(
		Name = "layout_border",
		Category = "Cosmetic",
		Description = "Border style for box() and rule() when they name none",
		ValidationPattern = @"^(none|ascii|mush|single|double|heavy|rounded)$",
		Group = "Layout",
		Order = 1,
		Tooltip = "One of none, ascii, mush, single, double, heavy or rounded. A client that cannot show "
			+ "box-drawing characters is sent the ascii pieces instead.")]
	string LayoutBorder,

	[property: SharpConfig(
		Name = "layout_theme",
		Category = "Cosmetic",
		Description = "Colour theme for layouts that name none: a theme name, or a theme as JSON",
		Group = "Layout",
		Order = 2,
		Tooltip = "Empty for no colour. A name from themes(), such as terminal or nord, or JSON such as "
			+ "{\"seed\":\"#7aa2f7\",\"harmony\":\"triadic\"}. See help LAYOUT THEMES.")]
	string LayoutTheme,

	[property: SharpConfig(
		Name = "image_hosts",
		Category = "Cosmetic",
		Description = "Which pictures image() and figure() may show: any, allow, block or off",
		ValidationPattern = @"^(any|allow|block|off)$",
		Group = "Layout",
		Order = 3,
		Tooltip = "any shows pictures from every host; allow only those on image_host_list; block all but "
			+ "those; off none. The game's own pictures (a relative address) are shown unless this is off. "
			+ "A refused picture shows its text art or description.")]
	string ImageHosts,

	[property: SharpConfig(
		Name = "image_host_list",
		Category = "Cosmetic",
		Description = "Hosts for image_hosts allow or block, space separated",
		Group = "Layout",
		Order = 4,
		Tooltip = "For example: i.imgur.com *.example.com. A *. entry covers every subdomain.")]
	string ImageHostList,

	[property: SharpConfig(
		Name = "portal_logo",
		Category = "Cosmetic",
		Description = "Picture at the top left of the web portal; empty for the SharpMUSH logo",
		ValidationPattern = PortalPicture.Pattern,
		Image = true,
		Group = "Portal",
		Order = 1,
		Tooltip = "A media upload (/api/wiki-assets/...) or an http(s) address. Shown square, at 26 pixels. "
			+ "The SharpMUSH logo takes the theme's accent colour; a picture of your own is shown as it is.")]
	string PortalLogo,

	[property: SharpConfig(
		Name = "portal_favicon",
		Category = "Cosmetic",
		Description = "Browser tab icon for the web portal; empty to use portal_logo",
		ValidationPattern = PortalPicture.Pattern,
		Image = true,
		Group = "Portal",
		Order = 2,
		Tooltip = "A media upload (/api/wiki-assets/...) or an http(s) address. When both this and portal_logo "
			+ "are empty, the tab shows the SharpMUSH logo. Browsers keep a tab icon for a while, so a change "
			+ "can take a reload to show.")]
	string PortalFavicon
);

/// <summary>What <c>portal_logo</c> and <c>portal_favicon</c> hold, and what the portal shows when they are empty.</summary>
public static class PortalPicture
{
	/// <summary>Empty, a path on this server, or an http(s) address, with no spaces.</summary>
	public const string Pattern = @"^(|/[^/\s]\S*|https?://\S+)$";

	/// <summary>The SharpMUSH logo, which the portal serves itself.</summary>
	public const string DefaultLogo = "/assets/Logo.svg";

	/// <summary>The tab icon for the configured pictures: the favicon, else the logo, else the SharpMUSH logo.</summary>
	public static string Favicon(CosmeticOptions cosmetic) =>
		NonEmpty(cosmetic.PortalFavicon) ?? NonEmpty(cosmetic.PortalLogo) ?? DefaultLogo;

	private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
