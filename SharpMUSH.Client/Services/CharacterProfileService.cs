using System.Text.Json;
using System.Text.RegularExpressions;
using SharpMUSH.Client.Models;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The portal-known profile fields the character banner reads (spec §3): <c>image</c>, <c>banner</c>
/// and <c>color</c> from profile-handler 1.5, and <c>role</c> when a game adds it. Everything else on
/// the profile is the character-header application's schema, rendered by <c>SchemaWidget</c>.
/// </summary>
public partial class CharacterProfileService(IHttpClientFactory httpClientFactory, CharacterDirectoryService directory)
{
	[GeneratedRegex("^#[0-9a-fA-F]{6}$")]
	private static partial Regex HexColour();

	/// <summary>
	/// The profile of the character named <paramref name="name"/> (case-insensitive).
	/// <see cref="ApiFailureKind.NotFound"/> when no character answers to it; another kind when the
	/// directory or the profile could not be read.
	/// </summary>
	public async Task<ApiResult<CharacterProfileData>> GetAsync(string name)
	{
		return await directory.ResolveObjidAsync(name) switch
		{
			string objid => await FetchAsync(objid),
			NotFound => new ApiFailure(ApiFailureKind.NotFound, $"No character is named {name}."),
			// Not the directory's own failure: a 404 on its route is not "no such character".
			ApiFailure => new ApiFailure(ApiFailureKind.Unexpected, "The character directory could not be read."),
		};
	}

	private async Task<ApiResult<CharacterProfileData>> FetchAsync(string objid)
	{
		var result = await httpClientFactory.CreateClient("api")
			.GetApiAsync<JsonElement>($"http/profile?objid={Uri.EscapeDataString(objid)}", "The server returned no profile.");
		return result switch
		{
			JsonElement body => Parse(body, objid),
			// The directory just found this character, so a 404 here means the profile hook is gone
			// (an admin removed GET`PROFILE), not that the character is: only the directory says missing.
			ApiFailure { Kind: ApiFailureKind.NotFound } failure => failure with { Kind = ApiFailureKind.Unexpected },
			ApiFailure failure => failure,
		};
	}

	private static ApiResult<CharacterProfileData> Parse(JsonElement body, string objid)
	{
		if (body.ValueKind != JsonValueKind.Object)
		{
			return new ApiFailure(ApiFailureKind.Unexpected, "The profile was not a JSON object.");
		}

		var fields = body.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object ? f : default;
		var colour = Field(fields, "color");
		return new CharacterProfileData(
			Name: Text(body, "character") ?? string.Empty,
			Objid: Text(body, "objid") ?? objid,
			Dbref: Text(body, "dbref") ?? objid.Split(':')[0],
			Image: Field(fields, "image"),
			Banner: Field(fields, "banner"),
			Color: colour is not null && HexColour().IsMatch(colour) ? colour : null,
			Role: Field(fields, "role"));
	}

	private static string? Text(JsonElement obj, string name) =>
		obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

	/// <summary>
	/// A field as a plain string or profile-handler's <c>{ value, visible }</c>; blank is null, and so is
	/// a field the handler marked <c>visible: false</c> — the same rule <c>SchemaViewRenderer</c> applies.
	/// </summary>
	private static string? Field(JsonElement fields, string name)
	{
		if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(name, out var v))
		{
			return null;
		}

		var raw = v.ValueKind switch
		{
			JsonValueKind.String => v.GetString(),
			JsonValueKind.Object when v.TryGetProperty("visible", out var shown) && shown.ValueKind == JsonValueKind.False => null,
			JsonValueKind.Object when v.TryGetProperty("value", out var inner) && inner.ValueKind == JsonValueKind.String => inner.GetString(),
			_ => null,
		};
		return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
	}
}
