using Microsoft.AspNetCore.Http;
using SharpMUSH.Server.Registration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication.External;

namespace SharpMUSH.Server.Authentication;

public static class AccountSecurityRegistration
{
	public static IServiceCollection AddAccountSecurity(this IServiceCollection services)
	{
		services.TryAddSingleton<AccountCredentialGate>();
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton<RecentAccountProofStore>();
		services.TryAddSingleton<IRecentAccountProofStore>(sp => sp.GetRequiredService<RecentAccountProofStore>());
		services.AddSingleton<IAccountCredentialRevocation>(sp => sp.GetRequiredService<RecentAccountProofStore>());
		services.AddSingleton<ExternalLoginFlowStore>();
		services.AddSingleton<IAccountCredentialRevocation>(sp => sp.GetRequiredService<ExternalLoginFlowStore>());
		services.AddSingleton<IAccountProofAuthorityValidator, AccountProofAuthorityValidator>();
		services.AddSingleton<AccountRecentProofService>();
		services.AddSingleton<AccountRecoveryHierarchy>();
		services.AddSingleton<AccountSecurityService>();
		services.AddSingleton<AccountSecurityCeremonyStore>();
		services.AddSingleton<IAccountCredentialRevocation>(sp => sp.GetRequiredService<AccountSecurityCeremonyStore>());
		services.AddSingleton<AccountLoginCompletion>();
		services.AddSingleton<ExternalLoginJourney>();
		services.AddSingleton<IExternalAuthenticationCompletion>(sp => sp.GetRequiredService<ExternalLoginJourney>());
		// MVC antiforgery attributes require the view-feature filter registration, even for JSON/form APIs.
		services.AddControllersWithViews();
		services.AddAntiforgery(options =>
		{
			options.HeaderName = "X-SharpMUSH-CSRF";
			options.Cookie.Name = "__Host-SharpMUSH.Antiforgery";
			options.Cookie.Path = "/"; options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
			options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Strict;
		});
		return services;
	}
}
