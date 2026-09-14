using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Parser;

public class LockBindingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IBooleanExpressionParser Locks => Factory.Services.GetRequiredService<IBooleanExpressionParser>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	private async Task<AnySharpObject> CreateSetter()
	{
		var created = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"create(Binding_{Guid.NewGuid():N})"));
		return (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(created!.Message!.ToPlainText())))).Expect<AnySharpObject>();
	}

	[Test]
	[Arguments("=")]
	[Arguments("$")]
	[Arguments("+")]
	[Arguments("@")]
	public async Task PrefixedObjectNamesMayContainLiteralColon(string prefix)
	{
		var name = $"Colon_{Guid.NewGuid():N}: Key";
		var created = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"create({name})"));
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		await Assert.That((await Locks.BindAsync(prefix + name, god)).Expect<string>())
			.IsEqualTo(prefix + created!.Message!.ToPlainText() + (prefix == "@" ? "/Basic" : ""));
	}

	[Test]
	public async Task ObjectBindingPrefersThingOverSameNamedPlayer()
	{
		var connections = Factory.Services.GetRequiredService<IConnectionService>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, connections, "LockPreference");
		var created = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"create({player.Name})"));
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		await Assert.That((await Locks.BindAsync(player.Name, god)).Expect<string>()).IsEqualTo(created!.Message!.ToPlainText());
	}

	[Test]
	public async Task AttributePatternEscapesMatchLiteralOperators()
	{
		var setter = await CreateSetter();
		await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"attrib_set(#{setter.Object().DBRef.Number}/VALUE,A!B)"));
		var bound = (await Locks.BindAsync(@"VALUE:A\!B", setter)).Expect<string>();
		await Assert.That(await Locks.Compile(bound)(setter, setter)).IsTrue();
	}

	[Test]
	[Arguments(":")]
	[Arguments("/")]
	public async Task LiteralDelimitersRemainPartOfEvaluatedValue(string separator)
	{
		var setter = await CreateSetter();
		await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"attrib_set(#{setter.Object().DBRef.Number}/VALUE,http://example.test/path)"));
		var bound = (await Locks.BindAsync("VALUE" + separator + "http://example.test/path", setter)).Expect<string>();
		await Assert.That(await Locks.Compile(bound)(setter, setter)).IsTrue();
	}

	[Test]
	[Arguments("VALUE:http://example.test/path")]
	[Arguments("VALUE/http://example.test/path")]
	[Arguments("NAME^Mixed:Case/Path")]
	[Arguments("NAME^Prefix^Suffix")]
	[Arguments("VALUE:a : b / c")]
	public async Task LiteralDelimitersSurviveBindingAndReadback(string expression)
	{
		var setter = await CreateSetter();
		var bound = (await Locks.BindAsync(expression, setter)).Expect<string>();
		await Assert.That(bound).IsEqualTo(expression);
		await Assert.That(await Locks.RenderAsync(bound, setter, LockRenderMode.Readback)).IsEqualTo(expression);
	}

	[Test]
	public async Task DecompilePreservesRepeatedLiteralSpaces()
	{
		var setter = await CreateSetter();
		await Assert.That(await Locks.RenderAsync("NAME^A  B", setter, LockRenderMode.Decompile)).IsEqualTo(@"NAME\^A%b%bB");
	}

	[Test]
	[Arguments("A B", "A B")]
	[Arguments("A!B", @"A\!B")]
	public async Task LiteralPatternsKeepSpacesAndEscapes(string suffix, string patternSuffix)
	{
		var prefix = $"Pattern_{Guid.NewGuid():N}_";
		var created = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"create({prefix}{suffix})"));
		var obj = (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(created!.Message!.ToPlainText())))).Expect<AnySharpObject>();
		var expression = "NAME^" + prefix + patternSuffix;
		var bound = (await Locks.BindAsync(expression, obj)).Expect<string>();
		await Assert.That(bound).IsEqualTo(expression);
		await Assert.That(await Locks.Compile(bound)(obj, obj)).IsTrue();
		await Assert.That(await Locks.RenderAsync(bound, obj, LockRenderMode.Readback)).IsEqualTo(expression);
	}

	[Test]
	public async Task ExamineHidesReferenceWhenLinkLockDeniesViewer()
	{
		var connections = Factory.Services.GetRequiredService<IConnectionService>();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, connections, "LockViewer");
		var viewer = (await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Expect<AnySharpObject>();
		var target = await CreateSetter();
		var reference = $"#{target.Object().DBRef.Number}";
		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@set {reference}=LINK_OK"));
		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@lock/link {reference}=#FALSE"));
		await Assert.That(await Locks.RenderAsync(reference, viewer, LockRenderMode.Examine)).IsEqualTo(target.Object().Name);
		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@lock/link {reference}=#TRUE"));
		await Assert.That(await Locks.RenderAsync(reference, viewer, LockRenderMode.Examine)).Contains("(" + reference);
	}

	[Test]
	[Arguments("A B", "A B")]
	[Arguments("A!B", @"A\!B")]
	[Arguments("A:B", @"A\:B")]
	public async Task BindingResolvesSpacedAndEscapedObjectNames(string suffix, string operandSuffix)
	{
		var prefix = $"Binding_{Guid.NewGuid():N}_";
		var created = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"create({prefix}{suffix})"));
		var reference = created!.Message!.ToPlainText();
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		await Assert.That((await Locks.BindAsync("=" + prefix + operandSuffix, god)).Expect<string>()).IsEqualTo("=" + reference);
	}

	[Test]
	[Arguments("")]
	[Arguments("=")]
	[Arguments("+")]
	[Arguments("$")]
	[Arguments("@")]
	public async Task MeCapturesExecutorRatherThanOwner(string prefix)
	{
		var setter = await CreateSetter();
		var bound = (await Locks.BindAsync(prefix + "me", setter)).Expect<string>();
		await Assert.That(bound).IsEqualTo($"{prefix}#{setter.Object().DBRef.Number}" + (prefix == "@" ? "/Basic" : ""));
		await Assert.That(Locks.IsBound(bound)).IsTrue();
	}

	[Test]
	public async Task BindingLeavesLiteralValuesIntact()
	{
		var setter = await CreateSetter();
		var expression = "=me | (VALUE:me & VALUE/me & NAME^MixedCase*)";
		var bound = (await Locks.BindAsync(expression, setter)).Expect<string>();
		await Assert.That(bound).IsEqualTo($"=#{setter.Object().DBRef.Number} | (VALUE:me & VALUE/me & NAME^MixedCase*)");
	}

	[Test]
	[Arguments("#TRUE | !missing_unique_binding_target")]
	[Arguments("=#2147483647")]
	[Arguments("#TRUE^")]
	public async Task BindingRejectsUnresolvedOrInvalidOperands(string expression)
	{
		var setter = await CreateSetter();
		await Assert.That(await Locks.BindAsync(expression, setter) is Error<string>).IsTrue();
	}

	[Test]
	public async Task BindingPreservesExplicitIdentityStamp()
	{
		var setter = await CreateSetter();
		var expression = "=" + setter.Object().DBRef;
		await Assert.That((await Locks.BindAsync(expression, setter)).Expect<string>()).IsEqualTo(expression);
	}

	[Test]
	public async Task BindingHonorsCancellation()
	{
		var setter = await CreateSetter();
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();
		await Assert.That(async () => await Locks.BindAsync("me", setter, cancellation.Token)).Throws<OperationCanceledException>();
	}

	[Test]
	public async Task RenderModesChangeOnlyObjectOperands()
	{
		var setter = await CreateSetter();
		var reference = $"#{setter.Object().DBRef.Number}";
		var expression = $"={reference} | VALUE:{reference}";
		await Assert.That(await Locks.RenderAsync(expression, setter, LockRenderMode.Readback)).IsEqualTo($"={reference}|VALUE:{reference}");
		await Assert.That(await Locks.RenderAsync(expression, setter, LockRenderMode.Decompile)).IsEqualTo($"=me|VALUE:{reference}");
		await Assert.That(await Locks.RenderAsync(expression, setter, LockRenderMode.Examine)).Contains(setter.Object().Name);
	}

	[Test]
	public async Task DecompileEscapesSoftcodeAndInvalidExpressionsAreMarked()
	{
		var setter = await CreateSetter();
		await Assert.That(await Locks.RenderAsync("NAME^%n[me]", setter, LockRenderMode.Decompile)).IsEqualTo(@"NAME\^\%n\[me\]");
		await Assert.That(await Locks.RenderAsync("!me", setter, LockRenderMode.Examine)).IsEqualTo("*INVALID LOCK*: !me");
	}
}
