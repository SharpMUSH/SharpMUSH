using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Verifies that <c>Net.Logins = false</c> refuses non-staff telnet connections
/// (<c>connect</c> character path, <c>connect guest</c>) while still allowing staff
/// (a WIZARD-flagged character, or a holder of the Login power) to connect — matching the
/// scoped configuration pattern used in <see cref="PlayerCreationConfigTests"/>.
/// </summary>
public class LoginsConfigTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
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


	[Test]
	public async ValueTask Connect_WhenLoginsDisabled_NonStaffRefused_StaffAndLoginPowerAllowed()
	{
		var defaultHome = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;
		var plebName = TestIsolationHelpers.GenerateUniqueName("LoginsPleb");
		await Mediator.Send(new CreatePlayerCommand(plebName, "pleb-password-1", defaultHome, defaultHome, startingQuota));
		var staffName = TestIsolationHelpers.GenerateUniqueName("LoginsStaff");
		var staffId = await Mediator.Send(new CreatePlayerCommand(staffName, "staff-password-1", defaultHome, defaultHome, startingQuota));
		var staff = (await Mediator.Send(new GetObjectNodeQuery(staffId))).Expect<AnySharpObject>();
		var wizard = await Mediator.Send(new GetObjectFlagQuery("WIZARD"));
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(staff, wizard!))).IsTrue();
		var loginName = TestIsolationHelpers.GenerateUniqueName("LoginsPower");
		var loginId = await Mediator.Send(new CreatePlayerCommand(loginName, "login-password-1", defaultHome, defaultHome, startingQuota));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power #{loginId.Number}=Login"));

		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Net = options.Net with { Logins = false }
		});
		var plebHandle = await AllocateHandleAsync();
		await Parser.CommandParse(plebHandle, ConnectionService, MarkupText.Plain($"connect {plebName} pleb-password-1"));
		await NotifyService.Received(1).Notify(
			Arg.Is<long>(h => h == plebHandle),
			Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextEquals(s, "Logins are disabled.")),
			null, INotifyService.NotificationType.Announce);

		// A private wizard can still connect with logins disabled.
		var staffHandle = await AllocateHandleAsync();
		var result = await Parser.CommandParse(staffHandle, ConnectionService, MarkupText.Plain($"connect {staffName} staff-password-1"));
		await Assert.That((result.Message.ToString()).Contains("#-1")).IsFalse();
		await Assert.That(ConnectionService.Get(staffHandle)?.Ref?.Number).IsEqualTo(staffId.Number);

		// So can a holder of the Login power (PennMUSH's Can_Login).
		var loginHandle = await AllocateHandleAsync();
		await Parser.CommandParse(loginHandle, ConnectionService, MarkupText.Plain($"connect {loginName} login-password-1"));
		await Assert.That(ConnectionService.Get(loginHandle)?.Ref?.Number).IsEqualTo(loginId.Number);

		// Guest login also refused.
		var guestHandle = await AllocateHandleAsync();
		await Parser.CommandParse(guestHandle, ConnectionService, MarkupText.Plain("connect guest"));
		await NotifyService.Received(1).Notify(
			Arg.Is<long>(h => h == guestHandle),
			Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextEquals(s, "Logins are disabled.")),
			null, INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// An account reaches its menu while logins are off when one of its characters may log in, but PLAY asks it of
	/// the character played, as PennMUSH's <c>Can_Login</c> does for each player.
	/// </summary>
	[Test]
	public async ValueTask Play_WhenLoginsDisabled_AsksTheCharacterPlayed()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		var defaultHome = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;
		var account = (await accountService.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("LoginsAcct"), null, "account-password-1")).Expect<SharpAccount>();
		var altName = TestIsolationHelpers.GenerateUniqueName("LoginsAlt");
		var altId = await Mediator.Send(new CreatePlayerCommand(altName, "alt-password-1", defaultHome, defaultHome, startingQuota));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power #{altId.Number}=Login"));
		var mortalName = TestIsolationHelpers.GenerateUniqueName("LoginsMortal");
		var mortalId = await Mediator.Send(new CreatePlayerCommand(mortalName, "mortal-password-1", defaultHome, defaultHome, startingQuota));
		await accountService.LinkCharacterAsync(account.Id!, altId);
		await accountService.LinkCharacterAsync(account.Id!, mortalId);

		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Net = options.Net with { Logins = false }
		});

		var mortalHandle = await AllocateHandleAsync();
		await ConnectionService.BindAccount(mortalHandle, account.Id!);
		await Parser.CommandParse(mortalHandle, ConnectionService, MarkupText.Plain($"play {mortalName}"));
		await Assert.That(ConnectionService.Get(mortalHandle)?.Ref).IsNull();
		await Assert.That(WebAppFactoryArg.Notifications.ForHandle(mortalHandle).Any(m => m.Contains("Logins are disabled.", StringComparison.Ordinal))).IsTrue();

		var altHandle = await AllocateHandleAsync();
		await ConnectionService.BindAccount(altHandle, account.Id!);
		await Parser.CommandParse(altHandle, ConnectionService, MarkupText.Plain($"play {altName}"));
		await Assert.That(ConnectionService.Get(altHandle)?.Ref?.Number).IsEqualTo(altId.Number);
	}
}
