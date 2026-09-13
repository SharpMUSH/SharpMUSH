using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class LockViewParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private INotifyService Notify => Factory.Services.GetRequiredService<INotifyService>();

	private async Task<string[]> Output(long handle, string command)
	{
		var count = Notify.ReceivedCalls().Count();
		await Factory.CommandParser.CommandParse(handle, Connections, MarkupText.Plain(command));
		return Notify.ReceivedCalls().Skip(count)
			.Where(call => call.GetMethodInfo().Name is "Notify" or "NotifyLocalized")
			.Select(call => call.GetArguments()[1])
			.Select(value => value switch { SharpMessage and string text => text, SharpMessage and MarkupText text => text.ToPlainText(), string text => text, _ => "" })
			.ToArray();
	}

	[Test]
	public async Task ExamineShowsSetterAndDecompileReplaysFlags()
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, Connections, "LockView");
		var created = await Factory.CommandParser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@create LockView{Guid.NewGuid():N}"));
		var target = created.Message!.ToPlainText();
		await Output(1, $"@lock {target}==me");
		await Output(1, $"@lset {target}/Basic=visual");
		await Output(1, $"@lset {target}/Basic=!no_inherit");
		var examined = string.Join("\n", await Output(player.Handle, $"examine {target}"));
		await Assert.That(examined).Contains("Basic Lock [#1v]: =God");
		var reference = $"#{DBRef.Parse(target).Number}";
		var decomposed = await Output(1, $"@decompile/db {target}");
		var lockLines = decomposed.Where(line => line.StartsWith("@lock/") || line.StartsWith("@lset ")).ToArray();
		await Assert.That(lockLines).Contains($"@lock/Basic {reference}==me");
		await Assert.That(lockLines).Contains($"@lset {reference}/Basic=!no_inherit");
		await Output(1, $"@unlock {target}");
		foreach (var line in lockLines) await Output(1, line);
		var readback = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"lock({target})"));
		await Assert.That(readback!.Message!.ToPlainText()).IsEqualTo("=#1");
		var flags = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"lockflags({target})"));
		await Assert.That(flags!.Message!.ToPlainText()).IsEqualTo("v");
	}
}
