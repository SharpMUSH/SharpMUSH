using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Parser;

public class LockOperandRangeTests
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
	public async Task OverflowingObjectOperandRejectsWholeExpression(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		foreach (var reference in new[] { "#2147483648:1", "#1:9223372036854775808", "#2147483648" })
		{
			var operand = prefix + reference;
			foreach (var expression in new[] { operand, "!" + operand, operand + " | #TRUE", "#TRUE | (" + operand + " & #FALSE)" })
			{
				await Assert.That(await Locks.Compile(expression)(god, god)).IsFalse();
				await Assert.That(Locks.Validate(expression, god)).IsFalse();
				await Assert.That(Locks.Normalize(expression)).IsEqualTo(expression);
			}
		}
	}

	[Test]
	[Arguments("")]
	[Arguments("=")]
	[Arguments("$")]
	[Arguments("+")]
	[Arguments("@")]
	public async Task MaximumObjectOperandRemainsValid(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		var expression = $"{prefix}#{int.MaxValue}:{long.MaxValue}";
		await Assert.That(Locks.Validate(expression, god)).IsTrue();
		await Assert.That(await Locks.Compile(expression)(god, god)).IsFalse();
		await Assert.That(Locks.Normalize(expression)).IsEqualTo(prefix == "@" ? expression + "/Basic" : expression);
	}

	[Test]
	[Arguments("#2147483648:1")]
	[Arguments("#1:9223372036854775808")]
	public async Task OversizedReferenceTextRemainsLiteralInAttributeAndEvalLocks(string value)
	{
		var created = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"create(RangeLiteral_{Guid.NewGuid():N})"));
		var reference = created!.Message!.ToPlainText();
		var obj = (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(reference)))).Expect<AnySharpObject>();
		await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"attrib_set({reference}/VALUE,{value})"));
		foreach (var separator in new[] { ":", "/" })
		{
			var expression = "VALUE" + separator + value;
			await Assert.That(Locks.Validate(expression, obj)).IsTrue();
			await Assert.That(Locks.Normalize(expression)).IsEqualTo(expression);
			await Assert.That(await Locks.Compile(expression)(obj, obj)).IsTrue();
		}
	}
}
