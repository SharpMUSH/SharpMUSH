using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The wizard WHO marks a website connection with <c>W</c> after its descriptor and shows the address
/// it came from in full, so staff can sitelock it like any telnet player's.
/// </summary>
public class WhoWebConnectionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async Task WizardWhoMarksAWebConnectionAndShowsItsWholeAddress()
	{
		const string address = "2001:db8:85a3:1234:5678:8a2e:370:7334";
		var web = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WhoWeb");
		var webHandle = TestIsolationHelpers.GenerateUniqueHandle();
		await ConnectionService.Register(webHandle, address, address, "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await ConnectionService.Bind(webHandle, web.DbRef);

		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WhoWebWiz");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));

		await Parser.CommandParse(wizard.Handle, ConnectionService, MarkupText.Plain("WHO"));

		var listing = WebAppFactoryArg.Notifications.ForHandle(wizard.Handle).Last(m => m.Contains("Player Name"));
		var row = listing.Split('\n').Single(line => line.Contains($"{webHandle}W"));

		await Assert.That(row).StartsWith(web.Name);
		await Assert.That(row).EndsWith(address);
	}
}
