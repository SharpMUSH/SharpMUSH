using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class SynchronousFunctionResultTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("fn", "syntax")]
	[Arguments("fn", "literal")]
	[Arguments("fn", "success")]
	[Arguments("render", "syntax")]
	[Arguments("render", "literal")]
	[Arguments("render", "success")]
	[Arguments("json", "syntax")]
	[Arguments("json", "literal")]
	[Arguments("json", "success")]
	[Arguments("jsonlambda", "syntax")]
	[Arguments("jsonlambda", "literal")]
	[Arguments("jsonlambda", "success")]
	[Arguments("namelist", "syntax")]
	[Arguments("namelist", "literal")]
	[Arguments("namelist", "success")]
	[Arguments("sql", "syntax")]
	[Arguments("sql", "literal")]
	[Arguments("sql", "success")]
	[Arguments("sqlheader", "syntax")]
	[Arguments("sqlheader", "literal")]
	[Arguments("sqlheader", "success")]
	[Arguments("speak", "syntax")]
	[Arguments("speak", "literal")]
	[Arguments("speak", "success")]
	[Arguments("speaknull", "syntax")]
	[Arguments("speaknull", "literal")]
	[Arguments("speaknull", "success")]
	[Arguments("allof", "syntax")]
	[Arguments("allof", "literal")]
	[Arguments("allof", "success")]
	[Arguments("allofdelimiter", "syntax")]
	[Arguments("allofdelimiter", "literal")]
	[Arguments("allofdelimiter", "success")]
	[Arguments("benchmark", "syntax")]
	[Arguments("benchmark", "literal")]
	[Arguments("benchmark", "success")]
	[Arguments("cond", "syntax")]
	[Arguments("cond", "literal")]
	[Arguments("cond", "success")]
	[Arguments("condall", "syntax")]
	[Arguments("condall", "literal")]
	[Arguments("condall", "success")]
	[Arguments("foreach", "syntax")]
	[Arguments("foreach", "literal")]
	[Arguments("foreach", "success")]
	[Arguments("if", "syntax")]
	[Arguments("if", "literal")]
	[Arguments("if", "success")]
	[Arguments("ifelse", "syntax")]
	[Arguments("ifelse", "literal")]
	[Arguments("ifelse", "success")]
	[Arguments("markdown", "syntax")]
	[Arguments("markdown", "literal")]
	[Arguments("markdown", "success")]
	[Arguments("case", "syntax")]
	[Arguments("case", "literal")]
	[Arguments("case", "success")]
	[Arguments("caseall", "syntax")]
	[Arguments("caseall", "literal")]
	[Arguments("caseall", "success")]
	[Arguments("switch", "syntax")]
	[Arguments("switch", "literal")]
	[Arguments("switch", "success")]
	[Arguments("switchall", "syntax")]
	[Arguments("switchall", "literal")]
	[Arguments("switchall", "success")]
	[Arguments("foreachlegacy", "syntax")]
	[Arguments("foreachlegacy", "literal")]
	[Arguments("foreachlegacy", "success")]
	[Arguments("foreachlist", "syntax")]
	[Arguments("foreachlist", "literal")]
	[Arguments("foreachlist", "success")]
	[Arguments("sqlquery", "syntax")]
	[Arguments("sqlquery", "literal")]
	[Arguments("sqlquery", "success")]
	[Arguments("sqlparameter", "syntax")]
	[Arguments("sqlparameter", "literal")]
	[Arguments("sqlparameter", "success")]
	[Arguments("jsonarray", "syntax")]
	[Arguments("jsonarray", "literal")]
	[Arguments("jsonarray", "success")]
	[Arguments("foreachdelimiter", "syntax")]
	[Arguments("foreachdelimiter", "literal")]
	[Arguments("foreachdelimiter", "success")]
	[Arguments("foreachseparator", "syntax")]
	[Arguments("foreachseparator", "literal")]
	[Arguments("foreachseparator", "success")]
	[Arguments("jsonseparatorinvalid", "syntax")]
	[Arguments("jsonseparatorinvalid", "literal")]
	[Arguments("jsonseparatorinvalid", "success")]
	[Arguments("jsonseparator", "syntax")]
	[Arguments("jsonseparator", "literal")]
	[Arguments("jsonseparator", "success")]
	[Arguments("jsonarrayseparator", "syntax")]
	[Arguments("jsonarrayseparator", "literal")]
	[Arguments("jsonarrayseparator", "success")]
	public async Task PreservesFailureStatusAndSuccessfulOutput(string kind, string mode)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		var actor = (await mediator.Send(new GetObjectNodeQuery(Factory.ExecutorDBRef))).Known();
		var name = "result" + Guid.NewGuid().ToString("N");
		var value = mode switch { "syntax" => "[", "literal" => "#-1 EXCEPTION: ordinary text", _ => "valid" };
		await attributes.SetAttributeAsync(actor, actor, name, MarkupText.Plain(value));
		await attributes.SetAttributeAsync(actor, actor, name + "TRANSFORM", MarkupText.Plain("transformed"));
		var child = $"ufun(me/{name})";
		var expression = kind switch
		{
			"fn" => $"fn(me/{name})",
			"render" => $"render(me,me/{name})",
			"json" => $"json_map(me/{name},1)",
			"jsonlambda" => $"json_map(#apply/ufun,1,,me/{name})",
			"namelist" => $"namelist(#2147483646,me/{name})",
			"sql" => $"mapsql(me/{name},SELECT 1)",
			"sqlheader" => $"mapsql(me/{name},SELECT 1,,1)",
			"speak" => $"speak(&Speaker,|\"a\",,me/{name})",
			"speaknull" => $"speak(&Speaker,|\"a\",,me/{name}TRANSFORM,me/{name})",
			"allof" => $"allof({child},|)",
			"allofdelimiter" => $"allof(1,2,{child})",
			"benchmark" => $"benchmark({child},1)",
			"cond" => $"cond({child},selected,default)",
			"condall" => $"condall(1,{child},1,tail)",
			"foreach" => $"foreach(a,{child})",
			"foreachdelimiter" => $"foreach(a b,value,{child})",
			"foreachseparator" => $"foreach(a b,value,,{child})",
			"jsonseparatorinvalid" => $"json_map(me/{name}TRANSFORM,invalid,{child})",
			"jsonseparator" => $"json_map(me/{name}TRANSFORM,1,{child})",
			"jsonarrayseparator" => $"json_array(1,{child})",
			"case" => $"case({child},never,selected,default)",
			"caseall" => $"caseall(x,x,{child},default)",
			"switch" => $"switch({child},never,selected,default)",
			"switchall" => $"switchall(x,x,{child},default)",
			"foreachlegacy" => $"foreach(a,##[{child}])",
			"foreachlist" => $"foreach({child},value)",
			"sqlquery" => $"sql({child})",
			"sqlparameter" => $"sql(SELECT ?,,,,{child})",
			"jsonarray" => $"json_array({child})",
			"if" => $"if({child},selected,default)",
			"ifelse" => $"ifelse({child},selected,default)",
			_ => ""
		};
		if (kind == "jsonlambda") expression = $"json_map(#lambda/{(mode == "syntax" ? "\\[" : value)},1)";
		if (kind == "markdown")
		{
			var owner = await actor.Object().Owner.WithCancellation(CancellationToken.None);
			var room = (await mediator.Send(new GetObjectNodeQuery(await mediator.Send(new CreateRoomCommand(name, owner))))).AsRoom;
			var target = (await mediator.Send(new GetObjectNodeQuery(await mediator.Send(new CreateThingCommand(name, room, owner, room))))).Known();
			await attributes.SetAttributeAsync(actor, target, "RENDERMARKUP`BOLD", MarkupText.Plain(value));
			expression = $"rendermarkdowncustom(**word**,{target.Object().DBRef})";
		}
		var result = (await Factory.FunctionParser.FunctionParse(MarkupText.Plain(expression)))!;
		await Assert.That(result.HadErrors).IsEqualTo(mode == "syntax" && kind != "markdown");
		if (kind == "markdown" && mode == "syntax") await Assert.That(result.Message!.ToPlainText()).Contains("word");
		if (kind is "fn" or "render" or "json" or "jsonlambda" && mode != "syntax")
			await Assert.That(result.Message!.ToPlainText()).IsEqualTo(value);
		if (mode != "syntax")
		{
			var expected = kind switch
			{
				"allof" => mode == "literal" ? "" : value,
				"allofdelimiter" => "1" + value + "2",
				"cond" or "if" or "ifelse" => mode == "literal" ? "default" : "selected",
				"condall" => value + "tail",
				"foreach" or "caseall" or "switchall" or "sql" => value,
				"foreachlegacy" => "a" + value,
				"sqlheader" => value + value,
				"case" or "switch" => "default",
				_ => null
			};
			if (expected is not null) await Assert.That(result.Message!.ToPlainText()).IsEqualTo(expected);
		}
		if (kind == "namelist") await Assert.That(result.Message!.ToPlainText()).IsEqualTo("#-1");
	}
	[Test]
	[Arguments("if(1,selected,CHILD)")]
	[Arguments("ifelse(0,CHILD,selected)")]
	[Arguments("cond(1,selected,CHILD)")]
	[Arguments("condall(0,CHILD,selected)")]
	[Arguments("case(x,x,selected,CHILD)")]
	[Arguments("caseall(x,never,CHILD,selected)")]
	[Arguments("switch(x,x,selected,CHILD)")]
	[Arguments("switchall(x,never,CHILD,selected)")]
	public async Task DoesNotEvaluateUnselectedMalformedBranch(string expression)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		var actor = (await mediator.Send(new GetObjectNodeQuery(Factory.ExecutorDBRef))).Known();
		var name = "skipped" + Guid.NewGuid().ToString("N");
		await attributes.SetAttributeAsync(actor, actor, name, MarkupText.Plain("["));
		var result = (await Factory.FunctionParser.FunctionParse(MarkupText.Plain(expression.Replace("CHILD", $"ufun(me/{name})"))))!;
		await Assert.That(result.HadErrors).IsFalse();
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo("selected");
	}

}
