using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Seeds the default <c>character-header</c> Widget application at first boot, so the character
/// profile header is a first-class Area-21 application (visible/manageable in /admin/applications)
/// that the layout system places by default at the top of the character page.
///
/// Idempotent: if an application with that slug already exists (admin edited or removed-and-re-added),
/// it is left untouched. The schema/data routes point at the bundled profile-handler softcode; the
/// <c>{objid}</c> token in the data route is filled per-character by the client at render time.
/// </summary>
public class DefaultApplicationsBootstrapService(
	IApplicationRegistryService applications,
	ILogger<DefaultApplicationsBootstrapService> logger) : IHostedService
{
	/// <summary>Slug of the seeded character-header application (also its widget name in layouts).</summary>
	public const string CharacterHeaderSlug = "character-header";

	internal static RegisteredApplication CharacterHeaderApplication => new(
		Slug: CharacterHeaderSlug,
		DisplayName: "Character Header",
		Icon: "Badge",
		Kind: ApplicationKind.Widget,
		SchemaUrl: "http/profile/schema",
		DataUrl: "http/profile?objid={objid}",
		SubmitRoute: null,
		MinimumRole: PortalRole.Guest,
		NavPlacement: null,
		Zones: [WidgetZone.MainContent],
		Order: 0,
		OwningPackage: null);

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		try
		{
			var existing = await applications.GetApplicationAsync(CharacterHeaderSlug);
			if (existing.IsT0)
			{
				logger.LogDebug("Application '{Slug}' already registered; leaving it as-is.", CharacterHeaderSlug);
				return;
			}

			await applications.UpsertApplicationAsync(CharacterHeaderApplication);
			logger.LogInformation("Seeded default '{Slug}' widget application.", CharacterHeaderSlug);
		}
		catch (Exception ex)
		{
			// Never let a seeding hiccup block startup; an admin can register it manually.
			logger.LogWarning(ex, "Could not seed the default '{Slug}' application.", CharacterHeaderSlug);
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
