using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Parser;

public class StampedLockOperandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IBooleanExpressionParser Locks => Factory.Services.GetRequiredService<IBooleanExpressionParser>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	[Test]
	[Arguments("")]
	[Arguments("=")]
	[Arguments("$")]
	[Arguments("+")]
	[Arguments("@")]
	public async Task StampedOperandPreservesIdentity(string prefix)
	{
		var created = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"create(StampedLock_{Guid.NewGuid():N})"));
		var target = (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(created!.Message!.ToPlainText())))).Expect<AnySharpObject>();
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		if (prefix == "+")
			await Mediator.Send(new MoveObjectCommand(target.MinusRoom(), god.AsContainer, (await target.Where()).Object().DBRef));
		var unlocker = prefix is "" or "=" ? target : god;
		var identity = target.Object().DBRef;
		await Assert.That(identity.CreationMilliseconds.HasValue).IsTrue();
		var expression = $"{prefix}{identity}";
		await Assert.That(Locks.Validate(expression, god)).IsTrue();
		await Assert.That(await Locks.Compile(expression)(god, unlocker)).IsTrue();
		var normalized = Locks.Normalize(expression);
		await Assert.That(normalized).IsEqualTo(prefix == "@" ? expression + "/Basic" : expression);
		await Assert.That(await Locks.Compile(normalized)(god, unlocker)).IsTrue();

		// A stored key for an earlier occupant of this number must reject the live identity.
		var previousIdentity = new DBRef(identity.Number, identity.CreationMilliseconds - 1);
		await Assert.That(await Locks.Compile($"{prefix}{previousIdentity}")(god, unlocker)).IsFalse();
		await Assert.That(await Locks.Compile($"{prefix}#{identity.Number}")(god, unlocker)).IsTrue();
		if (prefix == "@")
		{
			await Assert.That(await Locks.Compile($"@{identity}/Basic")(god, unlocker)).IsTrue();
			await Assert.That(await Locks.Compile($"@{previousIdentity}/Basic")(god, unlocker)).IsFalse();
		}
	}

	[Test]
	[Arguments("#123:456", "#123:456")]
	[Arguments("=#123:456", "=#123:456")]
	[Arguments("$#123:456", "$#123:456")]
	[Arguments("+#123:456", "+#123:456")]
	[Arguments("@#123:456/Use", "@#123:456/Use")]
	[Arguments("race:Elf", "RACE:Elf")]
	[Arguments("ref:#123:456", "REF:#123:456")]
	[Arguments("ref/#123:456", "REF/#123:456")]
	[Arguments("name^Test*", "NAME^TEST*")]
	[Arguments("=attr:value", "=attr:value")]
	[Arguments("#123:456abc", "#123:456abc")]
	[Arguments("!(#123:456 & #FALSE) | #TRUE", "!(#123:456 & #FALSE) | #TRUE")]
	public async Task NormalizationKeepsOperandAndSeparatorBoundaries(string expression, string expected)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		await Assert.That(Locks.Validate(expression, god)).IsTrue();
		await Assert.That(Locks.Normalize(expression)).IsEqualTo(expected);
	}

	[Test]
	public async Task StampedValuesRemainAttributeAndEvaluationComparisons()
	{
		var created = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"create(StampedValue_{Guid.NewGuid():N})"));
		var target = (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(created!.Message!.ToPlainText())))).Expect<AnySharpObject>();
		var identity = target.Object().DBRef.ToString();
		await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"attrib_set({identity}/RACE,Elf)"));
		await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"attrib_set({identity}/REF,{identity})"));
		await Assert.That(await Locks.Compile("RACE:Elf")(target, target)).IsTrue();
		await Assert.That(await Locks.Compile("RACE:Orc")(target, target)).IsFalse();
		await Assert.That(await Locks.Compile($"REF:{identity}")(target, target)).IsTrue();
		await Assert.That(await Locks.Compile($"REF/{identity}")(target, target)).IsTrue();
		await Assert.That(await Locks.Compile($"REF:{identity}0")(target, target)).IsFalse();
		await Assert.That(await Locks.Compile($"REF/{identity}0")(target, target)).IsFalse();
		await Assert.That(await Locks.Compile($"#FALSE & {identity} | #TRUE")(target, target)).IsTrue();
		await Assert.That(await Locks.Compile($"!({identity} | #FALSE)")(target, target)).IsFalse();
	}
}
