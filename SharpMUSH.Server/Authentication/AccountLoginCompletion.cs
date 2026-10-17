using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Server.Authentication;

/// <summary>Shared login policy and session/roster issuance for every account credential.</summary>
public sealed class AccountLoginCompletion(IAccountService accounts, IAccountSessionStore sessions,
 AccountClaimsService claims, IOptionsWrapper<SharpMUSHOptions> options, SitelockGuard sitelocks,
 IPortalThemeService themes, AccountCredentialGate gate)
{
	public Task<IActionResult> CompleteAsync(HttpContext context, SharpAccount account, bool remember,
	 int? preferredKey = null, long? preferredCreationTime = null, CancellationToken ct = default)
	 => gate.RunAsync<IActionResult>(async () =>
	 {
		 if (await accounts.GetByIdAsync(account.Id!, ct) is not { IsActive: true } current || current.CredentialEpoch != account.CredentialEpoch)
			 return new UnauthorizedObjectResult("Account not found or not active.");
		 var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		 if (sitelocks.IsBlocked(ip, "", SitelockGuard.Connect))
			 return new ObjectResult("Access from your location is restricted.") { StatusCode = 403 };
		 var characters = await accounts.GetCharactersAsync(current.Id!, ct);
		 if (!options.CurrentValue.Net.Logins && !await characters.ToAsyncEnumerable().AnyAsync(async (character, token) => await new AnySharpObject(character).IsWizard(token), ct))
			 return new ObjectResult("Logins are disabled.") { StatusCode = 403 };
		 var primary = characters.FirstOrDefault(c => c.Object.Key == preferredKey && c.Object.CreationTime == preferredCreationTime)
		 ?? ActingCharacterResolver.Primary(characters);
		 var summaries = await CharacterSummaryMapper.BuildSummariesAsync(characters,
		 actingKey: primary?.Object.Key, actingCreationTime: primary?.Object.CreationTime, themes: themes);
		 var role = await claims.ComputeAccountRoleAsync(current.Id!);
		 var permissions = await claims.ComputeGrantedScopesAsync(current.Id!);
		 var token = await sessions.CreateTokenAsync(current.Id!, TimeSpan.FromMinutes(15), ip, primary?.Object.Key, primary?.Object.CreationTime);
		 if (remember)
			 RememberedLoginCookie.Write(context.Response, await sessions.CreateRememberedLoginAsync(current.Id!, RememberedLoginCookie.Lifetime, ip));
		 return new OkObjectResult(new AuthController.AccountLoginResponse(current.Id!, current.Username, summaries, token,
		 current.MustChangePassword, role.ToString(), permissions.ToList()));
	 }, ct);
}
