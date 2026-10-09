using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class LockViewParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();

	/// <summary>
	/// What the command's enactor was told while it ran. God's bucket also takes other tests' output
	/// in that window, so a caller reading it picks out its own object's lines.
	/// </summary>
	private async Task<string[]> AsGod(string command) =>
		await Output(Factory.ExecutorDBRef, 1, command);

	private async Task<string[]> Output(TestIsolationHelpers.TestPlayer player, string command) =>
		await Output(player.DbRef, player.Handle, command);

	private async Task<string[]> Output(DBRef who, long handle, string command)
	{
		var before = Factory.Notifications.CountFor(who);
		await Factory.CommandParser.CommandParse(handle, Connections, MarkupText.Plain(command));
		return [.. Factory.Notifications.For(who).Skip(before)];
	}

	[Test]
	public async Task ExamineShowsSetterAndDecompileReplaysFlags()
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, Connections, "LockView");
		var created = await Factory.CommandParser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@create LockView{Guid.NewGuid():N}"));
		var target = created.Message.ToPlainText();
		await AsGod($"@lock {target}==me");
		await AsGod($"@lset {target}/Basic=visual");
		await AsGod($"@lset {target}/Basic=!no_inherit");
		var examined = string.Join("\n", await Output(player, $"examine {target}"));
		await Assert.That(examined).Contains("Basic Lock [#1v]: =God");
		var reference = $"#{DBRef.Parse(target).Number}";
		var decomposed = await AsGod($"@decompile/db {target}");
		var lockLines = decomposed
			.Where(line => (line.StartsWith("@lock/") && line.Contains($" {reference}=")) || line.StartsWith($"@lset {reference}/"))
			.ToArray();
		await Assert.That(lockLines).Contains($"@lock/Basic {reference}==me");
		await Assert.That(lockLines).Contains($"@lset {reference}/Basic=!no_inherit");
		await AsGod($"@unlock {target}");
		foreach (var line in lockLines) await AsGod(line);
		var readback = await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain($"lock({target})"));
		await Assert.That(readback.ToPlainText()).IsEqualTo("=#1");
		var flags = await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain($"lockflags({target})"));
		await Assert.That(flags.ToPlainText()).IsEqualTo("v");
	}
	[Test]
	public async Task AttributeLockDenialDoesNotReportSuccess()
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, Connections, "AttrLockView");
		var created = await Factory.CommandParser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@create AttrLock{Guid.NewGuid():N}"));
		var target = created.Message.ToPlainText();
		await AsGod($"&SECRET {target}=value");
		await AsGod($"@set {target}/SECRET=wizard");
		var output = string.Join("\n", await Output(player, $"@lock {target}/SECRET"));
		await Assert.That(output).DoesNotContain("AttributeLocked");
		var flags = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"flags({target}/SECRET)"));
		await Assert.That(flags!.Message.ToPlainText()).DoesNotContain("+");
	}

	[Test]
	public async Task LockingAttributeLeafChangesOnlyItsCreator()
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, Connections, "LeafLockOwner");
		var created = await Factory.CommandParser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@create LeafLock{Guid.NewGuid():N}"));
		var target = created.Message.ToPlainText();
		await AsGod($"&ROOT {target}=root value");
		await AsGod($"&ROOT`LEAF {target}=leaf value");
		await Assert.That(await Read($"owner({target}/ROOT)")).IsEqualTo("#1");
		await Assert.That(await Read($"owner({target}/ROOT`LEAF)")).IsEqualTo("#1");

		await Output(player, $"@lock {target}/ROOT`LEAF");

		await Assert.That(await Read($"owner({target}/ROOT)")).IsEqualTo("#1");
		await Assert.That(await Read($"owner({target}/ROOT`LEAF)")).IsEqualTo($"#{player.DbRef.Number}");
		await Assert.That(await Read($"flags({target}/ROOT)")).DoesNotContain("+");
		await Assert.That(await Read($"flags({target}/ROOT`LEAF)")).Contains("+");
		await Assert.That(await Read($"get({target}/ROOT)")).IsEqualTo("root value");
		await Assert.That(await Read($"get({target}/ROOT`LEAF)")).IsEqualTo("leaf value");

		async Task<string> Read(string expression)
			=> (await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();
	}

}
