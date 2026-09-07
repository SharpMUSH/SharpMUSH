using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="Library.Services.Interfaces.IApplicationRegistryService"/>: the Dynamic Application
/// registry (Area 21). Not ported yet — every member throws <see cref="NotImplementedException"/> until
/// a later task.
/// </summary>
public sealed partial class LightningDatabase
{
	public Task UpsertApplicationAsync(RegisteredApplication application)
		=> throw new NotImplementedException();

	public Task<OneOf<RegisteredApplication, NotFound>> GetApplicationAsync(string slug)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync()
		=> throw new NotImplementedException();

	public Task RemoveApplicationAsync(string slug)
		=> throw new NotImplementedException();
}
