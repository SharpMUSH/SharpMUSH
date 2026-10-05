using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The portal's Profile Handler page (<c>/admin/profiles</c>): the character directory and profile
/// API that the bundled <c>profile-handler</c> package attaches to the configured
/// <c>http_handler</c> object.
///
/// <para>Reset puts the package's own attributes back to the values this build ships. The package
/// manager alone will not do that: its three-way merge keeps a local edit or deletion of an
/// attribute the package has not changed since (<see cref="PackageAttributeAction.KeepLocal"/>),
/// which is right for an upgrade and exactly what an explicit reset is asked to undo. So reset
/// first lets the package manager install or upgrade the package (recording baselines), then
/// writes the planned value over every attribute whose live value still differs. The planned value
/// is the package value with its <c>{{$ref}}</c> placeholders resolved, and it equals the recorded
/// baseline, so afterwards the package manager sees those attributes as unmodified. Attributes on
/// the handler that the package does not manage are left alone. Reset refuses (409) unless the
/// installed version is the one this build ships, from the bundled source, and every recorded
/// baseline on the configured handler already holds this build's value; otherwise the write would land
/// where the registry does not track it. It holds <see cref="IPackageOperationGate"/> throughout, so no
/// other package operation can move the baselines under it.</para>
///
/// Routes:
///   GET  api/admin/profile-handler        — handler dbref/name + presence of each package attribute
///   POST api/admin/profile-handler/reset  — rewrite every package attribute with its shipped value
/// </summary>
[ApiController]
[Route("api/admin/profile-handler")]
public class ProfileHandlerAdminController(
	IMediator mediator,
	IPackageManifestService manifests,
	IPackageInstallService installer,
	IPackageRegistryService registry,
	IBundledPackageBootstrap bundled,
	IPackageOperationGate gate,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<ProfileHandlerAdminController> logger) : ControllerBase
{
	/// <summary>The bundled package whose attributes this page reports and resets.</summary>
	public const string PackageId = "profile-handler";

	public record AttributeStatusDto(string Attribute, bool Present);

	public record HandlerStatusDto(
		bool Configured,
		int? HandlerDbref,
		string? HandlerName,
		bool HandlerExists,
		IReadOnlyList<AttributeStatusDto> Attributes);

	public record ResetResultDto(int Written, IReadOnlyList<string> Failed);

	[HttpGet]
	[Authorize(Policy = PortalPermission.PlayersModerate)]
	public async Task<ActionResult<HandlerStatusDto>> Status(CancellationToken ct)
	{
		if (HandlerNumber() is not { } number)
		{
			return new HandlerStatusDto(false, null, null, false, []);
		}

		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(number)), ct) is not AnySharpObject handler)
		{
			return new HandlerStatusDto(true, number, null, false, []);
		}

		var statuses = new List<AttributeStatusDto>();
		foreach (var attribute in BundledManifest().Objects.Single().Attributes.Keys)
		{
			// GetAttributeQuery is all-or-nothing: it yields the path's nodes only when the leaf exists.
			var present = await mediator.CreateStream(
				new GetAttributeQuery(handler.Object().DBRef, attribute.Split('`')), ct).AnyAsync(ct);
			statuses.Add(new AttributeStatusDto(attribute, present));
		}

		return new HandlerStatusDto(true, number, handler.Object().Name, true, statuses);
	}

	[HttpPost("reset")]
	[Authorize(Policy = PortalPermission.PackagesAdmin)]
	public async Task<ActionResult<ResetResultDto>> Reset(CancellationToken ct)
	{
		if (HandlerNumber() is not { } number)
		{
			return BadRequest(new ApiErrorDto("No http_handler is configured."));
		}

		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(number)), ct) is not AnySharpObject)
		{
			return NotFound(new ApiErrorDto($"The configured http_handler #{number} does not exist."));
		}

		// From the first registry read to the last write, and the plan between them, no other package
		// operation may run (#1484): an upgrade advancing the baselines mid-reset would have the reset
		// put the old values back under the new baselines. The bundled install below re-enters the gate.
		return await gate.RunAsync(() => ResetExclusiveAsync(number, ct), ct);
	}

	/// <summary>The part of <see cref="Reset"/> that reads the registry and writes, inside the package-operation gate.</summary>
	private async Task<ActionResult<ResetResultDto>> ResetExclusiveAsync(int number, CancellationToken ct)
	{
		// A profile-handler from a configured remote or a fork is someone else's package, whatever its
		// version: its record and baselines describe that package's softcode, not this build's. Refuse
		// before bootstrap runs, which would otherwise upgrade an older one in place under the bundled
		// source, and the reset would then overwrite the fork.
		if (await registry.GetInstalledPackageAsync(PackageId) is InstalledPackageRecord existing
				&& !BundledPackages.IsCatalogueSource(existing.SourceRepo))
		{
			return StatusCode(StatusCodes.Status409Conflict, new ApiErrorDto(
				$"{PackageId} was installed from {existing.SourceRepo}, not from this server's bundled packages; " +
				"reinstall it from the bundled source through the package manager."));
		}

		if (!(await bundled.InstallBundledAsync([PackageId], ct)).Contains(PackageId))
		{
			return StatusCode(StatusCodes.Status409Conflict, new ApiErrorDto(
				$"{PackageId} could not be installed; check that http-handler is installed."));
		}

		// Bootstrap leaves a same-or-newer install alone and reports a failed upgrade's old record as
		// installed, so "installed" alone does not mean this build's version is. Writing this build's
		// values over any other version would change the softcode behind the registry's back: its
		// recorded version and baselines would describe code that is no longer there.
		var manifest = BundledManifest();
		if (await registry.GetInstalledPackageAsync(PackageId) is not InstalledPackageRecord installed
				|| !PackageVersion.TryParse(installed.Version, out var installedVersion)
				|| installedVersion.CompareTo(manifest.Version) != 0)
		{
			return StatusCode(StatusCodes.Status409Conflict, new ApiErrorDto(
				$"Reset restores {PackageId} v{manifest.Version}, which is not the installed version; " +
				"upgrade or reinstall it through the package manager."));
		}

		if (await PackageManagerAsync(ct) is not { } packageManager)
		{
			return StatusCode(StatusCodes.Status500InternalServerError,
				new ApiErrorDto("The package manager player does not exist."));
		}

		var plan = await installer.PlanAsync(manifest, cancellationToken: ct);
		if (plan.IsBlocked)
		{
			return StatusCode(StatusCodes.Status409Conflict, new ApiErrorDto(
				$"{PackageId} is blocked by:{string.Join(", ", plan.DependencyIssues.Select(i => i.PackageId))}."));
		}

		// Reset leaves the registry as it is, so it may only write where the recorded baseline already is
		// this build's value on this object. A baseline elsewhere (http_handler repointed since install) or
		// holding other content would leave the written value untracked, or tracked as local drift.
		var untracked = plan.Attributes
			.Where(change => change.NewValue is not null && change.BaseValue != change.NewValue)
			.Select(change => change.Attribute)
			.ToList();
		if (untracked.Count > 0)
		{
			return StatusCode(StatusCodes.Status409Conflict, new ApiErrorDto(
				$"The package registry's baselines for {PackageId} do not match this build on #{number} " +
				$"({string.Join(", ", untracked)}); reinstall it through the package manager."));
		}

		var written = 0;
		var failed = new List<string>();
		foreach (var change in plan.Attributes)
		{
			if (change.NewValue is not { } value || change.LiveValue == value)
			{
				continue;
			}

			if (await WriteAsync(change, value, packageManager, ct))
			{
				written++;
			}
			else
			{
				failed.Add(change.Attribute);
			}
		}

		logger.LogInformation("Profile handler reset on #{Handler}: {Written} written, {Failed} failed.",
			number, written, failed.Count);
		return new ResetResultDto(written, failed);
	}

	/// <summary>
	/// Writes one planned value, as the package manager writes it. A value that still names an object
	/// only created at apply time is not written: it would land with the placeholder in it.
	/// </summary>
	private async Task<bool> WriteAsync(
		PackageAttributeChange change, string value, SharpPlayer packageManager, CancellationToken ct) =>
		!change.RequiresApplyResolution
		&& HelperFunctions.ParseDbRef(change.Objid ?? string.Empty) is DBRef target
		&& await mediator.Send(new SetAttributeCommand(
			target, change.Attribute.Split('`'), MarkupText.Plain(value), packageManager), ct);

	private int? HandlerNumber() =>
		options.CurrentValue.Database.HttpHandler is { } handler and not 0 ? (int)handler : null;

	/// <summary>The player package writes are made as, resolved the way the package installer resolves it.</summary>
	private async Task<SharpPlayer?> PackageManagerAsync(CancellationToken ct) =>
		await mediator.Send(new GetObjectNodeQuery(
				new DBRef((int)DatabaseOptions.PackageManagerOrSeeded(options.CurrentValue.Database.PackageManager))), ct)
			is AnySharpObject and SharpPlayer player
			? player
			: null;

	private PackageManifest BundledManifest() =>
		manifests.ParseManifest(BundledPackages.ManifestYaml(PackageId)) switch
		{
			ParsedPackageManifest parsed => parsed.Manifest,
			PackageManifestFailure failure => throw new InvalidOperationException(
				$"Bundled {PackageId} manifest is invalid: {string.Join("; ", failure.Issues.Select(i => i.ToString()))}")
		};
}
