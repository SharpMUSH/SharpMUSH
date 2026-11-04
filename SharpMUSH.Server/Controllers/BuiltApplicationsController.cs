using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The applications the portal's builder makes: a Portal Schema Document and the maps that say where its values
/// live, kept as attributes on an object its author controls, and served here so a built application needs no
/// route code of its own.
/// </summary>
/// <remarks>
/// <para>
/// On the holder, with <c>&lt;SLUG&gt;</c> the slug in capitals: <c>DATA`APP`&lt;SLUG&gt;</c> (the document),
/// <c>…`SOURCES</c> (field key → attribute read from the viewed character), <c>…`SAVE</c> (field key → attribute
/// written on the sender) and <c>…`REQUIRED</c> (the keys a sent form must fill, space-separated).
/// </para>
/// <para>
/// Every read and write goes through <see cref="IAttributeService"/> as the session's character, as
/// <see cref="ObjectsController"/> does: a viewer sees only what they could read with <c>get()</c>, and a form
/// changes only what the sender could change with <c>&amp;</c>. The schema, data and act routes answer only for
/// an application registered as built (<see cref="ApplicationRenderKind.Built"/>), whose record names the holder,
/// so nobody can point them at another object's attributes.
/// </para>
/// Routes:
///   GET  api/built/{slug}/schema            — the registered application's document
///   GET  api/built/{slug}/data[?objid=]     — <c>{fields}</c>, each source read as the viewer from objid or the viewer
///   POST api/built/{slug}/act               — checks REQUIRED, then writes each SAVE field on the sender
///   GET  api/built/{slug}/source?object=    — the four attributes, for the builder to open (read as the caller)
///   PUT  api/built/{slug}/source            — writes them on an object the caller controls
/// </remarks>
[ApiController]
[Route("api/built")]
[Authorize]
public partial class BuiltApplicationsController(
	IMediator mediator,
	IAttributeService attributes,
	IApplicationRegistryService registry,
	IRealityPolicy reality,
	IVisibleWorldProjection projection) : ControllerBase
{
	/// <summary>The four attributes a built application keeps, as the builder sends and reads them.</summary>
	public sealed record BuiltSource(string? Schema, string? Sources, string? Save, string? Required, string? Holder = null);

	/// <summary>What the builder sends to save: the object to write on (<c>me</c>, a dbref or an objid) and the four values.</summary>
	public sealed record SaveRequest(string Object, string Schema, string? Sources, string? Save, string? Required);

	/// <summary>A save refused for its document: one line per problem, as <c>schemacheck()</c> words them.</summary>
	public sealed record SaveProblems(IReadOnlyList<string> Problems);

	[GeneratedRegex("^[a-z0-9_-]{1,40}$")]
	public static partial Regex SlugPattern();

	[GeneratedRegex("^[A-Za-z0-9_`.-]{1,256}$")]
	private static partial Regex AttributePattern();

	/// <summary>The attribute that holds application <paramref name="slug"/>'s document.</summary>
	public static string BaseAttribute(string slug) => $"DATA`APP`{slug.ToUpperInvariant()}";

	[HttpGet("{slug}/schema")]
	public async Task<IActionResult> Schema(string slug, CancellationToken ct)
	{
		if (await HolderAsync(slug, ct) is not { } holder) return NotBuilt(slug);
		return await ReadAsync(holder, holder, BaseAttribute(slug)) is { } text && Parse(text) is JsonObject
			? Content(text, "application/json")
			: NotBuilt(slug);
	}

	[HttpGet("{slug}/data")]
	public async Task<IActionResult> Data(string slug, [FromQuery] string? objid, CancellationToken ct)
	{
		var fields = new JsonObject();
		if (await HolderAsync(slug, ct) is not { } holder || await ViewerAsync(ct) is not { } viewer)
			return Ok(new JsonObject { ["fields"] = fields });

		var target = string.IsNullOrWhiteSpace(objid) ? viewer : await FindAsync(viewer, objid, ct);
		var sources = Map(await ReadAsync(holder, holder, $"{BaseAttribute(slug)}`SOURCES"));
		foreach (var (key, attribute) in sources)
		{
			// Read as the viewer: an attribute they could not read with get() comes back hidden.
			var read = target is null || !AttributePattern().IsMatch(attribute)
				? null
				: await attributes.GetAttributeAsync(viewer, target, attribute, IAttributeService.AttributeMode.Read);
			fields[key] = read switch
			{
				SharpAttribute[] found => Datum(found.Last().Value.ToPlainText(), true),
				None => Datum(string.Empty, true),
				_ => Datum(string.Empty, false),
			};
		}
		return Ok(new JsonObject { ["fields"] = fields });
	}

	[HttpPost("{slug}/act")]
	public async Task<IActionResult> Act(string slug, [FromBody] JsonObject? body, CancellationToken ct)
	{
		if (await HolderAsync(slug, ct) is not { } holder) return Ok(Failed($"No application called {slug} is built here."));
		if (await ViewerAsync(ct) is not { } viewer) return Ok(Failed("Sign in with a character to send this."));

		var save = Map(await ReadAsync(holder, holder, $"{BaseAttribute(slug)}`SAVE"));
		if (save.Count == 0) return Ok(Failed("This application saves nothing."));

		var values = body ?? [];
		var required = (await ReadAsync(holder, holder, $"{BaseAttribute(slug)}`REQUIRED") ?? string.Empty)
			.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var missing = new JsonObject();
		foreach (var key in required.Where(key => Posted(values, key).Length == 0)) missing[key] = "Required.";
		if (missing.Count > 0) return Ok(new JsonObject { ["ok"] = false, ["errors"] = missing });

		// Written as the sender, on the sender: they change only what they could change with &.
		var failed = new List<string>();
		foreach (var (key, attribute) in save)
		{
			if (!AttributePattern().IsMatch(attribute)
				|| await attributes.SetAttributeAsync(viewer, viewer, attribute, MarkupString.MarkupText.Plain(Posted(values, key))) is not Success)
				failed.Add(key);
		}
		return Ok(failed.Count > 0
			? Failed($"Could not save {string.Join(", ", failed)}.")
			: new JsonObject { ["ok"] = true, ["message"] = "Saved." });
	}

	[HttpGet("{slug}/source")]
	public async Task<IActionResult> Source(string slug, [FromQuery] string? @object, CancellationToken ct)
	{
		if (!SlugPattern().IsMatch(slug)) return BadRequest(new ApiErrorDto("A slug is lower-case letters, digits, _ and -."));
		if (await ViewerAsync(ct) is not { } caller) return Unauthorized();
		if (await FindAsync(caller, @object ?? "me", ct) is not { } holder) return NotFound(new ApiErrorDto($"I can't find {@object}."));

		var name = BaseAttribute(slug);
		return await ReadAsync(caller, holder, name) is { } schema
			? Ok(new BuiltSource(schema, await ReadAsync(caller, holder, $"{name}`SOURCES"), await ReadAsync(caller, holder, $"{name}`SAVE"),
				await ReadAsync(caller, holder, $"{name}`REQUIRED"), holder.Object().DBRef.ToString()))
			: NotFound(new ApiErrorDto($"{holder.Object().Name} holds no application called {slug}."));
	}

	[HttpPut("{slug}/source")]
	public async Task<IActionResult> SaveSource(string slug, [FromBody] SaveRequest request, CancellationToken ct)
	{
		if (!SlugPattern().IsMatch(slug)) return BadRequest(new ApiErrorDto("A slug is lower-case letters, digits, _ and -."));
		if (await ViewerAsync(ct) is not { } caller) return Unauthorized();
		if (await FindAsync(caller, request.Object, ct) is not { } holder) return NotFound(new ApiErrorDto($"I can't find {request.Object}."));
		if (PortalSchema.Check(Parse(request.Schema)) is { Count: > 0 } problems) return UnprocessableEntity(new SaveProblems(problems));

		var name = BaseAttribute(slug);
		foreach (var (attribute, value) in ((string, string?)[])[(name, request.Schema), ($"{name}`SOURCES", request.Sources),
			($"{name}`SAVE", request.Save), ($"{name}`REQUIRED", request.Required)])
		{
			var result = string.IsNullOrEmpty(value)
				? await attributes.ClearAttributeAsync(caller, holder, attribute, IAttributeService.AttributePatternMode.Exact)
				: await attributes.SetAttributeAsync(caller, holder, attribute, MarkupString.MarkupText.Plain(value));
			// Clearing what is not there is not a failure; anything else stops the save where it is.
			if (result is Error<string> error && !string.IsNullOrEmpty(value))
				return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto(error.Value));
		}
		return Ok(new BuiltSource(request.Schema, request.Sources, request.Save, request.Required, holder.Object().DBRef.ToString()));
	}

	/// <summary>The object holding a registered built application, while it still holds it.</summary>
	private async Task<AnySharpObject?> HolderAsync(string slug, CancellationToken ct)
	{
		if (!SlugPattern().IsMatch(slug)
			|| await registry.GetApplicationAsync(slug) is not RegisteredApplication application
			|| !string.Equals(application.RenderKind, ApplicationRenderKind.Built, StringComparison.OrdinalIgnoreCase)
			|| HolderOf(application.Config) is not { } dbref)
			return null;
		return await mediator.Send(new GetObjectNodeQuery(dbref), ct) is AnySharpObject holder && holder.Object().DBRef.Matches(dbref)
			? holder
			: null;
	}

	/// <summary>The holder a built application's configuration names, in its <c>holder</c>.</summary>
	public static DBRef? HolderOf(JsonElement? configuration) =>
		configuration is { ValueKind: JsonValueKind.Object } config
		&& config.TryGetProperty("holder", out var holder) && holder.ValueKind == JsonValueKind.String
		&& DBRef.TryParse(holder.GetString(), out var dbref)
			? dbref
			: null;

	private ValueTask<AnySharpObject?> ViewerAsync(CancellationToken ct) => User.ResolveExecutorAsync(projection, ct);

	/// <summary><c>me</c>, a dbref or an objid the caller can perceive; null otherwise.</summary>
	private async Task<AnySharpObject?> FindAsync(AnySharpObject caller, string reference, CancellationToken ct)
	{
		var text = reference.Trim();
		if (text.Equals("me", StringComparison.OrdinalIgnoreCase)) return caller;
		if (!DBRef.TryParse(text, out var parsed) || parsed is not { } dbref) return null;
		return await mediator.Send(new GetObjectNodeQuery(dbref), ct) is AnySharpObject found
			&& found.Object().DBRef.Matches(dbref)
			&& await reality.CanPerceiveAsync(caller.Object().DBRef, found.Object().DBRef, ct)
				? found
				: null;
	}

	/// <summary>An attribute's text read as <paramref name="reader"/>, or null when it is missing or they may not read it.</summary>
	private async Task<string?> ReadAsync(AnySharpObject reader, AnySharpObject holder, string attribute) =>
		await attributes.GetAttributeAsync(reader, holder, attribute, IAttributeService.AttributeMode.Read, parent: false) is SharpAttribute[] found
			? found.Last().Value.ToPlainText()
			: null;

	/// <summary>A JSON object of key → attribute, as the builder writes SOURCES and SAVE; empty when it is not one.</summary>
	private static IReadOnlyList<(string Key, string Attribute)> Map(string? json) =>
		Parse(json) is JsonObject map
			? [.. map.Where(pair => pair.Value is JsonValue v && v.GetValueKind() == JsonValueKind.String)
				.Select(pair => (pair.Key, pair.Value!.GetValue<string>()))]
			: [];

	/// <summary>A stored value as JSON: an array, object or boolean as it is, a number as a number, anything else as a string.</summary>
	private static JsonObject Datum(string stored, bool visible)
	{
		JsonNode? value = Parse(stored) is { } node && node.GetValueKind() is JsonValueKind.Array or JsonValueKind.Object
			or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number
			? node
			: JsonValue.Create(stored);
		return new JsonObject { ["value"] = value, ["visible"] = visible };
	}

	/// <summary>A posted field as text: a string as written, a list or number as its JSON, nothing as empty.</summary>
	private static string Posted(JsonObject body, string key) => body[key] switch
	{
		null => string.Empty,
		JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
		var node => node.ToJsonString(),
	};

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

	private static JsonObject Failed(string message) =>
		new() { ["ok"] = false, ["errors"] = new JsonObject { ["_global"] = message } };

	/// <summary>A view that says the application is not built here, so a stale link draws a message rather than an error.</summary>
	private ContentResult NotBuilt(string slug) => Content(new JsonObject
	{
		["kind"] = "view",
		["schema_version"] = 1,
		["pages"] = new JsonArray(new JsonObject
		{
			["key"] = "main",
			["order"] = 1,
			["sections"] = new JsonArray(new JsonObject
			{
				["order"] = 1,
				["elements"] = new JsonArray(new JsonObject { ["kind"] = "markdown", ["value"] = $"No application called {slug} is built here." }),
			}),
		}),
	}.ToJsonString(), "application/json");
}
