using System.Net.Http.Json;
using System.Text.Json;
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
	public static Task<ApiResult<T>> GetApiAsync<T>(this HttpClient http, string url, string whenEmpty) =>
		ReadingAsync<T>(() => http.GetAsync(url), whenEmpty);

	/// <summary>GETs a <typeparamref name="T"/> read with <paramref name="options"/> rather than the web defaults.</summary>
	public static Task<ApiResult<T>> GetApiAsync<T>(
		this HttpClient http, string url, string whenEmpty, JsonSerializerOptions options) =>
		ReadingAsync<T>(() => http.GetAsync(url), whenEmpty, options: options);

	/// <summary>
	/// GETs a body that is the payload itself — a file to hand the user — rather than a value to
	/// deserialise. An empty body is an empty file, not a failure.
	/// </summary>
	public static async Task<ApiResult<string>> GetTextApiAsync(this HttpClient http, string url)
	{
		try
		{
			using var response = await http.GetAsync(url);
			var body = await response.Content.ReadAsStringAsync();
			return response.IsSuccessStatusCode ? body : ApiFailure.FromStatus(response.StatusCode, body);
		}
		catch (Exception ex)
		{
			return ApiFailure.Transport(ex);
		}
	}

	/// <summary>POSTs <paramref name="body"/> as JSON and reads a <typeparamref name="TResult"/> back.</summary>
	public static Task<ApiResult<TResult>> PostApiAsync<TBody, TResult>(
		this HttpClient http, string url, TBody body, string whenEmpty) =>
		ReadingAsync<TResult>(() => http.PostAsJsonAsync(url, body), whenEmpty);

	/// <summary>
	/// POSTs content that is not JSON — a multipart upload — and reads a <typeparamref name="TResult"/> back.
	/// </summary>
	public static Task<ApiResult<TResult>> PostContentApiAsync<TResult>(
		this HttpClient http, string url, HttpContent content, string whenEmpty) =>
		ReadingAsync<TResult>(() => http.PostAsync(url, content), whenEmpty);

	/// <summary>PUTs <paramref name="body"/> as JSON and reads a <typeparamref name="TResult"/> back.</summary>
	public static Task<ApiResult<TResult>> PutApiAsync<TBody, TResult>(
		this HttpClient http, string url, TBody body, string whenEmpty) =>
		ReadingAsync<TResult>(() => http.PutAsJsonAsync(url, body), whenEmpty);

	/// <summary>PUTs <paramref name="body"/> as JSON where the answer is only whether it worked.</summary>
	public static async Task<ApiResult<Success>> PutApiAsync<TBody>(this HttpClient http, string url, TBody body) =>
		await SucceededAsync(() => http.PutAsJsonAsync(url, body));

	/// <summary>PUTs <paramref name="body"/> written with <paramref name="options"/>, where the answer is only whether it worked.</summary>
	public static async Task<ApiResult<Success>> PutApiAsync<TBody>(
		this HttpClient http, string url, TBody body, JsonSerializerOptions options) =>
		await SucceededAsync(() => http.PutAsJsonAsync(url, body, options));

	/// <summary>
	/// Sends a request the caller built and reads a <typeparamref name="T"/> back, giving up when
	/// <paramref name="cancellationToken"/> fires.
	/// </summary>
	/// <remarks>
	/// For the one read that cannot take the default path: the account session's own refresh, which
	/// runs inside the hydration the bearer handler waits on and so names its bearer itself, and which
	/// must not hold the first render for the named client's long timeout. Everything else uses the
	/// verb helpers above.
	/// </remarks>
	public static Task<ApiResult<T>> SendApiAsync<T>(
		this HttpClient http, HttpRequestMessage request, string whenEmpty, CancellationToken cancellationToken) =>
		ReadingAsync<T>(() => http.SendAsync(request, cancellationToken), whenEmpty, cancellationToken);

	/// <summary>DELETEs where the server answers with what is left.</summary>
	public static Task<ApiResult<TResult>> DeleteApiAsync<TResult>(this HttpClient http, string url, string whenEmpty) =>
		ReadingAsync<TResult>(() => http.DeleteAsync(url), whenEmpty);

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

	private static async Task<ApiResult<T>> ReadingAsync<T>(
		Func<Task<HttpResponseMessage>> send, string whenEmpty, CancellationToken cancellationToken = default,
		JsonSerializerOptions? options = null)
	{
		try
		{
			return await ReadAsync<T>(await send(), whenEmpty, options, cancellationToken);
		}
		catch (Exception ex)
		{
			return ApiFailure.Transport(ex);
		}
	}

	private static async Task<ApiResult<T>> ReadAsync<T>(
		HttpResponseMessage response, string whenEmpty, JsonSerializerOptions? options, CancellationToken cancellationToken)
	{
		using (response)
		{
			if (!response.IsSuccessStatusCode)
				return ApiFailure.FromStatus(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));

			try
			{
				return await response.Content.ReadFromJsonAsync<T>(options, cancellationToken)
					?? (ApiResult<T>)new ApiFailure(ApiFailureKind.Unexpected, whenEmpty, response.StatusCode);
			}
			catch (Exception ex) when (ex is JsonException or NotSupportedException)
			{
				return ApiFailure.Malformed(ex, response.StatusCode);
			}
		}
	}
}
