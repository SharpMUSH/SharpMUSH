using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
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
	/// </summary>
	[SharpCommand(Name = "@ACCOUNT", Switches = ["LIST", "NEWPASSWORD", "DISABLE", "ENABLE", "CLOSE", "DELETE", "LINK", "UNLINK"],
		Behavior = CommandBehavior.Default | CommandBehavior.EqSplit | CommandBehavior.RSNoParse,
		CommandLock = "FLAG^WIZARD", MinArgs = 0, MaxArgs = 2, ParameterNames = ["name", "value"])]
	public async ValueTask<Option<CallState>> AccountAdmin(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;
		var arg0 = args.TryGetValue("0", out var a0) ? a0.Message?.ToPlainText()?.Trim() : null;
		var arg1 = args.TryGetValue("1", out var a1) ? a1.Message?.ToPlainText() : null;

		if (switches.Contains("LIST"))
		{
			var accounts = await AccountService.GetAllAccountsAsync();
			var filtered = string.IsNullOrWhiteSpace(arg0)
				? accounts
				: accounts.Where(a => a.Username.Contains(arg0, StringComparison.OrdinalIgnoreCase)).ToList();
			var lines = filtered.Select(a =>
				$"{a.Username,-30} {StatusLabel(a.Status),-10} {(a.MustChangePassword ? "must-change-pw" : string.Empty)}");
			await NotifyService.Notify(executor,
				filtered.Count == 0 ? "No matching accounts." : string.Join("\n", lines));
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
		var characters = await AccountService.GetCharactersAsync(account.Id!);
		var charList = characters.Count == 0
			? "  (none)"
			: string.Join("\n", characters.Select(c => $"  {c.Object.Name} (#{c.Object.Key})"));
		await NotifyService.Notify(executor,
			$"Account: {account.Username}\n" +
			$"Email: {account.Email ?? "(none)"}\n" +
			$"Status: {StatusLabel(account.Status)}{(account.MustChangePassword ? ", must change password" : string.Empty)}\n" +
			$"Characters:\n{charList}");
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

		await AccountService.UnlinkCharacterAsync(account.Id!, player.Object.DBRef);
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

	private string StatusLabel(AccountStatus status) => status switch
	{
		AccountStatus.Active => "active",
		_ => status.ToString().ToUpperInvariant()
	};
}
