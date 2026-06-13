using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Storage for the Dynamic Application registry (Area 21): the schema-driven pages and widgets an
/// admin has linked into the portal. Implemented by every database provider; system data, never
/// visible to softcode, travels with backups.
/// </summary>
/// <remarks>
/// Single-fetch methods return <c>OneOf&lt;T, NotFound&gt;</c> rather than null, matching
/// <see cref="IPackageRegistryService"/> conventions.
/// </remarks>
public interface IApplicationRegistryService
{
	/// <summary>Creates or replaces a registered application (keyed by <see cref="RegisteredApplication.Slug"/>).</summary>
	Task UpsertApplicationAsync(RegisteredApplication application);

	/// <summary>Fetches one registered application by slug.</summary>
	Task<OneOf<RegisteredApplication, NotFound>> GetApplicationAsync(string slug);

	/// <summary>Lists all registered applications, ordered by <see cref="RegisteredApplication.Order"/> then slug.</summary>
	Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync();

	/// <summary>Removes a registered application by slug. Does not error if absent.</summary>
	Task RemoveApplicationAsync(string slug);
}
