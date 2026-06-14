namespace SharpMUSH.Library.Models.Portal.Widgets;

/// <summary>
/// Global settings that control sidebar visibility and widths.
/// </summary>
/// <param name="LeftSidebarEnabled">Whether the left sidebar is shown at all.</param>
/// <param name="RightSidebarEnabled">Whether the right sidebar is shown at all.</param>
/// <param name="FooterEnabled">Whether the footer widget zone bar is shown at all.</param>
/// <param name="LeftSidebarWidth">CSS width value for the left sidebar.</param>
/// <param name="RightSidebarWidth">CSS width value for the right sidebar.</param>
public record LayoutSettings(
	bool LeftSidebarEnabled,
	bool RightSidebarEnabled,
	bool FooterEnabled = false,
	string LeftSidebarWidth = "280px",
	string RightSidebarWidth = "280px");
