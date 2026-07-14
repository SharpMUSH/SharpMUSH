using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// Creates a new account and enters AccountMode.
	/// <para>Syntax: <c>register displayname [email] password</c></para>
	/// </summary>
	[SharpCommand(Name = "REGISTER", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 2, MaxArgs = 3, ParameterNames = [])]
	public static async ValueTask<Option<CallState>> Register(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var handle = parser.CurrentState.Handle!.Value;
		var state = ConnectionService!.Get(handle)?.State;

		if (state is IConnectionService.ConnectionState.LoggedIn)
		{
			await NotifyService!.Notify(handle, "You are already connected as a character.");
			return new None();
		}

		if (!Configuration!.CurrentValue.Net.PlayerCreation)
		{
			await NotifyPlayerCreationDisabledAsync(handle);
			return new None();
		}

		var rawArgs = parser.CurrentState.Arguments;
		string username, password;
		string? email = null;

		// The command framework places args in "0", "1", "2"
		var arg0 = rawArgs.TryGetValue("0", out var a0) ? a0.Message?.ToString()?.Trim() : null;
		var arg1 = rawArgs.TryGetValue("1", out var a1) ? a1.Message?.ToString()?.Trim() : null;
		var arg2 = rawArgs.TryGetValue("2", out var a2) ? a2.Message?.ToString()?.Trim() : null;

		if (arg2 is not null)
		{
			username = arg0 ?? string.Empty;
			email = arg1;
			password = arg2;
		}
		else if (arg1 is not null)
		{
			username = arg0 ?? string.Empty;
			password = arg1;
		}
		else
		{
			await NotifyService!.Notify(handle, "Usage: register <username> [email] <password>");
			return new None();
		}

		if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
		{
			await NotifyService!.Notify(handle, "Username and password cannot be empty.");
			return new None();
		}

		var result = await AccountService!.CreateAccountAsync(username, email, password);
		if (result.IsT1)
		{
			await NotifyService!.Notify(handle, result.AsT1.Value);
			return new None();
		}

		var account = result.AsT0;
		await ConnectionService.BindAccount(handle, account.Id!);

		await NotifyService!.Notify(handle,
			$"Account '{account.Username}' created successfully.\n" +
			"You have no characters yet.\n" +
			"Use: make <character-name> <password>    to create your first character.");
		return new CallState(account.Id!);
	}

	/// <summary>
	/// Authenticates to an existing account and enters AccountMode.
	/// <para>Syntax: <c>login displayname-or-email password</c></para>
	/// </summary>
	[SharpCommand(Name = "LOGIN", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 2, MaxArgs = 2, ParameterNames = ["identifier", "password"])]
	public static async ValueTask<Option<CallState>> Login(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var handle = parser.CurrentState.Handle!.Value;
		var state = ConnectionService!.Get(handle)?.State;

		if (state is IConnectionService.ConnectionState.LoggedIn)
		{
			await NotifyService!.Notify(handle, "You are already connected as a character.");
			return new None();
		}

		var identifier = parser.CurrentState.Arguments.TryGetValue("0", out var a0) ? a0.Message?.ToString()?.Trim() : null;
		var password = parser.CurrentState.Arguments.TryGetValue("1", out var a1) ? a1.Message?.ToString()?.Trim() : null;

		if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(password))
		{
			await NotifyService!.Notify(handle, "Usage: login <display-name-or-email> <password>");
			return new None();
		}

		var account = await AccountService!.AuthenticateAsync(identifier, password);
		if (account is null)
		{
			await NotifyService!.Notify(handle, "Invalid account name or password.");
			return new None();
		}

		if (!Configuration!.CurrentValue.Net.Logins)
		{
			var linked = await AccountService.GetCharactersAsync(account.Id!);
			if (!await AnyStaffCharacterAsync(linked))
			{
				await NotifyService!.Notify(handle, "Logins are disabled.");
				return new None();
			}
		}

		await ConnectionService.BindAccount(handle, account.Id!);

		var characters = await AccountService.GetCharactersAsync(account.Id!);
		if (characters.Count == 0)
		{
			await NotifyService!.Notify(handle,
				$"Logged in as {account.Username}. You have no characters yet.\n" +
				"Use: make <character-name> <password>    to create a character.");
		}
		else
		{
			var charList = string.Join("\n", characters.Select(c => $"  {c.Object.Name} (#{c.Object.Key})"));
			await NotifyService!.Notify(handle,
				$"Logged in as {account.Username}. Your characters:\n{charList}\n" +
				"Use: play <name>    to connect as a character\n" +
				"Use: make <name> <password>    to create a new character");
		}

		return new CallState(account.Id!);
	}

	/// <summary>
	/// Creates a new character and links it to the current account, then logs in.
	/// Only available in AccountMode.
	/// <para>Syntax: <c>make CharacterName Password</c></para>
	/// </summary>
	[SharpCommand(Name = "MAKE", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 2, MaxArgs = 2, ParameterNames = ["name", "password"])]
	public static async ValueTask<Option<CallState>> MakeCharacter(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var handle = parser.CurrentState.Handle!.Value;
		var connectionData = ConnectionService!.Get(handle);

		if (connectionData?.State != IConnectionService.ConnectionState.AccountMode)
		{
			await NotifyService!.Notify(handle,
				"You must be logged in to an account first. Use: login <display-name-or-email> <password>");
			return new None();
		}

		if (!Configuration!.CurrentValue.Net.PlayerCreation)
		{
			await NotifyPlayerCreationDisabledAsync(handle);
			return new None();
		}

		if (!connectionData.Metadata.TryGetValue("AccountId", out var accountId))
		{
			await NotifyService!.Notify(handle, "Session error: account ID not found.");
			return new None();
		}

		var charName = parser.CurrentState.Arguments.TryGetValue("0", out var a0) ? a0.Message?.ToString()?.Trim() : null;
		var charPassword = parser.CurrentState.Arguments.TryGetValue("1", out var a1) ? a1.Message?.ToString()?.Trim() : null;

		if (string.IsNullOrWhiteSpace(charName) || string.IsNullOrWhiteSpace(charPassword))
		{
			await NotifyService!.Notify(handle, "Usage: make <character-name> <password>");
			return new None();
		}

		// Create the MUSH character using the same path as @pcreate
		var defaultHome = Configuration!.CurrentValue.Database.DefaultHome;
		var defaultHomeDbref = new DBRef((int)defaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;

		var playerDbRef = await Mediator!.Send(new CreatePlayerCommand(charName, charPassword, defaultHomeDbref, defaultHomeDbref, startingQuota));

		await AccountService!.LinkCharacterAsync(accountId, playerDbRef);

		await ConnectionService.Bind(handle, playerDbRef);

		var playerNode = await Mediator.Send(new Library.Queries.Database.GetObjectNodeQuery(playerDbRef));
		if (!playerNode.IsPlayer)
		{
			await NotifyService!.Notify(handle, "Character creation succeeded but could not resolve player.");
			return new None();
		}
		var foundPlayer = playerNode.AsPlayer;

		await CompletePlayerLoginAsync(parser, handle, foundPlayer, playerDbRef);

		Logger?.LogInformation("Account {AccountId}: created character {Name} (#{Key}) via MAKE",
			accountId, charName, foundPlayer.Object.Key);

		return new CallState(playerDbRef);
	}

	/// <summary>
	/// Connects to an existing character linked to the current account.
	/// Only available in AccountMode.
	/// <para>Syntax: <c>play CharacterName</c></para>
	/// </summary>
	[SharpCommand(Name = "PLAY", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 1, MaxArgs = 1, ParameterNames = ["name"])]
	public static async ValueTask<Option<CallState>> PlayCharacter(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var handle = parser.CurrentState.Handle!.Value;
		var connectionData = ConnectionService!.Get(handle);

		if (connectionData?.State != IConnectionService.ConnectionState.AccountMode)
		{
			await NotifyService!.Notify(handle,
				"You must be logged in to an account first. Use: login <display-name-or-email> <password>");
			return new None();
		}

		if (!connectionData.Metadata.TryGetValue("AccountId", out var accountId))
		{
			await NotifyService!.Notify(handle, "Session error: account ID not found.");
			return new None();
		}

		var charName = parser.CurrentState.Arguments.TryGetValue("0", out var a0) ? a0.Message?.ToString()?.Trim() : null;
		if (string.IsNullOrWhiteSpace(charName))
		{
			await NotifyService!.Notify(handle, "Usage: play <character-name>");
			return new None();
		}

		var characters = await AccountService!.GetCharactersAsync(accountId);
		var character = characters.FirstOrDefault(c =>
			c.Object.Name.Equals(charName, StringComparison.OrdinalIgnoreCase));

		if (character is null)
		{
			await NotifyService!.Notify(handle,
				$"No character named '{charName}' is linked to your account.\n" +
				"Use: make <name> <password>    to create a new character");
			return new None();
		}

		var playerDbRef = new DBRef(character.Object.Key, character.Object.CreationTime);
		await ConnectionService.Bind(handle, playerDbRef);

		await CompletePlayerLoginAsync(parser, handle, character, playerDbRef);

		Logger?.LogInformation("Account {AccountId}: playing as {Name} (#{Key}) via PLAY",
			accountId, character.Object.Name, character.Object.Key);

		return new CallState(playerDbRef);
	}

	/// <summary>
	/// PennMUSH-style refusal for a disabled <c>Net.PlayerCreation</c>: prefer the configured
	/// <c>register_create_file</c> contents (same resolution as <see cref="Handlers.ConnectionStateEventHandler"/>'s
	/// <c>connect_file</c> handling) and fall back to the hardcoded message when it's unset/missing/empty.
	/// </summary>
	private static async ValueTask NotifyPlayerCreationDisabledAsync(long handle)
	{
		var registerFile = Configuration!.CurrentValue.Message.RegisterCreateFile;
		if (!string.IsNullOrEmpty(registerFile) && File.Exists(registerFile))
		{
			var registerText = await File.ReadAllTextAsync(registerFile);
			if (!string.IsNullOrWhiteSpace(registerText))
			{
				await NotifyService!.Notify(handle, registerText);
				return;
			}
		}

		await NotifyService!.Notify(handle, "Player creation is disabled on this server.");
	}

	/// <summary>
	/// PennMUSH semantics: an account qualifies for login while <c>Net.Logins</c> is disabled
	/// if ANY linked character is staff (character #1, or WIZARD-flagged). <c>IsWizard()</c>
	/// already covers character #1 (God), so a single async predicate suffices.
	/// </summary>
	private static async ValueTask<bool> AnyStaffCharacterAsync(IReadOnlyList<SharpPlayer> characters) =>
		await characters.ToAsyncEnumerable().AnyAsync(async (character, _) => await new AnySharpObject(character).IsWizard());
}
