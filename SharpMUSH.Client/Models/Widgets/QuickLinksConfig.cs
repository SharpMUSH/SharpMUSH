using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// Represents a single link entry in the Quick Links widget config.
/// </summary>
public record QuickLink(
	[property: WidgetConfigKey("LayCfgQuickLinkLabel")]
	string Label,
	[property: WidgetConfigKey("LayCfgQuickLinkUrl")]
	string Url,
	[property: WidgetConfigKey("LayCfgQuickLinkIcon")]
	string? Icon = null,
	[property: WidgetConfigKey("LayCfgQuickLinkNewTab")]
	bool NewTab = false);

/// <summary>
/// Config schema for the Quick Links widget.
/// </summary>
public record QuickLinksConfig(
	[property: WidgetConfigKey("LayCfgQuickLinks")]
	IReadOnlyList<QuickLink> Links);
