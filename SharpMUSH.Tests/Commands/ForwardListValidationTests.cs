using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH validates FORWARDLIST, DEBUGFORWARDLIST and MAILFORWARDLIST inside <c>do_set_atr</c>, before
/// <c>atr_add</c> (<c>src/attrib.c:2326-2358</c>), and refuses the whole set when any entry is not an
/// objid, does not name a live object, or names one unwilling to hear from the object being set.
/// SharpMUSH stored anything and only reported the problem at delivery time (#1218).
/// <para>
/// Every setter here is a mortal unless the test is about privilege: the shared fixture's handle 1 is
/// God, who controls everything, so a list set as God would pass <c>Can_MailForward</c> vacuously. The
/// expected texts were captured from a live PennMUSH (tools/oracle) on 2026-09-22.
/// </para>
/// </summary>
public class ForwardListValidationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task Run(TestIsolationHelpers.TestPlayer who, string command)
		=> await WebAppFactoryArg.CommandParserFor(who.DbRef, who.Handle)
			.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command));

	/// <summary>
	/// Everything <paramref name="who"/> was told while <paramref name="action"/> ran. Reads the
	/// recipient-keyed recorder rather than the session-shared NSubstitute call list, so a concurrent
	/// test's notifications are invisible here.
	/// </summary>
	private async Task<List<string>> Heard(TestIsolationHelpers.TestPlayer who, Func<Task> action)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who.DbRef);
		await action();
		return [.. WebAppFactoryArg.Notifications.For(who.DbRef).Skip(before)];
	}

	/// <summary>Reads as God, so the assertion never depends on the mortal's own read permission.</summary>
	private async Task<string> Get(DBRef who, string attribute)
		=> (await GodParser.FunctionParse(MarkupText.Plain($"[get(#{who.Number}/{attribute})]")))!.Message!.ToPlainText();

	/// <summary>
	/// <c>is_objid(curr)</c> is the first test of each word (<c>src/attrib.c:2331</c>). Captured:
	/// <c>&amp;MAILFORWARDLIST me=LB309 garbage</c> -&gt; <c>MAILFORWARDLIST should contain only dbrefs.</c>
	/// </summary>
	[Test]
	public async ValueTask AnEntryThatIsNotADbref_IsRefused()
	{
		var owner = await Player("FlNotDbref");
		var other = await Player("FlNotDbrefTo");

		var heard = await Heard(owner, () => Run(owner, $"&MAILFORWARDLIST me={other.Name} garbage"));

		await Assert.That(heard).Contains(m => m.Contains(
			string.Format(ErrorMessages.Notifications.ForwardListRequiresDbrefsFormat, "MAILFORWARDLIST")));
		await Assert.That(await Get(owner.DbRef, "MAILFORWARDLIST")).IsEqualTo(string.Empty)
			.Because("a refused forward list must not be stored at all");
	}

	/// <summary>
	/// <c>parse_objid</c> answers NOTHING for an objid whose creation stamp does not match, and Penn
	/// prints that answer. Captured: <c>&amp;MAILFORWARDLIST me=#4:123</c> -&gt;
	/// <c>Invalid dbref #-1 in MAILFORWARDLIST.</c>
	/// </summary>
	[Test]
	public async ValueTask AnObjidWithTheWrongCreationStamp_IsRefused()
	{
		var owner = await Player("FlStaleObjid");
		var other = await Player("FlStaleObjidTo");

		var heard = await Heard(owner,
			() => Run(owner, $"&MAILFORWARDLIST me=#{other.DbRef.Number}:123"));

		await Assert.That(heard).Contains(m => m.Contains(
			string.Format(ErrorMessages.Notifications.ForwardListInvalidDbrefFormat, -1, "MAILFORWARDLIST")));
		await Assert.That(await Get(owner.DbRef, "MAILFORWARDLIST")).IsEqualTo(string.Empty);
	}

	/// <summary>
	/// <c>Can_MailForward(thing, fwd)</c> with no mailforward lock SET on the target. The "is set" term
	/// (<c>hdrs/mushdb.h:130</c>) is what stops this passing: an unset lock evaluates true, so its
	/// verdict alone would let anyone fill another player's mailbox. Captured:
	/// <c>I don't think #4 wants LA127's mail.</c>
	/// </summary>
	[Test]
	public async ValueTask AStrangerWithNoMailforwardLock_IsRefused()
	{
		var owner = await Player("FlStranger");
		var other = await Player("FlStrangerTo");

		var heard = await Heard(owner, () => Run(owner, $"&MAILFORWARDLIST me=#{other.DbRef.Number}"));

		await Assert.That(heard).Contains(m => m.Contains(string.Format(
			ErrorMessages.Notifications.ForwardListTargetRefusesMailFormat, other.DbRef.Number, owner.Name)));
		await Assert.That(await Get(owner.DbRef, "MAILFORWARDLIST")).IsEqualTo(string.Empty);
	}

	/// <summary>
	/// The case that distinguishes "a lock is set and the setter passes it" from "no lock is set, so the
	/// evaluation is vacuously true": the same pair as above, once the target has actually set a
	/// mailforward lock the owner passes.
	/// </summary>
	[Test]
	public async ValueTask ATargetThatSetAPassingMailforwardLock_IsStored()
	{
		var owner = await Player("FlLockOk");
		var target = await Player("FlLockOkTo");

		await Run(target, $"@lock/mailforward me=#{owner.DbRef.Number}");
		await Run(owner, $"&MAILFORWARDLIST me=#{target.DbRef.Number}");

		await Assert.That(await Get(owner.DbRef, "MAILFORWARDLIST")).IsEqualTo($"#{target.DbRef.Number}");
	}

	/// <summary>Every word is validated, and a list of willing targets is stored whole.</summary>
	[Test]
	public async ValueTask AMultiEntryListOfWillingTargets_IsStored()
	{
		var owner = await Player("FlMulti");
		var first = await Player("FlMultiA");
		var second = await Player("FlMultiB");

		await Run(first, $"@lock/mailforward me=#{owner.DbRef.Number}");
		await Run(second, $"@lock/mailforward me=#{owner.DbRef.Number}");

		var list = $"#{first.DbRef.Number} #{second.DbRef.Number}";
		await Run(owner, $"&MAILFORWARDLIST me={list}");

		await Assert.That(await Get(owner.DbRef, "MAILFORWARDLIST")).IsEqualTo(list);
	}

	/// <summary>
	/// <c>s &amp;&amp; *s</c> (<c>src/attrib.c:2326</c>): the whole block is skipped for an empty value,
	/// so clearing a list is always allowed - including one that would no longer validate.
	/// </summary>
	[Test]
	public async ValueTask ClearingTheList_IsAllowed()
	{
		var owner = await Player("FlClear");
		var target = await Player("FlClearTo");

		await Run(target, $"@lock/mailforward me=#{owner.DbRef.Number}");
		await Run(owner, $"&MAILFORWARDLIST me=#{target.DbRef.Number}");
		await Assert.That(await Get(owner.DbRef, "MAILFORWARDLIST")).IsEqualTo($"#{target.DbRef.Number}")
			.Because("the precondition must hold before clearing means anything");

		await Run(owner, "&MAILFORWARDLIST me=");

		await Assert.That(await Get(owner.DbRef, "MAILFORWARDLIST")).IsEqualTo(string.Empty);
	}

	/// <summary>
	/// The subject of <c>Can_MailForward</c> is <c>thing</c>, the object being set - so a wizard is
	/// allowed by <c>controls(thing, fwd)</c> only when the wizard is the one whose list it is. The
	/// same pair in the other direction is the refusal above.
	/// </summary>
	[Test]
	public async ValueTask AControllerIsAllowedWhereAStrangerIsRefused()
	{
		var wizard = await Player("FlWizOwner");
		var mortal = await Player("FlWizOther");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));

		await Run(mortal, $"&MAILFORWARDLIST me=#{wizard.DbRef.Number}");
		await Assert.That(await Get(mortal.DbRef, "MAILFORWARDLIST")).IsEqualTo(string.Empty)
			.Because("a mortal does not control the wizard and the wizard has set no mailforward lock");

		await Run(wizard, $"&MAILFORWARDLIST me=#{mortal.DbRef.Number}");
		await Assert.That(await Get(wizard.DbRef, "MAILFORWARDLIST")).IsEqualTo($"#{mortal.DbRef.Number}")
			.Because("a wizard controls the target, which is what Can_MailForward asks about");
	}

	/// <summary>
	/// <c>FORWARDLIST</c> and <c>DEBUGFORWARDLIST</c> ask <c>Can_Forward</c> instead, and Penn words the
	/// refusal differently: "wants to hear from", not "wants ...'s mail". A forward lock the setter
	/// passes allows it, the same way the mailforward lock does.
	/// </summary>
	[Test]
	public async ValueTask ForwardlistUsesTheSpeechWordingAndTheForwardLock()
	{
		var owner = await Player("FlSpeech");
		var target = await Player("FlSpeechTo");

		var heard = await Heard(owner, () => Run(owner, $"&FORWARDLIST me=#{target.DbRef.Number}"));

		await Assert.That(heard).Contains(m => m.Contains(string.Format(
			ErrorMessages.Notifications.ForwardListTargetRefusesSpeechFormat, target.DbRef.Number, owner.Name)));
		await Assert.That(await Get(owner.DbRef, "FORWARDLIST")).IsEqualTo(string.Empty);

		await Run(target, $"@lock/forward me=#{owner.DbRef.Number}");
		await Run(owner, $"&FORWARDLIST me=#{target.DbRef.Number}");

		await Assert.That(await Get(owner.DbRef, "FORWARDLIST")).IsEqualTo($"#{target.DbRef.Number}")
			.Because("a forward lock that is set and passes is what Can_Forward's third term allows");
	}
}
