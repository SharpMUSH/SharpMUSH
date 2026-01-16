using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Configuration;

public class ConfigurationControllerTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	[Test]
	public async Task ImportConfiguration_ValidConfig_ReturnsCorrectValues()
	{
		var database = Factory.Services.GetRequiredService<ISharpDatabase>();
		var optionsWrapper = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var configReloadService = Factory.Services.GetRequiredService<ConfigurationReloadService>();
		var logger = Factory.Services.GetRequiredService<ILogger<ConfigurationController>>();
		
		var controller = new ConfigurationController(optionsWrapper, database, configReloadService, logger);

		const string configContent = """
		# Test configuration
		mud_name Test MUSH API Return
		port 4207
		ssl_port 4206
		""";

		var result = await controller.ImportConfiguration(configContent);

		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
		
		var okResult = (OkObjectResult)result.Result!;
		var response = (ConfigurationResponse)okResult.Value!;
		
		await Assert.That(response.Configuration.Net.MudName).IsEqualTo("Test MUSH API Return");
		await Assert.That(response.Configuration.Net.Port).IsEqualTo((uint)4207);
		await Assert.That(response.Configuration.Net.SslPort).IsEqualTo((uint)4206);
	}

	[Test]
	public async Task ImportConfiguration_EmptyConfig_ReturnsOk()
	{
		var database = Factory.Services.GetRequiredService<ISharpDatabase>();
		var optionsWrapper = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var configReloadService = Factory.Services.GetRequiredService<ConfigurationReloadService>();
		var logger = Factory.Services.GetRequiredService<ILogger<ConfigurationController>>();
		
		var controller = new ConfigurationController(optionsWrapper, database, configReloadService, logger);

		const string configContent = "";

		var result = await controller.ImportConfiguration(configContent);

		// Empty config is valid and returns default configuration
		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task GetConfiguration_ReturnsCurrentConfiguration()
	{
		var database = Factory.Services.GetRequiredService<ISharpDatabase>();
		var optionsWrapper = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var configReloadService = Factory.Services.GetRequiredService<ConfigurationReloadService>();
		var logger = Factory.Services.GetRequiredService<ILogger<ConfigurationController>>();
		
		var controller = new ConfigurationController(optionsWrapper, database, configReloadService, logger);

		var result = controller.GetConfiguration();

		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
		
		var okResult = (OkObjectResult)result.Result!;
		var response = (ConfigurationResponse)okResult.Value!;
		
		await Assert.That(response.Configuration).IsNotNull();
		await Assert.That(response.Metadata).IsNotNull();
	}

	[Test]
	public async Task ImportConfiguration_UpdatesOptionsMonitor()
	{
		var database = Factory.Services.GetRequiredService<ISharpDatabase>();
		var configReloadService = Factory.Services.GetRequiredService<ConfigurationReloadService>();
		
		var optionsMonitor = Factory.Services.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>();
		
		bool changeDetected = false;
		string? newMudName = null;
		
		var disposable = optionsMonitor.OnChange((options, name) =>
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
			await System.IO.File.WriteAllTextAsync(tempFile, configContent);
			var importedOptions = ReadPennMushConfig.Create(tempFile);
			System.IO.File.Delete(tempFile);

			await database.SetExpandedServerData(nameof(SharpMUSHOptions), importedOptions);

			configReloadService.SignalChange();

			await Task.Delay(200);

			await Assert.That(changeDetected).IsTrue();
			
			await Assert.That(newMudName).IsNotNull();
		}
		finally
		{
			disposable?.Dispose();
		}
	}
}
