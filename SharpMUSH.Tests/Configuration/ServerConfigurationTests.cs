using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// The options the running test server holds, and its real reload wiring: a stored options document plus
/// <see cref="ConfigurationReloadService"/>'s signal reaches <see cref="IOptionsMonitor{TOptions}"/> subscribers.
/// Everything that needs no server is in <see cref="ConfigurationTests"/> and <see cref="ConfigurationControllerTests"/>.
/// </summary>
/// <remarks>
/// <see cref="NotInParallelAttribute"/>: the reload test rewrites the whole stored options document, as
/// <see cref="Commands.ConfigSetCommandTests"/> does, and restores what it found; the read must not land
/// in between.
/// </remarks>
[NotInParallel]
public class ServerConfigurationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	/// <summary>The server is configured from the same test mushcnf.dst <see cref="ConfigurationTests.ParseConfigurationFile"/> parses.</summary>
	[Test]
	public async Task CanUseOptionsFromServer()
	{
		var configuration = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;

		await Assert.That(configuration.Chat.ChatTokenAlias).IsEqualTo('+');
		await Assert.That(configuration.Net.MudName).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	}

	[Test]
	public async Task ImportConfiguration_UpdatesOptionsMonitor()
	{
		var database = WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var configReloadService = WebAppFactoryArg.Services.GetRequiredService<ConfigurationReloadService>();
		var optionsMonitor = WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>();

		var changeDetected = false;
		string? newMudName = null;

		var original = await database.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions));
		using var subscription = optionsMonitor.OnChange((options, _) =>
		{
			changeDetected = true;
			newMudName = options.Net.MudName;
		});

		try
		{
			const string configContent = """
				# Test configuration
				mud_name Test Options Monitor Update
				port 4209
				ssl_port 4208
				""";

			var tempFile = Path.GetTempFileName();
			await File.WriteAllTextAsync(tempFile, configContent);
			var importedOptions = ReadPennMushConfig.Create(tempFile);
			File.Delete(tempFile);

			await database.SetExpandedServerData(nameof(SharpMUSHOptions), importedOptions);

			configReloadService.SignalChange();

			await Task.Delay(200);

			await Assert.That(changeDetected).IsTrue();

			await Assert.That(newMudName).IsNotNull();
		}
		finally
		{
			// The imported document replaces every option the shared server runs on, not only these three.
			await database.SetExpandedServerData(nameof(SharpMUSHOptions), original!);
			configReloadService.SignalChange();
		}
	}
}
