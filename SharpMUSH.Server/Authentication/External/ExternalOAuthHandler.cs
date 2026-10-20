using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.DiscriminatedUnions;
namespace SharpMUSH.Server.Authentication.External;

public sealed class ExternalOAuthOptions : OAuthOptions
{
	public string ProviderId { get; set; } = "";
	public string PublicOrigin { get; set; } = "";
}
/// <summary>Fixed-provider OAuth adapter; Grapevine's public code exchange never sends a secret or PKCE.</summary>
/// <remarks>GitHub's API refuses a request without a User-Agent, so every request names one.</remarks>
public sealed class ExternalOAuthHandler(IOptionsMonitor<ExternalOAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder,
 ExternalProviderCatalog catalog) : OAuthHandler<ExternalOAuthOptions>(options, new ExternalProtocolLoggerFactory(logger), encoder)
{
	protected override async Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
	{
		if (await catalog.EffectiveAsync(Options.ProviderId, Context.RequestAborted) is not EffectiveExternalProvider)
			return HandleRequestResult.Fail("Sign-in provider is unavailable.");
		return await base.HandleRemoteAuthenticateAsync();
	}
	protected override async Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context)
	{
		// Deliberately do not call the base exchange: it always sends client_secret.
		var values = new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["client_id"] = Options.ClientId, ["redirect_uri"] = Options.PublicOrigin + Options.CallbackPath, ["code"] = context.Code };
		if (ExternalProviderDefinition.Get(Options.ProviderId).RequiresClientSecret) values["client_secret"] = Options.ClientSecret;
		using var request = new HttpRequestMessage(HttpMethod.Post, Options.TokenEndpoint) { Content = new FormUrlEncodedContent(values) };
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		request.Headers.UserAgent.Add(UserAgent);
		try
		{
			using var response = await Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Context.RequestAborted);
			if (!response.IsSuccessStatusCode) return Failed();
			var document = await ReadJsonAsync(response.Content, Context.RequestAborted);
			if (!document.RootElement.TryGetProperty("access_token", out var token) || token.ValueKind != JsonValueKind.String
			 || string.IsNullOrWhiteSpace(token.GetString()) || token.GetString()!.Length > 16384 || token.GetString()!.Any(char.IsControl)) { document.Dispose(); return Failed(); }
			return OAuthTokenResponse.Success(document);
		}
		catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or TaskCanceledException)
		{ return Failed(); }
	}
	private static readonly ProductInfoHeaderValue UserAgent = new("SharpMUSH", "1.0");
	private static OAuthTokenResponse Failed() => OAuthTokenResponse.Failed(new AuthenticationFailureException("External token exchange failed."));
	internal static async Task<JsonDocument> ReadJsonAsync(HttpContent content, CancellationToken ct)
	{
		using var stream = await content.ReadAsStreamAsync(ct);
		using var buffer = new MemoryStream();
		var bytes = new byte[4096];
		int count;
		while ((count = await stream.ReadAsync(bytes, ct)) > 0)
		{ if (buffer.Length + count > 65536) throw new InvalidDataException("External response is too large."); await buffer.WriteAsync(bytes.AsMemory(0, count), ct); }
		return JsonDocument.Parse(buffer.ToArray());
	}
	protected override async Task<AuthenticationTicket> CreateTicketAsync(ClaimsIdentity identity, AuthenticationProperties properties, OAuthTokenResponse tokens)
	{
		try
		{
			if (await catalog.EffectiveAsync(Options.ProviderId, Context.RequestAborted) is not EffectiveExternalProvider provider) throw new AuthenticationFailureException("Sign-in provider is unavailable.");
			using var request = new HttpRequestMessage(HttpMethod.Get, Options.UserInformationEndpoint);
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			request.Headers.UserAgent.Add(UserAgent);
			using var response = await Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Context.RequestAborted);
			if (!response.IsSuccessStatusCode) throw new AuthenticationFailureException("External identity lookup failed.");
			using var profile = await ReadJsonAsync(response.Content, Context.RequestAborted);
			if (ExternalIdentityAdapter.OAuth(provider, profile.RootElement) is not ExternalIdentityProof proof) throw new AuthenticationFailureException("External identity lookup failed.");
			identity.AddClaim(new Claim("sub", proof.Key.Subject)); identity.AddClaim(new Claim("iss", proof.Key.Issuer));
			if (proof.Label is not null) identity.AddClaim(new Claim("name", proof.Label));
			return new AuthenticationTicket(new ClaimsPrincipal(identity), properties, Scheme.Name);
		}
		catch (Exception exception) when (exception is AuthenticationFailureException or HttpRequestException or JsonException or InvalidDataException or TaskCanceledException)
		{
			// The base OAuth exception path loses ticket properties; clear the validated transaction here.
			await Context.RequestServices.GetRequiredService<IExternalAuthenticationCompletion>().FailAsync(Context, properties, Context.RequestAborted);
			throw new AuthenticationFailureException("External identity lookup failed.");
		}
	}
	protected override string BuildChallengeUrl(AuthenticationProperties properties, string redirectUri)
	 => base.BuildChallengeUrl(properties, Options.PublicOrigin + Options.CallbackPath);
}
