using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Server;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// The controller against a substituted store: what an import parses and returns, and what it hands
/// the store, without writing over the configuration the shared test server runs on.
/// <see cref="ServerConfigurationTests"/> covers the reload wiring the store write feeds.
/// </summary>
public class ConfigurationControllerTests
{
	private static (ConfigurationController Controller, IExpandedDataStore Store, ConfigurationReloadService Reload) Create()
	{
		var options = new TestSharpMushOptions.FixedWrapper(TestSharpMushOptions.Create());
		var store = Substitute.For<IExpandedDataStore>();
		var reload = new ConfigurationReloadService();

		var config = StoreConfigOptionWriter.Create(store, options, reload);
		return (new ConfigurationController(options, config,
			new MushCnfImportService(config, store, NullLogger<MushCnfImportService>.Instance),
			Substitute.For<IAuditLog>(), NullLogger<ConfigurationController>.Instance), store, reload);
	}

	[Test]
	public async Task ImportConfiguration_ValidConfig_ReturnsCorrectValues()
	{
		var (controller, store, reload) = Create();
		var changeToken = reload.GetChangeToken();

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

		await store.Received(1).SetExpandedServerData(
			nameof(SharpMUSHOptions),
			Arg.Is<object>(o => o is SharpMUSHOptions && ((SharpMUSHOptions)o).Net.MudName == "Test MUSH API Return"),
			Arg.Any<CancellationToken>());
		await Assert.That(changeToken.HasChanged).IsTrue();
	}

	/// <summary>
	/// An uploaded configuration's <c>include</c> is not followed: it would name a file on the server,
	/// and whatever that file sets would be read into the configuration and sent back in the response.
	/// </summary>
	[Test]
	public async Task ImportConfiguration_DoesNotFollowAnIncludeToAServerFile()
	{
		var (controller, _, _) = Create();

		var serverFile = Path.Join(Path.GetTempPath(), $"sharpmush-server-{Guid.NewGuid():N}.cnf");
		await File.WriteAllLinesAsync(serverFile, ["mud_name Read From The Server", "restrict_command @dig nobody"]);
		try
		{
			var result = await controller.ImportConfiguration($"mud_name Test MUSH Uploaded\ninclude {serverFile}\n");

			await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
			var response = (ConfigurationResponse)((OkObjectResult)result.Result!).Value!;
			await Assert.That(response.Configuration.Net.MudName).IsEqualTo("Test MUSH Uploaded");
		}
		finally
		{
			File.Delete(serverFile);
		}
	}

	[Test]
	public async Task ImportConfiguration_EmptyConfig_ReturnsOk()
	{
		var (controller, _, _) = Create();

		var result = await controller.ImportConfiguration("");

		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
	}

	[Test]
	public async Task GetConfiguration_ReturnsCurrentConfiguration()
	{
		var (controller, _, _) = Create();

		var result = controller.GetConfiguration();

		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();

		var okResult = (OkObjectResult)result.Result!;
		var response = (ConfigurationResponse)okResult.Value!;

		await Assert.That(response.Configuration).IsNotNull();
		await Assert.That(response.Metadata).IsNotNull();
	}
}
