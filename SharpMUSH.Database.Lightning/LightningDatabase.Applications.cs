using OneOf;
using OneOf.Types;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="Library.Services.Interfaces.IApplicationRegistryService"/>: the Dynamic Application
/// registry (Area 21). Ported from <c>SurrealDatabase.Applications.cs</c>; keyed by
/// <see cref="RegisteredApplication.Slug"/> in <see cref="Tables.App"/>.
/// </summary>
public sealed partial class LightningDatabase
{
	private static ApplicationRecord ToApplicationRecord(RegisteredApplication application) => new()
	{
		Slug = application.Slug,
		DisplayName = application.DisplayName,
		Icon = application.Icon,
		Kind = application.Kind.ToString(),
		SchemaUrl = application.SchemaUrl,
		DataUrl = application.DataUrl,
		SubmitRoute = application.SubmitRoute,
		MinimumRole = application.MinimumRole.ToString(),
		NavPlacement = application.NavPlacement,
		Zones = ApplicationRegistryMapping.ZonesToString(application.Zones),
		SortOrder = application.Order,
		OwningPackage = application.OwningPackage,
		RenderKind = application.RenderKind,
		ComponentAssemblyUrl = application.ComponentAssemblyUrl,
		ComponentTypeName = application.ComponentTypeName
	};

	private static RegisteredApplication MapApplication(ApplicationRecord r) => new(
		r.Slug,
		r.DisplayName,
		r.Icon,
		Enum.Parse<ApplicationKind>(r.Kind, ignoreCase: true),
		r.SchemaUrl,
		r.DataUrl,
		r.SubmitRoute,
		Enum.Parse<PortalRole>(r.MinimumRole, ignoreCase: true),
		r.NavPlacement,
		ApplicationRegistryMapping.ZonesFromString(r.Zones),
		r.SortOrder,
		r.OwningPackage,
		r.RenderKind ?? ApplicationRenderKind.Schema,
		r.ComponentAssemblyUrl,
		r.ComponentTypeName);

	public async Task UpsertApplicationAsync(RegisteredApplication application)
		=> await Store.WriteAsync(tx =>
			tx.Put(Tables.App, Keys.Str(application.Slug), Codec.Serialize(ToApplicationRecord(application))));

	public Task<OneOf<RegisteredApplication, NotFound>> GetApplicationAsync(string slug)
	{
		var result = Store.Read(tx => tx.TryGet(Tables.App, Keys.Str(slug), out var bytes)
			? MapApplication(Codec.Deserialize<ApplicationRecord>(bytes))
			: (RegisteredApplication?)null);
		return Task.FromResult<OneOf<RegisteredApplication, NotFound>>(result is null ? new NotFound() : result);
	}

	public Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync()
	{
		var results = Store.Read(tx => tx.Range(Tables.App, [])
			.Select(e => MapApplication(Codec.Deserialize<ApplicationRecord>(e.Value)))
			.OrderBy(a => a.Order)
			.ThenBy(a => a.Slug, StringComparer.Ordinal)
			.ToList());
		return Task.FromResult<IReadOnlyList<RegisteredApplication>>(results);
	}

	public async Task RemoveApplicationAsync(string slug)
		=> await Store.WriteAsync(tx => tx.Delete(Tables.App, Keys.Str(slug)));
}
