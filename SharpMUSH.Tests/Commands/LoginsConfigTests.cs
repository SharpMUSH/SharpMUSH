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
/// (a WIZARD-flagged character) to connect — matching the
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
	public async ValueTask Connect_WhenLoginsDisabled_NonStaffRefused_StaffAllowed()
	{
		var defaultHome = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;
		var plebName = TestIsolationHelpers.GenerateUniqueName("LoginsPleb");
		await Mediator.Send(new CreatePlayerCommand(plebName, "pleb-password-1", defaultHome, defaultHome, startingQuota));
		var staffName = TestIsolationHelpers.GenerateUniqueName("LoginsStaff");
		var staffId = await Mediator.Send(new CreatePlayerCommand(staffName, "staff-password-1", defaultHome, defaultHome, startingQuota));
		var staff = (await Mediator.Send(new GetObjectNodeQuery(staffId))).Known;
		var wizard = await Mediator.Send(new GetObjectFlagQuery("WIZARD"));
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(staff, wizard!))).IsTrue();

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
		await Assert.That((result.Message?.ToString() ?? "").Contains("#-1")).IsFalse();
		await Assert.That(ConnectionService.Get(staffHandle)?.Ref?.Number).IsEqualTo(staffId.Number);

		// Guest login also refused.
		var guestHandle = await AllocateHandleAsync();
		await Parser.CommandParse(guestHandle, ConnectionService, MarkupText.Plain("connect guest"));
		await NotifyService.Received(1).Notify(
			Arg.Is<long>(h => h == guestHandle),
			Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextEquals(s, "Logins are disabled.")),
			null, INotifyService.NotificationType.Announce);
	}
}
