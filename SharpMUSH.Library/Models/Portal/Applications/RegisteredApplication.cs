using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Library.Models.Portal.Applications;

/// <summary>Whether a registered application is a full-page route or a placeable widget.</summary>
public enum ApplicationKind
{
	/// <summary>A nav-linked full-page application at <c>/apps/{slug}</c>.</summary>
	Page,

	/// <summary>A schema-driven widget that can be placed into a layout zone.</summary>
	Widget
}

/// <summary>
/// Registry record (Area 21) linking a portal entry point — a full page or a zone widget — to the
/// softcode HTTP-handler endpoints that produce its Portal Schema Document, data, and action results.
/// System data, never visible to softcode; persisted by every database provider and travels with
/// backups. Identity is the <see cref="Slug"/> (unique, user-facing); renaming the slug re-keys the
/// record, exactly as the package registry keys on package id.
/// </summary>
/// <param name="Slug">URL-safe unique key. Page apps render at <c>/apps/{slug}</c>.</param>
/// <param name="DisplayName">Human-readable label shown in nav and the widget palette.</param>
/// <param name="Icon">Material icon name, or null for a default.</param>
/// <param name="Kind">Full-page (<see cref="ApplicationKind.Page"/>) or placeable widget.</param>
/// <param name="SchemaUrl">Relative HTTP-handler route returning the Portal Schema Document (e.g. <c>http/chargen/schema</c>).</param>
/// <param name="DataUrl">Optional relative route returning data values (view display / form prefill).</param>
/// <param name="SubmitRoute">Optional base relative route for the schema's POST actions.</param>
/// <param name="MinimumRole">Lowest <see cref="PortalRole"/> that may see the nav entry / open the route. The hierarchy means higher roles always qualify.</param>
/// <param name="NavPlacement">Nav section hint for Page apps, or null to hide from nav.</param>
/// <param name="Zones">Allowed layout zones for Widget apps, or null for none.</param>
/// <param name="Order">Sort order within nav / listings (ascending).</param>
public sealed record RegisteredApplication(
	string Slug,
	string DisplayName,
	string? Icon,
	ApplicationKind Kind,
	string SchemaUrl,
	string? DataUrl,
	string? SubmitRoute,
	PortalRole MinimumRole,
	string? NavPlacement,
	IReadOnlyList<WidgetZone>? Zones,
	int Order);
