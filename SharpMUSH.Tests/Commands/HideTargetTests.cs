using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@hide &lt;descriptor&gt;</c> and <c>@hide &lt;player&gt;</c>, PennMUSH <c>hide_player</c>
/// (<c>bsd.c:7161-7251</c>).
/// </summary>
public class HideTargetTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private Task RunAs(TestIsolationHelpers.TestPlayer player, string command) =>
		WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command)).AsTask();

	private Task AsGod(string command) =>
		WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who);
		await action();
		return [.. WebAppFactoryArg.Notifications.For(who).Skip(before)];
	}

	[Test]
	public async ValueTask WizardHidesAnotherPlayer()
	{
		var wizard = await PlayerAsync("HideWiz");
		var target = await PlayerAsync("HideTarget");
		await AsGod($"@set {wizard.DbRef}=WIZARD");

		var heard = await MessagesWhile(wizard.DbRef, () => RunAs(wizard, $"@hide/on {target.Name}"));

		await Assert.That(heard).Contains($"{target.Name} no longer appears on the WHO list.");
		await Assert.That(ConnectionService.Get(target.Handle)?.IsHidden).IsTrue();

		heard = await MessagesWhile(wizard.DbRef, () => RunAs(wizard, $"@hide {target.Name}"));

		await Assert.That(heard).Contains($"{target.Name} now appears on the WHO list.");
		await Assert.That(ConnectionService.Get(target.Handle)?.IsHidden).IsFalse();
	}

	[Test]
	public async ValueTask HidePowerCannotHideAnotherPlayer()
	{
		var hider = await PlayerAsync("HidePow");
		var target = await PlayerAsync("HidePowTarget");
		await AsGod($"@power {hider.DbRef}=Hide");

		var heard = await MessagesWhile(hider.DbRef, () => RunAs(hider, $"@hide/on {target.Name}"));

		await Assert.That(heard).Contains("Permission denied. You need the game.wizard permission.");
		await Assert.That(ConnectionService.Get(target.Handle)?.IsHidden).IsFalse();

		heard = await MessagesWhile(hider.DbRef, () => RunAs(hider, $"@hide/on {target.Handle}"));

		await Assert.That(heard).Contains("Permission denied. You need one of these permissions: game.wizard, game.royalty, game.see_all.");
		await Assert.That(ConnectionService.Get(target.Handle)?.IsHidden).IsFalse();
	}

	[Test]
	public async ValueTask DescriptorTogglesOneConnection()
	{
		var hider = await PlayerAsync("HideDesc");
		await AsGod($"@power {hider.DbRef}=Hide");

		var heard = await MessagesWhile(hider.DbRef, () => RunAs(hider, $"@hide {hider.Handle}"));

		await Assert.That(heard).Contains("Connection hidden.");
		await Assert.That(ConnectionService.Get(hider.Handle)?.IsHidden).IsTrue();

		heard = await MessagesWhile(hider.DbRef, () => RunAs(hider, $"@hide {hider.Handle}"));

		await Assert.That(heard).Contains("Connection unhidden.");
		await Assert.That(ConnectionService.Get(hider.Handle)?.IsHidden).IsFalse();
	}
}
