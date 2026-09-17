using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Server.Services;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Server.Hubs;
using System.Security.Claims;

namespace SharpMUSH.Server.Authentication;

public static class CharacterClaimsExtensions
{
	/// <summary>Claimed full identity only; callers must validate its current account link before use.</summary>
	public static CapabilityActor? GetCapabilityActor(this ClaimsPrincipal user)
		=> user.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } account
			&& user.GetActingCharacter() is { IsObjid: true } character
				? new(account, character, character) : null;

	/// <summary>
	/// The character this request acts as, from the <c>character_dbref</c> claim, or
	/// <see langword="null"/> when the principal carries no character or an unparseable one. Not
	/// <see cref="ClaimTypes.NameIdentifier"/>, which carries the account id.
	/// </summary>
	/// <remarks>
	/// Returns a <see cref="DBRef"/> rather than the raw claim string so callers cannot compare or
	/// format two different spellings of the same character. Handlers emit the claim as an objid,
	/// so the creation time is normally present and <see cref="DBRef.Matches"/> will reject a
	/// recycled dbref; a bare <c>"#N"</c> claim still parses, with a null creation time.
	/// </remarks>
	public static DBRef? GetActingCharacter(this ClaimsPrincipal user)
		=> user.FindFirst(GameHub.CharacterDbrefClaim)?.Value is { } claim
			&& DBRef.TryParse(claim, out var dbref)
				? dbref
				: null;

	/// <summary>
	/// The player this request may act as: the claimed identity, re-checked against its current
	/// account link, or <see langword="null"/> when the principal carries no usable character.
	/// </summary>
	/// <remarks>
	/// This is the one rule. Four controllers used to carry their own copy, and they had drifted into
	/// three: <c>ObjectsController</c> validated the link, while the mail, gallery and guest-roster
	/// controllers trusted the <c>character_dbref</c> claim on its own. Under the
	/// <c>AccountSession</c> scheme the weak form is not exploitable — the handler re-derives the
	/// acting character from the account's live roster on every request — but it is the sort of
	/// difference that only holds while every scheme that can mint that claim happens to behave, and
	/// <c>GetCapabilityActor</c> plus <see cref="IVisibleWorldProjection.ResolveCharacterAsync"/>
	/// already say it properly: an objid, an active account, and a link that still exists.
	/// </remarks>
	public static async ValueTask<SharpPlayer?> ResolvePlayerAsync(
		this ClaimsPrincipal user, IVisibleWorldProjection projection, CancellationToken ct = default)
		=> user.GetCapabilityActor() is { } actor ? await projection.ResolveCharacterAsync(actor, ct) : null;

	/// <summary><see cref="ResolvePlayerAsync"/> for callers that want the object union.</summary>
	public static async ValueTask<AnySharpObject?> ResolveExecutorAsync(
		this ClaimsPrincipal user, IVisibleWorldProjection projection, CancellationToken ct = default)
		=> await user.ResolvePlayerAsync(projection, ct) is { } player ? (AnySharpObject)player : null;
}
