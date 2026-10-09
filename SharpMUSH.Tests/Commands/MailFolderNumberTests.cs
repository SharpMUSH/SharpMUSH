using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Mail folders carry PennMUSH's numbers (#1494): <c>maillist()</c> writes <c>&lt;folder number&gt;:&lt;message&gt;</c>
/// (<c>fun_maillist</c>, <c>extmail.c:844</c>) and that is what <c>mail()</c>, <c>@mail/read</c> and <c>@mail/file</c>
/// take back. Folder 0 is INBOX; a folder keeps its number when it is named or unnamed.
/// </summary>
public class MailFolderNumberTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<(TestIsolationHelpers.TestPlayer Player, Func<string, Task> Run, Func<string, Task<string>> Eval)>
		Mailbox(string name)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, name);
		var commands = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var functions = WebAppFactoryArg.FunctionParserFor(player.DbRef);

		return (player,
			async command => await commands.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command)),
			async expression => (await functions.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText());
	}

	[Test]
	public async Task MaillistWritesFolderNumbersThatMailTakesBack()
	{
		var (player, run, eval) = await Mailbox("MailFldNum");
		var first = TestIsolationHelpers.GenerateUniqueName("FldNumFirst");
		var second = TestIsolationHelpers.GenerateUniqueName("FldNumSecond");
		await run($"@mail #{player.DbRef.Number}={first}/One.");
		await run($"@mail #{player.DbRef.Number}={second}/Two.");

		await Assert.That(await eval("maillist()")).IsEqualTo("0:1 0:2");

		await run("@mail/file 2=3");

		await Assert.That(await eval("maillist(all)")).IsEqualTo("0:1 3:1");
		// parse_msglist matches "all" with strcasecmp (extmail.c:3000).
		await Assert.That(await eval("maillist(ALL)")).IsEqualTo("0:1 3:1");
		await Assert.That(await eval("maillist(All)")).IsEqualTo("0:1 3:1");
		await Assert.That(await eval("maillist(3:)")).IsEqualTo("3:1");
		await Assert.That(await eval("mailsubject(3:1)")).IsEqualTo(second);
		await Assert.That(await eval("mailsubject(me,3:1)")).IsEqualTo(second);
		await Assert.That(await eval("mail(3:1)")).IsEqualTo("Two.");
		await Assert.That(await eval("mailsubject(0:1)")).IsEqualTo(first);
		await Assert.That(await eval("folderstats(3)")).IsEqualTo("0 1 0");
		// There is no folder 16 (MAX_FOLDERS is 15): fun_maillist answers e_range.
		await Assert.That(await eval("maillist(16:)")).IsEqualTo("#-1 OUT OF RANGE");
		await Assert.That(await eval("mailsubject(16:1)")).IsEqualTo("#-1");

		var told = WebAppFactoryArg.Notifications.For(player.DbRef);
		await Assert.That(told).Contains(m => m.Contains("MAIL: Msg 0:2 filed in folder 3 [unnamed]", StringComparison.Ordinal));
	}

	[Test]
	public async Task ANamedFolderKeepsItsNumber()
	{
		var (player, run, eval) = await Mailbox("MailFldName");
		var subject = TestIsolationHelpers.GenerateUniqueName("FldNameSubj");
		await run($"@mail #{player.DbRef.Number}={subject}/Body.");
		await run("@mail/file 1=5");
		await run("@mail/folder 5=Kept");

		await Assert.That(await eval("maillist(5:)")).IsEqualTo("5:1");
		await Assert.That(await eval("maillist(all)")).IsEqualTo("5:1");
		await Assert.That(await eval("mailsubject(5:1)")).IsEqualTo(subject);
		await Assert.That(await eval("mailsubject(Kept:1)")).IsEqualTo(subject);

		await run("@mail/folder Kept");
		await Assert.That(await eval("maillist()")).IsEqualTo("5:1");

		await run("@mail/unfolder 5");
		await Assert.That(await eval("maillist(all)")).IsEqualTo("5:1");

		var told = WebAppFactoryArg.Notifications.For(player.DbRef);
		await Assert.That(told).Contains(m => m.Contains("MAIL: Folder 5 now named 'Kept'", StringComparison.Ordinal));
		await Assert.That(told).Contains(m => m.Contains("MAIL: Current folder set to 5 [KEPT].", StringComparison.Ordinal));
		await Assert.That(told).Contains(m => m.Contains("MAIL: Folder 5 now has no name", StringComparison.Ordinal));
	}

	/// <summary>A name no folder has yet makes a folder with the lowest number not in use.</summary>
	[Test]
	public async Task ANewFolderNameTakesTheLowestFreeNumber()
	{
		var (player, run, eval) = await Mailbox("MailFldFree");
		await run($"@mail #{player.DbRef.Number}=First/One.");
		await run($"@mail #{player.DbRef.Number}=Second/Two.");
		await run("@mail/file 2=1");
		await run("@mail/file 1=Later");

		// In the order the messages arrived, as fun_maillist walks the mailbox: First is in LATER, Second in 1.
		await Assert.That(await eval("maillist(all)")).IsEqualTo("2:1 1:1");

		var told = WebAppFactoryArg.Notifications.For(player.DbRef);
		await Assert.That(told).Contains(m => m.Contains("MAIL: Msg 0:1 filed in folder 2 [LATER]", StringComparison.Ordinal));
	}

	[Test]
	public async Task MailReadTakesAFolderNumber()
	{
		var (player, run, _) = await Mailbox("MailFldRead");
		var subject = TestIsolationHelpers.GenerateUniqueName("FldReadSubj");
		await run($"@mail #{player.DbRef.Number}=Stays/In the inbox.");
		await run($"@mail #{player.DbRef.Number}={subject}/Filed away.");
		await run("@mail/file 2=4");

		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await run("@mail/read 4:1");
		await run("@mail 4:1");
		var told = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();

		await Assert.That(told.Count(m => m.Contains(subject, StringComparison.Ordinal))).IsEqualTo(2);
		await Assert.That(told).DoesNotContain(m => m.Contains("In the inbox.", StringComparison.Ordinal));
	}

	[Test]
	[Arguments("@mail/file 1=16", "MAIL: Invalid folder specification")]
	[Arguments("@mail/folder 16", "MAIL: What folder is that?")]
	[Arguments("@mail/folder NoSuchFolder", "MAIL: What folder is that?")]
	[Arguments("@mail/read 16:1", "MAIL: Invalid message specification")]
	public async Task AFolderOutOfRangeIsRefused(string command, string refusal)
	{
		var (player, run, eval) = await Mailbox("MailFldBad");
		await run($"@mail #{player.DbRef.Number}=Kept/Body.");

		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await run(command);
		var told = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();

		await Assert.That(told).Contains(m => m.Contains(refusal, StringComparison.Ordinal));
		await Assert.That(await eval("maillist(all)")).IsEqualTo("0:1");
	}
}
