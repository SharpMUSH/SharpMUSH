using Mediator;
using SharpMUSH.Implementation;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class InputPredicateResultTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();

	[Test]
	[Arguments("ifelse", "syntax")]
	[Arguments("ifelse", "literal")]
	[Arguments("ifelse", "success")]
	[Arguments("skip", "syntax")]
	[Arguments("skip", "literal")]
	[Arguments("skip", "success")]
	[Arguments("switch", "syntax")]
	[Arguments("switch", "literal")]
	[Arguments("switch", "success")]
	[Arguments("select", "syntax")]
	[Arguments("select", "literal")]
	[Arguments("select", "success")]
	[Arguments("break", "syntax")]
	[Arguments("break", "literal")]
	[Arguments("break", "success")]
	[Arguments("assert", "syntax")]
	[Arguments("assert", "literal")]
	[Arguments("assert", "success")]
	[Arguments("switch-pattern", "syntax")]
	[Arguments("switch-pattern", "literal")]
	[Arguments("switch-pattern", "success")]
	[Arguments("break-body", "syntax")]
	[Arguments("break-body", "literal")]
	[Arguments("break-body", "success")]
	[Arguments("assert-body", "syntax")]
	[Arguments("assert-body", "literal")]
	[Arguments("assert-body", "success")]
	[Arguments("ifelse-skipped", "skipped")]
	[Arguments("switch-skipped", "skipped")]
	[Arguments("break-skipped", "skipped")]
	[Arguments("assert-skipped", "skipped")]
	public async Task EvaluatedPredicateFailureRetiresCapture(string command, string mode)
	{
		var connections = Get<IConnectionService>();
		var mediator = Get<IMediator>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, connections, "InputPredicate");
		try
		{
			var actor = (await mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known();
			var attributes = Get<IAttributeService>();
			await attributes.SetAttributeAsync(actor, actor, "PREDICATE", MarkupText.Plain(mode switch
			{
				"syntax" or "skipped" => "[",
				"literal" => "#-1 PARSER FAILURE ordinary text",
				_ => "1"
			}));
			const string predicate = "[ulocal(me/PREDICATE)]";
			var callback = command switch
			{
				"ifelse-skipped" => $"@ifelse 0=think {predicate},think clean",
				"switch-skipped" => $"@switch/inline/first 1=1,think clean,{predicate},think bad",
				"break-skipped" => $"@break 0=think {predicate}",
				"assert-skipped" => $"@assert 1=think {predicate}",
				"ifelse" => $"@ifelse {predicate}=think yes,think no",
				"skip" => $"@skip {predicate}=think clean",
				"switch" => $"@switch/inline {predicate}=*,think clean",
				"select" => $"@select/inline {predicate}=*,think clean",
				"break" => $"@break {predicate}",
				"assert" => $"@assert {predicate}",
				"switch-pattern" => $"@switch/inline 1={predicate},think match,think default",
				"break-body" => $"@break 1=think {predicate}",
				"assert-body" => $"@assert 0=think {predicate}",
				_ => throw new InvalidOperationException()
			};
			await attributes.SetAttributeAsync(actor, actor, "CALLBACK", MarkupText.Plain(callback));
			var parser = Get<IMUSHCodeParser>();
			var sessions = Get<IInputSessionService>();
			await parser.CommandParse(player.Handle, connections, MarkupText.Plain("@input/start me/CALLBACK=Answer:,120"));
			var session = sessions.GetCapturing(player.Handle);
			await Assert.That(session).IsNotNull();
			var result = await sessions.DeliverAsync(parser, session!, MarkupText.Plain("reply"));
			Console.WriteLine($"{command}/{mode}: errors={result?.HadErrors}, capture={sessions.GetCapturing(player.Handle)?.Id}");
			await Assert.That(result?.HadErrors).IsEqualTo(mode == "syntax");
			await Assert.That(sessions.GetCapturing(player.Handle)?.Id).IsEqualTo(mode == "syntax" ? null : session!.Id);
		}
		finally { await connections.Disconnect(player.Handle); }
	}

	[Test]
	[Arguments("condition", "syntax")]
	[Arguments("condition", "literal")]
	[Arguments("condition", "success")]
	[Arguments("argument", "syntax")]
	[Arguments("argument", "literal")]
	[Arguments("argument", "success")]
	[Arguments("invoker", "syntax")]
	[Arguments("invoker", "literal")]
	[Arguments("invoker", "success")]
	public async Task RetryRetainsOnlyEvaluatedFailures(string stage, string mode)
	{
		var connections = Get<IConnectionService>();
		var mediator = Get<IMediator>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, connections, "InputRetry");
		var invocations = 0;
		var original = (MUSHCodeParser)Get<IMUSHCodeParser>();
		var commands = new CommandLibraryService();
		foreach (var pair in original.CommandLibrary) commands.Add(pair.Key, pair.Value);
		var name = "@retryprobe" + Guid.NewGuid().ToString("N");
		commands.Add(name, (new CommandDefinition(
				new SharpCommandAttribute { Name = name, Behavior = CommandBehavior.Default, MinArgs = 0, MaxArgs = 0 },
				async parser =>
				{
					invocations++;
					return stage == "invoker" && invocations > 1
									? Option<CallState>.FromOption((await parser.FunctionParse(MarkupText.Plain("ulocal(me/PREDICATE)")))!)
									: Option<CallState>.FromOption(CallState.Empty);
				}), true));
		var parser = new MUSHCodeParser(original.Logger, original.FunctionLibrary, commands, original.Configuration, Factory.Services);
		try
		{
			var actor = (await mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known();
			var attributes = Get<IAttributeService>();
			await attributes.SetAttributeAsync(actor, actor, "PREDICATE", MarkupText.Plain(mode switch
			{
				"syntax" => "[",
				"literal" => "#-1 PARSER FAILURE ordinary text",
				_ => "0"
			}));
			var condition = stage == "condition" ? "[ulocal(me/PREDICATE)]" : "[setq(retrycounter,inc(%q<retrycounter>))][lt(%q<retrycounter>,2)]";
			var arguments = stage == "argument" ? "=[ulocal(me/PREDICATE)]" : "";
			await attributes.SetAttributeAsync(actor, actor, "CALLBACK", MarkupText.Plain($"think [setq(retrycounter,0)];{name};@retry {condition}{arguments}"));
			var sessions = Get<IInputSessionService>();
			await parser.CommandParse(player.Handle, connections, MarkupText.Plain("@input/start me/CALLBACK=Answer:,120"));
			var session = sessions.GetCapturing(player.Handle)!;
			await Assert.That(session).IsNotNull();
			var result = await sessions.DeliverAsync(parser, session, MarkupText.Plain("reply"));
			Console.WriteLine($"retry/{stage}/{mode}: errors={result?.HadErrors}, invocations={invocations}");
			await Assert.That(invocations).IsEqualTo(stage == "condition" ? 1 : 2);
			await Assert.That(result?.HadErrors).IsEqualTo(mode == "syntax");
			await Assert.That(sessions.GetCapturing(player.Handle)?.Id).IsEqualTo(mode == "syntax" ? null : session.Id);
		}
		finally { await connections.Disconnect(player.Handle); }
	}
}
