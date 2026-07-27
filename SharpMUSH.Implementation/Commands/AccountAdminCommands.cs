using OneOf.Types;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;

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
	/// <c>@account/close &lt;name&gt;</c> / <c>@account/delete &lt;name&gt;</c> — the account record is retained either way.</para>
	/// </summary>
	[SharpCommand(Name = "@ACCOUNT", Switches = ["LIST", "NEWPASSWORD", "DISABLE", "ENABLE", "CLOSE", "DELETE"],
		Behavior = CommandBehavior.Default | CommandBehavior.EqSplit | CommandBehavior.RSNoParse,
		CommandLock = "FLAG^WIZARD", MinArgs = 0, MaxArgs = 2, ParameterNames = ["name", "password"])]
	public static async ValueTask<Option<CallState>> AccountAdmin(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;
		var arg0 = args.TryGetValue("0", out var a0) ? a0.Message?.ToPlainText()?.Trim() : null;
		var arg1 = args.TryGetValue("1", out var a1) ? a1.Message?.ToPlainText() : null;

		if (switches.Contains("LIST"))
		{
			var accounts = await AccountService!.GetAllAccountsAsync();
			var filtered = string.IsNullOrWhiteSpace(arg0)
				? accounts
				: accounts.Where(a => a.Username.Contains(arg0, StringComparison.OrdinalIgnoreCase)).ToList();
			var lines = filtered.Select(a =>
				$"{a.Username,-30} {StatusLabel(a.Status),-10} {(a.MustChangePassword ? "must-change-pw" : string.Empty)}");
			await NotifyService!.Notify(executor,
				filtered.Count == 0 ? "No matching accounts." : string.Join("\n", lines));
			return CallState.Empty;
		}

		if (string.IsNullOrWhiteSpace(arg0))
		{
			await NotifyService!.Notify(executor, "Usage: @account[/list|/newpassword|/disable|/enable|/close|/delete] <name>[=<password>]");
			return CallState.Empty;
		}

		var account = await AccountService!.GetByUsernameAsync(arg0);
		if (account is null)
		{
			await NotifyService!.Notify(executor, $"No account named '{arg0}'.");
			return CallState.Empty;
		}

		if (switches.Contains("NEWPASSWORD"))
		{
			if (string.IsNullOrWhiteSpace(arg1))
			{
				await NotifyService!.Notify(executor, "Usage: @account/newpassword <name>=<password>");
				return CallState.Empty;
			}

			if (arg1.Length < 8)
			{
				await NotifyService!.Notify(executor, "Password must be at least 8 characters.");
				return CallState.Empty;
			}

			var result = await AccountService.SetPasswordAsync(account.Id!, arg1, mustChangePassword: true);
			if (result.IsT0)
			{
				await AccountSessionStore!.RevokeAllForAccountAsync(account.Id!);
			}
			await NotifyService!.Notify(executor, result.Match(
				_ => $"Password for account '{account.Username}' set; active sessions revoked. They must change it at next login.",
				err => err.Value));
			return CallState.Empty;
		}

		if (switches.Contains("DISABLE"))
		{
			var result = await AccountService.DisableAccountAsync(account.Id!);
			await NotifyService!.Notify(executor, result.Match(
				_ => $"Account '{account.Username}' disabled; active sessions revoked.",
				err => err.Value));
			return CallState.Empty;
		}

		if (switches.Contains("ENABLE"))
		{
			var result = await AccountService.EnableAccountAsync(account.Id!);
			await NotifyService!.Notify(executor, result.Match(
				_ => $"Account '{account.Username}' enabled.",
				err => err.Value));
			return CallState.Empty;
		}

		if (switches.Contains("CLOSE"))
		{
			var result = await AccountService.CloseAccountAsync(account.Id!);
			if (result.IsT0)
			{
				await NotifyService!.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.AccountClosedFormat), executor, account.Username);
			}
			else
			{
				await NotifyService!.Notify(executor, result.AsT1.Value);
			}

			return CallState.Empty;
		}

		if (switches.Contains("DELETE"))
		{
			var result = await AccountService.MarkAccountDeletedAsync(account.Id!);
			if (result.IsT0)
			{
				await NotifyService!.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.AccountMarkedDeletedFormat), executor, account.Username);
			}
			else
			{
				await NotifyService!.Notify(executor, result.AsT1.Value);
			}

			return CallState.Empty;
		}

		// No switch: show details.
		var characters = await AccountService.GetCharactersAsync(account.Id!);
		var charList = characters.Count == 0
			? "  (none)"
			: string.Join("\n", characters.Select(c => $"  {c.Object.Name} (#{c.Object.Key})"));
		await NotifyService!.Notify(executor,
			$"Account: {account.Username}\n" +
			$"Email: {account.Email ?? "(none)"}\n" +
			$"Status: {StatusLabel(account.Status)}{(account.MustChangePassword ? ", must change password" : string.Empty)}\n" +
			$"Characters:\n{charList}");
		return CallState.Empty;
	}

	private static string StatusLabel(AccountStatus status) => status switch
	{
		AccountStatus.Active => "active",
		_ => status.ToString().ToUpperInvariant()
	};
}
