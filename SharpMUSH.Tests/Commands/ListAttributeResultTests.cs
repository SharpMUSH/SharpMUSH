using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ListAttributeResultTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("revwords(a|b,|,:)", "b:a")]
	[Arguments("revwords(a b, ,|)", "b|a")]
	[Arguments("revwords(a b)", "b a")]
	[Arguments("revwords(single)", "single")]
	public async Task ReverseWordsUsesDocumentedSeparators(string expression, string expected)
	{
		var result = await Factory.FunctionParser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.Message!.Text).IsEqualTo(expected);
		await Assert.That(result.HadErrors).IsFalse();
	}

	[Test]
	[Arguments("filter", "attribute", "syntax")]
	[Arguments("filter", "attribute", "literal")]
	[Arguments("filter", "attribute", "success")]
	[Arguments("filter", "lambda", "syntax")]
	[Arguments("filter", "lambda", "literal")]
	[Arguments("filter", "lambda", "success")]
	[Arguments("filterbool", "attribute", "syntax")]
	[Arguments("filterbool", "attribute", "literal")]
	[Arguments("filterbool", "attribute", "success")]
	[Arguments("filterbool", "lambda", "syntax")]
	[Arguments("filterbool", "lambda", "literal")]
	[Arguments("filterbool", "lambda", "success")]
	[Arguments("fold", "attribute", "syntax")]
	[Arguments("fold", "attribute", "literal")]
	[Arguments("fold", "attribute", "success")]
	[Arguments("fold", "lambda", "syntax")]
	[Arguments("fold", "lambda", "literal")]
	[Arguments("fold", "lambda", "success")]
	[Arguments("map", "attribute", "syntax")]
	[Arguments("map", "attribute", "literal")]
	[Arguments("map", "attribute", "success")]
	[Arguments("map", "lambda", "syntax")]
	[Arguments("map", "lambda", "literal")]
	[Arguments("map", "lambda", "success")]
	[Arguments("mix", "attribute", "syntax")]
	[Arguments("mix", "attribute", "literal")]
	[Arguments("mix", "attribute", "success")]
	[Arguments("mix", "lambda", "syntax")]
	[Arguments("mix", "lambda", "literal")]
	[Arguments("mix", "lambda", "success")]
	[Arguments("munge", "attribute", "syntax")]
	[Arguments("munge", "attribute", "literal")]
	[Arguments("munge", "attribute", "success")]
	[Arguments("munge", "lambda", "syntax")]
	[Arguments("munge", "lambda", "literal")]
	[Arguments("munge", "lambda", "success")]
	[Arguments("sortby", "attribute", "syntax")]
	[Arguments("sortby", "attribute", "literal")]
	[Arguments("sortby", "attribute", "success")]
	[Arguments("sortby", "lambda", "syntax")]
	[Arguments("sortby", "lambda", "literal")]
	[Arguments("sortby", "lambda", "success")]
	[Arguments("sortkey", "attribute", "syntax")]
	[Arguments("sortkey", "attribute", "literal")]
	[Arguments("sortkey", "attribute", "success")]
	[Arguments("sortkey", "lambda", "syntax")]
	[Arguments("sortkey", "lambda", "literal")]
	[Arguments("sortkey", "lambda", "success")]
	[Arguments("step", "attribute", "syntax")]
	[Arguments("step", "attribute", "literal")]
	[Arguments("step", "attribute", "success")]
	[Arguments("step", "lambda", "syntax")]
	[Arguments("step", "lambda", "literal")]
	[Arguments("step", "lambda", "success")]
	[Arguments("every", "attribute", "syntax")]
	[Arguments("every", "attribute", "literal")]
	[Arguments("every", "attribute", "success")]
	[Arguments("every", "lambda", "syntax")]
	[Arguments("every", "lambda", "literal")]
	[Arguments("every", "lambda", "success")]
	[Arguments("some", "attribute", "syntax")]
	[Arguments("some", "attribute", "literal")]
	[Arguments("some", "attribute", "success")]
	[Arguments("some", "lambda", "syntax")]
	[Arguments("some", "lambda", "literal")]
	[Arguments("some", "lambda", "success")]
	[Arguments("filterq", "attribute", "syntax")]
	[Arguments("filterq", "attribute", "literal")]
	[Arguments("filterq", "attribute", "success")]
	[Arguments("filterq", "lambda", "syntax")]
	[Arguments("filterq", "lambda", "literal")]
	[Arguments("filterq", "lambda", "success")]
	[Arguments("json_group_by", "attribute", "syntax")]
	[Arguments("json_group_by", "attribute", "literal")]
	[Arguments("json_group_by", "attribute", "success")]
	[Arguments("json_group_by", "lambda", "syntax")]
	[Arguments("json_group_by", "lambda", "literal")]
	[Arguments("json_group_by", "lambda", "success")]
	[Arguments("chain", "attribute", "syntax")]
	[Arguments("chain", "attribute", "literal")]
	[Arguments("chain", "attribute", "success")]
	[Arguments("jiter", "attribute", "syntax")]
	[Arguments("jiter", "attribute", "literal")]
	[Arguments("jiter", "attribute", "success")]
	[Arguments("iter", "attribute", "syntax")]
	[Arguments("iter", "attribute", "literal")]
	[Arguments("iter", "attribute", "success")]
	[Arguments("firstof", "attribute", "syntax")]
	[Arguments("firstof", "attribute", "literal")]
	[Arguments("firstof", "attribute", "success")]
	[Arguments("strfirstof", "attribute", "syntax")]
	[Arguments("strfirstof", "attribute", "literal")]
	[Arguments("strfirstof", "attribute", "success")]
	[Arguments("revwords", "attribute", "syntax")]
	[Arguments("revwords", "attribute", "literal")]
	[Arguments("revwords", "attribute", "success")]
	[Arguments("words", "attribute", "syntax")]
	[Arguments("words", "attribute", "literal")]
	[Arguments("words", "attribute", "success")]
	public async Task ListFunctionPreservesNestedFailure(string function, string route, string mode)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		var actor = (await mediator.Send(new GetObjectNodeQuery(Factory.ExecutorDBRef))).Expect<AnySharpObject>();
		var name = "LISTERROR" + Guid.NewGuid().ToString("N");
		var value = mode switch { "syntax" => "[", "literal" => "#-1 EXCEPTION: ordinary text", _ => "1" };
		await attributes.SetAttributeAsync(actor, actor, name, MarkupText.Plain(value));
		var attribute = route == "attribute" ? "me/" + name : "#lambda/" + (mode == "syntax" ? "\\[" : value);
		var expression = function switch
		{
			"iter" => $"iter(a b,ufun({attribute}))",
			"firstof" or "strfirstof" => $"{function}(ufun({attribute}),valid)",
			"revwords" or "words" => $"{function}(ufun({attribute}))",
			"filterq" => $"filterq(R,{attribute},a b)",
			"mix" => $"mix({attribute},a b,c d)",
			"munge" => $"munge({attribute},a b,x y)",
			"step" => $"step({attribute},a b,1)",
			_ => $"{function}({attribute},a b)"
		};
		var result = await Factory.FunctionParser.FunctionParse(MarkupText.Plain(expression));
		TestDiagnostics.WriteLine($"{function}/{route}/{mode}: errors={result!.HadErrors}; text={result.Message}");
		await Assert.That(result.HadErrors).IsEqualTo(mode == "syntax");
		if (mode != "syntax")
		{
			var expected = function switch
			{
				"fold" or "chain" or "jiter" => value,
				"iter" or "map" or "mix" or "step" => value + " " + value,
				"munge" => "",
				"filter" or "filterbool" or "filterq" => mode == "success" ? "a b" : "",
				"every" or "some" => mode == "success" ? "1" : "0",
				"sortkey" => "a b",
				_ => null
			};
			if (expected is not null) await Assert.That(result.Message!.Text).IsEqualTo(expected);
		}
	}
}
