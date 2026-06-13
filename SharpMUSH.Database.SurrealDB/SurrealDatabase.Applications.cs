using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net.Models;

namespace SharpMUSH.Database.SurrealDB;

// IMPORTANT: SurrealDb.Net's embedded CBOR serializer ignores [JsonPropertyName].
// Property names MUST exactly match the SurrealDB field names stored in the DB —
// hence the camelCase property names on this record.
internal class SysApplicationDbRecord : Record
{
	public string slug { get; set; } = "";
	public string displayName { get; set; } = "";
	public string? icon { get; set; }
	public string kind { get; set; } = "";
	public string schemaUrl { get; set; } = "";
	public string? dataUrl { get; set; }
	public string? submitRoute { get; set; }
	public string minimumRole { get; set; } = "";
	public string? navPlacement { get; set; }
	public string zones { get; set; } = "";
	public int sortOrder { get; set; }
}

public partial class SurrealDatabase : IApplicationRegistryService
{
	#region Application Registry

	private const string SysApplicationFields =
		"id, slug, displayName, icon, kind, schemaUrl, dataUrl, submitRoute, minimumRole, navPlacement, zones, sortOrder";

	public async Task UpsertApplicationAsync(RegisteredApplication application)
	{
		var parameters = new Dictionary<string, object?>
		{
			["slug"] = application.Slug,
			["displayName"] = application.DisplayName,
			["icon"] = application.Icon,
			["kind"] = application.Kind.ToString(),
			["schemaUrl"] = application.SchemaUrl,
			["dataUrl"] = application.DataUrl,
			["submitRoute"] = application.SubmitRoute,
			["minimumRole"] = application.MinimumRole.ToString(),
			["navPlacement"] = application.NavPlacement,
			["zones"] = ApplicationRegistryMapping.ZonesToString(application.Zones),
			["sortOrder"] = application.Order
		};
		await ExecuteAsync("""
			UPSERT type::thing('sys_application', $slug) SET slug = $slug, displayName = $displayName,
				icon = $icon, kind = $kind, schemaUrl = $schemaUrl, dataUrl = $dataUrl,
				submitRoute = $submitRoute, minimumRole = $minimumRole, navPlacement = $navPlacement,
				zones = $zones, sortOrder = $sortOrder
			""", parameters);
	}

	public async Task<OneOf<RegisteredApplication, NotFound>> GetApplicationAsync(string slug)
	{
		var response = await ExecuteAsync(
			$"SELECT {SysApplicationFields} FROM sys_application WHERE slug = $slug",
			new Dictionary<string, object?> { ["slug"] = slug });
		var results = response.GetValue<List<SysApplicationDbRecord>>(0);

		return results?.Count > 0 ? MapApplication(results[0]) : new NotFound();
	}

	public async Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync()
	{
		var response = await ExecuteAsync(
			$"SELECT {SysApplicationFields} FROM sys_application ORDER BY sortOrder, slug");
		var results = response.GetValue<List<SysApplicationDbRecord>>(0) ?? [];
		return results.Select(MapApplication).ToList();
	}

	public async Task RemoveApplicationAsync(string slug)
	{
		await ExecuteAsync("DELETE type::thing('sys_application', $slug)",
			new Dictionary<string, object?> { ["slug"] = slug });
	}

	private static RegisteredApplication MapApplication(SysApplicationDbRecord r) => new(
		r.slug,
		r.displayName,
		r.icon,
		Enum.Parse<ApplicationKind>(r.kind, ignoreCase: true),
		r.schemaUrl,
		r.dataUrl,
		r.submitRoute,
		Enum.Parse<PortalRole>(r.minimumRole, ignoreCase: true),
		r.navPlacement,
		ApplicationRegistryMapping.ZonesFromString(r.zones),
		r.sortOrder);

	#endregion
}
