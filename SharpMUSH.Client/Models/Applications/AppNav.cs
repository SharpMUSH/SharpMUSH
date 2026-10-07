using System.Text.Json.Serialization;

namespace SharpMUSH.Client.Models.Applications;

/// <summary>
/// A page application's own sidebar, as its <c>nav_url</c> route answers for the viewer: groups of links,
/// each with an optional Material icon name and count. The section sidebar draws them in place of the
/// app's single link.
/// </summary>
public sealed record AppNav(
	[property: JsonPropertyName("groups")] IReadOnlyList<AppNavGroup>? Groups);

/// <summary>A labelled group of sidebar links; an empty label draws the links with no heading.</summary>
public sealed record AppNavGroup(
	[property: JsonPropertyName("label")] string? Label,
	[property: JsonPropertyName("items")] IReadOnlyList<AppNavItem>? Items);

/// <summary>
/// One sidebar link. <see cref="Path"/> is a portal address (<c>/apps/jobs?filter=mine</c>); a link to
/// anywhere else is dropped. <see cref="Count"/> shows beside the label when set.
/// </summary>
public sealed record AppNavItem(
	[property: JsonPropertyName("label")] string Label,
	[property: JsonPropertyName("path")] string Path,
	[property: JsonPropertyName("icon")] string? Icon = null,
	[property: JsonPropertyName("count")] int? Count = null);
