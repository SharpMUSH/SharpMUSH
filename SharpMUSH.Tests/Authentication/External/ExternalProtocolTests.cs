using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Validators;
using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication.External;
namespace SharpMUSH.Tests.Authentication.External;

public class ExternalProtocolTests
{
	private static ServiceProvider Create(string id, Backchannel backchannel, IExternalAuthenticationCompletion completion, LogCapture? logs = null, string audience = "common", string[]? allowedTenants = null)
	{
		var services = new ServiceCollection(); services.AddLogging(builder => { builder.SetMinimumLevel(LogLevel.Trace); if (logs is not null) builder.AddProvider(logs); }); services.AddDataProtection().UseEphemeralDataProtectionProvider();
		services.AddAuthentication("AccountSession");
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetExternalProviderSettingsQuery>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(new ExternalProviderSettings { Enabled = true, ClientId = "client-id", PublicOrigin = "https://game.example", RegistrationId = "registration-" + call.Arg<GetExternalProviderSettingsQuery>().Provider, Generation = "generation", MicrosoftAudience = audience, AllowedTenants = allowedTenants ?? [] }));
		var secrets = Substitute.For<ISecretStore>(); secrets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<FoundResult<string>>("confidential-secret"));
		var environment = Substitute.For<IHostEnvironment>(); environment.EnvironmentName.Returns(Environments.Production);
		services.AddSingleton(mediator); services.AddSingleton(secrets); services.AddSingleton(environment);
		services.AddExternalAccountAuthentication();
		services.AddSingleton(completion);
		services.Configure<ExternalOAuthOptions>(ExternalProviderDefinition.Get(id).Scheme, options => options.Backchannel = new HttpClient(backchannel));
		return services.BuildServiceProvider();
	}
	private static DefaultHttpContext Context(IServiceProvider services, string path) => new() { RequestServices = services, Request = { Scheme = "https", Host = new HostString("untrusted-host.example"), Path = path } };
	[Test]
	public async Task NamedSchemesPreserveAccountSessionAndUseStrictFrameworkOidcValidation()
	{
		using var services = Create("discord", new Backchannel(), Substitute.For<IExternalAuthenticationCompletion>());
		await Assert.That(services.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme).IsEqualTo("AccountSession");
		foreach (var provider in new[] { "google", "microsoft" })
		{
			var options = services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("External-" + provider);
			await Assert.That(options.ResponseType).IsEqualTo("code"); await Assert.That(options.UsePkce).IsTrue();
			await Assert.That(options.ProtocolValidator.RequireNonce).IsTrue(); await Assert.That(options.SaveTokens).IsFalse();
			await Assert.That(options.TokenValidationParameters.ValidateIssuer).IsTrue(); await Assert.That(options.TokenValidationParameters.ValidateAudience).IsTrue();
			await Assert.That(options.TokenValidationParameters.ValidateLifetime).IsTrue(); await Assert.That(options.TokenValidationParameters.RequireSignedTokens).IsTrue();
			await Assert.That(options.Scope).IsEquivalentTo(new[] { "openid", "profile" });
		}
	}
	[Test]
	[Arguments("discord", true)]
	[Arguments("grapevine", false)]
	public async Task OAuthCodeAndUserInfoAreValidatedWithoutPersistingTokens(string id, bool confidential)
	{
		var backchannel = new Backchannel(); var completion = Substitute.For<IExternalAuthenticationCompletion>();
		backchannel.Profile = id == "discord" ? "{\"id\":\"subject\",\"username\":\"display\"}" : "{\"uid\":\"subject\",\"username\":\"display\"}";
		using var services = Create(id, backchannel, completion);
		var callback = await CallbackAsync(services, id);
		await completion.Received(1).CompleteAsync(Arg.Any<HttpContext>(), Arg.Is<AuthenticationProperties>(p => p.Items["transaction"] == "original"), new ExternalIdentityKey("registration-" + id, ExternalProviderDefinition.Get(id).Issuer, "subject"), "display", Arg.Any<CancellationToken>());
		var values = QueryHelpers.ParseQuery("?" + backchannel.TokenBody);
		await Assert.That(values.ContainsKey("client_secret")).IsEqualTo(confidential);
		await Assert.That(values["redirect_uri"].ToString()).IsEqualTo("https://game.example/signin-external-" + id);
		await Assert.That(values.ContainsKey("code_verifier")).IsFalse();
		await Assert.That(callback.Response.Headers.SetCookie.ToString()).DoesNotContain("ExternalTransient");
		await Assert.That(backchannel.UserInfoAuthorization).IsEqualTo("Bearer remote-token");
	}
	[Test]
	public async Task TokenErrorsAndMissingIdentifiersFailWithOnlyGenericBrowserError()
	{
		foreach (var tokenError in new[] { true, false })
		{
			var logs = new LogCapture(); var backchannel = new Backchannel { TokenError = tokenError, Profile = "{\"username\":\"no-id\"}" }; var completion = Substitute.For<IExternalAuthenticationCompletion>();
			using var services = Create("grapevine", backchannel, completion, logs);
			var context = await CallbackAsync(services, "grapevine");
			await Assert.That(context.Response.Headers.Location.ToString()).IsEqualTo("/login?external=failed");
			await completion.Received(1).FailAsync(Arg.Any<HttpContext>(), Arg.Is<AuthenticationProperties?>(properties => properties != null && properties.Items["transaction"] == "original"), Arg.Any<CancellationToken>());
			await completion.DidNotReceive().CompleteAsync(Arg.Any<HttpContext>(), Arg.Any<AuthenticationProperties>(), Arg.Any<ExternalIdentityKey>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
			foreach (var secret in new[] { "raw-upstream-error", "synthetic-code", "remote-token", "confidential-secret" })
				await Assert.That(logs.Text).DoesNotContain(secret);
		}
	}
	[Test]
	[Arguments("discord", false)]
	[Arguments("grapevine", false)]
	[Arguments("discord", true)]
	public async Task OAuthDenialCleansOnlyTheValidatedTransaction(string provider, bool corruptState)
	{
		var completion = Substitute.For<IExternalAuthenticationCompletion>();
		using var services = Create(provider, new Backchannel(), completion);
		var callback = await CallbackAsync(services, provider, denied: true, corruptState: corruptState);
		await Assert.That(callback.Response.Headers.Location.ToString()).IsEqualTo("/login?external=failed");
		if (corruptState)
			await completion.DidNotReceive().FailAsync(Arg.Any<HttpContext>(), Arg.Is<AuthenticationProperties?>(properties => properties != null && properties.Items.ContainsKey("transaction")), Arg.Any<CancellationToken>());
		else
			await completion.Received(1).FailAsync(Arg.Any<HttpContext>(), Arg.Is<AuthenticationProperties?>(properties => properties != null && properties.Items["transaction"] == "original"), Arg.Any<CancellationToken>());
	}
	[Test]
	[Arguments("valid", true)]
	[Arguments("issuer-alias", true)]
	[Arguments("signature", false)]
	[Arguments("issuer", false)]
	[Arguments("audience", false)]
	[Arguments("expired", false)]
	[Arguments("nonce", false)]
	[Arguments("malformed", false)]
	[Arguments("unsigned", false)]
	[Arguments("disabled", false)]
	public async Task GoogleFrameworkValidatesSignedCodeFlowTokenAndNonce(string defect, bool accepted)
	=> await SignedOidcAsync("google", defect, accepted);
	[Test]
	[Arguments("work", true)]
	[Arguments("personal", true)]
	[Arguments("personal-consumers", true)]
	[Arguments("tenant-disallowed", false)]
	[Arguments("tenant-mismatch", false)]
	[Arguments("audience", false)]
	[Arguments("signing-key-issuer", false)]
	public async Task MicrosoftFrameworkValidatesSignedTenantAndConsumerCodeFlow(string defect, bool accepted)
	 => await SignedOidcAsync("microsoft", defect, accepted);
	private static async Task SignedOidcAsync(string provider, string defect, bool accepted)
	{
		using var rsa = RSA.Create(2048); using var wrong = RSA.Create(2048);
		const string work = "72f988bf-86f1-41af-91ab-2d7cd011db47";
		const string personal = "9188040d-6c67-4c5b-b112-36a304b66dad";
		var microsoft = provider == "microsoft";
		var tenant = defect.StartsWith("personal", StringComparison.Ordinal) ? personal : work;
		var audience = defect == "work" ? "organizations" : defect == "personal-consumers" ? "consumers" : "common";
		var key = new RsaSecurityKey(rsa) { KeyId = "fixture-key" };
		var metadataKey = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "fixture-key" });
		if (microsoft) metadataKey.AdditionalData["issuer"] = defect == "signing-key-issuer" ? "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000001/v2.0" : "https://login.microsoftonline.com/{tenantid}/v2.0";
		var scheme = "External-" + provider;
		var callbackPath = "/signin-external-" + provider;
		var issuer = microsoft ? "https://login.microsoftonline.com/" + tenant + "/v2.0" : "https://accounts.google.com";
		var completion = Substitute.For<IExternalAuthenticationCompletion>(); var backchannel = new Backchannel();
		var logs = new LogCapture(); using var services = Create("discord", backchannel, completion, logs, audience, defect == "tenant-disallowed" ? [personal] : []);
		var options = services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(scheme);
		options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration
		{
			Issuer = microsoft ? "https://login.microsoftonline.com/{tenantid}/v2.0" : "https://accounts.google.com", AuthorizationEndpoint = microsoft ? "https://login.microsoftonline.com/common/oauth2/v2.0/authorize" : "https://accounts.google.com/o/oauth2/v2/auth", TokenEndpoint = microsoft ? "https://login.microsoftonline.com/common/oauth2/v2.0/token" : "https://oauth2.googleapis.com/token", SigningKeys = { metadataKey }, JsonWebKeySet = new JsonWebKeySet { Keys = { metadataKey } }
		});
		options.Backchannel = new HttpClient(backchannel);
		if (microsoft)
		{
			await Assert.That(options.TokenValidationParameters.IssuerValidator?.Target is AadIssuerValidator).IsTrue();
			// Use the same supported issuer validator with deterministic metadata, including its fallback
			// discovery path for invalid issuers. This overload avoids the library's shared authority cache.
			options.TokenValidationParameters.IssuerValidator = AadIssuerValidator.GetAadIssuerValidator(
			 options.Authority ?? throw new InvalidOperationException("Missing Microsoft authority."), options.Backchannel,
			 _ => (BaseConfigurationManager)options.ConfigurationManager).Validate;
		}
		Exception? failure = null;
		var remoteFailed = options.Events.OnRemoteFailure;
		options.Events.OnRemoteFailure = context => { failure = context.Failure; return remoteFailed(context); };
		var failed = options.Events.OnAuthenticationFailed;
		options.Events.OnAuthenticationFailed = context => { failure = context.Exception; return failed(context); };
		string state, cookie, nonce;
		var requestReauthentication = defect is "valid" or "work" or "personal-consumers";
		using (var scope = services.CreateScope())
		{
			var challenge = Context(scope.ServiceProvider, "/start");
			await challenge.ChallengeAsync(scheme, new AuthenticationProperties(new Dictionary<string, string?> { ["transaction"] = "original", ["sm.external.reauth"] = requestReauthentication ? "true" : "false" }));
			var parameters = QueryHelpers.ParseQuery(new Uri(challenge.Response.Headers.Location.ToString()).Query);
			state = parameters["state"].ToString(); nonce = parameters["nonce"].ToString();
			cookie = string.Join("; ", challenge.Response.Headers.SetCookie.Select(value => value!.Split(';')[0]));
			await Assert.That(parameters["code_challenge_method"].ToString()).IsEqualTo("S256");
			await Assert.That(parameters.GetValueOrDefault("prompt").ToString()).IsEqualTo(requestReauthentication ? "select_account" : "");
			await Assert.That(parameters["redirect_uri"].ToString()).IsEqualTo("https://game.example" + callbackPath);
		}
		var now = DateTime.UtcNow;
		var jwt = new JwtSecurityToken(defect == "issuer" ? "https://attacker.example" : defect == "issuer-alias" ? "accounts.google.com" : issuer, defect == "audience" ? "another-client" : "client-id",
		 new[] { new Claim("sub", "subject"), new Claim("iat", Microsoft.IdentityModel.Tokens.EpochTime.GetIntDate(now).ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64), new Claim("nonce", defect == "nonce" ? "incorrect" : nonce), new Claim("name", "display") }.Concat(microsoft ? new[] { new Claim("tid", defect == "tenant-mismatch" ? personal : tenant), new Claim("ver", "2.0") } : []),
		 now.AddHours(-2), defect == "expired" ? now.AddHours(-1) : now.AddMinutes(5),
		 defect == "unsigned" ? null : new SigningCredentials(defect == "signature" ? new RsaSecurityKey(wrong) { KeyId = "fixture-key" } : key, SecurityAlgorithms.RsaSha256));
		backchannel.IdToken = defect == "malformed" ? "sensitive-malformed-id-token" : new JwtSecurityTokenHandler().WriteToken(jwt);
		if (defect == "disabled") services.GetRequiredService<IMediator>().Send(Arg.Any<GetExternalProviderSettingsQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new ExternalProviderSettings()));
		using var callbackScope = services.CreateScope(); var callback = Context(callbackScope.ServiceProvider, callbackPath);
		callback.Request.Method = "POST"; callback.Request.ContentType = "application/x-www-form-urlencoded";
		callback.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["code"] = "synthetic-code", ["state"] = state });
		callback.Request.Headers.Cookie = cookie;
		var handler = await callbackScope.ServiceProvider.GetRequiredService<IAuthenticationHandlerProvider>().GetHandlerAsync(callback, scheme);
		await ((IAuthenticationRequestHandler)handler!).HandleRequestAsync();
		foreach (var secret in new[] { "synthetic-code", "remote-token", "confidential-secret", backchannel.IdToken }) await Assert.That(logs.Text).DoesNotContain(secret);
		if (accepted)
		{
			await Assert.That(failure?.GetType().Name + ":" + failure?.Message.Split(':')[0]).IsEqualTo(":");
			await completion.Received(1).CompleteAsync(Arg.Any<HttpContext>(), Arg.Any<AuthenticationProperties>(), new ExternalIdentityKey("registration-" + provider, issuer, "subject"), "display", Arg.Any<CancellationToken>());
			var tokenParameters = QueryHelpers.ParseQuery("?" + backchannel.TokenBody);
			await Assert.That(tokenParameters.ContainsKey("code_verifier")).IsTrue();
			await Assert.That(tokenParameters["redirect_uri"].ToString()).IsEqualTo("https://game.example" + callbackPath);
		}
		else
		{
			await completion.Received(1).FailAsync(Arg.Any<HttpContext>(), Arg.Is<AuthenticationProperties?>(properties => properties != null && properties.Items["transaction"] == "original"), Arg.Any<CancellationToken>());
			await completion.DidNotReceive().CompleteAsync(Arg.Any<HttpContext>(), Arg.Any<AuthenticationProperties>(), Arg.Any<ExternalIdentityKey>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
			await Assert.That(callback.Response.Headers.Location.ToString()).IsEqualTo("/login?external=failed");
		}
	}

	private static async Task<DefaultHttpContext> CallbackAsync(ServiceProvider services, string id, bool denied = false, bool corruptState = false)
	{
		var definition = ExternalProviderDefinition.Get(id);
		string state, cookie;
		using (var scope = services.CreateScope())
		{
			var challenge = Context(scope.ServiceProvider, "/start");
			await challenge.ChallengeAsync(definition.Scheme, new AuthenticationProperties(new Dictionary<string, string?> { ["transaction"] = "original" }));
			var location = new Uri(challenge.Response.Headers.Location.ToString());
			var parameters = QueryHelpers.ParseQuery(location.Query);
			await Assert.That(parameters["redirect_uri"].ToString()).IsEqualTo("https://game.example" + definition.CallbackPath);
			state = parameters["state"].ToString(); cookie = challenge.Response.Headers.SetCookie.Single()!.Split(';')[0];
		}
		using var callbackScope = services.CreateScope();
		var callback = Context(callbackScope.ServiceProvider, definition.CallbackPath);
		callback.Request.QueryString = QueryString.Create(new Dictionary<string, string?> { ["state"] = corruptState ? "invalid-state" : state, [denied ? "error" : "code"] = denied ? "access_denied" : "synthetic-code" });
		callback.Request.Headers.Cookie = cookie;
		var handler = await callbackScope.ServiceProvider.GetRequiredService<IAuthenticationHandlerProvider>().GetHandlerAsync(callback, definition.Scheme);
		await ((IAuthenticationRequestHandler)handler!).HandleRequestAsync();
		return callback;
	}
	private sealed class LogCapture : ILoggerProvider, ILogger
	{
		private readonly System.Collections.Concurrent.ConcurrentQueue<string> messages = new();
		public string Text => string.Join("\n", messages);
		public ILogger CreateLogger(string categoryName) => this;
		public void Dispose() { }
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel level) => true;
		public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception) + exception);
	}
	private sealed class Backchannel : HttpMessageHandler
	{
		public bool TokenError { get; init; }
		public string? IdToken { get; set; }
		public string Profile { get; set; } = "{}";
		public string TokenBody { get; private set; } = "";
		public string? UserInfoAuthorization { get; private set; }
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			if (request.Method == HttpMethod.Post)
			{
				TokenBody = await request.Content!.ReadAsStringAsync(ct);
				if (IdToken is not null) return new(HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { access_token = "remote-token", token_type = "Bearer", id_token = IdToken })) };
				return new(TokenError ? HttpStatusCode.BadRequest : HttpStatusCode.OK) { Content = new StringContent(TokenError ? "{\"error\":\"raw-upstream-error\"}" : "{\"access_token\":\"remote-token\",\"token_type\":\"Bearer\"}") };
			}
			UserInfoAuthorization = request.Headers.Authorization?.ToString(); return new(HttpStatusCode.OK) { Content = new StringContent(Profile) };
		}
	}
}
