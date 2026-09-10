using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class AttributeResultCoverageTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("ulocal", "syntax")]
	[Arguments("ulocal", "literal")]
	[Arguments("ulocal", "success")]
	[Arguments("pfun", "syntax")]
	[Arguments("pfun", "literal")]
	[Arguments("pfun", "success")]
	[Arguments("zfun", "syntax")]
	[Arguments("zfun", "literal")]
	[Arguments("zfun", "success")]
	[Arguments("eval", "syntax")]
	[Arguments("eval", "literal")]
	[Arguments("eval", "success")]
	[Arguments("get_eval", "syntax")]
	[Arguments("get_eval", "literal")]
	[Arguments("get_eval", "success")]
	[Arguments("edefault", "syntax")]
	[Arguments("edefault", "literal")]
	[Arguments("edefault", "success")]
	[Arguments("default-fallback", "syntax")]
	[Arguments("default-fallback", "literal")]
	[Arguments("default-fallback", "success")]
	[Arguments("edefault-fallback", "syntax")]
	[Arguments("edefault-fallback", "literal")]
	[Arguments("edefault-fallback", "success")]
	[Arguments("udefault-fallback", "syntax")]
	[Arguments("udefault-fallback", "literal")]
	[Arguments("udefault-fallback", "success")]
	[Arguments("uldefault-fallback", "syntax")]
	[Arguments("uldefault-fallback", "literal")]
	[Arguments("uldefault-fallback", "success")]
	[Arguments("udefault-body", "syntax")]
	[Arguments("udefault-body", "literal")]
	[Arguments("udefault-body", "success")]
	[Arguments("uldefault-body", "syntax")]
	[Arguments("uldefault-body", "literal")]
	[Arguments("uldefault-body", "success")]
	[Arguments("udefault-selector", "syntax")]
	[Arguments("udefault-selector", "literal")]
	[Arguments("udefault-selector", "success")]
	[Arguments("uldefault-selector", "syntax")]
	[Arguments("uldefault-selector", "literal")]
	[Arguments("uldefault-selector", "success")]
	[Arguments("regedit", "syntax")]
	[Arguments("regedit", "literal")]
	[Arguments("regedit", "success")]
	[Arguments("regeditall", "syntax")]
	[Arguments("regeditall", "literal")]
	[Arguments("regeditall", "success")]
	[Arguments("regediti", "syntax")]
	[Arguments("regediti", "literal")]
	[Arguments("regediti", "success")]
	[Arguments("regedit-input", "syntax")]
	[Arguments("regedit-input", "literal")]
	[Arguments("regedit-input", "success")]
	[Arguments("regedit-pattern", "syntax")]
	[Arguments("regedit-pattern", "literal")]
	[Arguments("regedit-pattern", "success")]
	[Arguments("udefault-argument", "syntax")]
	[Arguments("udefault-argument", "literal")]
	[Arguments("udefault-argument", "success")]
	[Arguments("uldefault-argument", "syntax")]
	[Arguments("uldefault-argument", "literal")]
	[Arguments("uldefault-argument", "success")]
	[Arguments("regeditalli", "syntax")]
	[Arguments("regeditalli", "literal")]
	[Arguments("regeditalli", "success")]
	[Arguments("default-selector", "syntax")]
	[Arguments("default-selector", "literal")]
	[Arguments("default-selector", "success")]
	[Arguments("edefault-selector", "syntax")]
	[Arguments("edefault-selector", "literal")]
	[Arguments("edefault-selector", "success")]
	public async Task ExistingAttributeFunctionsPreserveNestedFailure(string kind, string mode)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		var id = await TestIsolationHelpers.CreateTestPlayerAsync(Factory.Services, mediator, "AttrResult");
		var actor = (await mediator.Send(new GetObjectNodeQuery(id))).Known();
		var god = (await mediator.Send(new GetObjectNodeQuery(Factory.ExecutorDBRef))).Known();
		if (kind == "pfun") await mediator.Send(new SetObjectParentCommand(actor, god));
		if (kind == "zfun") await mediator.Send(new SetObjectZoneCommand(actor, god));
		var value = mode switch { "syntax" => "[", "literal" => "#-1 EXCEPTION: ordinary text", _ => "ok" };
		var selector = kind.EndsWith("-selector", StringComparison.Ordinal);
		if (selector && mode == "success") value = $"{id}/TARGET";
		await attributes.SetAttributeAsync(actor, actor, "BODY", MarkupText.Plain(value));
		await attributes.SetAttributeAsync(actor, actor, "TARGET", MarkupText.Plain("selected"));
		var call = $"ufun({id}/BODY)";
		var expression = kind switch
		{
			"eval" => $"eval({id},BODY)",
			"get_eval" => $"get_eval({id}/BODY)",
			"edefault" => $"edefault({id}/BODY,unused)",
			"udefault-body" => $"udefault({id}/BODY,unused)",
			"uldefault-body" => $"uldefault({id}/BODY,unused)",
			"udefault-argument" => $"udefault({id}/TARGET,unused,{call})",
			"uldefault-argument" => $"uldefault({id}/TARGET,unused,{call})",
			"default-selector" => $"default({call},unused)",
			"edefault-selector" => $"edefault({call},unused)",
			"udefault-selector" => $"udefault({call},unused)",
			"uldefault-selector" => $"uldefault({call},unused)",
			"regedit-input" => $"regedit({call},ZZZZ,unused)",
			"regedit-pattern" => $"regedit(x,{call},unused)",
			_ when kind.EndsWith("-fallback", StringComparison.Ordinal) => $"{kind.Replace("-fallback", "")}({id}/MISSING,{call})",
			_ when kind.StartsWith("regedit", StringComparison.Ordinal) => $"{kind}(x,x,{call})",
			_ => $"{kind}({id}/BODY)"
		};
		var parser = Factory.FunctionParser.FromState(ParserState.RootFor(id));
		var result = await parser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.HadErrors).IsEqualTo(mode == "syntax");
		if (mode != "syntax")
		{
			if (selector && mode == "literal") await Assert.That(result.Message!.ToPlainText()).StartsWith("#-1");
			else await Assert.That(result.Message!.ToPlainText()).IsEqualTo(selector || kind.EndsWith("-argument", StringComparison.Ordinal) ? "selected" : kind == "regedit-pattern" ? "x" : value);
		}
	}

	[Test]
	[Arguments("subj", "syntax")]
	[Arguments("subj", "literal")]
	[Arguments("subj", "success")]
	[Arguments("obj", "syntax")]
	[Arguments("obj", "literal")]
	[Arguments("obj", "success")]
	[Arguments("poss", "syntax")]
	[Arguments("poss", "literal")]
	[Arguments("poss", "success")]
	[Arguments("aposs", "syntax")]
	[Arguments("aposs", "literal")]
	[Arguments("aposs", "success")]
	public async Task ConfiguredPronounFunctionsPreserveNestedFailure(string function, string mode)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		var id = await TestIsolationHelpers.CreateTestPlayerAsync(Factory.Services, mediator, "PronounResult");
		var actor = (await mediator.Send(new GetObjectNodeQuery(id))).Known();
		var value = mode switch { "syntax" => "[", "literal" => "#-1 EXCEPTION: ordinary text", _ => "ok" };
		await attributes.SetAttributeAsync(actor, actor, "PRONOUN", MarkupText.Plain(value));
		var current = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(current with
		{
			Attribute = current.Attribute with
			{
				SubjectivePronounAttribute = $"{id}/PRONOUN",
				ObjectivePronounAttribute = $"{id}/PRONOUN",
				PossessivePronounAttribute = $"{id}/PRONOUN",
				AbsolutePossessivePronounAttribute = $"{id}/PRONOUN"
			}
		});
		var functions = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Functions.Functions>(Factory.Services, options);
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var parser = (original with { FunctionLibrary = functions.Get() }).FromState(ParserState.RootFor(id));
		var result = await parser.FunctionParse(MarkupText.Plain($"{function}(me)"));
		await Assert.That(result!.HadErrors).IsEqualTo(mode == "syntax");
		if (mode != "syntax") await Assert.That(result.Message!.ToPlainText()).IsEqualTo(value);
	}
}
