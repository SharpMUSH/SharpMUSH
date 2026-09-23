using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH asks <c>ok_player_name(name, player, thing)</c> (<c>src/predicat.c:709</c>) wherever a
/// player gets a name: <c>create_player</c> from the connect screen with no one asking
/// (<c>src/player.c:404</c>), <c>do_pcreate</c> and <c>pcreate()</c> with the creator asking
/// (<c>src/player.c:342</c>), and a rename with the renamer asking about the player renamed
/// (<c>src/predicat.c:788</c>). A banned name (<c>@sitelock/name</c>) is refused unless the one
/// asking is a wizard or the player already has it, and no name may be taken twice.
/// </summary>
public class PlayerNameEnforcementTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IAccountService AccountService => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>Short enough for the test world's player_name_len of 15.</summary>
	private static string ShortName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..12];

	private static IDisposable Banning(string pattern)
		=> TestOptionsOverride.Scope(o => o with { BannedNames = new BannedNamesOptions([pattern]) });

	private async Task<bool> PlayerExists(string name)
		=> await Mediator.CreateStream(new GetPlayerQuery(name)).AnyAsync(p => p.Object.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

	/// <summary>A connection in the account menu, where <c>make</c> creates a character.</summary>
	private async Task<long> AccountMenuHandle()
	{
		var handle = TestIsolationHelpers.GenerateUniqueHandle();
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		var account = await AccountService.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("NameAcct"), null, "somepassword");
		await ConnectionService.BindAccount(handle, account.Expect<SharpAccount>().Id!);
		return handle;
	}

	private async Task<List<string>> MakeAs(long handle, string name)
	{
		var before = WebAppFactoryArg.Notifications.CountForHandle(handle);
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"make {name} somepassword"));
		return [.. WebAppFactoryArg.Notifications.ForHandle(handle).Skip(before)];
	}

	[Test]
	public async ValueTask Make_ABannedName_IsNotAllowed()
	{
		var name = ShortName("Zmb");
		var handle = await AccountMenuHandle();
		try
		{
			using var _ = Banning("Zmb*");
			var messages = await MakeAs(handle, name);

			await Assert.That(messages).Contains("That name is not allowed.");
			await Assert.That(await PlayerExists(name)).IsFalse();
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	/// <summary><c>make</c> created a second player under a name already taken; <c>create_player</c> refuses.</summary>
	[Test]
	public async ValueTask Make_ATakenName_IsRefused()
	{
		var name = ShortName("Zmt");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@pcreate {name}=somepassword"));
		var handle = await AccountMenuHandle();
		try
		{
			var messages = await MakeAs(handle, name);

			await Assert.That(messages).Contains("There is already a player with that name.");
			await Assert.That(await Mediator.CreateStream(new GetPlayerQuery(name)).CountAsync()).IsEqualTo(1);
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	/// <summary>A wizard creating a player is exempt from the ban (<c>Wizard(player)</c>).</summary>
	[Test]
	public async ValueTask Pcreate_ByAWizard_MayUseABannedName()
	{
		var name = ShortName("Zpw");
		using var _ = Banning("Zpw*");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@pcreate {name}=somepassword"));

		await Assert.That(await PlayerExists(name)).IsTrue();
	}

	[Test]
	public async ValueTask PcreateFunction_ATakenName_IsRefused()
	{
		var name = ShortName("Zpf");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@pcreate {name}=somepassword"));

		var result = await Parser.FunctionParse(MarkupText.Plain($"pcreate({name},otherpassword)"));

		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PlayerNameInUse);
		await Assert.That(await Mediator.CreateStream(new GetPlayerQuery(name)).CountAsync()).IsEqualTo(1);
	}

	/// <summary>A mortal cannot rename themselves to a banned name; a wizard may rename them to it.</summary>
	[Test]
	public async ValueTask Rename_ToABannedName_OnlyAWizardMay()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "N");
		var banned = ShortName("Zrn");
		using var _ = Banning("Zrn*");

		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@name me={banned}"));
		await Assert.That(await PlayerExists(banned)).IsFalse().Because("a mortal may not take a banned name");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@name {mortal.DbRef}={banned}"));
		await Assert.That(await PlayerExists(banned)).IsTrue().Because("a wizard renaming is exempt");
	}
}
