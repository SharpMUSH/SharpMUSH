using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// Represents a single link entry in the Quick Links widget config.
/// </summary>
public record QuickLink(
	[property: WidgetConfigKey("LayCfgQuickLinkLabel", LabelKey = "LayCfgQuickLinkLabelLabel")]
	string Label,
	[property: WidgetConfigKey("LayCfgQuickLinkUrl", LabelKey = "LayCfgQuickLinkUrlLabel")]
	string Url,
	[property: WidgetConfigKey("LayCfgQuickLinkIcon", LabelKey = "LayCfgQuickLinkIconLabel")]
	string? Icon = null,
	[property: WidgetConfigKey("LayCfgQuickLinkNewTab", LabelKey = "LayCfgQuickLinkNewTabLabel")]
	bool NewTab = false);

/// <summary>
/// Config schema for the Quick Links widget.
/// </summary>
public record QuickLinksConfig(
	[property: WidgetConfigKey("LayCfgQuickLinks", LabelKey = "LayCfgQuickLinksLabel")]
	IReadOnlyList<QuickLink> Links);
