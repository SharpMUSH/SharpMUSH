namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// Config schema for the manually-placed Schema Widget: the softcode HTTP-handler routes it fetches.
/// Application-backed placements leave this unset and resolve their routes from the application
/// catalog by slug instead.
/// </summary>
/// <param name="SchemaUrl">
/// Route returning a Portal Schema Document. Required — with no schema URL and no matching
/// application slug the widget renders "schema unavailable". May contain the page-context tokens
/// <c>{objid}</c> and <c>{character}</c>.
/// </param>
/// <param name="DataUrl">
/// Optional route returning the data to bind into the schema. Same token substitution applies; a
/// <c>{objid}</c> token that cannot be filled skips the data fetch and renders the schema alone.
/// </param>
public record SchemaWidgetConfig(string? SchemaUrl = null, string? DataUrl = null);
