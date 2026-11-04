using System.Security.Claims;
using System.Text.Json;
using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Server.Services;

/// <summary>
/// What registering a built application checks: the object its record names must exist, be controlled by the
/// registering character, and already hold the document. The routes are the server's own (<see cref="BuiltApplicationsController"/>).
/// </summary>
public sealed class BuiltApplicationRegistration(
	IMediator mediator,
	IAttributeService attributes,
	IPermissionService permissions,
	IApplicationRegistryService registry,
	IVisibleWorldProjection projection)
{
	public static string SchemaRoute(string slug) => $"api/built/{slug}/schema";

	/// <summary>A widget reads the character it is placed for; a page reads the viewer.</summary>
	public static string DataRoute(string slug, bool widget) => widget ? $"api/built/{slug}/data?objid={{objid}}" : $"api/built/{slug}/data";

	public static string ActRoute(string slug) => $"api/built/{slug}/act";

	/// <summary>
	/// The configuration to store, <c>{"holder":"&lt;objid&gt;"}</c>, or why the registration is refused. A record
	/// edited without naming a holder keeps the one it had.
	/// </summary>
	public async Task<Result<JsonElement>> CheckAsync(ClaimsPrincipal user, string slug, JsonElement? config, CancellationToken ct)
	{
		if (!BuiltApplicationsController.SlugPattern().IsMatch(slug))
			return new Error<string>("A built application's slug is lower-case letters, digits, _ and -.");
		if (await user.ResolveExecutorAsync(projection, ct) is not { } caller)
			return new Error<string>("Registering a built application needs a character.");

		var named = BuiltApplicationsController.HolderOf(config);
		if (named is null && await registry.GetApplicationAsync(slug) is Library.Models.Portal.Applications.RegisteredApplication existing) named = BuiltApplicationsController.HolderOf(existing.Config);
		if (named is not { } dbref) return new Error<string>("Name the object holding the application: {\"holder\":\"<objid>\"}.");

		if (await mediator.Send(new GetObjectNodeQuery(dbref), ct) is not AnySharpObject holder || !holder.Object().DBRef.Matches(dbref))
			return new Error<string>($"There is no object {dbref}.");
		if (!await permissions.Controls(caller, holder))
			return new Error<string>($"Permission denied. You don't control {holder.Object().Name}.");
		if (await attributes.GetAttributeAsync(holder, holder, BuiltApplicationsController.BaseAttribute(slug), IAttributeService.AttributeMode.Read, parent: false) is not SharpAttribute[])
			return new Error<string>($"{holder.Object().Name} holds no application called {slug}. Save it from the builder first.");

		var objid = holder.Object().DBRef.ToString();
		return JsonSerializer.SerializeToElement(new { holder = objid });
	}
}
