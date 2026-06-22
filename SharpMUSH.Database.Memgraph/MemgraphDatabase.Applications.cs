using Neo4j.Driver;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase : IApplicationRegistryService
{
	#region Application Registry

	// Node label: :SysApplication, keyed by slug.

	public async Task UpsertApplicationAsync(RegisteredApplication application)
	{
		await ExecuteWithRetryAsync("""
			MERGE (a:SysApplication {slug: $slug})
			SET a.displayName = $displayName, a.icon = $icon, a.kind = $kind, a.schemaUrl = $schemaUrl,
			    a.dataUrl = $dataUrl, a.submitRoute = $submitRoute, a.minimumRole = $minimumRole,
			    a.navPlacement = $navPlacement, a.zones = $zones, a.order = $order, a.owningPackage = $owningPackage,
			    a.renderKind = $renderKind, a.componentAssemblyUrl = $componentAssemblyUrl,
			    a.componentTypeName = $componentTypeName
			""",
			new
			{
				slug = application.Slug,
				displayName = application.DisplayName,
				icon = application.Icon,
				kind = application.Kind.ToString(),
				schemaUrl = application.SchemaUrl,
				dataUrl = application.DataUrl,
				submitRoute = application.SubmitRoute,
				minimumRole = application.MinimumRole.ToString(),
				navPlacement = application.NavPlacement,
				zones = ApplicationRegistryMapping.ZonesToString(application.Zones),
				order = application.Order,
				owningPackage = application.OwningPackage,
				renderKind = application.RenderKind,
				componentAssemblyUrl = application.ComponentAssemblyUrl,
				componentTypeName = application.ComponentTypeName
			});
	}

	public async Task<OneOf<RegisteredApplication, NotFound>> GetApplicationAsync(string slug)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (a:SysApplication {slug: $slug}) RETURN a", new { slug });

		return result.Result.Count == 0
			? new NotFound()
			: MapApplicationNode(result.Result[0]["a"].As<INode>());
	}

	public async Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync()
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (a:SysApplication) RETURN a ORDER BY a.order, a.slug");
		return result.Result.Select(r => MapApplicationNode(r["a"].As<INode>())).ToList();
	}

	public async Task RemoveApplicationAsync(string slug)
	{
		await ExecuteWithRetryAsync(
			"MATCH (a:SysApplication {slug: $slug}) DETACH DELETE a", new { slug });
	}

	private static RegisteredApplication MapApplicationNode(INode node) => new(
		node.Properties["slug"].As<string>(),
		node.Properties["displayName"].As<string>(),
		OptionalString(node, "icon"),
		Enum.Parse<ApplicationKind>(node.Properties["kind"].As<string>(), ignoreCase: true),
		node.Properties["schemaUrl"].As<string>(),
		OptionalString(node, "dataUrl"),
		OptionalString(node, "submitRoute"),
		Enum.Parse<PortalRole>(node.Properties["minimumRole"].As<string>(), ignoreCase: true),
		OptionalString(node, "navPlacement"),
		ApplicationRegistryMapping.ZonesFromString(OptionalString(node, "zones")),
		node.Properties["order"].As<int>(),
		OptionalString(node, "owningPackage"),
		OptionalString(node, "renderKind") ?? ApplicationRenderKind.Schema,
		OptionalString(node, "componentAssemblyUrl"),
		OptionalString(node, "componentTypeName"));

	#endregion
}
