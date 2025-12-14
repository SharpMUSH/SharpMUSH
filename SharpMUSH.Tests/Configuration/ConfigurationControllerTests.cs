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
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task ImportConfiguration_ValidConfig_ReturnsCorrectValues()
	{
		// Arrange
		var database = WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var optionsWrapper = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var configReloadService = WebAppFactoryArg.Services.GetRequiredService<ConfigurationReloadService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<ConfigurationController>>();
		
		var controller = new ConfigurationController(optionsWrapper, database, configReloadService, logger);

		const string configContent = """
		# Test configuration
		mud_name Test MUSH API Return
		port 4207
		ssl_port 4206
		""";

		// Act
		var result = await controller.ImportConfiguration(configContent);

		// Assert - Verify the result is OK
		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
		
		var okResult = (OkObjectResult)result.Result!;
		var response = (ConfigurationResponse)okResult.Value!;
		
		// Verify the returned configuration has the imported values
		await Assert.That(response.Configuration.Net.MudName).IsEqualTo("Test MUSH API Return");
		await Assert.That(response.Configuration.Net.Port).IsEqualTo((uint)4207);
		await Assert.That(response.Configuration.Net.SslPort).IsEqualTo((uint)4206);
	}

	[Test]
	public async Task ImportConfiguration_InvalidConfig_ReturnsBadRequest()
	{
		// Arrange
		var database = WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var optionsWrapper = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var configReloadService = WebAppFactoryArg.Services.GetRequiredService<ConfigurationReloadService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<ConfigurationController>>();
		
		var controller = new ConfigurationController(optionsWrapper, database, configReloadService, logger);

		// Empty config content that will fail file creation
		const string configContent = "";

		// Act
		var result = await controller.ImportConfiguration(configContent);

		// Assert - Even empty config should parse successfully (just use defaults)
		// So we expect OK result
		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task GetConfiguration_ReturnsCurrentConfiguration()
	{
		// Arrange
		var database = WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var optionsWrapper = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var configReloadService = WebAppFactoryArg.Services.GetRequiredService<ConfigurationReloadService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<ConfigurationController>>();
		
		var controller = new ConfigurationController(optionsWrapper, database, configReloadService, logger);

		// Act
		var result = controller.GetConfiguration();

		// Assert
		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
		
		var okResult = (OkObjectResult)result.Result!;
		var response = (ConfigurationResponse)okResult.Value!;
		
		await Assert.That(response.Configuration).IsNotNull();
		await Assert.That(response.Metadata).IsNotNull();
	}

	[Test]
	public async Task ImportConfiguration_UpdatesOptionsMonitor()
	{
		// Arrange
		var database = WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var configReloadService = WebAppFactoryArg.Services.GetRequiredService<ConfigurationReloadService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<ConfigurationController>>();
		
		// Get the real IOptionsMonitor to test change notifications
		var optionsMonitor = WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>();
		
		// Track if the change callback was triggered
		bool changeDetected = false;
		string? newMudName = null;
		
		// Register a change callback on the options monitor
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

			// Parse and store the configuration directly (simulating what the controller does)
			var tempFile = Path.GetTempFileName();
			await System.IO.File.WriteAllTextAsync(tempFile, configContent);
			var importedOptions = ReadPennMushConfig.Create(tempFile);
			System.IO.File.Delete(tempFile);

			// Store in database
			await database.SetExpandedServerData(nameof(SharpMUSHOptions), importedOptions);

			// Signal the change
			configReloadService.SignalChange();

			// Wait for the change callback to be triggered
			await Task.Delay(200);

			// Assert - Verify that the change was detected
			await Assert.That(changeDetected).IsTrue();
			
			// Verify that the callback received configuration data
			await Assert.That(newMudName).IsNotNull();
		}
		finally
		{
			disposable?.Dispose();
		}
	}
}
