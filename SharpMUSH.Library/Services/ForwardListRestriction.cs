using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// PennMUSH's forward-list validation (<c>do_set_atr</c>, <c>src/attrib.c:2326-2358</c>): every word of
/// a <c>FORWARDLIST</c>, <c>DEBUGFORWARDLIST</c> or <c>MAILFORWARDLIST</c> value must be an objid that
/// names a real object willing to hear from the object being set, or the whole set is refused.
/// </summary>
/// <remarks>
/// <para>
/// The subject of both willingness macros is <c>thing</c> - the object being SET - not the player doing
/// the setting. <c>Can_Forward(p,x)</c> is <c>controls(p,x) || Pemit_All(p) || (a Forward lock is SET
/// and p passes it)</c>; <c>Can_MailForward(p,x)</c> is <c>IsPlayer(x) &amp;&amp; (controls(p,x) ||
/// (a MailForward lock is SET and p passes it))</c> (<c>hdrs/mushdb.h:124-133</c>). "Is set" is load
/// bearing: an unset lock evaluates true, so its verdict alone would let anyone fill another player's
/// mailbox.
/// </para>
/// <para>
/// Separate from <see cref="AttributeValueRestriction"/>, which is a pure function of the entry and the
/// value; this needs the database and the permission service. Separate too from
/// <c>MailDelivery.MayForwardTo</c>, which re-checks the same rule per entry at send time and stays
/// where it is - that one is in SharpMUSH.Implementation, which SharpMUSH.Library cannot reference.
/// </para>
/// </remarks>
internal static class ForwardListRestriction
{
	/// <summary>
	/// <c>parse_objid</c> answers <c>NOTHING</c> for anything that does not name a live object - an
	/// out-of-range dbref and an objid whose creation stamp does not match alike - and Penn prints that
	/// answer, so the refusal reads <c>#-1</c> rather than echoing what was typed. Captured live on
	/// 2026-09-22: <c>&amp;MAILFORWARDLIST me=#4:123</c> -&gt; <c>Invalid dbref #-1 in MAILFORWARDLIST.</c>
	/// </summary>
	private const int Nothing = -1;

	/// <summary>Whether <paramref name="attributeName"/> is one of the three lists Penn validates.</summary>
	public static bool Applies(string attributeName)
		=> attributeName is "FORWARDLIST" or "DEBUGFORWARDLIST" or "MAILFORWARDLIST";

	/// <summary>
	/// <see cref="Success"/> if every entry of <paramref name="value"/> is acceptable, or the message
	/// Penn refuses it with. An empty value is always acceptable: <c>s &amp;&amp; *s</c>
	/// (<c>src/attrib.c:2326</c>) means clearing a list is never validated.
	/// </summary>
	public static async ValueTask<Result<Success>> CheckAsync(IMediator mediator,
		IPermissionService permissionService,
		AnySharpObject thing,
		string attributeName,
		string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return new Success();
		}

		var isMail = attributeName is "MAILFORWARDLIST";

		foreach (var entry in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			// is_objid: "^#\d+(:\d+)?$", which is exactly what DBRef parses.
			if (!DBRef.TryParse(entry, out var parsed) || parsed is not { } reference)
			{
				return new Error<string>(string.Format(
					ErrorMessages.Notifications.ForwardListRequiresDbrefsFormat, attributeName));
			}

			// GetObjectNodeQuery applies the objid creation-stamp check itself, so a stale objid comes
			// back as None here rather than as the object that reused the number.
			if (await mediator.Send(new GetObjectNodeQuery(reference)) is not AnySharpObject forward
					|| !forward.Object().DBRef.SameObjectAs(reference))
			{
				return new Error<string>(string.Format(
					ErrorMessages.Notifications.ForwardListInvalidDbrefFormat, Nothing, attributeName));
			}

			if (await MayForwardToAsync(permissionService, thing, forward, isMail))
			{
				continue;
			}

			return new Error<string>(string.Format(
				isMail
					? ErrorMessages.Notifications.ForwardListTargetRefusesMailFormat
					: ErrorMessages.Notifications.ForwardListTargetRefusesSpeechFormat,
				forward.Object().DBRef.Number, thing.Object().Name));
		}

		return new Success();
	}

	/// <summary><c>Can_MailForward(thing, forward)</c> or <c>Can_Forward(thing, forward)</c>.</summary>
	private static async ValueTask<bool> MayForwardToAsync(IPermissionService permissionService,
		AnySharpObject thing, AnySharpObject forward, bool isMail)
	{
		if (isMail && forward is not SharpPlayer)
		{
			return false;
		}

		if (await permissionService.Controls(thing, forward))
		{
			return true;
		}

		// Pemit_All(x) is `Wizard(x) || has_power_by_name(x, "PEMIT_ALL")` (hdrs/mushdb.h:56) - the
		// wizard term is not redundant with controls() above, which returns 0 for a God target before
		// it ever reaches its own Wizard(who) test.
		if (!isMail && (await thing.IsWizard() || await thing.HasPower("Pemit_All")))
		{
			return true;
		}

		var lockType = isMail ? LockType.MailForward : LockType.Forward;

		return forward.Object().Locks.ContainsKey(lockType.ToString())
			&& await permissionService.PassesLock(thing, forward, lockType);
	}
}
