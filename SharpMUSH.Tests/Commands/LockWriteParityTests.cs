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
		=> (await Parser.CommandParse(1, Connections, MarkupText.Plain($"@create LockParity{Guid.NewGuid():N}"))).Message!.ToPlainText();

	private async Task<string> Read(string expression)
		=> (await Factory.FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();

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
		var target = created.Message!.ToPlainText();
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
		var target = created.Message!.ToPlainText();
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
		var cloned = (await Factory.Services.GetRequiredService<ISharpDatabase>().GetObjectNodeAsync(DBRef.Parse(clone.Message!.ToPlainText()))).Expect<AnySharpObject>();
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
		var target = created.Message!.ToPlainText();

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
			MarkupText.Plain($"@lset {created.Message!.ToPlainText()}=visual"));

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
		var target = created.Message!.ToPlainText();

		await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@unlock {target}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedRendering(
			Factory.Services.GetRequiredService<INotifyService>(),
			nameof(ErrorMessages.Notifications.ObjectAlreadyUnlocked),
			$"{name}(#{DBRef.Parse(target).Number}) - Basic (already) unlocked.", player.DbRef)).IsTrue();
	}
}
