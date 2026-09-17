using System.Net.Http.Json;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The one fetch flow every typed client over the game's REST API uses: send, turn a non-success
/// status into an <see cref="ApiFailure"/> carrying the server's own text, read the body, and treat
/// a body that deserialises to <see langword="null"/> as a failure rather than as an empty value.
/// </summary>
/// <remarks>
/// <para>Each of the two-dozen services under <c>Services/</c> used to spell this out for itself, and
/// they disagreed about the answer: some returned <see langword="bool"/> and discarded the reason,
/// some a nullable with the same effect, some a <c>(value, error)</c> tuple whose <c>(null, null)</c>
/// state nothing meant but was reachable. <see cref="ApiResult{T}"/> has no such arm — every path
/// names either a value or a reason.</para>
///
/// <para>Catching <see cref="Exception"/> is deliberate and narrowing it would be a regression. This
/// runs in the browser: an exception type nobody enumerated — a malformed base address, a handler an
/// analyzer has not heard of — would otherwise escape into the render loop and take the page down,
/// which is worse than showing the message. The catch is the boundary, not a swallow; the detail
/// reaches the operator either way.</para>
///
/// <para>Nothing here touches <c>Authorization</c>. The <c>"api"</c> client's
/// <see cref="AccountSessionBearerHandler"/> attaches the account-session bearer, and it hydrates the
/// session first — a caller that sets the header itself suppresses that hydration and sends an
/// unauthenticated request during a page refresh.</para>
/// </remarks>
public static class ApiCall
{
	/// <param name="whenEmpty">What to say when the call succeeded and the body was <c>null</c>.</param>
	public static async Task<ApiResult<T>> GetApiAsync<T>(this HttpClient http, string url, string whenEmpty)
	{
		try
		{
			return await ReadAsync<T>(await http.GetAsync(url), whenEmpty);
		}
		catch (Exception ex)
		{
			return ApiFailure.Transport(ex);
		}
	}

	/// <summary>POSTs <paramref name="body"/> as JSON and reads a <typeparamref name="TResult"/> back.</summary>
	public static async Task<ApiResult<TResult>> PostApiAsync<TBody, TResult>(
		this HttpClient http, string url, TBody body, string whenEmpty)
	{
		try
		{
			return await ReadAsync<TResult>(await http.PostAsJsonAsync(url, body), whenEmpty);
		}
		catch (Exception ex)
		{
			return ApiFailure.Transport(ex);
		}
	}

	/// <summary>POSTs <paramref name="body"/> as JSON where the answer is only whether it worked.</summary>
	public static async Task<ApiResult<Success>> PostApiAsync<TBody>(this HttpClient http, string url, TBody body) =>
		await SucceededAsync(() => http.PostAsJsonAsync(url, body));

	/// <summary>POSTs with no body — the shape the enable/disable style of endpoint takes.</summary>
	public static async Task<ApiResult<Success>> PostApiAsync(this HttpClient http, string url) =>
		await SucceededAsync(() => http.PostAsync(url, content: null));

	public static async Task<ApiResult<Success>> DeleteApiAsync(this HttpClient http, string url) =>
		await SucceededAsync(() => http.DeleteAsync(url));

	private static async Task<ApiResult<Success>> SucceededAsync(Func<Task<HttpResponseMessage>> send)
	{
		try
		{
			using var response = await send();
			return response.IsSuccessStatusCode
				? new Success()
				: ApiFailure.FromStatus(response.StatusCode, await response.Content.ReadAsStringAsync());
		}
		catch (Exception ex)
		{
			return ApiFailure.Transport(ex);
		}
	}

	private static async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response, string whenEmpty)
	{
		using (response)
		{
			if (!response.IsSuccessStatusCode)
				return ApiFailure.FromStatus(response.StatusCode, await response.Content.ReadAsStringAsync());

			try
			{
				return await response.Content.ReadFromJsonAsync<T>()
					?? (ApiResult<T>)new ApiFailure(ApiFailureKind.Unexpected, whenEmpty, response.StatusCode);
			}
			catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
			{
				return ApiFailure.Malformed(ex, response.StatusCode);
			}
		}
	}
}
