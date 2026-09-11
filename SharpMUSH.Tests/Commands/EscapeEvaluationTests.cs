using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A backslash escape is decoded exactly once, by the evaluation that consumes the argument. Every
/// expected value here was read off PennMUSH 1.8.8 (tools/oracle) by typing the same line as #1.
/// </summary>
public class EscapeEvaluationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParserFor(_player.DbRef, _player.Handle);

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private TestIsolationHelpers.TestPlayer _player = null!;

	[Before(TUnit.Core.HookType.Test)]
	public async Task CreatePlayer()
	{
		_player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "Escape");
	}

	[After(TUnit.Core.HookType.Test)]
	public async Task DisconnectPlayer()
	{
		if (_player is not null)
			await ConnectionService.Disconnect(_player.Handle);
	}

	private async ValueTask<List<string>> Run(string command)
	{
		var pre = NotifyService.ReceivedCalls().Count();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain(command));
		return NotifyService.ReceivedCalls().Skip(pre)
			.Where(call => call.GetMethodInfo().Name == nameof(INotifyService.Notify))
			.Select(call => call.GetArguments())
			.Where(args => args.Length > 1 && args[0] is AnySharpObject target && target.Object().DBRef == _player.DbRef)
			.Select(args => args[1] switch
			{
				OneOf<MString, string> oneOf => oneOf.Match(m => m.ToPlainText(), s => s),
				MString m => m.ToPlainText(),
				string s => s,
				_ => string.Empty
			})
			.ToList();
	}

	[Test]
	[Arguments(@"think Esc1 \[", "Esc1 [")]
	[Arguments(@"think Esc2 \\", @"Esc2 \")]
	[Arguments(@"think Esc3 \[add(1,2)\]", "Esc3 [add(1,2)]")]
	[Arguments(@"think Esc4 \\\[", @"Esc4 \[")]
	[Arguments(@"think Esc5 \%# {\[x\]}", "Esc5 %# [x]")]
	[Arguments(@"think Esc6 [strcat(\[,x)]", "Esc6 [x")]
	[Arguments(@"think [if(1,Esc7 \[x\] \\)]", @"Esc7 [x] \")]
	[Arguments(@"think [switch(a,a,Esc8 \[x\] \\)]", @"Esc8 [x] \")]
	[Arguments(@"think [iter(a,Esc9 \[x\] \\)]", @"Esc9 [x] \")]
	[Arguments(@"@pemit me=Esc10 \[ \\", @"Esc10 [ \")]
	[Arguments(@"@pemit/list me=Esc11 \[", "Esc11 [")]
	[Arguments(@"]think Esc12 \[x\] \\", @"Esc12 \[x\] \\")]
	[Arguments(@"@emit/noeval Esc13 \[x\] \\", @"Esc13 \[x\] \\")]
	[Arguments(@"@switch/inline 1=1,think Esc14 \\\[", @"Esc14 \[")]
	[Arguments(@"@dolist/inline a=think Esc15 \\\[", @"Esc15 \[")]
	public async Task EscapeIsDecodedOnce(string command, string expected)
	{
		var messages = await Run(command);

		await Assert.That(messages).Contains(expected)
			.Because($"`{command}` must print `{expected}`; got: [{string.Join(" | ", messages)}]");
	}

	[Test]
	[Arguments(@"@set me=ESC_SET_SUB:\%#", "ESC_SET_SUB", "%#")]
	[Arguments(@"@set me=ESC_SET_MIX:\[x\] \\", "ESC_SET_MIX", @"[x] \")]
	[Arguments(@"@set me=ESC_SET_TRIPLE:\\\[", "ESC_SET_TRIPLE", @"\[")]
	[Arguments(@"&ESC_AMP me=\[x\]", "ESC_AMP", @"\[x\]")]
	public async Task StoredValueIsDecodedOnce(string command, string attribute, string expected)
	{
		await Run(command);
		var messages = await Run($"think [get(me/{attribute})]");

		await Assert.That(messages).Contains(expected)
			.Because($"`{command}` must store `{expected}`; got: [{string.Join(" | ", messages)}]");
	}

	// & stores its value raw, and the stored action is then evaluated once when it runs.
	[Test]
	public async Task StoredActionIsDecodedOnceWhenRun()
	{
		await Run(@"&ESC_ACTION me=think Esc16 \\\[");
		var messages = await Run("@include me/ESC_ACTION");

		await Assert.That(messages).Contains(@"Esc16 \[")
			.Because($"got: [{string.Join(" | ", messages)}]");
	}
}
