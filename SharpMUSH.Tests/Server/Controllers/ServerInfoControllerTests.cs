using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Unit tests for <see cref="ServerInfoController"/>: the anonymous server-info endpoint must
/// surface <c>Net.Guests</c> so the portal can decide whether to offer a "play as guest" entry,
/// and <c>Net.MudName</c> so the portal can brand the shell with the real game name.
/// </summary>
public class ServerInfoControllerTests
{
	private static SharpMUSHOptions DefaultOptions()
		=> new OptionsService(Substitute.For<ISharpDatabase>(), []).Create(string.Empty);

	private static ServerInfoController MakeController(bool guestsEnabled, string mudName = "SharpMUSH")
	{
		var options = DefaultOptions();
		options = options with { Net = options.Net with { Guests = guestsEnabled, MudName = mudName } };

		var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		wrapper.CurrentValue.Returns(options);

		return new ServerInfoController(wrapper);
	}

	[Test]
	public async Task Get_ReportsGuestsEnabled_WhenNetGuestsIsTrue()
	{
		var result = MakeController(guestsEnabled: true).Get();

		var response = (ServerInfoController.ServerInfoResponse)((OkObjectResult)result).Value!;
		await Assert.That(response.GuestsEnabled).IsTrue();
	}

	[Test]
	public async Task Get_ReportsGuestsDisabled_WhenNetGuestsIsFalse()
	{
		var result = MakeController(guestsEnabled: false).Get();

		var response = (ServerInfoController.ServerInfoResponse)((OkObjectResult)result).Value!;
		await Assert.That(response.GuestsEnabled).IsFalse();
	}

	[Test]
	public async Task Get_ReportsConfiguredMudName()
	{
		var result = MakeController(guestsEnabled: true, mudName: "My Grand Game").Get();

		var response = (ServerInfoController.ServerInfoResponse)((OkObjectResult)result).Value!;
		await Assert.That(response.MudName).IsEqualTo("My Grand Game");
	}
}
