using OneOf;
using OneOf.Types;
using SharpMUSH.Client.Models;
using SharpMUSH.Library.API;
using System.Net.Http.Json;
using System.Text.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Typed client for <c>api/objects</c> — object info, attribute CRUD and object creation.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the softcode round-trip <see cref="MushQueryService"/> used to run for the same
/// operations. That route went down the terminal WebSocket, which is line-delimited, so an
/// attribute value had to have its newlines rewritten as <c>%r</c> to survive as one message —
/// and since <c>&amp;</c> does not evaluate direct input, the literal <c>%r</c> was what got
/// stored. Over HTTP a value is just a JSON string, so nothing has to be encoded and nothing has
/// to be decoded on the way back.
/// </para>
/// <para>
/// Every method returns <see cref="OneOf{T0,T1}"/> with <see cref="ApiFailure"/> on the failed arm,
/// matching <see cref="CharacterDirectoryService"/>. Nothing here throws for an unreachable server
/// or a refused request: these are called straight from Blazor event handlers, where an escaping
/// exception bypasses the page's error banner entirely.
/// </para>
/// <para>
/// <see cref="MushQueryService"/> keeps the operations that genuinely are softcode evaluation:
/// free-form <c>lsearch</c> expressions and <c>u()</c>.
/// </para>
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

	public async Task<OneOf<MushObject, ApiFailure>> GetObjectAsync(int dbref)
	{
		var summary = await SendAsync<ObjectSummaryDto>(HttpMethod.Get, $"api/objects/{dbref}");
		if (summary.TryPickT1(out var failure, out var dto)) return failure;

		var attributes = await GetAttributesAsync(dbref);

		return new MushObject
		{
			Dbref = dbref,
			Name = dto.Name,
			Type = ParseType(dto.Type),
			Owner = dto.Owner,
			Flags = string.Join(' ', dto.Flags),
			// A readable object with unreadable attributes is still worth showing; the attribute
			// pane renders empty rather than the whole selection failing.
			Attributes = attributes.Match(list => list, _ => []),
		};
	}

	public async Task<OneOf<List<MushAttribute>, ApiFailure>> GetAttributesAsync(int dbref)
	{
		var result = await SendAsync<List<AttributeDto>>(
			HttpMethod.Get, $"api/objects/{dbref}/attributes?depth={ListDepth}");

		return result.Match<OneOf<List<MushAttribute>, ApiFailure>>(
			attributes => attributes.Select(a => new MushAttribute
			{
				Name = a.Name,
				Value = a.Value,
				AttributeFlags = [.. a.Flags],
			}).ToList(),
			failure => failure);
	}

	public async Task<OneOf<string, ApiFailure>> GetAttributeAsync(int dbref, string attribute)
	{
		var result = await SendAsync<AttributeDto>(HttpMethod.Get, AttrPath(dbref, attribute));

		return result.Match<OneOf<string, ApiFailure>>(dto => dto.Value, failure => failure);
	}

	/// <summary>Stores <paramref name="value"/> verbatim — newlines included.</summary>
	public async Task<OneOf<Success, ApiFailure>> SetAttributeAsync(int dbref, string attribute, string value)
		=> await SendAsync(HttpMethod.Put, AttrPath(dbref, attribute), new SetAttributeRequest(value));

	public async Task<OneOf<Success, ApiFailure>> DeleteAttributeAsync(int dbref, string attribute)
		=> await SendAsync(HttpMethod.Delete, AttrPath(dbref, attribute));

	/// <summary>Creates an object, returning its dbref number.</summary>
	public async Task<OneOf<int, ApiFailure>> CreateObjectAsync(string name, MushObjectType type)
	{
		var typeName = type switch
		{
			MushObjectType.Room => "ROOM",
			MushObjectType.Exit => "EXIT",
			_ => "THING",
		};

		var result = await SendAsync<CreatedObjectDto>(
			HttpMethod.Post, "api/objects", new CreateObjectRequest(name, typeName));

		if (result.TryPickT1(out var failure, out var created)) return failure;

		// '#N' or '#N:creationTime' — the browser addresses objects by number.
		var number = created.Dbref.TrimStart('#').Split(':')[0];

		return int.TryParse(number, out var parsed)
			? parsed
			: new ApiFailure(ApiFailureKind.Unexpected, $"Malformed dbref in response: '{created.Dbref}'.");
	}

	/// <summary>Sends a request whose success carries no body.</summary>
	private async Task<OneOf<Success, ApiFailure>> SendAsync(HttpMethod method, string url, object? body = null)
	{
		try
		{
			using var response = await SendCoreAsync(method, url, body);

			return response.IsSuccessStatusCode
				? new Success()
				: ApiFailure.FromStatus(response.StatusCode, await ServerMessageAsync(response));
		}
		catch (Exception ex) when (IsRequestFailure(ex))
		{
			return ApiFailure.Transport(ex);
		}
	}

	/// <summary>Sends a request and deserializes its body.</summary>
	private async Task<OneOf<T, ApiFailure>> SendAsync<T>(HttpMethod method, string url, object? body = null)
	{
		try
		{
			using var response = await SendCoreAsync(method, url, body);

			if (!response.IsSuccessStatusCode)
			{
				return ApiFailure.FromStatus(response.StatusCode, await ServerMessageAsync(response));
			}

			var value = await response.Content.ReadFromJsonAsync<T>();

			return value is null
				? new ApiFailure(ApiFailureKind.Unexpected, "The server returned an empty body.", response.StatusCode)
				: OneOf<T, ApiFailure>.FromT0(value);
		}
		catch (Exception ex) when (IsRequestFailure(ex))
		{
			return ApiFailure.Transport(ex);
		}
	}

	private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body)
	{
		using var request = new HttpRequestMessage(method, url);
		if (body is not null)
		{
			request.Content = JsonContent.Create(body, body.GetType());
		}

		return await Client.SendAsync(request);
	}

	/// <summary>The engine's own refusal text, when the response carried one.</summary>
	private static async Task<string?> ServerMessageAsync(HttpResponseMessage response)
	{
		try
		{
			return (await response.Content.ReadFromJsonAsync<ApiErrorDto>())?.Error;
		}
		catch (Exception ex) when (IsRequestFailure(ex))
		{
			// A refusal without a readable body is still a refusal; the status carries the meaning.
			return null;
		}
	}

	/// <summary>
	/// Failures worth turning into a value rather than letting escape. Anything else is a bug here
	/// and should keep throwing.
	/// </summary>
	private static bool IsRequestFailure(Exception ex) => ex switch
	{
		HttpRequestException or JsonException or NotSupportedException or TaskCanceledException
			or OperationCanceledException or InvalidOperationException => true,
		_ => false
	};

	private static MushObjectType ParseType(string type) => type.ToUpperInvariant() switch
	{
		"THING" => MushObjectType.Thing,
		"ROOM" => MushObjectType.Room,
		"EXIT" => MushObjectType.Exit,
		"PLAYER" => MushObjectType.Player,
		_ => MushObjectType.Unknown,
	};
}
