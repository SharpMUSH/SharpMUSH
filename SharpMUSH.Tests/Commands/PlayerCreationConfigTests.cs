using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Commands;

public class PlayerCreationConfigTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IAccountService AccountService => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test, NotInParallel("ConfigMutation")]
	public async ValueTask Register_WhenPlayerCreationDisabled_Refuses()
	{
		var options = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var original = options.CurrentValue;
		var restricted = original with { Net = original.Net with { PlayerCreation = false } };
		options.CurrentValue.Returns(restricted);
		try
		{
			var handle = 2001L;
			await Parser.CommandParse(handle, ConnectionService, MModule.single("register nocreate-user somepassword"));

			await NotifyService.Received(1).Notify(
				Arg.Is<long>(h => h == handle),
				Arg.Is<OneOf<MString, string>>(s =>
					TestHelpers.MessagePlainTextEquals(s, "Player creation is disabled on this server.")),
				null, INotifyService.NotificationType.Announce);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	[Test, NotInParallel("ConfigMutation")]
	public async ValueTask MakeCharacter_WhenPlayerCreationDisabled_Refuses()
	{
		var options = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var original = options.CurrentValue;

		var handle = 2002L;
		// Get the connection into AccountMode the same way a successful `register` does
		// internally (CreateAccountAsync + BindAccount), rather than driving it through the
		// `register` socket command itself: that command's own arg-splitting only ever
		// populates a single positional argument for space-separated NoParse commands
		// (pre-existing behavior of REGISTER/LOGIN/MAKE, unrelated to this task), so
		// "register name password" cannot reach AccountMode via CommandParse in this harness.
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		var accountResult = await AccountService.CreateAccountAsync("make-blocked-user", null, "somepassword");
		await ConnectionService.BindAccount(handle, accountResult.AsT0.Id!);

		var restricted = original with { Net = original.Net with { PlayerCreation = false } };
		options.CurrentValue.Returns(restricted);
		try
		{
			await Parser.CommandParse(handle, ConnectionService, MModule.single("make NewCharacter somepassword"));

			await NotifyService.Received(1).Notify(
				Arg.Is<long>(h => h == handle),
				Arg.Is<OneOf<MString, string>>(s =>
					TestHelpers.MessagePlainTextEquals(s, "Player creation is disabled on this server.")),
				null, INotifyService.NotificationType.Announce);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}
}
