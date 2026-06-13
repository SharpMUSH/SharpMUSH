using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase : IApplicationRegistryService
{
	#region Application Registry

	private class ApplicationDbDoc
	{
		public string Slug { get; set; } = "";
		public string DisplayName { get; set; } = "";
		public string? Icon { get; set; }
		public string Kind { get; set; } = "";
		public string SchemaUrl { get; set; } = "";
		public string? DataUrl { get; set; }
		public string? SubmitRoute { get; set; }
		public string MinimumRole { get; set; } = "";
		public string? NavPlacement { get; set; }
		public string Zones { get; set; } = "";
		public int Order { get; set; }
		public string? OwningPackage { get; set; }
	}

	public async Task UpsertApplicationAsync(RegisteredApplication application)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"UPSERT { _key: @key } INSERT @doc REPLACE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Applications },
				{ "key", application.Slug },
				{ "doc", ToDoc(application) }
			});

		static Dictionary<string, object?> ToDoc(RegisteredApplication a) => new()
		{
			["_key"] = a.Slug,
			["Slug"] = a.Slug,
			["DisplayName"] = a.DisplayName,
			["Icon"] = a.Icon,
			["Kind"] = a.Kind.ToString(),
			["SchemaUrl"] = a.SchemaUrl,
			["DataUrl"] = a.DataUrl,
			["SubmitRoute"] = a.SubmitRoute,
			["MinimumRole"] = a.MinimumRole.ToString(),
			["NavPlacement"] = a.NavPlacement,
			["Zones"] = ApplicationRegistryMapping.ZonesToString(a.Zones),
			["Order"] = a.Order,
			["OwningPackage"] = a.OwningPackage
		};
	}

	public async Task<OneOf<RegisteredApplication, NotFound>> GetApplicationAsync(string slug)
	{
		var result = await arangoDb.Query.ExecuteAsync<ApplicationDbDoc>(handle,
			"FOR d IN @@c FILTER d._key == @key RETURN d",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Applications },
				{ "key", slug }
			});

		return result.Count == 0 ? new NotFound() : Map(result[0]);
	}

	public async Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync()
	{
		var result = await arangoDb.Query.ExecuteAsync<ApplicationDbDoc>(handle,
			"FOR d IN @@c SORT d.Order, d.Slug RETURN d",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Applications } });

		return result.Select(Map).ToList();
	}

	public async Task RemoveApplicationAsync(string slug)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR d IN @@c FILTER d._key == @key REMOVE d IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Applications },
				{ "key", slug }
			});
	}

	private static RegisteredApplication Map(ApplicationDbDoc d) => new(
		d.Slug,
		d.DisplayName,
		d.Icon,
		Enum.Parse<ApplicationKind>(d.Kind, ignoreCase: true),
		d.SchemaUrl,
		d.DataUrl,
		d.SubmitRoute,
		Enum.Parse<PortalRole>(d.MinimumRole, ignoreCase: true),
		d.NavPlacement,
		ApplicationRegistryMapping.ZonesFromString(d.Zones),
		d.Order,
		d.OwningPackage);

	#endregion
}
