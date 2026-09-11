using SharpMUSH.Library.DiscriminatedUnions;
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
/// Every method returns <see cref="ApiResult{T}"/> with <see cref="ApiFailure"/> on the failed arm,
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

	public async Task<ApiResult<MushObject>> GetObjectAsync(int dbref)
	{
		var summary = await SendAsync<ObjectSummaryDto>(HttpMethod.Get, $"api/objects/{dbref}");
		if (summary is ApiFailure failure) return failure;

		var dto = summary.AsT0;

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

	public async Task<ApiResult<List<MushAttribute>>> GetAttributesAsync(int dbref)
	{
		var result = await SendAsync<List<AttributeDto>>(
			HttpMethod.Get, $"api/objects/{dbref}/attributes?depth={ListDepth}");

		return result.Match<ApiResult<List<MushAttribute>>>(
			attributes => attributes.Select(a => new MushAttribute
			{
				Name = a.Name,
				Value = a.Value,
				AttributeFlags = [.. a.Flags],
			}).ToList(),
			failure => failure);
	}

	public async Task<ApiResult<string>> GetAttributeAsync(int dbref, string attribute)
	{
		var result = await SendAsync<AttributeDto>(HttpMethod.Get, AttrPath(dbref, attribute));

		return result.Match<ApiResult<string>>(dto => dto.Value, failure => failure);
	}

	/// <summary>Stores <paramref name="value"/> verbatim — newlines included.</summary>
	public async Task<ApiResult<Success>> SetAttributeAsync(int dbref, string attribute, string value)
		=> await SendAsync(HttpMethod.Put, AttrPath(dbref, attribute), new SetAttributeRequest(value));

	public async Task<ApiResult<Success>> DeleteAttributeAsync(int dbref, string attribute)
		=> await SendAsync(HttpMethod.Delete, AttrPath(dbref, attribute));

	/// <summary>Creates an object, returning its dbref number.</summary>
	public async Task<ApiResult<int>> CreateObjectAsync(string name, MushObjectType type)
	{
		var typeName = type switch
		{
			MushObjectType.Room => "ROOM",
			MushObjectType.Exit => "EXIT",
			_ => "THING",
		};

		var result = await SendAsync<CreatedObjectDto>(
			HttpMethod.Post, "api/objects", new CreateObjectRequest(name, typeName));

		if (result is ApiFailure failure) return failure;

		var created = result.AsT0;

		// '#N' or '#N:creationTime' — the browser addresses objects by number.
		var number = created.Dbref.TrimStart('#').Split(':')[0];

		return int.TryParse(number, out var parsed)
			? parsed
			: new ApiFailure(ApiFailureKind.Unexpected, $"Malformed dbref in response: '{created.Dbref}'.");
	}

	/// <summary>Sends a request whose success carries no body.</summary>
	private async Task<ApiResult<Success>> SendAsync(HttpMethod method, string url, object? body = null)
	{
		HttpResponseMessage response;
		try
		{
			response = await SendCoreAsync(method, url, body);
		}
		catch (Exception ex) when (IsTransportFailure(ex))
		{
			return ApiFailure.Transport(ex);
		}

		using (response)
		{
			return response.IsSuccessStatusCode
				? new Success()
				: ApiFailure.FromStatus(response.StatusCode, await ServerMessageAsync(response));
		}
	}

	/// <summary>Sends a request and deserializes its body.</summary>
	private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string url, object? body = null)
	{
		HttpResponseMessage response;
		try
		{
			response = await SendCoreAsync(method, url, body);
		}
		catch (Exception ex) when (IsTransportFailure(ex))
		{
			return ApiFailure.Transport(ex);
		}

		using (response)
		{
			if (!response.IsSuccessStatusCode)
			{
				return ApiFailure.FromStatus(response.StatusCode, await ServerMessageAsync(response));
			}

			// Reading the body is a separate failure mode from reaching the server: a malformed
			// response reported as "could not reach the server" sends the reader to the wrong place.
			try
			{
				var value = await response.Content.ReadFromJsonAsync<T>();

				return value is null
					? new ApiFailure(ApiFailureKind.Unexpected, "The server returned an empty body.", response.StatusCode)
					: ApiResult<T>.FromT0(value);
			}
			catch (Exception ex) when (IsBodyFailure(ex))
			{
				return ApiFailure.Malformed(ex, response.StatusCode);
			}
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
		catch (Exception ex) when (IsBodyFailure(ex) || IsTransportFailure(ex))
		{
			// A refusal without a readable body is still a refusal; the status carries the meaning.
			return null;
		}
	}

	/// <summary>
	/// The request never produced a response: unreachable server, dropped connection, timeout.
	/// </summary>
	/// <remarks>
	/// <see cref="TaskCanceledException"/> needs no separate arm — it derives from
	/// <see cref="OperationCanceledException"/>, which is how HttpClient surfaces a timeout.
	/// </remarks>
	private static bool IsTransportFailure(Exception ex) =>
		ex is HttpRequestException or OperationCanceledException;

	/// <summary>
	/// A response arrived but its body could not be turned into the expected shape.
	/// </summary>
	/// <remarks>
	/// <see cref="InvalidOperationException"/> belongs here but NOT around request dispatch:
	/// <c>ReadFromJsonAsync</c> resolves the response charset and throws it for one it cannot
	/// parse, which is an unreadable body — whereas the same exception escaping
	/// <see cref="SendCoreAsync"/> means this service misused HttpClient and should keep throwing.
	/// That is why the two try blocks are separate rather than one predicate over the whole call.
	/// </remarks>
	private static bool IsBodyFailure(Exception ex) =>
		ex is JsonException or NotSupportedException or InvalidOperationException;

	private static MushObjectType ParseType(string type) => type.ToUpperInvariant() switch
	{
		"THING" => MushObjectType.Thing,
		"ROOM" => MushObjectType.Room,
		"EXIT" => MushObjectType.Exit,
		"PLAYER" => MushObjectType.Player,
		_ => MushObjectType.Unknown,
	};
}
