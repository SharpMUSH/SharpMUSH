using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class InputHookFailureTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("IGNORE", "syntax")]
	[Arguments("BEFORE", "syntax")]
	[Arguments("AFTER", "syntax")]
	[Arguments("IGNORE", "throw")]
	[Arguments("BEFORE", "throw")]
	[Arguments("AFTER", "throw")]
	[Arguments("IGNORE", "literal")]
	[Arguments("BEFORE", "literal")]
	[Arguments("AFTER", "literal")]
	[Arguments("IGNORE", "legacy")]
	[Arguments("BEFORE", "legacy")]
	[Arguments("AFTER", "legacy")]
	[Arguments("IGNORE", "success")]
	[Arguments("BEFORE", "success")]
	[Arguments("AFTER", "success")]
	public async Task HookFailureRetiresCaptureWithoutChangingHookControlFlow(string hookType, string mode)
	{
		var connections = Factory.Services.GetRequiredService<IConnectionService>();
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var sessions = Factory.Services.GetRequiredService<IInputSessionService>();
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, connections, "InputHook");
		var original = (SharpMUSH.Implementation.MUSHCodeParser)Factory.Services.GetRequiredService<IMUSHCodeParser>();
		var commandName = "@hookprobe" + Guid.NewGuid().ToString("N");
		var functionName = "hookfailure" + Guid.NewGuid().ToString("N");
		var bodyCalls = 0;
		var functionCalls = 0;
		var commands = new CommandLibraryService();
		foreach (var pair in original.CommandLibrary) commands.Add(pair.Key, pair.Value);
		commands.Add(commandName, (new CommandDefinition(new SharpCommandAttribute
		{
			Name = commandName, Behavior = CommandBehavior.Default, MinArgs = 0, MaxArgs = 0
		}, _ => { bodyCalls++; return ValueTask.FromResult<Option<CallState>>(new CallState("body")); }), true));
		var functions = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) functions.Add(pair.Key, pair.Value);
		functions.Add(functionName, (new FunctionDefinition(new SharpFunctionAttribute
		{
			Name = functionName, Flags = FunctionFlags.Regular, MinArgs = 0, MaxArgs = 0
		}, _ => { functionCalls++; throw new InvalidOperationException("hook failed"); }), true));
		var hooks = Substitute.For<IHookService>();
		hooks.GetHookAsync(Arg.Any<string>(), Arg.Any<string>())
			.Returns(ValueTask.FromResult<Option<CommandHook>>(new OneOf.Types.None()));
		hooks.GetHookAsync(Arg.Is<string>(s => s.Equals(commandName, StringComparison.OrdinalIgnoreCase)), hookType)
			.Returns(ValueTask.FromResult<Option<CommandHook>>(new CommandHook(hookType, player.DbRef, "HOOKBODY")));
		var legacy = Substitute.For<IAttributeService>();
		legacy.EvaluateAttributeFunctionAsync(Arg.Any<IMUSHCodeParser>(), Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
			Arg.Any<string>(), Arg.Any<Dictionary<string, CallState>>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(ValueTask.FromResult(MarkupText.Plain("#-1 EXCEPTION: ordinary text")));
		var provider = Substitute.For<IServiceProvider>();
		provider.GetService(Arg.Any<Type>()).Returns(call => call.Arg<Type>() == typeof(IHookService) ? hooks
			: mode == "legacy" && call.Arg<Type>() == typeof(IAttributeService) ? legacy : Factory.Services.GetService(call.Arg<Type>()));
		var parser = new SharpMUSH.Implementation.MUSHCodeParser(original.Logger, functions, commands, original.Configuration, provider);
		try
		{
			var actor = (await mediator.Send(new SharpMUSH.Library.Queries.Database.GetObjectNodeQuery(player.DbRef))).Known();
			var hookText = mode switch { "syntax" => "[", "throw" => functionName + "()", "literal" => "#-1 EXCEPTION: ordinary text", _ => "1" };
			await attributes.SetAttributeAsync(actor, actor, "HOOKBODY", MarkupText.Plain(hookText));
			await attributes.SetAttributeAsync(actor, actor, "CALLBACK", MarkupText.Plain(commandName));
			await original.CommandParse(player.Handle, connections, MarkupText.Plain("@input/start me/CALLBACK=Answer:,120"));
			var session = sessions.GetCapturing(player.Handle);
			await Assert.That(session).IsNotNull();
			var result = await sessions.DeliverAsync(parser, session!, MarkupText.Plain("reply"));
			await hooks.Received().GetHookAsync(Arg.Is<string>(s => s.Equals(commandName, StringComparison.OrdinalIgnoreCase)), hookType);
			if (mode == "throw") await Assert.That(functionCalls).IsEqualTo(1);
			if (mode == "legacy") await legacy.Received(1).EvaluateAttributeFunctionAsync(Arg.Any<IMUSHCodeParser>(), Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				"HOOKBODY", Arg.Any<Dictionary<string, CallState>>(), true, false);
			var failed = mode is "syntax" or "throw";
			await Assert.That(result!.HadErrors).IsEqualTo(failed);
			await Assert.That(sessions.GetCapturing(player.Handle) is null).IsEqualTo(failed);
			if (!failed) await Assert.That(bodyCalls).IsEqualTo(hookType == "IGNORE" && mode is "literal" or "legacy" ? 0 : 1);
		}
		finally { await connections.Disconnect(player.Handle); }
	}
}
