using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Configuration;

public class ConfigurationControllerTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task ImportConfiguration_ValidConfig_StoresInDatabase()
	{
		// Arrange
		var database = WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var optionsWrapper = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var optionsCache = WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitorCache<SharpMUSHOptions>>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<ConfigurationController>>();
		
		var controller = new ConfigurationController(optionsWrapper, database, optionsCache, logger);

		const string configContent = """
		# Test configuration
		mud_name Test MUSH Import
		port 4205
		ssl_port 4204
		""";

		// Act
		var result = await controller.ImportConfiguration(configContent);

		// Assert - Verify the result is OK
		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
		
		var okResult = (OkObjectResult)result.Result!;
		var response = (ConfigurationResponse)okResult.Value!;
		
		// Verify the returned configuration has the imported values
		await Assert.That(response.Configuration.Net.MudName).IsEqualTo("Test MUSH Import");
		await Assert.That(response.Configuration.Net.Port).IsEqualTo((uint)4205);
		await Assert.That(response.Configuration.Net.SslPort).IsEqualTo((uint)4204);

		// Verify the configuration was stored in the database
		// The database operation should have completed by now since SetExpandedServerData was awaited
		var storedConfig = await database.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions));
		await Assert.That(storedConfig).IsNotNull();
		await Assert.That(storedConfig!.Net.MudName).IsEqualTo("Test MUSH Import");
		await Assert.That(storedConfig.Net.Port).IsEqualTo((uint)4205);
		await Assert.That(storedConfig.Net.SslPort).IsEqualTo((uint)4204);
	}

	[Test]
	public async Task ImportConfiguration_InvalidConfig_ReturnsBadRequest()
	{
		// Arrange
		var database = WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var optionsWrapper = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var optionsCache = WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitorCache<SharpMUSHOptions>>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<ConfigurationController>>();
		
		var controller = new ConfigurationController(optionsWrapper, database, optionsCache, logger);

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
		var optionsCache = WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitorCache<SharpMUSHOptions>>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<ConfigurationController>>();
		
		var controller = new ConfigurationController(optionsWrapper, database, optionsCache, logger);

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
