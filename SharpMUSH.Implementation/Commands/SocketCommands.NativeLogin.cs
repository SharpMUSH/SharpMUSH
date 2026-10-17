using System.Globalization;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private async ValueTask<Option<CallState>> HandleNativeTokenLogin(IMUSHCodeParser parser, long handle,
		string secret, string ipAddress, string hostName)
	{
		var transport = ConnectionService.Get(handle);
		SharpPlayer? selected = null;
		SharpPlayer? committed = null;
		var policyNotified = false;
		try
		{
			var result = await NativeLogins.RedeemAsync(secret, ipAddress, handle.ToString(CultureInfo.InvariantCulture), async character =>
			{
				if (await Mediator.Send(new GetObjectNodeQuery(character)) is not (AnySharpObject and SharpPlayer player)) return false;
				if (SitelockMatcher.IsBlocked(Configuration.CurrentValue.SitelockRules.Rules, ipAddress, hostName, SitelockMatcher.ConnectFlag)) return false;
				if (await AccountRefusalAsync(character) is { } refusal)
				{
					await NotifyService.Notify(handle, refusal);
					policyNotified = true;
					return false;
				}
				if (!Configuration.CurrentValue.Net.Logins && !await new AnySharpObject(player).IsWizard())
				{
					await NotifyLoginsDisabledAsync(handle, new AnySharpObject(player));
					policyNotified = true;
					return false;
				}
				selected = player;
				return true;
			}, async (character, validateFinalAuthority) => await ConnectionService.BindAuthorizedAsync(handle, character,
				async () => transport is not null && ReferenceEquals(transport.Metadata, ConnectionService.Get(handle)?.Metadata)
					&& await validateFinalAuthority()), sourceHost: hostName);

			if (result is bool bound && bound && selected is { } loggedIn) committed = loggedIn;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception error)
		{
			// Authorization or backchannel errors may carry credential text. Keep only the exception type.
			Logger.LogWarning("Native login failed for handle {Handle}: {ErrorType}", handle, error.GetType().Name);
		}

		if (committed is { } player)
		{
			// Binding consumed the credential and committed authorization. Completion failures cannot turn it into a failed login.
			try { await CompletePlayerLoginAsync(parser, handle, player, player.Object.DBRef); }
			catch (Exception error)
			{
				Logger.LogWarning("Native login completion failed for handle {Handle}: {ErrorType}", handle, error.GetType().Name);
			}
			Logger.LogInformation("Native login succeeded for player #{Key} from {IP}", player.Object.Key, ipAddress);
			return new CallState(player.Object.DBRef);
		}

		await TriggerLoginFailAsync(handle, ipAddress, "invalid or expired native login token", "#-1", "token");
		if (!policyNotified)
			await NotifyService.Notify(handle, "Invalid or expired login token. Generate a new one in the portal.");
		return new CallState(ErrorMessages.Returns.InvalidPassword);
	}
}
