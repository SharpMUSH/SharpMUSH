using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Registration;

public static class NativeLoginRegistration
{
	public static IServiceCollection AddNativeCharacterCredentials(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton<AccountCredentialGate>();
		services.TryAddSingleton<RecentAccountProofStore>();
		services.TryAddSingleton<IRecentAccountProofStore>(sp => sp.GetRequiredService<RecentAccountProofStore>());
		services.TryAddSingleton<NativeLoginSecretStore>();
		services.TryAddSingleton<INativeLoginSecretStore>(sp => sp.GetRequiredService<NativeLoginSecretStore>());
		services.AddSingleton<IAccountCredentialRevocation>(sp => sp.GetRequiredService<NativeLoginSecretStore>());
		services.TryAddSingleton<INativeLoginPolicy, NativeLoginPolicy>();
		services.TryAddSingleton<NativeLoginService>();
		return services;
	}
}
