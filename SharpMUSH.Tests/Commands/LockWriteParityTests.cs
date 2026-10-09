using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using Mediator;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Extensions;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Commands;

public class LockWriteParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.CommandParser;

	private async Task<string> Create()
		=> (await Parser.CommandParse(1, Connections, MarkupText.Plain($"@create LockParity{Guid.NewGuid():N}"))).Message.ToPlainText();

	private async Task<string> Read(string expression)
		=> (await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	[Test]
	public async Task InvalidReplacementPreservesPreviousLock()
	{
		var target = await Create();
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lock {target}=#TRUE"));
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lock {target}=#TRUE&"));
		await Assert.That(await Read($"lock({target})")).IsEqualTo("#TRUE");
	}

	[Test]
	public async Task FlagChangesPersistAcrossExpressionReplacement()
	{
		var target = await Create();
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lock {target}=#TRUE"));
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lset {target}/Basic=visual"));
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lock {target}=#FALSE"));
		await Assert.That(await Read($"lockflags({target})")).IsEqualTo("vi");
	}

	[Test]
	public async Task EmptyKeyRemovesLock()
	{
		var target = await Create();
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lock {target}=#FALSE"));
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lock {target}="));
		await Assert.That(await Read($"lock({target})")).IsEqualTo("*UNLOCKED*");
		await Assert.That(await Read($"lockowner({target})")).IsEqualTo("#-1 NO SUCH LOCK");
	}

	[Test]
	public async Task MortalSetterIsCapturedAndCannotOverwriteWizardLock()
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, Connections, "LockMortal");
		var created = await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@create MortalLock{Guid.NewGuid():N}"));
		var target = created.Message.ToPlainText();
		await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@lock {target}=me"));
		await Assert.That(await Read($"lock({target})")).IsEqualTo($"#{player.DbRef.Number}");
		await Assert.That(await Read($"lockowner({target})")).IsEqualTo($"#{player.DbRef.Number}");
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lset {target}/Basic=wizard"));
		await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@lock {target}=#TRUE"));
		await Assert.That(await Read($"lock({target})")).IsEqualTo($"#{player.DbRef.Number}");
		await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@unlock {target}"));
		await Assert.That(await Read($"lock({target})")).IsEqualTo($"#{player.DbRef.Number}");
	}

	[Test]
	public async Task WizardSetterIsIndependentOfTargetOwner()
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, Connections, "LockOwner");
		var created = await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@create WizardLock{Guid.NewGuid():N}"));
		var target = created.Message.ToPlainText();
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lock {target}==me"));
		await Assert.That(await Read($"lock({target})")).IsEqualTo("=#1");
		await Assert.That(await Read($"lockowner({target})")).IsEqualTo("#1");
		await Assert.That(await Read($"elock({target},#{player.DbRef.Number})")).IsEqualTo("0");
		await Assert.That(await Read($"elock({target},#1)")).IsEqualTo("1");
	}

	[Test]
	public async Task AttributeLockSyntaxRoutesToAttributeFlags()
	{
		var target = await Create();
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"&SECRET {target}=value"));
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lock {target}/SECRET"));
		await Assert.That(await Read($"hasattr({target},SECRET)")).IsEqualTo("1");
		await Assert.That(await Read($"flags({target}/SECRET)")).Contains("+");
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@unlock {target}/SECRET"));
		await Assert.That(await Read($"flags({target}/SECRET)")).DoesNotContain("+");
	}

	[Test]
	[Arguments("no", "i")]
	[Arguments("visual !no_clone", "vi")]
	[Arguments("visual nonsense", "vi")]
	public async Task FlagInputMatchesPennPrivilegeParsing(string input, string expected)
	{
		var target = await Create();
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lock {target}=#TRUE"));
		if (input == "no") await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lset {target}/Basic=!no_inherit"));
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lset {target}/Basic={input}"));
		await Assert.That(await Read($"lockflags({target})")).IsEqualTo(expected);
	}

	[Test]
	[Arguments("=me")]
	[Arguments("=#2147483647")]
	public async Task ClonePreservesFailingStoredExpression(string expression)
	{
		var target = await Create();
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var source = (await mediator.Send(new GetObjectNodeQuery(DBRef.Parse(target)))).Expect<AnySharpObject>();
		await Factory.Services.GetRequiredService<ISharpDatabase>().SetLockAsync(source.Object(), "Basic", new SharpLockData(expression));
		source.Object().WithLock("Basic", new SharpLockData(expression));
		var clone = await Parser.CommandParse(1, Connections, MarkupText.Plain($"@clone {target}=CloneLock{Guid.NewGuid():N}"));
		var cloned = (await Factory.Services.GetRequiredService<ISharpDatabase>().GetObjectNodeAsync(DBRef.Parse(clone.Message.ToPlainText()))).Expect<AnySharpObject>();
		await Assert.That(cloned.Object().Locks["Basic"].LockString).IsEqualTo(expression);
		await Assert.That(await Factory.Services.GetRequiredService<ILockService>().Evaluate(LockType.Basic, cloned, source)).IsFalse();
	}
	[Test]
	public async Task PrefixedAliasKeepsStandardDefaults()
	{
		var target = await Create();
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lock/user:tport {target}=#TRUE"));
		await Assert.That(await Read($"lockflags({target}/Teleport)")).IsEqualTo("i");
	}

	[Test]
	[Arguments("visual !no_clone", "vic")]
	[Arguments("!visual no_clone", "i")]
	[Arguments("!visual !no_clone", "ic")]
	public async Task FlagNegationSelectsMaskWithoutIndependentlyClearingExistingFlags(string input, string expected)
	{
		var target = await Create();
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lock {target}=#TRUE"));
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lset {target}/Basic=visual no_clone"));
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@lset {target}/Basic={input}"));
		await Assert.That(await Read($"lockflags({target})")).IsEqualTo(expected);
	}

	/// <summary>
	/// Every neighbour of <c>@LSET</c> reports through the localisation table; it reported through
	/// hardcoded English, so a player on a non-default locale got the message in English and a
	/// translator had nothing to translate. PennMUSH <c>do_lset</c> (<c>src/lock.c:913,949</c>)
	/// wraps all three of these strings in <c>T()</c>.
	/// </summary>
	[Test]
	[Arguments("visual", nameof(ErrorMessages.Notifications.LockFlagsSet), "lock flags set.")]
	[Arguments("!visual", nameof(ErrorMessages.Notifications.LockFlagsUnset), "lock flags unset.")]
	public async Task LsetReportsThroughTheLocalisationTable(string flags, string key, string tail)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, Connections, "LsetLocale");
		var name = $"LsetLocale{Guid.NewGuid():N}";
		var created = await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@create {name}"));
		var target = created.Message.ToPlainText();

		await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@lock {target}=#TRUE"));
		await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@lset {target}/Basic={flags}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedRendering(
			Factory.Services.GetRequiredService<INotifyService>(), key, $"{name}/Basic - {tail}", player.DbRef)).IsTrue();
	}

	/// <summary>PennMUSH <c>do_lset</c> (<c>src/lock.c:913</c>) — <c>T("No lock name given.")</c>.</summary>
	[Test]
	public async Task LsetWithoutALockNameReportsThroughTheLocalisationTable()
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, Connections, "LsetNoName");
		var created = await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@create LsetNoName{Guid.NewGuid():N}"));

		await Parser.CommandParse(player.Handle, Connections,
			MarkupText.Plain($"@lset {created.Message.ToPlainText()}=visual"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			Factory.Services.GetRequiredService<INotifyService>(),
			nameof(ErrorMessages.Notifications.NoLockNameGiven), player.DbRef)).IsTrue();
	}

	/// <summary>PennMUSH <c>do_unlock</c> (<c>src/lock.c:676</c>) — <c>"%s(%s) - %s (already) unlocked."</c>.</summary>
	[Test]
	public async Task UnlockingALockThatWasNeverSetReportsThroughTheLocalisationTable()
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, Connections, "UnlockNever");
		var name = $"UnlockNever{Guid.NewGuid():N}";
		var created = await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@create {name}"));
		var target = created.Message.ToPlainText();

		await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@unlock {target}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedRendering(
			Factory.Services.GetRequiredService<INotifyService>(),
			nameof(ErrorMessages.Notifications.ObjectAlreadyUnlocked),
			$"{name}(#{DBRef.Parse(target).Number}) - Basic (already) unlocked.", player.DbRef)).IsTrue();
	}

	private async Task<(TestIsolationHelpers.TestPlayer Player, string Name, string Target)> MortalWithThingAsync(string prefix)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, Connections, prefix);
		var name = $"{prefix}{Guid.NewGuid():N}";
		var created = await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@create {name}"));
		return (player, name, created.Message.ToPlainText());
	}

	private Task<CallState> As(TestIsolationHelpers.TestPlayer player, string command)
		=> Parser.CommandParse(player.Handle, Connections, MarkupText.Plain(command)).AsTask();

	/// <summary>
	/// <c>fun_lock</c> with a key is <c>do_lock</c> (<c>src/fundb.c:1311-1335</c>), so it tells the caller
	/// what <c>@lock</c> tells them, then answers the lock as it now stands.
	/// </summary>
	[Test]
	public async Task LockFunctionReportsWhatTheCommandReports()
	{
		var (player, name, target) = await MortalWithThingAsync("LockFnReport");

		var answer = await As(player, $"think lock({target}/Enter,#TRUE)");

		await Assert.That(answer.Message.ToPlainText()).IsEqualTo("#TRUE");
		await Factory.Notifications.WaitForAsync(player.DbRef, $"{name}(#{DBRef.Parse(target).Number}) - Enter locked.");
	}

	/// <summary><c>do_lock</c> (<c>src/lock.c:725-727</c>): matched, but not the locker's to lock.</summary>
	[Test]
	[Arguments("@lock {0}=#FALSE")]
	[Arguments("think lock({0},#FALSE)")]
	public async Task LockingWhatYouDoNotControlIsYouCantLockThat(string template)
	{
		var (player, _, _) = await MortalWithThingAsync("LockNotYours");
		var other = await Create();

		await As(player, string.Format(template, other));

		await Factory.Notifications.WaitForAsync(player.DbRef, "You can't lock that!");
		await Assert.That(await Read($"lock({other})")).IsEqualTo("*UNLOCKED*");
	}

	/// <summary><c>do_lock</c> (<c>src/lock.c:715-718</c>): its match is silent and words the miss itself.</summary>
	[Test]
	public async Task LockingNothingIsIDontSeeWhatYouWantToLock()
	{
		var (player, _, _) = await MortalWithThingAsync("LockNothing");

		await As(player, $"@lock LockNoSuchThing{Guid.NewGuid():N}=#TRUE");

		await Factory.Notifications.WaitForAsync(player.DbRef, "I don't see what you want to lock!");
	}

	/// <summary>The key is parsed before the lock type is checked (<c>src/lock.c:738-742</c>).</summary>
	[Test]
	public async Task ABadKeyIsReportedBeforeABadLockType()
	{
		var (player, _, target) = await MortalWithThingAsync("LockBadKey");

		await As(player, $"@lock/NoSuchLockType {target}=#TRUE&");

		await Factory.Notifications.WaitForAsync(player.DbRef, "I don't understand that key.");
		await Assert.That(Factory.Notifications.For(player.DbRef)).DoesNotContain(ErrorMessages.Notifications.UnknownLockType);
	}

	/// <summary><c>check_lock_type</c> (<c>src/lock.c:636-640</c>) has its own word for a <c>|</c>.</summary>
	[Test]
	public async Task APipeInAUserLockNameIsNamed()
	{
		var (player, _, target) = await MortalWithThingAsync("LockPipe");

		await As(player, $"@lock/user:a|b {target}=#TRUE");

		await Factory.Notifications.WaitForAsync(player.DbRef, "The character '|' may not be used in lock names.");
	}

	/// <summary><c>fun_lset</c> is <c>do_lset</c> (<c>src/fundb.c:1296-1308</c>) and reports as <c>@lset</c> does.</summary>
	[Test]
	public async Task LsetFunctionReportsWhatTheCommandReports()
	{
		var (player, name, target) = await MortalWithThingAsync("LsetFnReport");
		await As(player, $"@lock {target}=#TRUE");

		await As(player, $"think lset({target}/Basic,visual)");

		await Factory.Notifications.WaitForAsync(player.DbRef, $"{name}/Basic - lock flags set.");
		await Assert.That(await Read($"lockflags({target})")).Contains("v");
	}

	/// <summary>
	/// <c>do_lset</c> resolves the lock the way every other lock write does: an alias or any casing
	/// lands on the canonical lock, and the report names it as stored.
	/// </summary>
	[Test]
	[Arguments("tport")]
	[Arguments("TELEPORT")]
	[Arguments("teleport")]
	public async Task LsetAcceptsAliasesAndAnyCasing(string spelling)
	{
		var (player, name, target) = await MortalWithThingAsync("LsetAlias");
		await As(player, $"@lock/Teleport {target}=#TRUE");

		await As(player, $"@lset {target}/{spelling}=visual");

		await Factory.Notifications.WaitForAsync(player.DbRef, $"{name}/Teleport - lock flags set.");
		await Assert.That(await Read($"lockflags({target}/Teleport)")).Contains("v");
	}

	/// <summary>
	/// <c>do_lset</c> parses the flags before it looks the lock up (<c>src/lock.c:926-935</c>), so a bad
	/// flag on a lock that is not there is the bad flag.
	/// </summary>
	[Test]
	public async Task LsetReportsAnUnknownFlagBeforeAMissingLock()
	{
		var (player, _, target) = await MortalWithThingAsync("LsetBadFlag");

		await As(player, $"@lset {target}/Basic=zzz");

		await Factory.Notifications.WaitForAsync(player.DbRef, "Unrecognized lock flag.");
		await Assert.That(Factory.Notifications.For(player.DbRef)).DoesNotContain(ErrorMessages.Notifications.NoSuchLock);
	}

	/// <summary><c>do_lset</c> on a lock the object does not have.</summary>
	[Test]
	public async Task LsetOnAMissingLockIsNoSuchLock()
	{
		var (player, _, target) = await MortalWithThingAsync("LsetMissing");

		await As(player, $"@lset {target}/Basic=visual");

		await Factory.Notifications.WaitForAsync(player.DbRef, "No such lock.");
	}

	/// <summary>
	/// <c>do_unlock</c> asks <c>getlock</c>, which walks the parent chain (<c>src/lock.c:323-330</c>), so
	/// an inherited lock is not "(already) unlocked"; <c>delete_lock</c> then finds nothing of the child's
	/// own and succeeds.
	/// </summary>
	[Test]
	public async Task UnlockingAnInheritedLockReportsUnlocked()
	{
		var (player, name, target) = await MortalWithThingAsync("UnlockInherited");
		var parent = (await As(player, $"@create UnlockParent{Guid.NewGuid():N}")).Message.ToPlainText();
		await As(player, $"@lock {parent}=#FALSE");
		// Every standard lock starts no_inherit; a child only sees one its parent lets go of.
		await As(player, $"@lset {parent}/Basic=!no_inherit");
		await As(player, $"@parent {target}={parent}");

		await As(player, $"@unlock {target}");

		await Factory.Notifications.WaitForAsync(player.DbRef, $"{name}(#{DBRef.Parse(target).Number}) - Basic unlocked.");
	}
}
