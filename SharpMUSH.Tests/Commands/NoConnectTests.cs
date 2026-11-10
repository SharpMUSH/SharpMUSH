using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>protection_flags</c>: RhostMUSH's NOCONNECT keeps a player from connecting, by
/// <c>connect</c> and by an account's <c>play</c>.
/// </summary>
public class NoConnectTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IOptionsWrapper<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
	private readonly List<long> _handles = [];

	private async Task<long> AllocateHandleAsync()
	{
		var handle = TestIsolationHelpers.GenerateUniqueHandle();
		_handles.Add(handle);
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => System.Text.Encoding.UTF8);
		return handle;
	}

	[After(Test)]
	public async Task DisconnectHandles()
	{
		foreach (var handle in _handles)
			await ConnectionService.Disconnect(handle);
	}

	private async Task<(string Name, DBRef Id)> NoConnectPlayerAsync(string password)
	{
		if (await Mediator.Send(new GetObjectFlagQuery("NOCONNECT")) is null)
		{
			await Mediator.Send(new CreateObjectFlagCommand("NOCONNECT", null, "", false, ["wizard"], ["wizard"], ["PLAYER"]));
		}

		var home = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var name = TestIsolationHelpers.GenerateUniqueName("Barred");
		var id = await Mediator.Send(new CreatePlayerCommand(name, password, home, home, (int)Configuration.CurrentValue.Limit.StartingQuota));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set #{id.Number}=NOCONNECT"));
		return (name, id);
	}

	private static IDisposable Protecting(bool on)
		=> TestOptionsOverride.Scope(options => options with { Compatibility = options.Compatibility with { ProtectionFlags = on } });

	[Test]
	public async ValueTask ConnectRefusesANoConnectPlayer()
	{
		var (name, id) = await NoConnectPlayerAsync("barred-password-1");

		using (Protecting(true))
		{
			var refused = await AllocateHandleAsync();
			await Parser.CommandParse(refused, ConnectionService, MarkupText.Plain($"connect {name} barred-password-1"));
			await Assert.That(ConnectionService.Get(refused)?.Ref).IsNull();
			await Assert.That(WebAppFactoryArg.Notifications.ForHandle(refused)
				.Any(m => m.Contains("That character may not connect.", StringComparison.Ordinal))).IsTrue();
		}

		using (Protecting(false))
		{
			var allowed = await AllocateHandleAsync();
			await Parser.CommandParse(allowed, ConnectionService, MarkupText.Plain($"connect {name} barred-password-1"));
			await Assert.That(ConnectionService.Get(allowed)?.Ref?.Number).IsEqualTo(id.Number);
		}
	}

	[Test]
	public async ValueTask PlayRefusesANoConnectCharacter()
	{
		var accounts = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		var account = (await accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("BarredAcct"), null, "account-password-1"))
			.Expect<SharpAccount>();
		var (name, id) = await NoConnectPlayerAsync("barred-password-2");
		await accounts.LinkCharacterAsync(account.Id!, id);

		using (Protecting(true))
		{
			var handle = await AllocateHandleAsync();
			await ConnectionService.BindAccount(handle, account.Id!);
			await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"play {name}"));
			await Assert.That(ConnectionService.Get(handle)?.Ref).IsNull();
			await Assert.That(WebAppFactoryArg.Notifications.ForHandle(handle)
				.Any(m => m.Contains("That character may not connect.", StringComparison.Ordinal))).IsTrue();
		}
	}
}
