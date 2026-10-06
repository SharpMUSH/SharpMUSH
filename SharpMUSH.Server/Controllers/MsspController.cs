using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Mssp;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The MSSP page: the report crawlers read, and the <c>mssp</c> option an administrator edits. The
/// variables the server reports itself (<see cref="MsspCatalog.ServerReported"/>) are refused here;
/// they change where they come from.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
public class MsspController(
	IOptionsWrapper<SharpMUSHOptions> options,
	IExpandedDataStore database,
	ConfigurationReloadService configReloadService,
	IMsspReportService report,
	IAuditLog audit)
	: ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<MsspSettingsResponse>> Get()
		=> Ok(new MsspSettingsResponse(await report.BuildAsync(),
			MsspCatalog.Normalize(options.CurrentValue.Mssp.Variables, out _)));

	/// <summary>
	/// Replaces the <c>mssp</c> option. Refused whole, with every reason, when any variable cannot be
	/// kept: a save that quietly dropped one would read as saved.
	/// </summary>
	[HttpPut]
	public async Task<ActionResult<MsspSettingsResponse>> Put([FromBody] MsspSettingsRequest request)
	{
		// Counted before normalizing, so an oversized body is not validated entry by entry first.
		if (request.Settings is { Count: > MsspCatalog.MaxVariables })
		{
			return BadRequest(new { error = $"At most {MsspCatalog.MaxVariables} variables can be set." });
		}

		var settings = MsspCatalog.Normalize(request.Settings ?? [], out var problems);
		if (problems.Count > 0)
		{
			return BadRequest(new { error = string.Join(" ", problems) });
		}

		// The persisted copy rather than options.CurrentValue, as SitelockController does: another
		// change saved a moment ago must not be overwritten with a stale snapshot.
		var current = await database.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions))
			?? options.CurrentValue;
		await database.SetExpandedServerData(nameof(SharpMUSHOptions), current with { Mssp = new MsspOptions(settings) });
		configReloadService.SignalChange();
		await audit.RecordPortalAsync(User, AuditActions.ConfigSet, AuditTargets.Of(AuditTargetKinds.Setting, "mssp"),
			JsonSerializer.Serialize(settings));

		return Ok(new MsspSettingsResponse(await report.BuildAsync(), settings));
	}
}
