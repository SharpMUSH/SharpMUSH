using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A character made from a site whose sitelock rule carries <c>suspect</c> is set SUSPECT, and wizards
/// hear of it (PennMUSH <c>src/player.c:562</c>, <c>src/bsd.c:5912</c>). #2103.
/// </summary>
public class SuspectSiteTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService Connections => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private static IDisposable Suspecting(string host)
		=> TestOptionsOverride.Scope(o => o with
		{
			SitelockRules = new SitelockRulesOptions(new Dictionary<string, string[]>(o.SitelockRules.Rules) { [host] = ["suspect"] })
		});

	private async Task<long> ConnectFromAsync(string host)
	{
		var handle = TestIsolationHelpers.GenerateUniqueHandle();
		await Connections.Register(handle, "192.0.2.1", host, "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return handle;
	}

	private async Task<bool> IsSuspect(DBRef player)
		=> await Mediator.Send(new GetObjectNodeQuery(player)) is AnySharpObject node && await node.HasFlag("SUSPECT");

	[Test]
	public async Task ACharacterMadeFromASuspectSiteIsSuspectAndWizardsHear()
	{
		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, Connections, "SuspWiz");
		await WebAppFactoryArg.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@set #{wizard.DbRef.Number}=WIZARD"));
		var host = TestIsolationHelpers.GenerateUniqueName("suspect") + ".example";
		var handle = await ConnectFromAsync(host);
		var account = await WebAppFactoryArg.Services.GetRequiredService<IAccountService>()
			.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("SuspAcct"), null, "somepassword");
		await Connections.BindAccount(handle, account.Expect<SharpAccount>().Id!);
		var name = $"Susp{Guid.NewGuid():N}"[..12];

		using (Suspecting(host))
		{
			await WebAppFactoryArg.CommandParser.CommandParse(handle, Connections, MarkupText.Plain($"make {name} somepassword"));
		}

		await Assert.That(Connections.Get(handle)?.Ref).IsNotNull();
		await Assert.That(await IsSuspect(Connections.Get(handle)!.Ref!.Value)).IsTrue();
		await Assert.That(WebAppFactoryArg.Notifications.ForHandle(wizard.Handle))
			.Contains($"GAME: Suspect {name} created.");
	}
}
