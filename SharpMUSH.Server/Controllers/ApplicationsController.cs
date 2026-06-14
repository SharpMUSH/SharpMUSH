using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Dynamic Application registry API (Area 21). The registry links a portal entry point — a full page
/// (<c>/apps/{slug}</c>) or a placeable widget — to the softcode HTTP-handler endpoints that produce
/// its Portal Schema Document, data, and action results.
///
/// Routes:
///   GET    /api/applications          — list registered applications (authenticated; client filters by role)
///   GET    /api/applications/{slug}    — fetch one
///   POST   /api/applications           — create or update one (Wizard+); validates the schema endpoint
///   DELETE /api/applications/{slug}    — remove one (Wizard+)
///
/// Reads are available to any authenticated user because the records are nav metadata only; the
/// actual data and actions are authorized by the in-game handler. Registration is Wizard+ (decision
/// 10.3), matching layout/theme editing.
/// </summary>
[ApiController]
[Route("api/applications")]
[Authorize]
public class ApplicationsController(
	IApplicationRegistryService registry,
	IHttpHandlerCommandDispatcher dispatcher,
	ILogger<ApplicationsController> logger) : ControllerBase
{
	public record ApplicationDto(
		string Slug,
		string DisplayName,
		string? Icon,
		string Kind,
		string SchemaUrl,
		string? DataUrl,
		string? SubmitRoute,
		string MinimumRole,
		string? NavPlacement,
		string[] Zones,
		int Order,
		string? OwningPackage = null);

	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<ApplicationDto>>> List()
	{
		var apps = await registry.GetApplicationsAsync();
		return Ok(apps.Select(ToDto).ToList());
	}

	[HttpGet("{slug}")]
	public async Task<ActionResult<ApplicationDto>> Get(string slug)
	{
		var result = await registry.GetApplicationAsync(slug);
		return result.Match<ActionResult<ApplicationDto>>(
			app => Ok(ToDto(app)),
			_ => NotFound());
	}

	[HttpPost]
	[Authorize(Policy = PortalPermission.ApplicationsAdmin)]
	public async Task<IActionResult> Upsert([FromBody] ApplicationDto dto)
	{
		if (string.IsNullOrWhiteSpace(dto.Slug) || string.IsNullOrWhiteSpace(dto.DisplayName)
			|| string.IsNullOrWhiteSpace(dto.SchemaUrl))
		{
			return BadRequest(new { error = "Slug, display name, and schema URL are required." });
		}

		if (!Enum.TryParse<ApplicationKind>(dto.Kind, ignoreCase: true, out var kind))
		{
			return BadRequest(new { error = $"Unknown application kind: {dto.Kind}" });
		}

		if (!Enum.TryParse<PortalRole>(dto.MinimumRole, ignoreCase: true, out var role))
		{
			return BadRequest(new { error = $"Unknown role: {dto.MinimumRole}" });
		}

		// Validate the schema endpoint returns parseable JSON before persisting — the same defensive
		// stance the client's SchemaAppService takes, but enforced server-side at registration time.
		var (ok, reason) = await ValidateSchemaEndpointAsync(dto.SchemaUrl);
		if (!ok)
		{
			return BadRequest(new { error = $"Schema endpoint validation failed: {reason}" });
		}

		// Preserve provenance: a manual edit of a package-installed application keeps its OwningPackage,
		// so uninstalling the package still reclaims the record. Manual registrations stay unowned.
		var existing = await registry.GetApplicationAsync(dto.Slug.Trim());
		var owningPackage = existing.Match(app => app.OwningPackage, _ => null);

		var application = new RegisteredApplication(
			dto.Slug.Trim(),
			dto.DisplayName.Trim(),
			string.IsNullOrWhiteSpace(dto.Icon) ? null : dto.Icon.Trim(),
			kind,
			dto.SchemaUrl.Trim(),
			string.IsNullOrWhiteSpace(dto.DataUrl) ? null : dto.DataUrl.Trim(),
			string.IsNullOrWhiteSpace(dto.SubmitRoute) ? null : dto.SubmitRoute.Trim(),
			role,
			string.IsNullOrWhiteSpace(dto.NavPlacement) ? null : dto.NavPlacement.Trim(),
			ParseZones(dto.Zones),
			dto.Order,
			owningPackage);

		await registry.UpsertApplicationAsync(application);
		logger.LogInformation("Registered application '{Slug}' ({Kind}).", application.Slug, application.Kind);
		return Ok(ToDto(application));
	}

	[HttpDelete("{slug}")]
	[Authorize(Policy = PortalPermission.ApplicationsAdmin)]
	public async Task<IActionResult> Delete(string slug)
	{
		await registry.RemoveApplicationAsync(slug);
		logger.LogInformation("Removed application '{Slug}'.", slug);
		return Ok(new { deleted = true });
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	/// <summary>
	/// Runs a GET against the schema route through the same dispatcher the <c>/http</c> route uses,
	/// and confirms a successful, parseable-JSON response. Returns (ok, reason-when-not-ok).
	/// </summary>
	private async Task<(bool Ok, string Reason)> ValidateSchemaEndpointAsync(string schemaUrl)
	{
		var path = ToHandlerPath(schemaUrl);
		if (path is null)
		{
			return (false, "schema URL must be an /http handler route (e.g. http/chargen/schema).");
		}

		try
		{
			var result = await dispatcher.DispatchAsync("GET", path, string.Empty, [], HttpContext.RequestAborted);
			return result.Match(
				handled =>
				{
					if (handled.Status is < 200 or >= 300)
					{
						return (false, $"handler returned HTTP {handled.Status}.");
					}

					try
					{
						using var _ = JsonDocument.Parse(handled.Body);
						return (true, string.Empty);
					}
					catch (JsonException ex)
					{
						return (false, $"response was not valid JSON ({ex.Message}).");
					}
				},
				_ => (false, "no http_handler route is configured for that path."));
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Schema endpoint validation threw for {SchemaUrl}.", schemaUrl);
			return (false, "the handler threw while producing the schema.");
		}
	}

	/// <summary>
	/// Converts a stored schema URL (relative, e.g. <c>http/chargen/schema</c> or <c>/http/chargen/schema</c>)
	/// into the dispatcher's path form (<c>/chargen/schema</c>). Returns null for non-/http inputs.
	/// </summary>
	private static string? ToHandlerPath(string schemaUrl)
	{
		var trimmed = schemaUrl.Trim().TrimStart('/');
		if (!trimmed.StartsWith("http/", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var remainder = trimmed["http/".Length..];
		return string.IsNullOrWhiteSpace(remainder) ? null : $"/{remainder}";
	}

	private static IReadOnlyList<WidgetZone>? ParseZones(string[]? zones)
		=> zones is null || zones.Length == 0
			? null
			: ApplicationRegistryMapping.ZonesFromString(string.Join(",", zones));

	private static ApplicationDto ToDto(RegisteredApplication a) => new(
		a.Slug,
		a.DisplayName,
		a.Icon,
		a.Kind.ToString(),
		a.SchemaUrl,
		a.DataUrl,
		a.SubmitRoute,
		a.MinimumRole.ToString(),
		a.NavPlacement,
		(a.Zones ?? []).Select(z => z.ToString()).ToArray(),
		a.Order,
		a.OwningPackage);
}
