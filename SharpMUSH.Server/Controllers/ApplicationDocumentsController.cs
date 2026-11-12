using System.Text.Json;
using System.Text.Json.Nodes;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The stored documents of registered applications the builder did not make, so the builder can open and save them
/// in place. A schema route that serves a document kept as it is, <c>GET`PROFILE`SCHEMA</c> answering with
/// <c>v(DATA`PROFILE`SCHEMA)</c>, keeps it beside the route on the <c>http_handler</c>: the route's path with
/// <c>DATA</c> in place of the verb. An application whose schema URL is <c>http/&lt;path&gt;</c> and whose handler holds
/// a JSON object at <c>DATA`&lt;PATH&gt;</c> is listed here; one that builds its document in softcode has no such
/// attribute and is not.
/// </summary>
/// <remarks>
/// <para>
/// Every read and write goes through <see cref="IAttributeService"/> as the session's character, so the caller sees
/// only what they could read with <c>get()</c> and saves only where they could write with <c>&amp;</c>. Only the
/// document is written: the application's own routes still supply its values and take its actions.
/// </para>
/// <para>
/// A document a package installed stays the package's attribute. The package manager upgrades with a three-way
/// merge, so an edit made here is kept as local change: an upgrade that changes the same document is a conflict, which
/// the package manager asks about and the bundled packages' own upgrade at startup settles by keeping the game's
/// version. The listing says which package owns it and whether it differs from what that package installed.
/// </para>
/// Routes:
///   GET api/built-documents         — the registered applications whose document can be edited here
///   GET api/built-documents/{slug}  — one of them, with its document
///   PUT api/built-documents/{slug}  — writes its document, after the same checks as schemacheck()
/// </remarks>
[ApiController]
[Route("api/built-documents")]
[Authorize]
public class ApplicationDocumentsController(
	IMediator mediator,
	IAttributeService attributes,
	IApplicationRegistryService registry,
	IPackageRegistryService packages,
	IPermissionService permissions,
	IVisibleWorldProjection projection,
	IOptionsWrapper<SharpMUSHOptions> options) : ControllerBase
{
	/// <summary>
	/// An application's stored document: where it is kept, whether the caller may change it, and the package that
	/// installed it with whether it still holds what that package installed.
	/// </summary>
	public sealed record StoredDocument(string Slug, string Name, string Kind, string Holder, string HolderName,
		string Attribute, bool Controls, string? Package, bool Changed);

	/// <summary>One stored document and its text.</summary>
	public sealed record StoredDocumentSource(StoredDocument Document, string Schema);

	/// <summary>The document to save.</summary>
	public sealed record SaveRequest(string Schema);

	/// <summary>
	/// The attribute holding the document a schema URL serves: <c>http/profile/schema</c> is <c>DATA`PROFILE`SCHEMA</c>.
	/// Null for a URL that is not an <c>http/</c> route, or whose path has a placeholder or anything an attribute name
	/// cannot hold. The query string is not part of it.
	/// </summary>
	public static string? DocumentAttribute(string? schemaUrl)
	{
		var path = (schemaUrl ?? string.Empty).Trim().TrimStart('/');
		var query = path.IndexOf('?');
		if (query >= 0) path = path[..query];
		if (!path.StartsWith("http/", StringComparison.OrdinalIgnoreCase)) return null;
		var segments = path["http/".Length..].Split('/');
		return segments.All(segment => segment.Length > 0 && segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
			? "DATA`" + string.Join('`', segments).ToUpperInvariant()
			: null;
	}

	[HttpGet]
	public async Task<IActionResult> List(CancellationToken ct)
	{
		if (await ViewerAsync(ct) is not { } caller) return Unauthorized();
		if (await HandlerAsync(ct) is not { } handler) return Ok(Array.Empty<StoredDocument>());

		var found = new List<StoredDocument>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var application in (await registry.GetApplicationsAsync()).OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase))
		{
			if (await OpenAsync(caller, handler, application) is { } opened && seen.Add(opened.Document.Attribute))
				found.Add(opened.Document);
		}
		return Ok(found);
	}

	[HttpGet("{slug}")]
	public async Task<IActionResult> Get(string slug, CancellationToken ct)
	{
		if (await ViewerAsync(ct) is not { } caller) return Unauthorized();
		return await HandlerAsync(ct) is { } handler
			&& await registry.GetApplicationAsync(slug) is RegisteredApplication application
			&& await OpenAsync(caller, handler, application) is { } opened
			? Ok(opened)
			: NotFound(new ApiErrorDto($"No application called {slug} keeps a document that can be edited here."));
	}

	[HttpPut("{slug}")]
	public async Task<IActionResult> Save(string slug, [FromBody] SaveRequest request, CancellationToken ct)
	{
		if (await ViewerAsync(ct) is not { } caller) return Unauthorized();
		if (await HandlerAsync(ct) is not { } handler
			|| await registry.GetApplicationAsync(slug) is not RegisteredApplication application
			|| await OpenAsync(caller, handler, application) is not { } opened)
			return NotFound(new ApiErrorDto($"No application called {slug} keeps a document that can be edited here."));
		if (PortalSchema.Check(Parse(request.Schema)) is { Count: > 0 } problems)
			return UnprocessableEntity(new BuiltApplicationsController.SaveProblems(problems));

		return await attributes.SetAttributeAsync(caller, handler, opened.Document.Attribute, MarkupString.MarkupText.Plain(request.Schema)) switch
		{
			Error<string> error => StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto(error.Value)),
			_ => await OpenAsync(caller, handler, application) is { } saved ? Ok(saved) : NotFound(),
		};
	}

	/// <summary>
	/// <paramref name="application"/>'s stored document as <paramref name="caller"/> reads it, or null when it is not a
	/// schema application, keeps no document beside its route, or the caller cannot read it.
	/// </summary>
	private async Task<StoredDocumentSource?> OpenAsync(AnySharpObject caller, AnySharpObject handler, RegisteredApplication application)
	{
		if (!string.Equals(application.RenderKind, ApplicationRenderKind.Schema, StringComparison.OrdinalIgnoreCase)
			|| DocumentAttribute(application.SchemaUrl) is not { } attribute
			|| await attributes.GetAttributeAsync(caller, handler, attribute, IAttributeService.AttributeMode.Read, parent: false) is not SharpAttribute[] read
			|| read.Last().Value.ToPlainText() is not { } text
			|| Parse(text) is not JsonObject document)
			return null;

		var holder = handler.Object().DBRef.ToString();
		var managed = (await packages.GetManagedAttributesForObjectAsync(holder))
			.FirstOrDefault(record => string.Equals(record.Attribute, attribute, StringComparison.OrdinalIgnoreCase));
		return new StoredDocumentSource(new StoredDocument(
			application.Slug,
			application.DisplayName,
			(string?)(document["kind"] as JsonValue) ?? "view",
			holder,
			handler.Object().Name,
			attribute,
			await permissions.Controls(caller, handler),
			managed?.PackageId,
			managed is not null && managed.BaselineValue != text), text);
	}

	/// <summary>The configured <c>http_handler</c>, which serves every <c>http/</c> route.</summary>
	private async Task<AnySharpObject?> HandlerAsync(CancellationToken ct) =>
		options.CurrentValue.Database.HttpHandler is { } number and not 0
			&& await mediator.Send(new GetObjectNodeQuery(new DBRef((int)number)), ct) is AnySharpObject handler
			? handler
			: null;

	private ValueTask<AnySharpObject?> ViewerAsync(CancellationToken ct) => User.ResolveExecutorAsync(projection, ct);

	private static JsonNode? Parse(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return null;
		try
		{
			return JsonNode.Parse(text);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
