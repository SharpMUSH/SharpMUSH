using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class RealityDebugOutputTests
{
	[ClassDataSource<RealityGameServerFactory>(Shared = SharedType.PerTestSession)]
	public required RealityGameServerFactory Factory { get; init; }
	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();

	[Test, NotInParallel]
	[Arguments(false, false, false)]
	[Arguments(false, true, false)]
	[Arguments(true, false, false)]
	[Arguments(false, false, true)]
	[Arguments(false, true, true)]
	[Arguments(true, false, true)]
	public async Task DebugOwnerAndForwardingRespectExecutingObject(bool disabled, bool visible, bool forward)
	{
		await CheckDebug(disabled, visible, forward, false);
	}

	[Test, NotInParallel]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	public async Task SubstitutionDebugRespectsExecutingObject(bool disabled, bool visible)
	{
		await CheckDebug(disabled, visible, false, true);
	}

	[Test, NotInParallel]
	[Arguments(true)]
	[Arguments(false)]
	public async Task DebugForwardingUsesRecipientPerception(bool recipientCanReceive)
		=> await CheckDebug(false, true, true, false, true, recipientCanReceive);

	private async Task CheckDebug(bool disabled, bool visible, bool forward, bool substitution,
		bool asymmetric = false, bool recipientCanReceive = true)
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var owner = (await objects.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var room = await owner.Location.WithCancellation(CancellationToken.None);
		var source = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("debug source", room, owner, room)))).Expect<SharpThing>();
		var receiver = forward
			? (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("debug recipient", room, owner, room)))).Expect<AnySharpObject>().Object()
			: owner.Object;
		if (forward) await Get<IAttributeService>().SetAttributeAsync(owner, source, "DEBUGFORWARDLIST", MarkupText.Plain(receiver.DBRef.ToString()));
		try
		{
			await policy.SaveObjectAsync(source.Object.Id!, ObjectReality.Default(source.Object.DBRef) with { Transmit = [visible ? "normal" : "ghost"] }, default);
			if (asymmetric)
			{
				await policy.SaveObjectAsync(source.Object.Id!, ObjectReality.Default(source.Object.DBRef) with
				{ Receive = ["normal"], Transmit = ["ghost"] }, default);
				await policy.SaveObjectAsync(receiver.Id!, ObjectReality.Default(receiver.DBRef) with
				{ Receive = [recipientCanReceive ? "ghost" : "normal"], Transmit = ["ghost"] }, default);
			}
			await policy.SaveConfigurationAsync(new(1, !disabled, ["normal", "ghost"]), default);
			var output = new HttpResponseContext();
			using (Get<IHttpOutputCapture>().BeginCapture(receiver.Key, output))
			{
				var parser = Factory.CommandParser.FromState(ParserState.RootFor(source.Object.DBRef) with { Flags = ParserStateFlags.Debug });
				if (substitution)
					await parser.CommandListParse(MarkupText.Plain("@pemit me=%# secret-substitution"));
				else
					await parser.FunctionParse(MarkupText.Plain("add(137,246)"));
			}
			await Assert.That(output.Body.ToString().Contains(substitution ? "secret-substitution" : "add(137,246)")).IsEqualTo(disabled || visible && recipientCanReceive);
		}
		finally { await policy.SaveConfigurationAsync(original, default); }
	}
}
