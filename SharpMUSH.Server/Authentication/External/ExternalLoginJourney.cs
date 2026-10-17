using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Authentication.External;

/// <summary>Server-only authorization-code journey. Provider responses become cookie-bound, single-use
/// handoffs; an account bearer is returned only by the same-origin completion POST.</summary>
public sealed class ExternalLoginJourney(ExternalLoginFlowStore flows, ExternalProviderCatalog providers,
 IMediator mediator, IAccountService accounts, IAccountSessionStore sessions, AccountRecentProofService proofs,
 AccountCredentialGate gate, AccountLoginCompletion login, IOptionsWrapper<SharpMUSHOptions> options,
 SitelockGuard sitelocks, TimeProvider time, IAccountProofAuthorityValidator authorityValidator) : IExternalAuthenticationCompletion
{
	public const string TransactionProperty = "sm.external.txn";
	public const string HandoffCookie = "__Secure-SharpMUSH.ExternalHandoff";
	public const string SignupCookie = "__Secure-SharpMUSH.ExternalSignup";
	public static string BindingCookie(string id) => "__Host-SharpMUSH.ExternalBinding." + id;
	private const string ApiPath = "/api/auth/external";
	private static CookieOptions Cookie(TimeSpan lifetime, bool crossSite = false, string path = ApiPath) => new()
	{ HttpOnly = true, Secure = true, SameSite = crossSite ? SameSiteMode.None : SameSiteMode.Strict, Path = path, MaxAge = lifetime, IsEssential = true };
	private static IActionResult Refused() => new BadRequestObjectResult("The sign-in request expired or could not be verified. Start again.");
	public static string? Bearer(HttpContext context)
	 => context.Request.Headers.Authorization.FirstOrDefault() is { } header && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
		? header[7..].Trim() : null;

	public Task<IActionResult> BeginAsync(HttpContext context, string providerId, string returnPath, bool remember,
	 ExternalFlowPurpose purpose, string? grant, AccountSecurityOperation? operation, string? identityId, CancellationToken ct)
	 => gate.RunAsync<IActionResult>(async () =>
	 {
		 if (!Enum.IsDefined(purpose)) return Refused();
		 if (await providers.EffectiveAsync(providerId, ct) is not EffectiveExternalProvider provider) return Refused();
		 var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		 if (sitelocks.IsBlocked(ip, "", SitelockGuard.Connect)) return new ObjectResult("Access from your location is restricted.") { StatusCode = 403 };
		 if (purpose == ExternalFlowPurpose.Login) return Begin(null, null, null);
		 var token = Bearer(context);
		 if (token is null || await sessions.InspectAsync(token, ct) is not IAccountSessionStore.SessionIdentity session
		 || await accounts.GetByIdAsync(session.AccountId, ct) is not { IsActive: true, MustChangePassword: false } account) return new UnauthorizedResult();
		 ExternalIdentityKey? expected = null;
		 if (purpose == ExternalFlowPurpose.Reauthenticate)
		 {
			 if (operation is null || !Enum.IsDefined(operation.Value)) return Refused();
			 var identities = await mediator.Send(new GetExternalIdentitiesQuery(account.Id!), ct);
			 expected = identities.FirstOrDefault(identity => identity.Id == identityId && identity.Key.Registration == provider.Settings.RegistrationId)?.Key;
			 if (expected is null) return Refused();
			 return Begin(account, token, expected);
		 }
		 return await proofs.WithProofAsync<AccountProofAuthority>(account.Id!, token, AccountSecurityOperation.LinkIdentity, grant,
						(_, authority) => Task.FromResult<Result<AccountProofAuthority>>(authority), ct) is AccountProofAuthority sourceAuthority
						? Begin(account, token, null, sourceAuthority) : Refused();

		 IActionResult Begin(SharpAccount? source, string? sourceToken, ExternalIdentityKey? identity, AccountProofAuthority? sourceAuthority = null)
		 {
			 return flows.Begin(providerId, provider.Generation, returnPath, remember, purpose, source?.Id,
			 sourceToken is null ? null : AccountSessionDigest.Compute(sourceToken), source?.CredentialEpoch ?? 0, operation, identity, sourceAuthority) switch
			 {
				 ExternalFlowStart started => Started(started),
				 _ => Refused()
			 };
		 }
		 IActionResult Started(ExternalFlowStart start)
		 {
			 context.Response.Cookies.Append(BindingCookie(start.TransactionId), start.BrowserSecret,
			 Cookie(ExternalLoginFlowStore.TransactionLifetime, crossSite: true, path: "/"));
			 return new OkObjectResult(new { start.TransactionId, ChallengePath = ApiPath + "/challenge" });
		 }
	 }, ct);

	public async Task<IActionResult> ChallengeAsync(HttpContext context, string transactionId, CancellationToken ct)
	{
		if (flows.ReadRemote(transactionId, context.Request.Cookies[BindingCookie(transactionId)]) is not ExternalFlowTransaction transaction
		 || await CurrentAsync(transaction, ct) is not EffectiveExternalProvider provider) return Refused();
		var properties = new AuthenticationProperties { RedirectUri = "/auth/external/complete" };
		properties.Items[TransactionProperty] = transaction.Id;
		if (transaction.Purpose == ExternalFlowPurpose.Reauthenticate) properties.Items["sm.external.reauth"] = "true";
		return new ChallengeResult(provider.Definition.Scheme, properties);
	}

	public Task FailAsync(HttpContext context, AuthenticationProperties? properties, CancellationToken cancellationToken)
			=> gate.RunAsync(() =>
			{
				if (properties is not null && properties.Items.TryGetValue(TransactionProperty, out var id) && id is not null
							&& flows.TakeRemote(id, context.Request.Cookies[BindingCookie(id)]) is ExternalFlowTransaction)
					ClearBinding(context, id);
				return Task.CompletedTask;
			}, cancellationToken);

	public Task CompleteAsync(HttpContext context, AuthenticationProperties properties, ExternalIdentityKey key, string? label, CancellationToken ct)
			=> gate.RunAsync(async () =>
			{
				if (!properties.Items.TryGetValue(TransactionProperty, out var id) || id is null
							|| flows.TakeRemote(id, context.Request.Cookies[BindingCookie(id)]) is not ExternalFlowTransaction transaction)
				{ context.Response.Redirect("/login?external=failed"); return; }
				if (await CurrentAsync(transaction, ct) is not EffectiveExternalProvider provider
							|| key.Registration != provider.Settings.RegistrationId
							|| transaction.Purpose == ExternalFlowPurpose.Reauthenticate && transaction.ExpectedIdentity != key)
				{ ClearBinding(context, id); context.Response.Redirect("/login?external=failed"); return; }
				if (transaction.Purpose == ExternalFlowPurpose.Login && await mediator.Send(new GetExternalIdentityQuery(key), ct) is ExternalIdentity identity)
				{
					if (await accounts.GetByIdAsync(identity.AccountId, ct) is not { IsActive: true } account)
					{ ClearBinding(context, id); context.Response.Redirect("/login?external=failed"); return; }
					transaction = transaction with { AccountId = account.Id, CredentialEpoch = account.CredentialEpoch, ExpectedIdentity = key, AuthenticatedIdentityId = identity.Id };
				}
				if (flows.HandOff(transaction, key, label) is not string cookie)
				{ ClearBinding(context, id); context.Response.Redirect("/login?external=failed"); return; }
				context.Response.Cookies.Append(HandoffCookie, cookie, Cookie(ExternalLoginFlowStore.HandoffLifetime));
				context.Response.Headers.CacheControl = "no-store";
				context.Response.Redirect("/auth/external/complete");
			}, ct);

	public Task<IActionResult> FinishAsync(HttpContext context, CancellationToken ct)
	 => gate.RunAsync<IActionResult>(async () =>
	 {
		 var held = flows.TakeHandoff(context.Request.Cookies[HandoffCookie], id => context.Request.Cookies[BindingCookie(id)]);
		 context.Response.Cookies.Delete(HandoffCookie, Cookie(TimeSpan.Zero));
		 if (held is not ExternalFlowHandoff handoff) return Refused();
		 var transaction = handoff.Transaction;
		 ClearBinding(context, transaction.Id);
		 if (await CurrentAsync(transaction, ct) is not EffectiveExternalProvider) return Refused();
		 if (transaction.Purpose == ExternalFlowPurpose.Login)
		 {
			 if (transaction.AccountId is not null && await mediator.Send(new GetExternalIdentityQuery(handoff.Key), ct) is ExternalIdentity identity)
			 {
				 if (await accounts.GetByIdAsync(identity.AccountId, ct) is not { IsActive: true } account) return new UnauthorizedResult();
				 await mediator.Send(new RecordExternalIdentityUseCommand(account.Id!, handoff.Key, time.GetUtcNow()), ct);
				 ClearBinding(context, transaction.Id);
				 return WithReturn(await login.CompleteAsync(context, account, transaction.Remember, ct: ct), transaction.ReturnPath);
			 }
			 if (!CanCreate(context)) return new ObjectResult("Account creation is disabled or restricted.") { StatusCode = 403 };
			 if (flows.BeginSignup(handoff) is not string pending) return Refused();
			 context.Response.Cookies.Append(SignupCookie, pending, Cookie(ExternalLoginFlowStore.SignupLifetime));
			 context.Response.Cookies.Append(BindingCookie(transaction.Id), context.Request.Cookies[BindingCookie(transaction.Id)]!, Cookie(ExternalLoginFlowStore.SignupLifetime, crossSite: true, path: "/"));
			 return new OkObjectResult(new { SignupRequired = true, transaction.ReturnPath });
		 }
		 if (!MatchingBearer(context, transaction)) return new UnauthorizedResult();
		 if (transaction.Purpose == ExternalFlowPurpose.Link)
			 return await mediator.Send(new AttachExternalIdentityCommand(transaction.AccountId!, handoff.Key, handoff.Label), ct) switch
			 {
				 ExternalIdentity => await Linked(),
				 Error<string> error => new ConflictObjectResult(error.Value)
			 };
		 async Task<IActionResult> Linked()
		 {
			 ClearBinding(context, transaction.Id);

			 return new OkObjectResult(new { Linked = true, SignedOut = false, transaction.ReturnPath });
		 }
		 if (transaction.Operation is not { } operation || Bearer(context) is not { } token) return Refused();
		 if (await mediator.Send(new GetExternalIdentityQuery(handoff.Key), ct) is not ExternalIdentity attached || attached.AccountId != transaction.AccountId) return Refused();
		 return await proofs.VerifiedAsync(transaction.AccountId!, token, operation, "external", attached.Id,
		 async token => await mediator.Send(new GetExternalIdentityQuery(handoff.Key), token) is ExternalIdentity identity && identity.AccountId == transaction.AccountId, ct, providerGeneration: transaction.Generation) switch
		 {
			 RecentAccountProof proof => FinishProof(proof),
			 _ => Refused()
		 };
		 IActionResult FinishProof(RecentAccountProof proof)
		 {
			 ClearBinding(context, transaction.Id);
			 return new OkObjectResult(new { proof.Grant, proof.ExpiresAt, Operation = operation, transaction.ReturnPath });
		 }
	 }, ct);

	public Task<IActionResult> SignupAsync(HttpContext context, string username, CancellationToken ct)
	 => gate.RunAsync<IActionResult>(async () =>
	 {
		 var cookie = context.Request.Cookies[SignupCookie];
		 if (flows.ReadSignup(cookie, id => context.Request.Cookies[BindingCookie(id)]) is not ExternalFlowHandoff handoff
		 || handoff.Transaction.Purpose != ExternalFlowPurpose.Login || await CurrentAsync(handoff.Transaction, ct) is not EffectiveExternalProvider) return Refused();
		 if (!CanCreate(context)) return new ObjectResult("Account creation is disabled or restricted.") { StatusCode = 403 };
		 return await mediator.Send(new ProvisionExternalAccountCommand(handoff.Key, username, handoff.Label), ct) switch
		 {
			 SharpAccount account => await Registered(account),
			 Error<string> error => new ConflictObjectResult(error.Value)
		 };
		 async Task<IActionResult> Registered(SharpAccount account)
		 {
			 flows.FinishSignup(cookie!);
			 context.Response.Cookies.Delete(SignupCookie, Cookie(TimeSpan.Zero));
			 ClearBinding(context, handoff.Transaction.Id);
			 return WithReturn(await login.CompleteAsync(context, account, handoff.Transaction.Remember, ct: ct), handoff.Transaction.ReturnPath);
		 }
	 }, ct);

	public void Cancel(HttpContext context)
	{
		if (context.Request.Cookies[SignupCookie] is { } cookie)
		{
			if (flows.ReadSignup(cookie, id => context.Request.Cookies[BindingCookie(id)]) is ExternalFlowHandoff pending)
			{ ClearBinding(context, pending.Transaction.Id); flows.FinishSignup(cookie); }
		}
		if (flows.TakeHandoff(context.Request.Cookies[HandoffCookie], id => context.Request.Cookies[BindingCookie(id)]) is ExternalFlowHandoff pendingHandoff)
			ClearBinding(context, pendingHandoff.Transaction.Id);
		context.Response.Cookies.Delete(SignupCookie, Cookie(TimeSpan.Zero));
		context.Response.Cookies.Delete(HandoffCookie, Cookie(TimeSpan.Zero));
	}
	private static void ClearBinding(HttpContext context, string id) => context.Response.Cookies.Delete(BindingCookie(id), Cookie(TimeSpan.Zero, crossSite: true, path: "/"));
	private static IActionResult WithReturn(IActionResult result, string returnPath)
			=> result is OkObjectResult { Value: SharpMUSH.Server.Controllers.AuthController.AccountLoginResponse login } ? new OkObjectResult(login with { ReturnPath = returnPath }) : result;
	private bool CanCreate(HttpContext context)
	{
		var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		return options.CurrentValue.Net.PlayerCreation && options.CurrentValue.Net.Logins && !sitelocks.IsBlocked(ip, "", SitelockGuard.Create)
		 && !sitelocks.IsBlocked(ip, "", SitelockGuard.Connect);
	}
	private static bool MatchingBearer(HttpContext context, ExternalFlowTransaction transaction)
	 => Bearer(context) is { } token && AccountSessionDigest.Compute(token) == transaction.SessionDigest;
	private async Task<EffectiveExternalProvider?> CurrentAsync(ExternalFlowTransaction transaction, CancellationToken ct)
	{
		if (await providers.EffectiveAsync(transaction.Provider, ct) is not EffectiveExternalProvider provider || provider.Generation != transaction.Generation) return null;
		if (transaction.Purpose == ExternalFlowPurpose.Login)
		{
			if (transaction.AccountId is null) return provider;
			if (transaction.ExpectedIdentity is null || transaction.AuthenticatedIdentityId is null
					|| await accounts.GetByIdAsync(transaction.AccountId, ct) is not { IsActive: true } known || known.CredentialEpoch != transaction.CredentialEpoch
					|| await mediator.Send(new GetExternalIdentityQuery(transaction.ExpectedIdentity), ct) is not ExternalIdentity identity
					|| identity.AccountId != transaction.AccountId || identity.Id != transaction.AuthenticatedIdentityId) return null;
			return provider;
		}
		if (transaction.Purpose == ExternalFlowPurpose.Link && (transaction.SourceAuthority is null
				|| !await authorityValidator.IsCurrentAsync(transaction.AccountId!, transaction.SourceAuthority, ct))) return null;
		if (transaction.AccountId is null || transaction.SessionDigest is null
		 || await sessions.InspectDigestAsync(transaction.SessionDigest, ct) is not IAccountSessionStore.SessionIdentity session || session.AccountId != transaction.AccountId
		 || await accounts.GetByIdAsync(transaction.AccountId, ct) is not { IsActive: true, MustChangePassword: false } account
		 || account.CredentialEpoch != transaction.CredentialEpoch) return null;
		return provider;
	}
}
