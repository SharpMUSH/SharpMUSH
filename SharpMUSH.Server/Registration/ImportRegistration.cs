using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Server.Services;
using SharpMUSH.Server.Services.Imports;

namespace SharpMUSH.Server.Registration;

internal static class ImportRegistration
{
	public static IServiceCollection AddIsolatedDatabaseImports(this IServiceCollection services)
	{
		services.AddSingleton(sp => new ImportPromotionJournal(sp.GetRequiredService<LightningWorldPath>().Value,
			DataProtectionRegistration.KeyRingPath(sp.GetRequiredService<LightningWorldPath>().Value)));
		services.AddSingleton<ImportPreviewCoordinator>();
		services.AddSingleton<ImportSessionRegistry>();
		services.AddSingleton<StartupImportCoordinator>();
		services.AddSingleton<PennMUSHDatabaseConversionService>();
		services.AddHostedService<ImportConnectionResetService>();
		return services;
	}
}
