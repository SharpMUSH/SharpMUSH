using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
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
}
