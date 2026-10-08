using MarkupString.Ansi;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Commands;

public class PlayerCreationConfigTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

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


	/// <summary>
	/// The register message is one record in the shared world, so the tests that change it, and the ones that read
	/// it, take turns.
	/// </summary>
	private const string RegisterMessage = "GameMessage.Register";

	private IGameMessageService Messages => WebAppFactoryArg.Services.GetRequiredService<IGameMessageService>();

	private string[] HeardBy(long handle) => [.. WebAppFactoryArg.Notifications.ForHandle(handle)];

	private async ValueTask<long> AccountModeHandleAsync(string prefix)
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
		var accountResult = await AccountService.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName(prefix), null, "somepassword");
		await ConnectionService.BindAccount(handle, accountResult.Expect<SharpAccount>().Id!);
		return handle;
	}

	/// <summary>Runs <paramref name="body"/> with the register message set to <paramref name="text"/>, then puts it back.</summary>
	private async ValueTask WithRegisterMessageAsync(string? text, Func<ValueTask> body)
	{
		var before = await Messages.IsDefaultAsync(GameMessage.Register) ? null : await Messages.GetTextAsync(GameMessage.Register);
		await Messages.SetTextAsync(GameMessage.Register, text);
		try
		{
			await body();
		}
		finally
		{
			await Messages.SetTextAsync(GameMessage.Register, before);
		}
	}

	[Test]
	[NotInParallel(RegisterMessage)]
	public async ValueTask Register_WhenPlayerCreationDisabled_ShowsTheShippedRegisterMessage()
	{
		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Net = options.Net with { PlayerCreation = false }
		});
		var handle = AllocateHandle();
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"register {TestIsolationHelpers.GenerateUniqueName("RegisterBlocked")} somepassword"));

		await Assert.That(HeardBy(handle)).Contains(AnsiEscapeParser.Parse(Messages.ShippedText(GameMessage.Register)).ToPlainText());
	}

	[Test]
	[NotInParallel(RegisterMessage)]
	public async ValueTask Register_WhenPlayerCreationDisabled_ShowsTheEditedRegisterMessage()
		=> await WithRegisterMessageAsync("Custom registration message.", async () =>
		{
			using var configuration = TestOptionsOverride.Scope(options => options with
			{
				Net = options.Net with { PlayerCreation = false }
			});
			var handle = AllocateHandle();
			await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"register {TestIsolationHelpers.GenerateUniqueName("RegisterBlocked")} somepassword"));

			await Assert.That(HeardBy(handle)).Contains("Custom registration message.");
		});

	[Test]
	[NotInParallel(RegisterMessage)]
	public async ValueTask MakeCharacter_WhenPlayerCreationDisabled_RefusesWithTheFixedLineWhenTheMessageIsEmpty()
		=> await WithRegisterMessageAsync("", async () =>
		{
			var handle = await AccountModeHandleAsync("MakeBlocked");
			using var configuration = TestOptionsOverride.Scope(options => options with
			{
				Net = options.Net with { PlayerCreation = false }
			});
			await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"make {TestIsolationHelpers.GenerateUniqueName("MakeCharacter")} somepassword"));

			await Assert.That(HeardBy(handle)).Contains("Player creation is disabled on this server.");
		});

	[Test]
	[NotInParallel(RegisterMessage)]
	public async ValueTask MakeCharacter_WhenPlayerCreationDisabled_ShowsTheEditedRegisterMessage()
		=> await WithRegisterMessageAsync("Custom registration message.", async () =>
		{
			var handle = await AccountModeHandleAsync("MakeFile");
			using var configuration = TestOptionsOverride.Scope(options => options with
			{
				Net = options.Net with { PlayerCreation = false }
			});
			await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"make {TestIsolationHelpers.GenerateUniqueName("MakeCharacter")} somepassword"));

			await Assert.That(HeardBy(handle)).Contains("Custom registration message.");
		});
}
