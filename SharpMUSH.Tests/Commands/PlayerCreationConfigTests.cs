using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
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
	private readonly List<long> _handles = [];

	private long AllocateHandle()
	{
		var handle = TestIsolationHelpers.GenerateUniqueHandle();
		_handles.Add(handle);
		return handle;
	}

	[After(Test)]
	public async Task DisconnectHandles()
	{
		foreach (var handle in _handles)
			await ConnectionService.Disconnect(handle);
	}


	[Test]
	public async ValueTask Register_WhenPlayerCreationDisabled_Refuses()
	{
		// Point register_create_file at a nonexistent path so the hardcoded fallback message
		// (rather than any register.txt that happens to resolve on disk) is what gets exercised.
		var missingRegisterFile = $"{Guid.NewGuid()}.nonexistent.txt";
		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Net = options.Net with { PlayerCreation = false },
			Message = options.Message with { RegisterCreateFile = missingRegisterFile }
		});
		var handle = AllocateHandle();
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"register {TestIsolationHelpers.GenerateUniqueName("RegisterBlocked")} somepassword"));

		await NotifyService.Received(1).Notify(
			Arg.Is<long>(h => h == handle),
			Arg.Is<OneOf<MString, string>>(s =>
				TestHelpers.MessagePlainTextEquals(s, "Player creation is disabled on this server.")),
			null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Register_WhenPlayerCreationDisabled_ShowsConfiguredRegisterFile()
	{
		var registerFilePath = Path.Combine(Path.GetTempPath(), $"register-{Guid.NewGuid()}.txt");
		const string registerFileContents = "Custom registration message from register_create_file.";
		await File.WriteAllTextAsync(registerFilePath, registerFileContents);

		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Net = options.Net with { PlayerCreation = false },
			Message = options.Message with { RegisterCreateFile = registerFilePath }
		});
		try
		{
			var handle = AllocateHandle();
			await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"register {TestIsolationHelpers.GenerateUniqueName("RegisterBlocked")} somepassword"));

			await NotifyService.Received(1).Notify(
				Arg.Is<long>(h => h == handle),
				Arg.Is<OneOf<MString, string>>(s =>
					TestHelpers.MessagePlainTextEquals(s, registerFileContents)),
				null, INotifyService.NotificationType.Announce);
		}
		finally
		{
			File.Delete(registerFilePath);
		}
	}

	[Test]
	public async ValueTask MakeCharacter_WhenPlayerCreationDisabled_Refuses()
	{
		var handle = AllocateHandle();
		// Get the connection into AccountMode the same way a successful `register` does
		// internally (CreateAccountAsync + BindAccount), rather than driving it through the
		// `register` socket command itself: that command's own arg-splitting only ever
		// populates a single positional argument for space-separated NoParse commands
		// (pre-existing behavior of REGISTER/LOGIN/MAKE, unrelated to this task), so
		// "register name password" cannot reach AccountMode via CommandParse in this harness.
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		var accountResult = await AccountService.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("MakeBlocked"), null, "somepassword");
		await ConnectionService.BindAccount(handle, accountResult.AsT0.Id!);

		var missingRegisterFile = $"{Guid.NewGuid()}.nonexistent.txt";
		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Net = options.Net with { PlayerCreation = false },
			Message = options.Message with { RegisterCreateFile = missingRegisterFile }
		});
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"make {TestIsolationHelpers.GenerateUniqueName("MakeCharacter")} somepassword"));

		await NotifyService.Received(1).Notify(
			Arg.Is<long>(h => h == handle),
			Arg.Is<OneOf<MString, string>>(s =>
				TestHelpers.MessagePlainTextEquals(s, "Player creation is disabled on this server.")),
			null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask MakeCharacter_WhenPlayerCreationDisabled_ShowsConfiguredRegisterFile()
	{
		var registerFilePath = Path.Combine(Path.GetTempPath(), $"register-{Guid.NewGuid()}.txt");
		const string registerFileContents = "Custom registration message from register_create_file.";
		await File.WriteAllTextAsync(registerFilePath, registerFileContents);

		var handle = AllocateHandle();
		// Get the connection into AccountMode the same way a successful `register` does
		// internally (CreateAccountAsync + BindAccount) — see the comment in
		// MakeCharacter_WhenPlayerCreationDisabled_Refuses for why `register` itself can't
		// drive this through CommandParse in this harness.
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		var accountResult = await AccountService.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("MakeFile"), null, "somepassword");
		await ConnectionService.BindAccount(handle, accountResult.AsT0.Id!);

		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Net = options.Net with { PlayerCreation = false },
			Message = options.Message with { RegisterCreateFile = registerFilePath }
		});
		try
		{
			await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"make {TestIsolationHelpers.GenerateUniqueName("MakeCharacter")} somepassword"));

			await NotifyService.Received(1).Notify(
				Arg.Is<long>(h => h == handle),
				Arg.Is<OneOf<MString, string>>(s =>
					TestHelpers.MessagePlainTextEquals(s, registerFileContents)),
				null, INotifyService.NotificationType.Announce);
		}
		finally
		{
			File.Delete(registerFilePath);
		}
	}
}
