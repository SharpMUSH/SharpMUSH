using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// PennMUSH renamed the <c>Pueblo_Send</c> power to <c>Send_OOB</c> "to reflect its new use for
/// other, non-Pueblo-related, out of band messages. Pueblo_Send remains as an alias."
/// (<c>game/txt/hlp/pennv186.hlp:86</c>), and applies the rename on database load
/// (<c>src/flags.c:850-855</c>). The same release note adds: "Mortals can now use oob() on
/// themselves, and those with the Send_OOB power can use it to send to anyone."
///
/// SharpMUSH gated <c>oob()</c> on a power named <c>Send_OOB</c> while seeding only
/// <c>Pueblo_Send</c>, so the gate could never open and the documented grant was unreachable.
/// </summary>
public class SendOobPowerTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async Task<string> ThinkAs(TestIsolationHelpers.TestPlayer who, string code)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who.DbRef);
		await Parser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain($"think {code}"));
		return WebAppFactoryArg.Notifications.For(who.DbRef).Skip(before).LastOrDefault() ?? string.Empty;
	}

	[Test]
	public async Task Oob_WithoutTheSendOobPower_IsDeniedAgainstAnotherPlayer()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator(), ConnectionService, "OobPlainMortal");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator(), ConnectionService, "OobPlainTarget");

		var said = await ThinkAs(mortal, $"oob({target.DbRef}, testpkg)");

		await Assert.That(said).IsEqualTo(ErrorMessages.Returns.PermissionDenied)
			.Because("a mortal without the power may only oob() themselves");
	}

	[Test]
	public async Task Oob_WithTheSendOobPowerGranted_MayTargetAnotherPlayer()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator(), ConnectionService, "OobPoweredMortal");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator(), ConnectionService, "OobPoweredTarget");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {mortal.DbRef}=Send_OOB"));

		var said = await ThinkAs(mortal, $"oob({target.DbRef}, testpkg)");

		await Assert.That(said).IsNotEqualTo(ErrorMessages.Returns.PermissionDenied)
			.Because("Send_OOB is exactly the power oob() gates on, so granting it must open the gate");
	}

	[Test]
	public async Task Oob_GrantedByThePuebloSendAlias_MayTargetAnotherPlayer()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator(), ConnectionService, "OobAliasMortal");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator(), ConnectionService, "OobAliasTarget");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {mortal.DbRef}=Pueblo_Send"));

		var said = await ThinkAs(mortal, $"oob({target.DbRef}, testpkg)");

		await Assert.That(said).IsNotEqualTo(ErrorMessages.Returns.PermissionDenied)
			.Because("PennMUSH keeps Pueblo_Send as an alias of Send_OOB, so it grants the same power");
	}

	private Mediator.IMediator Mediator() => WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>();
}
