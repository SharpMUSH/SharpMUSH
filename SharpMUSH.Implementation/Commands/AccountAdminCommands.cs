using System.Globalization;
using MarkupString.Layout;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// Wizard-only web-account administration.
	/// <para>Syntax:
	/// <c>@account &lt;name&gt;</c> — show account details;
	/// <c>@account/list [pattern]</c>;
	/// <c>@account/newpassword &lt;name&gt;=&lt;password&gt;</c> — set + force change on next login;
	/// <c>@account/disable &lt;name&gt;</c> / <c>@account/enable &lt;name&gt;</c>;
	/// <c>@account/close &lt;name&gt;</c> / <c>@account/delete &lt;name&gt;</c> — the account record is retained either way;
	/// <c>@account/link &lt;name&gt;=&lt;player&gt;</c> / <c>@account/unlink &lt;name&gt;=&lt;player&gt;</c> — attach a character to the account or take it off.</para>
	/// <para><c>@account/claim &lt;character&gt;=&lt;password&gt;</c> is for anyone playing a character on an account: it
	/// links another character to that account, as the account menu's <c>claim</c> does.</para>
	/// </summary>
	[SharpCommand(Name = "@ACCOUNT", Switches = ["LIST", "NEWPASSWORD", "DISABLE", "ENABLE", "CLOSE", "DELETE", "LINK", "UNLINK", "CLAIM"],
		Behavior = CommandBehavior.Default | CommandBehavior.EqSplit | CommandBehavior.RSNoParse,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["name", "value"])]
	public async ValueTask<Option<CallState>> AccountAdmin(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;
		var arg0 = args.TryGetValue("0", out var a0) ? a0.Message.ToPlainText()?.Trim() : null;
		var arg1 = args.TryGetValue("1", out var a1) ? a1.Message.ToPlainText() : null;

		if (switches.Contains("CLAIM"))
		{
			return await ClaimForOwnAccountAsync(executor, arg0, arg1);
		}

		// Everything but /claim administers other people's accounts.
		if (!await executor.IsWizard())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (switches.Contains("LIST"))
		{
			var accounts = await AccountService.GetAllAccountsAsync();
			var filtered = string.IsNullOrWhiteSpace(arg0)
				? accounts
				: accounts.Where(a => a.Username.Contains(arg0, StringComparison.OrdinalIgnoreCase)).ToList();
			if (filtered.Count == 0)
			{
				await NotifyService.Notify(executor, "No matching accounts.");
				return CallState.Empty;
			}

			var grid = new Grid([.. filtered.Select(AccountEntry)]) { Gap = 3 };
			var panel = filtered.Any(a => a.MustChangePassword)
				? ServerLayout.Panel(MarkupText.Plain("Accounts"), grid, ServerLayout.Body(MarkupText.Plain("* must change password at next login")))
				: ServerLayout.Panel(MarkupText.Plain("Accounts"), grid);
			await NotifyService.Notify(executor, ServerLayout.Build(panel, 78));
			return CallState.Empty;
		}

		if (string.IsNullOrWhiteSpace(arg0))
		{
			await NotifyService.Notify(executor, "Usage: @account[/list|/newpassword|/disable|/enable|/close|/delete] <name>[=<password>], or @account/link|/unlink <name>=<player>");
			return CallState.Empty;
		}

		var account = await AccountService.GetByUsernameAsync(arg0);
		if (account is null)
		{
			await NotifyService.Notify(executor, $"No account named '{arg0}'.");
			return CallState.Empty;
		}

		if (switches.Contains("NEWPASSWORD"))
		{
			if (string.IsNullOrWhiteSpace(arg1))
			{
				await NotifyService.Notify(executor, "Usage: @account/newpassword <name>=<password>");
				return CallState.Empty;
			}

			if (arg1.Length < 8)
			{
				await NotifyService.Notify(executor, "Password must be at least 8 characters.");
				return CallState.Empty;
			}

			var result = await AccountService.SetPasswordAsync(account.Id!, arg1, mustChangePassword: true);
			if (result is Success)
			{
				await AccountSessionStore.RevokeAllForAccountAsync(account.Id!);
				await Audit.RecordAsync(executor, AuditActions.AccountPassword, AuditTargets.Of(account));
			}
			await NotifyService.Notify(executor, result switch
			{
				Success => $"Password for account '{account.Username}' set; active sessions revoked. They must change it at next login.",
				Error<string> err => err.Value
			});
			return CallState.Empty;
		}

		if (switches.Contains("DISABLE"))
		{
			var result = await AccountService.DisableAccountAsync(account.Id!);
			if (result is Success)
				await Audit.RecordAsync(executor, AuditActions.AccountStatus, AuditTargets.Of(account), nameof(AccountStatus.Disabled));
			await NotifyService.Notify(executor, result switch
			{
				Success => $"Account '{account.Username}' disabled; active sessions revoked.",
				Error<string> err => err.Value
			});
			return CallState.Empty;
		}

		if (switches.Contains("ENABLE"))
		{
			var result = await AccountService.EnableAccountAsync(account.Id!);
			if (result is Success)
				await Audit.RecordAsync(executor, AuditActions.AccountStatus, AuditTargets.Of(account), nameof(AccountStatus.Active));
			await NotifyService.Notify(executor, result switch
			{
				Success => $"Account '{account.Username}' enabled.",
				Error<string> err => err.Value
			});
			return CallState.Empty;
		}

		if (switches.Contains("CLOSE"))
		{
			var result = await AccountService.CloseAccountAsync(account.Id!);
			if (result is Error<string> err)
			{
				await NotifyService.Notify(executor, err.Value);
			}
			else
			{
				await Audit.RecordAsync(executor, AuditActions.AccountStatus, AuditTargets.Of(account), nameof(AccountStatus.Closed));
				await NotifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.AccountClosedFormat), executor, account.Username);
			}

			return CallState.Empty;
		}

		if (switches.Contains("DELETE"))
		{
			var result = await AccountService.MarkAccountDeletedAsync(account.Id!);
			if (result is Error<string> err)
			{
				await NotifyService.Notify(executor, err.Value);
			}
			else
			{
				await Audit.RecordAsync(executor, AuditActions.AccountStatus, AuditTargets.Of(account), nameof(AccountStatus.Deleted));
				await NotifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.AccountMarkedDeletedFormat), executor, account.Username);
			}

			return CallState.Empty;
		}

		if (switches.Contains("LINK") || switches.Contains("UNLINK"))
		{
			var unlink = switches.Contains("UNLINK");
			if (string.IsNullOrWhiteSpace(arg1))
			{
				await NotifyService.Notify(executor, $"Usage: @account/{(unlink ? "unlink" : "link")} <name>=<player>");
				return CallState.Empty;
			}

			return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, arg1.Trim()) switch
			{
				AnySharpObject and SharpPlayer player when unlink => await UnlinkFromAccountAsync(executor, account, player),
				AnySharpObject and SharpPlayer player => await LinkToAccountAsync(executor, account, player),
				AnySharpObject => throw new InvalidOperationException("A player lookup found something that is not a player."),
				Error<CallState> error => error.Value
			};
		}

		// No switch: show details.
		await NotifyService.Notify(executor, await DescribeAccountAsync(account));
		return CallState.Empty;
	}

	/// <summary>An account's email, status and characters, as a panel titled with its name.</summary>
	private async ValueTask<MString> DescribeAccountAsync(SharpAccount account)
	{
		var characters = await AccountService.GetCharactersAsync(account.Id!);
		var details = ServerLayout.KeyValues([
			("Email", MarkupText.Plain(account.Email ?? "(none)")),
			("Status", MarkupText.Plain(StatusLabel(account.Status) + (account.MustChangePassword ? ", must change password" : string.Empty))),
			("Characters", MarkupText.Plain(characters.Count.ToString(CultureInfo.InvariantCulture)))]);
		Block characterList = characters.Count == 0
			? new TextBlock(MarkupText.Plain("No characters."))
			: ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Character")) { Min = 10 },
					new TableColumn(MarkupText.Plain("Dbref")) { Wrap = false },
				],
				characters.Select(character => (IEnumerable<string>)[character.Object.Name, $"#{character.Object.Key}"]));
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"Account {account.Username}"),
			details, new Rule(), characterList), 78);
	}

	/// <summary>
	/// <c>@account/claim</c>: links <paramref name="name"/> to the account the executor's own character is on, proven
	/// by that character's password or by the password of the account that holds it now.
	/// </summary>
	private async ValueTask<Option<CallState>> ClaimForOwnAccountAsync(AnySharpObject executor, string? name, string? password)
	{
		if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(password))
		{
			await NotifyService.Notify(executor, "Usage: @account/claim <character>=<password>");
			return CallState.Empty;
		}

		if (executor is not SharpPlayer player
			|| await AccountService.GetAccountForCharacterAsync(player.Object.DBRef) is not { } account)
		{
			await NotifyService.Notify(executor, "You are not playing a character on an account. Log in to one, or ask staff to link you.");
			return CallState.Empty;
		}

		return await AccountService.ClaimCharacterAsync(account.Id!, name, password) switch
		{
			SharpPlayer claimed => await ClaimedForOwnAccountAsync(executor, account, claimed),
			LinkedElsewhere => await NotifiedAsync(executor, "That character is on another account. Give that account's password to move it here."),
			NotFound => await NotifiedAsync(executor, "No character has that name and password."),
		};
	}

	private async ValueTask<Option<CallState>> ClaimedForOwnAccountAsync(AnySharpObject executor, SharpAccount account, SharpPlayer claimed)
	{
		await NotifyService.Notify(executor, $"{claimed.Object.Name} is now linked to your account '{account.Username}'.");
		return new CallState(claimed.Object.DBRef);
	}

	private async ValueTask<Option<CallState>> NotifiedAsync(AnySharpObject executor, string message)
	{
		await NotifyService.Notify(executor, message);
		return CallState.Empty;
	}

	/// <summary>
	/// <c>@account/link</c>: attaches a character to an account without its password. Only God links God, and
	/// only a wizard links a wizard, since the account takes on the character's roles.
	/// </summary>
	private async ValueTask<Option<CallState>> LinkToAccountAsync(AnySharpObject executor, SharpAccount account, SharpPlayer player)
	{
		if (await OutranksAsync(player, executor))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		return await AccountService.AttachCharacterAsync(account.Id!, player) switch
		{
			SharpPlayer => await LinkedToAccountAsync(executor, account, player),
			LinkedElsewhere elsewhere => await RefusedLinkAsync(executor, player, elsewhere.Account),
		};
	}

	private async ValueTask<Option<CallState>> LinkedToAccountAsync(AnySharpObject executor, SharpAccount account, SharpPlayer player)
	{
		await Audit.RecordAsync(executor, AuditActions.CharacterLink, AuditTargets.Of(player), account.Username);
		await NotifyService.Notify(executor, $"{player.Object.Name} is now linked to account '{account.Username}'.");
		return new CallState(player.Object.DBRef);
	}

	private async ValueTask<Option<CallState>> RefusedLinkAsync(AnySharpObject executor, SharpPlayer player, SharpAccount holder)
	{
		await NotifyService.Notify(executor,
			$"{player.Object.Name} is linked to account '{holder.Username}'. " +
			$"Use @account/unlink {holder.Username}={player.Object.Name} first.");
		return CallState.Empty;
	}

	/// <summary><c>@account/unlink</c>: takes a character off an account, which keeps the character.</summary>
	private async ValueTask<Option<CallState>> UnlinkFromAccountAsync(AnySharpObject executor, SharpAccount account, SharpPlayer player)
	{
		if (await AccountService.GetAccountForCharacterAsync(player.Object.DBRef) is not { } holder || holder.Id != account.Id)
		{
			await NotifyService.Notify(executor, $"{player.Object.Name} is not linked to account '{account.Username}'.");
			return CallState.Empty;
		}

		if (await AccountService.UnlinkCharacterAsync(account.Id!, player.Object.DBRef) is Error<string> refused)
		{
			await NotifyService.Notify(executor, refused.Value);
			return CallState.Empty;
		}

		await Audit.RecordAsync(executor, AuditActions.CharacterUnlink, AuditTargets.Of(player), account.Username);
		await NotifyService.Notify(executor, $"{player.Object.Name} is no longer linked to account '{account.Username}'.");
		return new CallState(player.Object.DBRef);
	}

	/// <summary>True when <paramref name="player"/> stands above <paramref name="executor"/>: God above everyone else, a wizard above non-wizards.</summary>
	private static async ValueTask<bool> OutranksAsync(SharpPlayer player, AnySharpObject executor)
	{
		AnySharpObject target = player;
		return target.IsGod() ? !executor.IsGod() : await target.IsWizard() && !await executor.IsWizard();
	}

	/// <summary>One <c>@account/list</c> entry: the account's name, a <c>*</c> when it must change its password, and its state.</summary>
	private MarkupText AccountEntry(SharpAccount account) =>
		MarkupText.Plain($"{account.Username}{(account.MustChangePassword ? "*" : string.Empty)} ({StatusLabel(account.Status)})");

	private string StatusLabel(AccountStatus status) => status switch
	{
		AccountStatus.Active => "active",
		_ => status.ToString().ToUpperInvariant()
	};
}
