using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class InputHookFailureTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("BEFORE", "branch-veto")]
	[Arguments("BEFORE", "branch-plugin")]
	[Arguments("AFTER", "branch-plugin")]
	[Arguments("BEFORE", "branch-override")]
	[Arguments("AFTER", "branch-override")]
	[Arguments("BEFORE", "branch-extend")]
	[Arguments("AFTER", "branch-extend")]
	[Arguments("BEFORE", "branch-invalid")]
	[Arguments("BEFORE", "branch-lock")]
	[Arguments("IGNORE", "nested")]
	[Arguments("BEFORE", "nested")]
	[Arguments("AFTER", "nested")]
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
		var overrideCalls = 0;
		var commands = new CommandLibraryService();
		foreach (var pair in original.CommandLibrary) commands.Add(pair.Key, pair.Value);
		commands.Add(commandName, (new CommandDefinition(new SharpCommandAttribute
		{
			Name = commandName, Behavior = CommandBehavior.Default, MinArgs = 0, MaxArgs = 0,
			CommandLock = mode == "branch-lock" ? "FLAG^WIZARD" : ""
		}, _ => { bodyCalls++; return ValueTask.FromResult<Option<CallState>>(new CallState("body")); }), true));
		commands.Add(commandName + "override", (new CommandDefinition(new SharpCommandAttribute
		{
			Name = commandName + "override", Behavior = CommandBehavior.Default, MinArgs = 0, MaxArgs = 0
		}, _ => { overrideCalls++; return ValueTask.FromResult<Option<CallState>>(new CallState("override result")); }), true));
		var functions = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) functions.Add(pair.Key, pair.Value);
		functions.Add(functionName, (new FunctionDefinition(new SharpFunctionAttribute
		{
			Name = functionName, Flags = FunctionFlags.Regular, MinArgs = 0, MaxArgs = 0
		}, _ => { functionCalls++; throw new InvalidOperationException("hook failed"); }), true));
		var hooks = Substitute.For<IHookService>();
		hooks.GetHookAsync(Arg.Any<string>(), Arg.Any<string>())
			.Returns(ValueTask.FromResult<Option<CommandHook>>(new SharpMUSH.Library.DiscriminatedUnions.None()));
		hooks.GetHookAsync(Arg.Is<string>(s => s.Equals(commandName, StringComparison.OrdinalIgnoreCase)), hookType)
			.Returns(ValueTask.FromResult<Option<CommandHook>>(new CommandHook(hookType, player.DbRef, "HOOKBODY")));
		if (mode is "branch-override" or "branch-extend")
			hooks.GetHookAsync(Arg.Is<string>(s => s.Equals(commandName, StringComparison.OrdinalIgnoreCase)), mode == "branch-override" ? "OVERRIDE" : "EXTEND")
				.Returns(ValueTask.FromResult<Option<CommandHook>>(new CommandHook(mode == "branch-override" ? "OVERRIDE" : "EXTEND", player.DbRef, "OVERRIDE")));
		var plugin = Substitute.For<IPluginHookDispatcher>();
		plugin.HasCommandInterceptors.Returns(true);
		plugin.CommandBeforeAsync(Arg.Any<IMUSHCodeParser>(), Arg.Any<string>()).Returns(ValueTask.FromResult(mode != "branch-veto"));
		plugin.CommandTryOverrideAsync(Arg.Any<IMUSHCodeParser>(), Arg.Any<string>())
			.Returns(ValueTask.FromResult<Option<CallState>?>(new CallState("plugin result")));
		var discovery = Substitute.For<ICommandDiscoveryService>();
		var legacy = Substitute.For<IAttributeService>();
		legacy.EvaluateAttributeFunctionAsync(Arg.Any<IMUSHCodeParser>(), Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
			Arg.Any<string>(), Arg.Any<Dictionary<string, CallState>>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(ValueTask.FromResult(MarkupText.Plain("#-1 EXCEPTION: ordinary text")));
		var provider = Substitute.For<IServiceProvider>();
		provider.GetService(Arg.Any<Type>()).Returns(call => call.Arg<Type>() == typeof(IHookService) ? hooks
			: mode == "legacy" && call.Arg<Type>() == typeof(IAttributeService) ? legacy
			: mode is "branch-veto" or "branch-plugin" && call.Arg<Type>() == typeof(IPluginHookDispatcher) ? plugin
			: mode is "branch-override" or "branch-extend" && call.Arg<Type>() == typeof(ICommandDiscoveryService) ? discovery
			: Factory.Services.GetService(call.Arg<Type>()));
		var parser = new SharpMUSH.Implementation.MUSHCodeParser(original.Logger, functions, commands, original.Configuration, provider);
		try
		{
			var actor = (await mediator.Send(new SharpMUSH.Library.Queries.Database.GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>();
			var matchAttribute = new SharpAttribute("override", "OVERRIDE", "OVERRIDE", [], 0, "OVERRIDE", null!, null!, null!)
			{ Value = MarkupText.Plain(commandName + "override") };
			discovery.MatchUserDefinedCommand(Arg.Any<IMUSHCodeParser>(), Arg.Any<IAsyncEnumerable<AnySharpObject>>(), Arg.Any<MarkupText>())
				.Returns(ValueTask.FromResult<Option<IEnumerable<(AnySharpObject, SharpAttribute, Dictionary<string, CallState>)>>>(
					new[] { (actor, matchAttribute, new Dictionary<string, CallState>()) }));
			var hookText = mode switch { "syntax" => "[", "nested" => "ufun(me/NESTED)", "throw" => functionName + "()", "literal" => "#-1 EXCEPTION: ordinary text", _ => "1" };
			if (mode.StartsWith("branch-", StringComparison.Ordinal)) hookText = "[";
			if (mode == "nested") await attributes.SetAttributeAsync(actor, actor, "NESTED", MarkupText.Plain("["));
			await attributes.SetAttributeAsync(actor, actor, "HOOKBODY", MarkupText.Plain(hookText));
			await attributes.SetAttributeAsync(actor, actor, "CALLBACK", MarkupText.Plain(commandName + (mode is "branch-extend" or "branch-invalid" ? "/extra" : "")));
			await original.CommandParse(player.Handle, connections, MarkupText.Plain("@input/start me/CALLBACK=Answer:,120"));
			var session = sessions.GetCapturing(player.Handle);
			await Assert.That(session).IsNotNull();
			var result = await sessions.DeliverAsync(parser, session!, MarkupText.Plain("reply"));
			await hooks.Received().GetHookAsync(Arg.Is<string>(s => s.Equals(commandName, StringComparison.OrdinalIgnoreCase)), hookType);
			if (mode == "throw") await Assert.That(functionCalls).IsEqualTo(1);
			if (mode == "legacy") await legacy.Received(1).EvaluateAttributeFunctionAsync(Arg.Any<IMUSHCodeParser>(), Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				"HOOKBODY", Arg.Any<Dictionary<string, CallState>>(), true, false);
			var failed = mode is "syntax" or "throw" or "nested" || mode.StartsWith("branch-", StringComparison.Ordinal);
			await Assert.That(result!.HadErrors).IsEqualTo(failed);
			await Assert.That(sessions.GetCapturing(player.Handle) is null).IsEqualTo(failed);
			if (mode.StartsWith("branch-", StringComparison.Ordinal))
			{
				await Assert.That(bodyCalls).IsEqualTo(0);
				await Assert.That(overrideCalls).IsEqualTo(mode is "branch-override" or "branch-extend" ? 1 : 0);
				if (mode == "branch-plugin") await Assert.That(result.Message!.Text).IsEqualTo("plugin result");
				if (mode == "branch-invalid") await Assert.That(result.Message!.Text).IsEqualTo("#-1 INVALID SWITCH: extra");
			}
			if (!failed) await Assert.That(bodyCalls).IsEqualTo(hookType == "IGNORE" && mode is "literal" or "legacy" ? 0 : 1);
		}
		finally { await connections.Disconnect(player.Handle); }
	}
}
