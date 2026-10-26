using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Connected players standing in rooms of their own, so a test can assert everything one of them heard
/// without other tests' arrivals and departures in the shared start room landing in the window.
/// </summary>
internal sealed class OwnRoomPlayers(ServerWebAppFactory factory)
{
	private IConnectionService ConnectionService => factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();

	public async Task<DBRef> DigRoomAsync(string prefix)
	{
		var dug = await factory.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		return DBRef.Parse(dug.Message!.ToPlainText().Trim());
	}

	/// <summary>A connected player in <paramref name="room"/>, or in a room dug for it alone.</summary>
	public async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix, DBRef? room = null)
		=> await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			factory.Services, Mediator, ConnectionService, prefix, room ?? await DigRoomAsync($"{prefix}Room"));

	/// <summary>As <see cref="PlayerAsync"/>, with the WIZARD flag set by God.</summary>
	public async Task<TestIsolationHelpers.TestPlayer> WizardAsync(string prefix, DBRef? room = null)
	{
		var wizard = await PlayerAsync(prefix, room);
		await factory.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		return wizard;
	}

	/// <summary>Runs <paramref name="command"/> as <paramref name="player"/> and waits for what it queued.</summary>
	public async Task RunAsync(TestIsolationHelpers.TestPlayer player, string command)
	{
		await factory.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await factory.QueueBarrierAsync();
	}

	/// <summary>Everything <paramref name="listener"/> was told while <paramref name="action"/> ran.</summary>
	public async Task<List<string>> HeardWhile(TestIsolationHelpers.TestPlayer listener, Func<Task> action)
	{
		var before = factory.Notifications.CountFor(listener.DbRef);
		await action();
		return [.. factory.Notifications.For(listener.DbRef).Skip(before)];
	}

	/// <summary>Everything <paramref name="player"/> was told while running <paramref name="command"/>.</summary>
	public Task<List<string>> HeardRunning(TestIsolationHelpers.TestPlayer player, string command)
		=> HeardWhile(player, () => RunAsync(player, command));
}
