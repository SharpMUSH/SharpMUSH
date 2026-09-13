using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class LockDecompileReplayTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private ILockService Locks => Factory.Services.GetRequiredService<ILockService>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();

	private async Task<AnySharpObject> Object(DBRef reference)
		=> (await Mediator.Send(new GetObjectNodeQuery(reference))).Expect<AnySharpObject>();

	private async Task<string> Read(string expression)
		=> (await Factory.FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();

	private async Task<string[]> Output(string command)
	{
		var recipient = Factory.ExecutorDBRef;
		var count = Factory.Notifications.CountFor(recipient);
		await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command));
		return Factory.Notifications.For(recipient).Skip(count).ToArray();
	}

	[Test]
	public async Task InvalidLockDiagnosticKeepsDecompilePrefix()
	{
		var created = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"create(InvalidLock{Guid.NewGuid():N})"));
		var reference = DBRef.Parse(created!.Message!.ToPlainText());
		var target = await Object(reference);
		var invalid = new SharpLockData("=me");
		await Factory.Services.GetRequiredService<ISharpDatabase>().SetLockAsync(target.Object(), "Basic", invalid);
		target.Object().WithLock("Basic", invalid);
		var output = await Output($"@decompile/db {reference}=PREFIX:");
		await Assert.That(output).Contains("PREFIX:@@ Invalid Basic lock omitted; replace it explicitly before decompiling.");
		await Assert.That(output.Any(line => line.Contains("@lock/", StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	[Arguments("Basic", "#TRUE&(!#FALSE|=me)", true, true)]
	[Arguments("Basic", "=me|NAME^ReplayPass*", true, false)]
	[Arguments("user:Replay", "NAME^%n[me]", false, false)]
	[Arguments("user:Replay", "NAME^A  B", false, true)]
	[Arguments("user:Replay", "VALUE:http://example.test/path", false, true)]
	[Arguments("user:Replay", "VALUE/http://example.test/path", true, true)]
	[Arguments("user:Replay", "VALUE:%n[me]", false, true)]
	public async Task DecompileCommandsRestoreExpressionMeaningAndFlags(string requestedName, string expression,
		bool godPasses, bool targetPasses)
	{
		var name = expression == "NAME^A  B" ? "A  B" : $"Replay_{Guid.NewGuid():N}";
		var createName = expression == "NAME^A  B" ? "A%b%bB" : name;
		var created = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"create({createName})"));
		var reference = DBRef.Parse(created!.Message!.ToPlainText());
		var target = await Object(reference);
		await Assert.That(target.Object().Name).IsEqualTo(name);
		var god = await Object(new DBRef(1));
		var attribute = expression.Contains("http://", StringComparison.Ordinal) ? "http://example.test/path" : "%n[me]";
		await Mediator.Send(new SetAttributeCommand(reference, ["VALUE"], MarkupText.Plain(attribute), god.Expect<SharpPlayer>()));
		(await Locks.SetAsync(god, target, requestedName, expression)).Expect<Success>();
		var lockName = requestedName.StartsWith("user:", StringComparison.Ordinal) ? requestedName[5..].ToUpperInvariant() : requestedName;
		(await Locks.SetFlagsAsync(god, await Object(reference), lockName, "visual no_clone locked")).Expect<Success>();
		if (lockName == "Basic")
			(await Locks.SetFlagsAsync(god, await Object(reference), lockName, "!no_inherit")).Expect<Success>();

		var lockTarget = $"#{reference.Number}/{lockName}";
		var original = await Read($"lock({lockTarget})");
		var originalFlags = await Read($"lockflags({lockTarget})");
		await Assert.That(await Read($"elock({lockTarget},#1)")).IsEqualTo(godPasses ? "1" : "0");
		await Assert.That(await Read($"elock({lockTarget},#{reference.Number})")).IsEqualTo(targetPasses ? "1" : "0");

		var output = await Output($"@decompile/db #{reference.Number}");
		var commands = output.Where(line => line.StartsWith("@lock/", StringComparison.Ordinal)
			|| line.StartsWith("@lset ", StringComparison.Ordinal)).ToArray();
		await Assert.That(commands.Any(line => line.StartsWith(lockName == "Basic" ? "@lock/Basic " : "@lock/user:REPLAY ", StringComparison.Ordinal))).IsTrue();
		await Assert.That(commands.Any(line => line.StartsWith("@lset ", StringComparison.Ordinal))).IsTrue();
		if (lockName == "Basic") await Assert.That(commands).Contains($"@lset #{reference.Number}/Basic=!no_inherit");

		await Output($"@unlock/{lockName} #{reference.Number}");
		await Assert.That(await Read($"lock({lockTarget})")).IsEqualTo("*UNLOCKED*");
		foreach (var command in commands) await Output(command);

		await Assert.That(await Read($"lock({lockTarget})")).IsEqualTo(original);
		await Assert.That(await Read($"lockflags({lockTarget})")).IsEqualTo(originalFlags);
		await Assert.That(await Read($"elock({lockTarget},#1)")).IsEqualTo(godPasses ? "1" : "0");
		await Assert.That(await Read($"elock({lockTarget},#{reference.Number})")).IsEqualTo(targetPasses ? "1" : "0");
	}
}
