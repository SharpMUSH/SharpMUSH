using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Sitelock options checked once the player is known, PennMUSH <c>connect_player</c>
/// (<c>src/player.c:264-321</c>): <c>!god</c>/<c>!wizard</c>/<c>!admin</c> refuse that class of
/// player, and <c>suspect</c> sets the player SUSPECT.
/// </summary>
public class SitelockLoginTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IOptionsWrapper<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
	private readonly List<long> _handles = [];

	private async Task<long> HandleFromAsync(string host)
	{
		var handle = TestIsolationHelpers.GenerateUniqueHandle();
		_handles.Add(handle);
		await ConnectionService.Register(handle, "192.0.2.10", host, "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => System.Text.Encoding.UTF8);
		return handle;
	}

	[After(Test)]
	public async Task DisconnectHandles()
	{
		foreach (var handle in _handles)
			await ConnectionService.Disconnect(handle);
	}

	private async Task<(string Name, AnySharpObject Player)> PlayerAsync(string prefix, string password, string? flag = null)
	{
		var home = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var dbref = await Mediator.Send(new CreatePlayerCommand(name, password, home, home, (int)Configuration.CurrentValue.Limit.StartingQuota));
		var player = (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();
		if (flag is not null)
		{
			await Mediator.Send(new SetObjectFlagCommand(player, (await Mediator.Send(new GetObjectFlagQuery(flag)))!));
		}

		return (name, player);
	}

	private static IDisposable Rule(string host, params string[] options)
		=> TestOptionsOverride.Scope(current => current with
		{
			SitelockRules = new SitelockRulesOptions(new Dictionary<string, string[]>(current.SitelockRules.Rules) { [host] = options })
		});

	private Task Connect(long handle, string name, string password)
		=> WebAppFactoryArg.CommandParser.CommandParse(handle, ConnectionService, MarkupText.Plain($"connect {name} {password}")).AsTask();

	[Test]
	public async ValueTask NoWizardRefusesAWizardButNotAMortal()
	{
		var host = $"{TestIsolationHelpers.GenerateUniqueName("nowiz")}.test".ToLowerInvariant();
		var (wizardName, wizard) = await PlayerAsync("SitelockWiz", "wizard-password-1", "WIZARD");
		var (mortalName, mortal) = await PlayerAsync("SitelockMortal", "mortal-password-1");
		using var rule = Rule(host, "!wizard");

		var wizardHandle = await HandleFromAsync(host);
		await Connect(wizardHandle, wizardName, "wizard-password-1");

		await Assert.That(ConnectionService.Get(wizardHandle)?.Ref).IsNull();
		await Assert.That(WebAppFactoryArg.Notifications.ForHandle(wizardHandle)).Contains("Access from your location is restricted.");

		var mortalHandle = await HandleFromAsync(host);
		await Connect(mortalHandle, mortalName, "mortal-password-1");

		await Assert.That(ConnectionService.Get(mortalHandle)?.Ref?.Number).IsEqualTo(mortal.Object().DBRef.Number);
		await Assert.That(wizard.Object().DBRef.Number).IsNotEqualTo(mortal.Object().DBRef.Number);
	}

	[Test]
	public async ValueTask SuspectSiteSetsThePlayerSuspect()
	{
		var host = $"{TestIsolationHelpers.GenerateUniqueName("suspect")}.test".ToLowerInvariant();
		var (name, player) = await PlayerAsync("SitelockSuspect", "suspect-password-1");
		using var rule = Rule(host, "suspect");

		var handle = await HandleFromAsync(host);
		await Connect(handle, name, "suspect-password-1");

		await Assert.That(ConnectionService.Get(handle)?.Ref?.Number).IsEqualTo(player.Object().DBRef.Number);
		var reloaded = (await Mediator.Send(new GetObjectNodeQuery(player.Object().DBRef))).Expect<AnySharpObject>();
		await Assert.That(await reloaded.HasFlag("SUSPECT")).IsTrue();
	}
}
