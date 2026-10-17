using SharpMUSH.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpMUSH.Library.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Validators;
using SharpMUSH.Implementation.Handlers.Database;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Secrets;
namespace SharpMUSH.Server.Authentication.External;

public static class ExternalAuthenticationRegistration
{
	public const string TransientScheme = "ExternalTransient";
	public static IServiceCollection AddExternalAccountAuthentication(this IServiceCollection services)
	{
		services.AddAccountSecurity();
		services.TryAddSingleton<AccountCredentialGate>();
		services.AddSingleton<IExternalProviderSettingsStore, ExternalProviderSettingsStore>();
		services.AddSingleton<ExternalProviderCatalog>();
		services.AddHostedService<ExternalProviderStartup>();
		// No defaults change. APIs continue to accept AccountSession alone.
		var builder = services.AddAuthentication().AddCookie(TransientScheme, options =>
		{
			options.Cookie.Name = "__Host-SharpMUSH.ExternalTransient";
			options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.SameSite = SameSiteMode.Lax;
			options.ExpireTimeSpan = TimeSpan.FromMinutes(5); options.SlidingExpiration = false;
		});
		foreach (var definition in ExternalProviderDefinition.All)
		{
			if (definition.RequiresClientSecret) services.AddSingleton(new SecretDefinition(definition.SecretName, definition.DisplayName + " account sign-in client secret."));
			if (definition.OpenIdConnect)
			{
				services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());
				builder.AddRemoteScheme<OpenIdConnectOptions, ExternalOpenIdConnectHandler>(definition.Scheme, definition.DisplayName, _ => { });
				services.AddOptions<OpenIdConnectOptions>(definition.Scheme).Configure<ExternalProviderCatalog>((options, catalog) => ConfigureOidc(options, catalog.ProtocolSettingsAsync(definition.Id).GetAwaiter().GetResult()));
			}
			else
			{
				builder.AddOAuth<ExternalOAuthOptions, ExternalOAuthHandler>(definition.Scheme, _ => { });
				services.AddOptions<ExternalOAuthOptions>(definition.Scheme).Configure<ExternalProviderCatalog>((options, catalog) => ConfigureOAuth(options, catalog.ProtocolSettingsAsync(definition.Id).GetAwaiter().GetResult()));
			}
		}
		return services;
	}
	private static void Common(RemoteAuthenticationOptions options, EffectiveExternalProvider provider)
	{
		options.SignInScheme = TransientScheme; options.CallbackPath = provider.Definition.CallbackPath;
		options.SaveTokens = false; options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(5);
		options.CorrelationCookie.HttpOnly = true; options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
		options.BackchannelHttpHandler = new HttpClientHandler { AllowAutoRedirect = false };
	}
	internal static void ConfigureOAuth(ExternalOAuthOptions options, EffectiveExternalProvider provider)
	{
		Common(options, provider);
		options.ProviderId = provider.Definition.Id; options.PublicOrigin = provider.Settings.PublicOrigin;
		options.ClientId = string.IsNullOrEmpty(provider.Settings.ClientId) ? "disabled" : provider.Settings.ClientId;
		// OAuthOptions requires a nonempty secret even for a public client. The adapter never transmits this sentinel.
		options.ClientSecret = provider.ClientSecret ?? "public-client-no-secret";
		options.AuthorizationEndpoint = provider.Definition.AuthorizationEndpoint; options.TokenEndpoint = provider.Definition.TokenEndpoint; options.UserInformationEndpoint = provider.Definition.UserInformationEndpoint;
		options.UsePkce = false; options.Scope.Clear(); foreach (var scope in provider.Definition.Scopes) options.Scope.Add(scope);
		options.Events.OnRemoteFailure = FailedAsync;
		options.Events.OnTicketReceived = CompleteAsync;
		options.Events.OnRedirectToAuthorizationEndpoint = async context =>
		{
			if (await context.HttpContext.RequestServices.GetRequiredService<ExternalProviderCatalog>().EffectiveAsync(provider.Definition.Id, context.HttpContext.RequestAborted) is not EffectiveExternalProvider)
				context.Response.Redirect("/login?external=failed");
			else context.Response.Redirect(context.RedirectUri);
		};
	}
	internal static void ConfigureOidc(OpenIdConnectOptions options, EffectiveExternalProvider provider)
	{
		Common(options, provider);
		options.Authority = provider.Definition.Id == "google" ? "https://accounts.google.com" : "https://login.microsoftonline.com/" + provider.Settings.MicrosoftAudience + "/v2.0";
		options.ClientId = string.IsNullOrEmpty(provider.Settings.ClientId) ? "disabled" : provider.Settings.ClientId;
		options.ClientSecret = provider.ClientSecret ?? "disabled";
		options.ResponseType = OpenIdConnectResponseType.Code; options.ResponseMode = OpenIdConnectResponseMode.FormPost;
		options.UsePkce = true; options.MapInboundClaims = false; options.GetClaimsFromUserInfoEndpoint = false;
		// The final identity adapter needs the validated issuer; framework defaults delete it after TokenValidated.
		options.ClaimActions.Clear();
		options.Scope.Clear(); foreach (var scope in provider.Definition.Scopes) options.Scope.Add(scope);
		options.ProtocolValidator.RequireNonce = true;
		options.NonceCookie.HttpOnly = true; options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
		options.TokenValidationParameters.ValidateIssuer = true; options.TokenValidationParameters.ValidateAudience = true;
		options.TokenValidationParameters.ValidateLifetime = true; options.TokenValidationParameters.RequireExpirationTime = true;
		options.TokenValidationParameters.RequireSignedTokens = true; options.TokenValidationParameters.ValidAudience = options.ClientId;
		options.TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(1);
		if (provider.Definition.Id == "google") options.TokenValidationParameters.ValidIssuers = ["https://accounts.google.com", "accounts.google.com"];
		if (provider.Definition.Id == "microsoft")
		{
			options.TokenValidationParameters.ValidIssuer = "https://login.microsoftonline.com/{tenantid}/v2.0";
			options.TokenValidationParameters.IssuerValidator = AadIssuerValidator.GetAadIssuerValidator(options.Authority).Validate;
			options.TokenValidationParameters.EnableAadSigningKeyIssuerValidation();
		}
		options.Events.OnRedirectToIdentityProvider = async context =>
		{
			if (await context.HttpContext.RequestServices.GetRequiredService<ExternalProviderCatalog>().EffectiveAsync(provider.Definition.Id, context.HttpContext.RequestAborted) is not EffectiveExternalProvider)
			{ context.Response.Redirect("/login?external=failed"); context.HandleResponse(); return; }
			context.ProtocolMessage.RedirectUri = provider.Settings.PublicOrigin + provider.Definition.CallbackPath;
			if (context.Properties?.Items.TryGetValue("sm.external.reauth", out var reauthenticate) == true && reauthenticate == "true") context.ProtocolMessage.Prompt = "select_account";
		};
		options.Events.OnAuthorizationCodeReceived = context =>
		{
			if (context.TokenEndpointRequest is not null) context.TokenEndpointRequest.RedirectUri = provider.Settings.PublicOrigin + provider.Definition.CallbackPath;
			return Task.CompletedTask;
		};
		options.Events.OnMessageReceived = async context =>
		{
			if (await context.HttpContext.RequestServices.GetRequiredService<ExternalProviderCatalog>().EffectiveAsync(provider.Definition.Id, context.HttpContext.RequestAborted) is not EffectiveExternalProvider)
			{
				await context.HttpContext.RequestServices.GetRequiredService<IExternalAuthenticationCompletion>().FailAsync(context.HttpContext, context.Properties, context.HttpContext.RequestAborted);
				context.Fail("Sign-in provider is unavailable.");
			}
		};
		options.Events.OnTokenValidated = async context =>
		{
			// Code flow's framework validation allows unsigned backchannel tokens; this product requires signed identity proof.
			var invalidSignature = context.SecurityToken is null || string.IsNullOrEmpty(context.SecurityToken.RawSignature) || context.SecurityToken.Header.Alg is null or "none";
			if (invalidSignature || context.Principal is null || context.Properties is null || ExternalIdentityAdapter.OpenId(provider, context.Principal) is not ExternalIdentityProof)
			{
				// ResultContext.Fail does not copy Properties into its result. Clear validated state before refusing.
				await context.HttpContext.RequestServices.GetRequiredService<IExternalAuthenticationCompletion>().FailAsync(context.HttpContext, context.Properties, context.HttpContext.RequestAborted);
				context.Fail("External identity is invalid.");
			}
		};
		options.Events.OnAuthenticationFailed = async context =>
		{
			await context.HttpContext.RequestServices.GetRequiredService<IExternalAuthenticationCompletion>().FailAsync(context.HttpContext, context.Properties, context.HttpContext.RequestAborted);
			// The framework supplies its validated state to RemoteFailure after this event.
			// Handling here would discard that state and leave the pending transaction alive.
		};
		options.Events.OnRemoteFailure = FailedAsync; options.Events.OnTicketReceived = CompleteAsync;
	}
	private static async Task FailedAsync(RemoteFailureContext context)
	{
		await context.HttpContext.RequestServices.GetRequiredService<IExternalAuthenticationCompletion>().FailAsync(context.HttpContext, context.Properties, context.HttpContext.RequestAborted);
		context.Response.Redirect("/login?external=failed"); context.HandleResponse();
	}
	private static async Task CompleteAsync(TicketReceivedContext context)
	{
		context.HandleResponse();
		var providerId = ExternalProviderDefinition.All.Single(definition => definition.Scheme == context.Scheme.Name).Id;
		var catalog = context.HttpContext.RequestServices.GetRequiredService<ExternalProviderCatalog>();
		if (await catalog.EffectiveAsync(providerId, context.HttpContext.RequestAborted) is not EffectiveExternalProvider provider
		 || context.Principal is null || context.Properties is null || ExternalIdentityAdapter.OpenId(provider, context.Principal) is not ExternalIdentityProof proof)
		{
			await context.HttpContext.RequestServices.GetRequiredService<IExternalAuthenticationCompletion>().FailAsync(context.HttpContext, context.Properties, context.HttpContext.RequestAborted);
			context.Response.Redirect("/login?external=failed"); return;
		}
		await context.HttpContext.RequestServices.GetRequiredService<IExternalAuthenticationCompletion>().CompleteAsync(context.HttpContext, context.Properties, proof.Key, proof.Label, context.HttpContext.RequestAborted);
	}
}
/// <summary>Freeze every named protocol configuration after migration and before accepting browser requests.</summary>
public sealed class ExternalProviderStartup(ExternalProviderCatalog catalog, IServiceProvider services) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await catalog.InitializeAsync(cancellationToken);
		foreach (var definition in ExternalProviderDefinition.All)
			if (definition.OpenIdConnect) services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(definition.Scheme);
			else services.GetRequiredService<IOptionsMonitor<ExternalOAuthOptions>>().Get(definition.Scheme);
	}
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
