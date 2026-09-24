using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.BUnit.Controllers;

public class ConfigurationControllerTests
{
	private static SharpMUSHOptions CreateDefaultOptions() => TestOptions.Create();

	private ConfigurationController CreateController(SharpMUSHOptions? options = null)
	{
		var opts = options ?? CreateDefaultOptions();
		var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		wrapper.CurrentValue.Returns(opts);
		var database = Substitute.For<ISharpDatabase>();
		var reloadService = new ConfigurationReloadService();
		var logger = Substitute.For<ILogger<ConfigurationController>>();
		return new ConfigurationController(wrapper, database, reloadService, logger);
	}

	[TUnit.Core.Test]
	public async Task GetConfiguration_ReturnsOk()
	{
		var controller = CreateController();
		var result = controller.GetConfiguration();

		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
		var ok = (OkObjectResult)result.Result!;
		await Assert.That(ok.Value).IsTypeOf<ConfigurationResponse>();
	}

	[TUnit.Core.Test]
	public async Task ExportConfiguration_ReturnsJsonContent()
	{
		var controller = CreateController();
		var result = controller.ExportConfiguration();

		await Assert.That(result).IsTypeOf<ContentResult>();
		var content = (ContentResult)result;
		await Assert.That(content.ContentType).IsEqualTo("application/json");
		await Assert.That(content.Content).IsNotNull();
	}

	[TUnit.Core.Test]
	public async Task UpdateConfiguration_EmptyUpdates_ReturnsBadRequest()
	{
		var controller = CreateController();
		var result = await controller.UpdateConfiguration(new Dictionary<string, JsonElement>());

		await Assert.That(result.Result).IsTypeOf<BadRequestObjectResult>();
	}

	[TUnit.Core.Test]
	public async Task UpdateConfiguration_InvalidPath_ReturnsBadRequest()
	{
		var controller = CreateController();
		var updates = new Dictionary<string, JsonElement>
		{
			["InvalidSingleSegment"] = JsonSerializer.SerializeToElement(42)
		};

		var result = await controller.UpdateConfiguration(updates);
		await Assert.That(result.Result).IsTypeOf<BadRequestObjectResult>();
	}

	[TUnit.Core.Test]
	public async Task UpdateConfiguration_UnknownCategory_ReturnsBadRequest()
	{
		var controller = CreateController();
		var updates = new Dictionary<string, JsonElement>
		{
			["FakeCategory.FakeProp"] = JsonSerializer.SerializeToElement("value")
		};

		var result = await controller.UpdateConfiguration(updates);
		await Assert.That(result.Result).IsTypeOf<BadRequestObjectResult>();
	}

	[TUnit.Core.Test]
	public async Task UpdateConfiguration_ValidNetPort_ReturnsOkWithUpdatedConfig()
	{
		var controller = CreateController();
		var updates = new Dictionary<string, JsonElement>
		{
			["Net.Port"] = JsonSerializer.SerializeToElement(9999)
		};

		var result = await controller.UpdateConfiguration(updates);
		if (result.Result is BadRequestObjectResult bad)
			throw new Exception("BadRequest: " + System.Text.Json.JsonSerializer.Serialize(bad.Value));
		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();

		var ok = (OkObjectResult)result.Result!;
		var response = (ConfigurationResponse)ok.Value!;
		await Assert.That((int)response.Configuration.Net.Port).IsEqualTo(9999);
	}

	/// <summary>
	/// A path is matched case-insensitively in both halves. It used to disagree with itself: updates were
	/// grouped by category with an OrdinalIgnoreCase dictionary but the category was then resolved with a
	/// case-sensitive GetProperty, so "net.Port" reported an unknown category while "Net.port" applied.
	/// </summary>
	[TUnit.Core.Test]
	[Arguments("net.Port")]
	[Arguments("Net.port")]
	[Arguments("NET.PORT")]
	public async Task UpdateConfiguration_PathIsCaseInsensitive(string path)
	{
		var controller = CreateController();
		var updates = new Dictionary<string, JsonElement>
		{
			[path] = JsonSerializer.SerializeToElement(9998)
		};

		var result = await controller.UpdateConfiguration(updates);
		if (result.Result is BadRequestObjectResult bad)
			throw new Exception("BadRequest: " + System.Text.Json.JsonSerializer.Serialize(bad.Value));

		var ok = (OkObjectResult)result.Result!;
		await Assert.That((int)((ConfigurationResponse)ok.Value!).Configuration.Net.Port).IsEqualTo(9998);
	}

