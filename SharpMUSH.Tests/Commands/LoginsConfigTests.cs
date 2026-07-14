using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Verifies that <c>Net.Logins = false</c> refuses non-staff telnet connections
/// (<c>connect</c> character path, <c>connect guest</c>) while still allowing staff
/// (character #1 / any WIZARD-flagged character) to connect — matching the
/// config-mutation pattern used in <see cref="PlayerCreationConfigTests"/>.
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

	[Test, NotInParallel("ConfigMutation")]
	public async ValueTask Connect_WhenLoginsDisabled_NonStaffRefused_StaffAllowed()
	{
		var defaultHome = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;
		await Mediator.Send(new CreatePlayerCommand("LoginsPleb", "pleb-password-1", defaultHome, defaultHome, startingQuota));

		var options = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var original = options.CurrentValue;
		options.CurrentValue.Returns(original with { Net = original.Net with { Logins = false } });
		try
		{
			var plebHandle = 3001L;
			await Parser.CommandParse(plebHandle, ConnectionService, MModule.single("connect LoginsPleb pleb-password-1"));
			await NotifyService.Received(1).Notify(
				Arg.Is<long>(h => h == plebHandle),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Logins are disabled.")),
				null, INotifyService.NotificationType.Announce);

			// Staff bypass: God (#1, empty hash) still connects with logins disabled.
			var godHandle = 3002L;
			var result = await Parser.CommandParse(godHandle, ConnectionService, MModule.single("connect God anything"));
			await Assert.That((result.Message?.ToString() ?? "").Contains("#-1")).IsFalse();

			// Guest login also refused.
			var guestHandle = 3003L;
			await Parser.CommandParse(guestHandle, ConnectionService, MModule.single("connect guest"));
			await NotifyService.Received(1).Notify(
				Arg.Is<long>(h => h == guestHandle),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Logins are disabled.")),
				null, INotifyService.NotificationType.Announce);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}
}
