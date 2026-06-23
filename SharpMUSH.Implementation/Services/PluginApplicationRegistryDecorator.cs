using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Read-only overlay decorator over the DB-backed <see cref="IApplicationRegistryService"/>: the catalog's
/// plugin-contributed <see cref="RegisteredApplication"/>(s) are unioned into reads
/// (<see cref="GetApplicationsAsync"/> / <see cref="GetApplicationAsync"/>) while their plugins are loaded,
/// and are otherwise absent — they are never persisted and travel only in memory.
///
/// <para><b>Writes pass through to the DB inner, never to plugin-owned slugs.</b> A plugin app is not
/// admin-editable: <see cref="UpsertApplicationAsync"/> refuses to overwrite a plugin-owned slug, and
/// <see cref="RemoveApplicationAsync"/> ignores one (an overlay row cannot be deleted; it disappears when its
/// plugin unloads). Both still operate normally on DB-backed slugs.</para>
///
/// <para><b>Slug-collision rule:</b> the DB/built-in record wins. A plugin overlay entry whose slug already
/// exists in the DB is skipped with a logged warning; identity stays the <see cref="RegisteredApplication.Slug"/>.
/// Ordering of the merged list is <see cref="RegisteredApplication.Order"/> then slug — the inner contract.</para>
/// </summary>
public sealed class PluginApplicationRegistryDecorator(
	IApplicationRegistryService inner,
	PluginCatalog catalog,
	ILogger<PluginApplicationRegistryDecorator> logger) : IApplicationRegistryService
{
	/// <summary>The current plugin-contributed apps, isolated so a single throwing source cannot break reads.</summary>
	private IReadOnlyList<RegisteredApplication> PluginApplications()
	{
		var result = new List<RegisteredApplication>();
		foreach (var source in catalog.ApplicationSources)
		{
			try
			{
				result.AddRange(source.GetApplications());
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "An IApplicationSource threw while contributing applications; skipping it.");
			}
		}

		return result;
	}

	/// <summary>True when <paramref name="slug"/> is contributed by a loaded plugin (overlay, not persisted).</summary>
	private bool IsPluginOwned(string slug) =>
		PluginApplications().Any(a => string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));

	public async Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync()
	{
		var dbApps = await inner.GetApplicationsAsync();
		var pluginApps = PluginApplications();
		if (pluginApps.Count == 0)
		{
			return dbApps;
		}

		// DB/built-in wins on a slug collision: index the DB slugs, then fold in only the non-colliding
		// plugin overlays (logging each skip), and re-order the union by Order then slug to preserve the
		// inner contract.
		var dbSlugs = new HashSet<string>(dbApps.Select(a => a.Slug), StringComparer.OrdinalIgnoreCase);
		var merged = new List<RegisteredApplication>(dbApps);
		foreach (var app in pluginApps)
		{
			if (dbSlugs.Add(app.Slug))
			{
				merged.Add(app);
			}
			else
			{
				logger.LogWarning(
					"Plugin application '{Slug}' collides with a DB-backed/built-in application; the DB record wins and the plugin overlay is skipped.",
					app.Slug);
			}
		}

		return merged
			.OrderBy(a => a.Order)
			.ThenBy(a => a.Slug, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	public async Task<OneOf<RegisteredApplication, NotFound>> GetApplicationAsync(string slug)
	{
		// DB/built-in wins: try the inner first, only fall back to the plugin overlay when the DB has no
		// such slug.
		var dbResult = await inner.GetApplicationAsync(slug);
		if (dbResult.IsT0)
		{
			return dbResult;
		}

		var overlay = PluginApplications()
			.FirstOrDefault(a => string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));
		return overlay is not null ? overlay : new NotFound();
	}

	public async Task UpsertApplicationAsync(RegisteredApplication application)
	{
		if (IsPluginOwned(application.Slug))
		{
			logger.LogWarning(
				"Refusing to upsert application '{Slug}': it is contributed by a loaded plugin and is not admin-editable.",
				application.Slug);
			return;
		}

		await inner.UpsertApplicationAsync(application);
	}

	public async Task RemoveApplicationAsync(string slug)
	{
		if (IsPluginOwned(slug))
		{
			logger.LogWarning(
				"Refusing to remove application '{Slug}': it is a plugin-contributed overlay (not persisted; it disappears when its plugin unloads).",
				slug);
			return;
		}

		await inner.RemoveApplicationAsync(slug);
	}
}
