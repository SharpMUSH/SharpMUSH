using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class LaneBcProbe3
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private Mediator.IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>();

	private static void Log(string line) => File.AppendAllText("/tmp/probe3-out.txt", line + "\n");

	[Test]
	public async Task ProbeOobDeliveries()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ProbeOobM");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ProbeOobT");
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@power {mortal.DbRef}=Send_OOB"));

		var before = WebAppFactoryArg.Notifications.DeliveryCountFor(mortal.DbRef);
		await WebAppFactoryArg.CommandParser.CommandParse(mortal.Handle, ConnectionService,
			MarkupText.Plain($"think oob({target.DbRef}, testpkg)"));

		var all = WebAppFactoryArg.Notifications.DeliveriesFor(mortal.DbRef).Skip(before).ToList();
		Log($"GRANTED mortal={mortal.DbRef} deliveries={all.Count}");
		foreach (var d in all) Log($"  sender={d.Sender?.ToString() ?? "<null>"} type={d.Type} msg=[{d.Message}]");
		Log($"  self-sent={all.Count(d => d.Sender == mortal.DbRef)}");

		// And the table arithmetic, both line lengths.
		foreach (var e in new[] { "table(a b c d,5,12,%b,--)", "table(a b c d,5,16,%b,--)" })
		{
			var r = (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain(e)))!.Message!;
			Log($"TABLE {e} => [{r.ToPlainText().Replace("\n", "\\n")}]");
		}
	}
}
