using SharpMUSH.Client.Models;
using SharpMUSH.Library.API;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Typed client for <c>api/objects</c> — object info, attribute CRUD and object creation.
/// </summary>
/// <remarks>
/// This replaces the softcode round-trip <see cref="MushQueryService"/> used to run for the same
/// operations. That route went down the terminal WebSocket, which is line-delimited, so an
/// attribute value had to have its newlines rewritten as <c>%r</c> to survive as one message —
/// and since <c>&amp;</c> does not evaluate direct input, the literal <c>%r</c> was what got
/// stored. Over HTTP a value is just a JSON string, so nothing has to be encoded and nothing has
/// to be decoded on the way back.
///
/// <see cref="MushQueryService"/> keeps the operations that genuinely are softcode evaluation:
/// free-form <c>lsearch</c> expressions and <c>u()</c>.
/// </remarks>
public class ObjectApiService(IHttpClientFactory httpClientFactory)
{
	/// <summary>
	/// Attribute-tree levels requested when listing. The engine's own default is 1 (what
	/// <c>examine</c> shows); the editor wants leaves too, which the old <c>lattr(#N/**)</c>
	/// listing also returned.
	/// </summary>
	private const int ListDepth = 10;

	private HttpClient Client => httpClientFactory.CreateClient("api");

	private static string AttrPath(int dbref, string attribute)
		=> $"api/objects/{dbref}/attributes/{Uri.EscapeDataString(attribute)}";

	public async Task<MushObject?> GetObjectAsync(int dbref)
	{
		var response = await Client.GetAsync($"api/objects/{dbref}");
		if (!response.IsSuccessStatusCode) return null;

		var summary = await response.Content.ReadFromJsonAsync<ObjectSummaryDto>();
		if (summary is null) return null;

		return new MushObject
		{
			Dbref = dbref,
			Name = summary.Name,
			Type = ParseType(summary.Type),
			Owner = summary.Owner,
			Flags = string.Join(' ', summary.Flags),
			Attributes = await GetAttributesAsync(dbref),
		};
	}

	public async Task<List<MushAttribute>> GetAttributesAsync(int dbref)
	{
		var attributes = await Client.GetFromJsonAsync<List<AttributeDto>>(
			$"api/objects/{dbref}/attributes?depth={ListDepth}");

		return attributes?.Select(a => new MushAttribute
		{
			Name = a.Name,
			Value = a.Value,
			AttributeFlags = [.. a.Flags],
		}).ToList() ?? [];
	}

	public async Task<string?> GetAttributeAsync(int dbref, string attribute)
	{
		var response = await Client.GetAsync(AttrPath(dbref, attribute));
		if (!response.IsSuccessStatusCode) return null;

		return (await response.Content.ReadFromJsonAsync<AttributeDto>())?.Value;
	}

	/// <summary>
	/// Stores <paramref name="value"/> verbatim — newlines included. Returns the server's refusal
	/// message when the acting character may not write there, or <see langword="null"/> on success.
	/// </summary>
	public async Task<string?> SetAttributeAsync(int dbref, string attribute, string value)
	{
		var response = await Client.PutAsJsonAsync(
			AttrPath(dbref, attribute), new SetAttributeRequest(value));

		return await ErrorOrNullAsync(response);
	}

	public async Task<string?> DeleteAttributeAsync(int dbref, string attribute)
		=> await ErrorOrNullAsync(await Client.DeleteAsync(AttrPath(dbref, attribute)));

	/// <summary>Creates an object, returning its dbref number, or null with the refusal message.</summary>
	public async Task<(int? Dbref, string? Error)> CreateObjectAsync(string name, MushObjectType type)
	{
		var typeName = type switch
		{
			MushObjectType.Room => "ROOM",
			MushObjectType.Exit => "EXIT",
			_ => "THING",
		};

		var response = await Client.PostAsJsonAsync("api/objects", new CreateObjectRequest(name, typeName));
		if (!response.IsSuccessStatusCode)
		{
			return (null, await ErrorOrNullAsync(response));
		}

		var created = await response.Content.ReadFromJsonAsync<CreatedObjectDto>();
		// '#N' or '#N:creationTime' — the browser addresses objects by number.
		var number = created?.Dbref.TrimStart('#').Split(':')[0];

		return int.TryParse(number, out var parsed) ? (parsed, null) : (null, "Malformed dbref in response.");
	}

	/// <summary>The server's message for a failed call, or <see langword="null"/> when it succeeded.</summary>
	private static async Task<string?> ErrorOrNullAsync(HttpResponseMessage response)
	{
		if (response.IsSuccessStatusCode) return null;

		var error = response.StatusCode == HttpStatusCode.NotFound
			? null
			: (await response.Content.ReadFromJsonAsync<ApiErrorDto>().ConfigureAwait(false))?.Error;

		return error ?? $"Request failed ({(int)response.StatusCode}).";
	}

	private static MushObjectType ParseType(string type) => type.ToUpperInvariant() switch
	{
		"THING" => MushObjectType.Thing,
		"ROOM" => MushObjectType.Room,
		"EXIT" => MushObjectType.Exit,
		"PLAYER" => MushObjectType.Player,
		_ => MushObjectType.Unknown,
	};
}
