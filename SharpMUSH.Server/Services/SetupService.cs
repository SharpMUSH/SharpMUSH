using OneOf;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// First-run setup: while ServerState.SetupCompleted is false, the game is unclaimed and
/// the web wizard may claim it — first visitor wins. Claiming renames the pre-generated
/// #1-linked admin account, sets its password, and flips SetupCompleted.
/// </summary>
public class SetupService(ISharpDatabase database, IAccountService accountService)
{
	private readonly SemaphoreSlim _claimLock = new(1, 1);

	public async ValueTask<bool> NeedsSetupAsync(CancellationToken ct = default)
		=> !(await database.GetServerStateAsync(ct)).SetupCompleted;

	public async ValueTask<OneOf<Success, Error<string>>> CompleteAsync(string username, string password, CancellationToken ct = default)
	{
		await _claimLock.WaitAsync(ct);
		try
		{
			if ((await database.GetServerStateAsync(ct)).SetupCompleted)
				return new Error<string>("Setup has already been completed.");

			var account = await accountService.GetAccountForCharacterAsync(new DBRef(1), ct);
			if (account is null)
			{
				// Edge case: bootstrap never ran or the link was removed — create and link.
				account = await accountService.CreateUnclaimedAccountAsync(username, ct);
				await accountService.LinkCharacterAsync(account.Id!, new DBRef(1), ct);
			}
			else if (!string.Equals(account.Username, username, StringComparison.Ordinal))
			{
				var rename = await accountService.ChangeUsernameAsync(account.Id!, username, ct);
				if (rename.IsT1)
					return rename.AsT1; // username taken — claim NOT consumed
			}

			var setPassword = await accountService.SetPasswordAsync(account.Id!, password, mustChangePassword: false, ct);
			if (setPassword.IsT1)
				return setPassword.AsT1;

			await database.SetServerSetupCompletedAsync(true, ct);
			return new Success();
		}
		finally
		{
			_claimLock.Release();
		}
	}
}
