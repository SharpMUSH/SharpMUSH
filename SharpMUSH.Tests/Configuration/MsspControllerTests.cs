using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Configuration;

/// <summary>The MSSP page's API against a substituted store, so the shared server's configuration is untouched.</summary>
public class MsspControllerTests
{
	private static (MsspController Controller, IExpandedDataStore Store, ConfigurationReloadService Reload) Create()
	{
		var options = new TestSharpMushOptions.FixedWrapper(TestSharpMushOptions.Create());
		var store = Substitute.For<IExpandedDataStore>();
		var report = Substitute.For<IMsspReportService>();
		report.BuildAsync().Returns(new ValueTask<IReadOnlyList<MsspReportedVariable>>([]));
		var reload = new ConfigurationReloadService();
		return (new MsspController(options, store, reload, report, Substitute.For<IAuditLog>()), store, reload);
	}

	[Test]
	public async Task SavingStoresTheSettingsCanonicalAndSignalsTheChange()
	{
		var (controller, store, reload) = Create();
		var changed = reload.GetChangeToken();

		var result = await controller.Put(new MsspSettingsRequest(new Dictionary<string, string[]>
		{
			["genre"] = ["Fantasy"],
			["Pay_To_Play"] = ["0"]
		}));

		var response = (MsspSettingsResponse)((OkObjectResult)result.Result!).Value!;
		await Assert.That(response.Settings.Keys.ToArray()).IsEquivalentTo(new[] { "GENRE", "PAY TO PLAY" });
		await store.Received(1).SetExpandedServerData(nameof(SharpMUSHOptions),
			Arg.Is<object>(o => o is SharpMUSHOptions && ((SharpMUSHOptions)o).Mssp.Variables["GENRE"][0] == "Fantasy"),
			Arg.Any<CancellationToken>());
		await Assert.That(changed.HasChanged).IsTrue();
	}

	/// <summary>A save with one variable that cannot be kept saves nothing: dropping it quietly would read as saved.</summary>
	[Test]
	public async Task AVariableTheServerReportsRefusesTheWholeSave()
	{
		var (controller, store, _) = Create();

		var result = await controller.Put(new MsspSettingsRequest(new Dictionary<string, string[]>
		{
			["GENRE"] = ["Fantasy"],
			["PLAYERS"] = ["9001"]
		}));

		await Assert.That(result.Result).IsTypeOf<BadRequestObjectResult>();
		await store.DidNotReceive().SetExpandedServerData(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
	}
}
