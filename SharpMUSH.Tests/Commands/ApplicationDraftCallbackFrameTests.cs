using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ApplicationDraftCallbackFrameTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private Task Cmd(string code) => Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(code)).AsTask();
	private async Task<string> Read(string attribute) => (await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain($"get({attribute})"))).ToPlainText();

	[Test]
	[Arguments(false, "review")]
	[Arguments(true, "review")]
	[Arguments(false, "file")]
	[Arguments(true, "file")]
	public async Task DraftCallbacksOwnTheirInputsAndIdentityAndRestoreTheCallingFrame(bool nested, string action)
	{
		var target = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "DraftFrame");
		await Cmd($"&FORM {target}=[{{\"key\":\"text\",\"type\":\"text\",\"default\":\"literal\"}}]");
		foreach (var suffix in new[] { "VALIDATE", "FILE" })
		{
			await Cmd($"&FORM`{suffix} {target}=&{suffix}OUT {target}=[hasarg(draft_actor)]|[hasarg(draft_actor,supplied)]|[hasarg(draft_fields)]|[hasarg(draft_fields,supplied)]|[hasarg(draft_action)]|[hasarg(draft_action,supplied)]|[hasarg(parent)]|[hasarg(parent,supplied)]|[hasarg(0,supplied)]|%<parent>|%<draft_actor>|%<draft_fields>|%<draft_action>|%=|%0; think setq(appdraft_result,json(object,ok,json(boolean,true)))");
		}
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(Connections);
		await Connections.Bind(handle, Factory.ExecutorDBRef);
		try
		{
			var environment = new Dictionary<string, CallState> { ["parent"] = new("inherited"), ["0"] = new("parent-zero") };
			var parent = ParserState.RootFor(Factory.ExecutorDBRef) with
			{
				Handle = handle,
				ConnectionSessionId = Connections.Get(handle)!.Metadata.GetValueOrDefault("SessionId"),
				EnvironmentRegisters = environment
			};
			var parser = Factory.CommandParser.FromState(parent);
			await parser.CommandListParse(MarkupText.Plain($"@appdraft {target}/FORM=new"));
			if (action == "file") await parser.CommandListParse(MarkupText.Plain($"@appdraft {target}/FORM=review"));
			var snapshot = "%<parent>|[hasarg(parent,supplied)]|[hasarg(draft_fields,supplied)]|%=|%0";
			var body = $"&BEFORE {target}={snapshot}; @appdraft {target}/FORM={action}; &AFTER {target}={snapshot}";
			if (nested)
			{
				await Cmd($"&PARENT {target}={body}");
				await parser.CommandListParse(MarkupText.Plain($"@include/args {target}/PARENT=parent,inherited"));
			}
			else await parser.CommandListParse(MarkupText.Plain(body));

			var before = await Read($"{target}/BEFORE");
			await Assert.That(await Read($"{target}/AFTER")).IsEqualTo(before);
			await Assert.That(before).StartsWith(nested ? "inherited|1|0|" : "inherited|0|0|");
			foreach (var suffix in action == "file" ? new[] { "VALIDATE", "FILE" } : new[] { "VALIDATE" })
			{
				var identity = $"#{target.Number}/FORM`{suffix}";
				await Assert.That(await Read($"{target}/{suffix}OUT"))
					.IsEqualTo($"1|1|1|1|1|1|1|0|0|inherited|{Factory.ExecutorDBRef}|{{\"text\":\"literal\"}}|{action}|{identity}|{before.Split('|')[^1]}");
			}
			await Assert.That(parser.CurrentState.Arguments).IsSameReferenceAs(parent.Arguments);
			await Assert.That(parser.CurrentState.SuppliedArgumentKeys).IsNull();
			await Assert.That(parser.CurrentState.CurrentEvaluation).IsNull();
			await Assert.That(parser.CurrentState.EnvironmentRegisters).IsSameReferenceAs(environment);
		}
		finally { await Connections.Disconnect(handle); }
	}
}
