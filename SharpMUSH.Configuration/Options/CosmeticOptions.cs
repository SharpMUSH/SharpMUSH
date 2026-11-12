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
		Description = "Where @moniker colours a name, and for which types of object",
		ValidationPattern = @"^\s*(!?(alltypes|players|things|rooms|exits|everywhere|chat|say|movement|look|unparse|who|system|announcements)(\s+|$))*$",
		Group = "Names",
		Order = 4,
		Tooltip = "A space-separated list. Places: chat, say, movement, look, unparse, who, system, announcements, or "
			+ "everywhere. Types: players, things, rooms, exits, or alltypes. !word takes one away, as in "
			+ "everywhere !who. Empty for nowhere. See help MONIKERS2.")]
	string MonikerList,

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
		Choices = ["double", "single", "heavy", "rounded", "mush", "ascii", "none"],
		Group = "Layout",
		Order = 1,
		Tooltip = "A client that cannot show box-drawing characters is sent the ascii pieces instead. "
			+ "See help LAYOUT BORDERS.")]
	string LayoutBorder,

	[property: SharpConfig(
		Name = "layout_theme",
		Category = "Cosmetic",
		Description = "Colour theme for layouts that name none: a theme name, or a theme as JSON",
		Group = "Layout",
		Order = 2,
		Tooltip = "sharpmush, the portal's teal on black, unless set. none for no colour. A name from themes(), such "
			+ "as terminal, nord or cyberpunk, or JSON such as {\"seed\":\"#7aa2f7\",\"harmony\":\"triadic\"}. "
			+ "See help LAYOUT THEMES.")]
	string LayoutTheme,

	[property: SharpConfig(
		Name = "image_hosts",
		Category = "Cosmetic",
		Description = "Which pictures image() and figure() may show: any, allow, block or off",
		Choices = ["any", "allow", "block", "off"],
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
		Example = "https://example.com/crest.svg",
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
		Example = "https://example.com/icon.png",
		Group = "Portal",
		Order = 2,
		Tooltip = "A media upload (/api/wiki-assets/...) or an http(s) address. When both this and portal_logo "
			+ "are empty, the tab shows the SharpMUSH logo. Browsers keep a tab icon for a while, so a change "
			+ "can take a reload to show.")]
	string PortalFavicon
)
{
	[SharpConfig(
		Name = "portal_banner",
		Category = "Cosmetic",
		Description = "Wide picture behind the game's name on the portal's Home, wiki and sign-in pages; empty for none",
		ValidationPattern = PortalPicture.Pattern,
		Image = true,
		Example = "https://example.com/banner.jpg",
		Group = "Portal",
		Order = 3,
		Tooltip = "A media upload (/api/wiki-assets/...) or an http(s) address. Shown about four times as wide as it is "
			+ "tall and cropped to fit, so keep what matters near the middle. Link previews in Discord and other "
			+ "sites show it too.")]
	public string PortalBanner { get; init; } = "";

	[SharpConfig(
		Name = "portal_tagline",
		Category = "Cosmetic",
		Description = "One short line under the game's name on the portal and in link previews",
		ValidationPattern = PortalIdentity.TaglinePattern,
		Example = "Gaslight intrigue in a city that never sleeps.",
		Group = "Portal",
		Order = 4,
		Tooltip = "At most 120 characters, such as: Gaslight intrigue in a city that never sleeps.")]
	public string PortalTagline { get; init; } = "";

	[SharpConfig(
		Name = "portal_description",
		Category = "Cosmetic",
		Description = "Two or three sentences about the game for the portal's Home and sign-in pages and link previews",
		ValidationPattern = PortalIdentity.DescriptionPattern,
		Example = "Players run the guilds of a rain-soaked city, trading favours and secrets between the docks and the palace.",
		Group = "Portal",
		Order = 5,
		Tooltip = "At most 300 characters of plain text. While it is empty, link previews use "
			+ "onboarding_introduction, the introduction written during setup.")]
	public string PortalDescription { get; init; } = "";

	[SharpConfig(
		Name = "portal_side_art",
		Category = "Cosmetic",
		Description = "Picture at the right of the game's name on the portal's Home page; empty for Sharpie when there is no banner and portal_sharpie is on",
		ValidationPattern = PortalPicture.Pattern,
		Image = true,
		Example = "https://example.com/mascot.png",
		Group = "Portal",
		Order = 6,
		Tooltip = "A media upload (/api/wiki-assets/...) or an http(s) address, such as the game's mascot or crest. "
			+ "Shown as it is, at most 144 pixels tall and 192 wide, over the banner when there is one.")]
	public string PortalSideArt { get; init; } = "";

	[SharpConfig(
		Name = "portal_sharpie",
		Category = "Cosmetic",
		Description = "Show Sharpie, the guide's mascot, at the right of the game's name on Home when portal_side_art and portal_banner are empty",
		Group = "Portal",
		Order = 7,
		Tooltip = "Sharpie is drawn for the genre and art style chosen in Prepare for players. Off leaves that space empty.")]
	public bool PortalSharpie { get; init; } = true;

	[SharpConfig(Name = "max_picture_columns", Category = "Cosmetic", Description = "Maximum width in character cells for telnet and MXP figures", Group = "Pictures", Min = 1, Max = 2000)]
	public int MaxPictureColumns { get; init; } = 200;

	[SharpConfig(Name = "max_picture_rows", Category = "Cosmetic", Description = "Maximum height in character cells for telnet and MXP figures; preserves aspect ratio", Group = "Pictures", Min = 1, Max = 1000)]
	public int MaxPictureRows { get; init; } = 100;

}

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

/// <summary>The game's identity in the portal: its banner, side picture, tagline and description.</summary>
public static class PortalIdentity
{
	/// <summary>One line of at most 120 characters, or empty.</summary>
	public const string TaglinePattern = @"^[^\r\n]{0,120}$";

	/// <summary>At most 300 characters, or empty.</summary>
	public const string DescriptionPattern = @"^[\s\S]{0,300}$";

	/// <summary><c>portal_banner</c>, or null when it is empty.</summary>
	public static string? Banner(CosmeticOptions cosmetic) => NonEmpty(cosmetic.PortalBanner);

	/// <summary><c>portal_tagline</c>, or empty.</summary>
	public static string Tagline(CosmeticOptions cosmetic) => NonEmpty(cosmetic.PortalTagline) ?? "";

	/// <summary><c>portal_side_art</c>, or null when it is empty.</summary>
	public static string? SideArt(CosmeticOptions cosmetic) => NonEmpty(cosmetic.PortalSideArt);

	/// <summary><c>portal_description</c>, or empty. The portal shows only this: Home already shows the introduction.</summary>
	public static string Description(CosmeticOptions cosmetic) => NonEmpty(cosmetic.PortalDescription) ?? "";

	/// <summary>What a link preview describes the game with: <c>portal_description</c>, else <c>onboarding_introduction</c>.</summary>
	public static string PreviewDescription(SharpMUSHOptions options) =>
		NonEmpty(options.Cosmetic.PortalDescription) ?? NonEmpty(options.Onboarding?.Introduction) ?? "";

	private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