	/// <summary>A property that does not exist in the named category is rejected, not silently ignored.</summary>
	[TUnit.Core.Test]
	public async Task UpdateConfiguration_PropertyFromAnotherCategory_ReturnsBadRequest()
	{
		var controller = CreateController();
		var updates = new Dictionary<string, JsonElement>
		{
			["Net.PlayerStart"] = JsonSerializer.SerializeToElement(5)
		};

		var result = await controller.UpdateConfiguration(updates);

		// Several paths return BadRequest, so assert which one: the property is real but lives in Database,
		// and it used to match no NetOptions constructor parameter and be dropped with a 200 OK.
		var bad = (BadRequestObjectResult)result.Result!;
		await Assert.That(JsonSerializer.Serialize(bad.Value)).Contains("Unknown property");
	}

	[TUnit.Core.Test]
	public async Task UpdateConfiguration_BooleanToggle_Works()
	{
		var controller = CreateController();
		var updates = new Dictionary<string, JsonElement>
		{
			["Net.UseWebsockets"] = JsonSerializer.SerializeToElement(false)
		};

		var result = await controller.UpdateConfiguration(updates);
		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();

		var ok = (OkObjectResult)result.Result!;
		var response = (ConfigurationResponse)ok.Value!;
		await Assert.That(response.Configuration.Net.UseWebsockets).IsFalse();
	}

	[TUnit.Core.Test]
	public async Task UpdateConfiguration_MultipleProperties_AllApplied()
	{
		var controller = CreateController();
		var updates = new Dictionary<string, JsonElement>
		{
			["Net.Port"] = JsonSerializer.SerializeToElement(8888),
			["Net.SslPort"] = JsonSerializer.SerializeToElement(8443)
		};

		var result = await controller.UpdateConfiguration(updates);
		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();

		var ok = (OkObjectResult)result.Result!;
		var response = (ConfigurationResponse)ok.Value!;
		await Assert.That((int)response.Configuration.Net.Port).IsEqualTo(8888);
		await Assert.That((int)response.Configuration.Net.SslPort).IsEqualTo(8443);
	}

	[TUnit.Core.Test]
	public async Task UpdateConfiguration_StringField_Works()
	{
		var controller = CreateController();
		var updates = new Dictionary<string, JsonElement>
		{
			["Net.MudName"] = JsonSerializer.SerializeToElement("TestMUSH")
		};

		var result = await controller.UpdateConfiguration(updates);
		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();

		var ok = (OkObjectResult)result.Result!;
		var response = (ConfigurationResponse)ok.Value!;
		await Assert.That(response.Configuration.Net.MudName).IsEqualTo("TestMUSH");
	}

	[TUnit.Core.Test]
	public async Task UpdateConfiguration_CrossCategory_BothApplied()
	{
		var controller = CreateController();
		var updates = new Dictionary<string, JsonElement>
		{
			["Net.Port"] = JsonSerializer.SerializeToElement(7777),
			["Log.LogCommands"] = JsonSerializer.SerializeToElement(true)
		};

		var result = await controller.UpdateConfiguration(updates);
		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();

		var ok = (OkObjectResult)result.Result!;
		var response = (ConfigurationResponse)ok.Value!;
		await Assert.That((int)response.Configuration.Net.Port).IsEqualTo(7777);
		await Assert.That(response.Configuration.Log.LogCommands).IsTrue();
	}

	[TUnit.Core.Test]
	public async Task UpdateConfiguration_PersistsToDatabase()
	{
		var database = Substitute.For<ISharpDatabase>();
		var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		wrapper.CurrentValue.Returns(CreateDefaultOptions());
		var controller = new ConfigurationController(
			wrapper, database, new ConfigurationReloadService(),
			Substitute.For<ILogger<ConfigurationController>>());

		var updates = new Dictionary<string, JsonElement>
		{
			["Net.Port"] = JsonSerializer.SerializeToElement(5555)
		};

		await controller.UpdateConfiguration(updates);

		await database.Received(1).SetExpandedServerData(
			nameof(SharpMUSHOptions),
			Arg.Any<SharpMUSHOptions>());
	}
}
