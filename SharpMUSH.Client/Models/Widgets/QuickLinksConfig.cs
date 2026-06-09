namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// Represents a single link entry in the Quick Links widget config.
/// </summary>
public record QuickLink(
	string Label,
	string Url,
	string? Icon = null,
	bool NewTab = false);

/// <summary>
/// Config schema for the Quick Links widget.
/// </summary>
public record QuickLinksConfig(IReadOnlyList<QuickLink> Links);
