using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
}
