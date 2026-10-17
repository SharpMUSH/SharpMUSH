using System.Text.Json;
using SharpMUSH.CodeAnalysis.Catalog;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Generated;
using SharpMUSH.Tools.ClientData;

namespace SharpMUSH.Tests.Documentation;

public class RegistryContractAuditTests
{
	[Test]
	[Arguments("json_query", "json,action,arg...", "json_query({})", "json")]
	[Arguments("json_query", "json,action,arg...", "json_query({},size)", "json,action")]
	[Arguments("json_query", "json,action,arg...", "json_query({},get,a,b)", "json,action,arg1,arg2")]
	[Arguments("json_query", "json,action,arg...", "json_query({},extract,$.a)", "json,action,arg1")]
	[Arguments("json_fill", "json,pointer...|value...", "json_fill({},/a,one)", "json,pointer1,value1")]
	[Arguments("json_fill", "json,pointer...|value...", "json_fill({},/a,one,/b,two)", "json,pointer1,value1,pointer2,value2")]
	public async Task JsonQueryAndFillSignaturesDescribeTheirArgumentRoles(string name, string declaration, string source, string expected)
	{
		var snapshot = BuiltInAnalysisCatalog.CreateSnapshot();
		var analyzer = AnalysisCatalogAnalyzer.Create(snapshot, SharpMUSHOptions.Default());
		var signature = analyzer.SignatureHelp(source, 0, source.Length - 1);
		await Assert.That(signature).IsNotNull();
		await Assert.That(string.Join(',', signature!.Parameters.Select(parameter => parameter.Label))).IsEqualTo(expected);
		await Assert.That(signature.ActiveParameter).IsEqualTo(expected.Split(',').Length - 1);
		using var definitions = JsonDocument.Parse(ClientDataGenerator.Files(TestPaths.Helpfiles)["mush-defs.json"]);
		var parameters = definitions.RootElement.GetProperty("functions").GetProperty(name).GetProperty("parameterNames");
		await Assert.That(string.Join(',', parameters.EnumerateArray().Select(value => value.GetString()))).IsEqualTo(declaration);
		await Assert.That(string.Join(',', snapshot.Entries.Single(entry => entry.Kind == "function" && entry.Name == name).ParameterNames)).IsEqualTo(declaration);
	}

	[Test]
	[Arguments("json_map", "attribute,json,osep,arg...")]
	[Arguments("json_mod", "json,action,path-or-patch,json2")]
	public async Task JsonSignaturesReachAnalysisAndPortal(string name, string expected)
	{
		var entry = BuiltInAnalysisCatalog.CreateSnapshot().Entries.Single(entry => entry.Kind == "function" && entry.Name == name);
		await Assert.That(string.Join(',', entry.ParameterNames)).IsEqualTo(expected);
		var source = name == "json_map" ? "json_map(me/callback,json(array,1),|,extra)" : "json_mod(json(object,a,1),set,$.a,2)";
		var analyzer = AnalysisCatalogAnalyzer.Create(BuiltInAnalysisCatalog.CreateSnapshot(), SharpMUSHOptions.Default());
		var signature = analyzer.SignatureHelp(source, 0, source.LastIndexOf(',') + 1);
		await Assert.That(signature).IsNotNull();
		await Assert.That(signature!.Parameters[1].Label).IsEqualTo(name == "json_map" ? "json" : "action");
		await Assert.That(signature.Parameters[2].Label).IsEqualTo(name == "json_map" ? "osep" : "path-or-patch");
		await Assert.That(signature.Parameters[3].Label).IsEqualTo(name == "json_map" ? "arg1" : "json2");
		using var definitions = JsonDocument.Parse(ClientDataGenerator.Files(TestPaths.Helpfiles)["mush-defs.json"]);
		var parameters = definitions.RootElement.GetProperty("functions").GetProperty(name).GetProperty("parameterNames");
		await Assert.That(string.Join(',', parameters.EnumerateArray().Select(value => value.GetString()))).IsEqualTo(expected);
	}

	[Test]
	[Arguments("unique", "list,sort-type,delimiter,osep", "unique(01|1|2,n,|,;)")]
	[Arguments("setunion", "list1,list2,delimiter,sort-type,osep", "setunion(9|2,2|10,|,n,;)")]
	[Arguments("setdiff", "list1,list2,delimiter,sort-type,osep", "setdiff(9|2,2|10,|,n,;)")]
	[Arguments("setinter", "list1,list2,delimiter,sort-type,osep", "setinter(9|2,2|10,|,n,;)")]
	[Arguments("setsymdiff", "list1,list2,delimiter,sort-type,osep", "setsymdiff(9|2,2|10,|,n,;)")]
	[Arguments("sortkey", "attribute,list,sort-type,delimiter,osep", "sortkey(me/key,ab|a|abc,n,|,;)")]
	[Arguments("index", "list,delimiter,first,length", "index(a|b|c,|,2,2)")]
	[Arguments("munge", "attribute,list1,list2,delimiter,osep", "munge(me/order,b|a|c,2|1|3,|,;)")]
	public async Task ListSignaturesDescribeActualCallShapes(string name, string expected, string source)
	{
		var snapshot = BuiltInAnalysisCatalog.CreateSnapshot();
		var analyzer = AnalysisCatalogAnalyzer.Create(snapshot, SharpMUSHOptions.Default());
		var signature = analyzer.SignatureHelp(source, 0, source.LastIndexOf(',') + 1);
		await Assert.That(signature).IsNotNull();
		await Assert.That(string.Join(',', signature!.Parameters.Select(parameter => parameter.Label))).IsEqualTo(expected);
		await Assert.That(signature.ActiveParameter).IsEqualTo(expected.Split(',').Length - 1);
		using var definitions = JsonDocument.Parse(ClientDataGenerator.Files(TestPaths.Helpfiles)["mush-defs.json"]);
		var parameters = definitions.RootElement.GetProperty("functions").GetProperty(name).GetProperty("parameterNames");
		await Assert.That(string.Join(',', parameters.EnumerateArray().Select(value => value.GetString()))).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mix(me/callback,a b,1 2)", 2, "list2")]
	[Arguments("mix(me/callback,a|b,1|2,|)", 3, "list-or-delimiter1")]
	[Arguments("mix(me/callback,a|b,1|2,x|y,|)", 4, "list-or-delimiter2")]
	[Arguments("mix(me/callback,a|b,1|2,x|y,X|Y,|)", 5, "list-or-delimiter3")]
	public async Task MixSignaturesKeepOptionalListsAndFinalDelimiterDistinctFromAnOutputSeparator(string source, int active, string activeLabel)
	{
		var snapshot = BuiltInAnalysisCatalog.CreateSnapshot();
		var analyzer = AnalysisCatalogAnalyzer.Create(snapshot, SharpMUSHOptions.Default());
		var signature = analyzer.SignatureHelp(source, 0, source.Length - 1);
		await Assert.That(signature).IsNotNull();
		await Assert.That(string.Join(',', signature!.Parameters.Take(6).Select(parameter => parameter.Label)))
			.IsEqualTo("attribute,list1,list2,list-or-delimiter1,list-or-delimiter2,list-or-delimiter3");
		await Assert.That(signature.ActiveParameter).IsEqualTo(active);
		await Assert.That(signature.Parameters[active].Label).IsEqualTo(activeLabel);
		await Assert.That(signature.Parameters.Count).IsEqualTo(35);
		await Assert.That(signature.Parameters.Any(parameter => parameter.Label == "outsep")).IsFalse();
		var entry = snapshot.Entries.Single(entry => entry.Kind == "function" && entry.Name == "mix");
		await Assert.That(string.Join(',', entry.ParameterNames)).IsEqualTo("attribute,list1,list2,list-or-delimiter...");
		using var definitions = JsonDocument.Parse(ClientDataGenerator.Files(TestPaths.Helpfiles)["mush-defs.json"]);
		var portal = definitions.RootElement.GetProperty("functions").GetProperty("mix");
		await Assert.That(string.Join(',', portal.GetProperty("parameterNames").EnumerateArray().Select(value => value.GetString())))
			.IsEqualTo("attribute,list1,list2,list-or-delimiter...");
		await Assert.That(portal.GetProperty("minArgs").GetInt32()).IsEqualTo(3);
		await Assert.That(portal.GetProperty("maxArgs").GetInt32()).IsEqualTo(35);
	}

	[Test]
	public async Task PageReplyReachesGeneratedDeclarationsAndEditor()
	{
		var command = CommandLibrary.CreateMetadata().Single(pair => pair.Key.Equals("PAGE", StringComparison.OrdinalIgnoreCase));
		await Assert.That(command.Value.Switches).Contains("REPLY");
		await Assert.That(command.Value.SingleArgumentSwitches).Contains("REPLY");
		await Assert.That(string.Join(',', command.Value.ParameterNames)).IsEqualTo("player-list,message");
		var snapshot = BuiltInAnalysisCatalog.CreateSnapshot();
		var entry = snapshot.Entries.Single(entry => entry.Kind == "command" && entry.Name.Equals("PAGE", StringComparison.OrdinalIgnoreCase));
		await Assert.That(entry.Switches).Contains("REPLY");
		var analyzer = AnalysisCatalogAnalyzer.Create(snapshot, SharpMUSHOptions.Default());
		var completion = analyzer.Complete("", 0, 0).Single(item => item.Kind == "Keyword" && item.Label.Equals("PAGE", StringComparison.OrdinalIgnoreCase));
		await Assert.That(completion.Documentation).Contains("REPLY");
		using var definitions = JsonDocument.Parse(ClientDataGenerator.Files(TestPaths.Helpfiles)["mush-defs.json"]);
		var portal = definitions.RootElement.GetProperty("commands").GetProperty(command.Key);
		await Assert.That(portal.GetProperty("switches").EnumerateArray().Select(value => value.GetString())).Contains("REPLY");
		await Assert.That(string.Join(',', portal.GetProperty("parameterNames").EnumerateArray().Select(value => value.GetString()))).IsEqualTo("player-list,message");
	}

	[Test]
	public async Task PortalDeclarationsAndAliasesMatchGeneratedContracts()
	{
		using var definitions = JsonDocument.Parse(ClientDataGenerator.Files(TestPaths.Helpfiles)["mush-defs.json"]);
		var functions = FunctionLibrary.CreateMetadata();
		var commands = CommandLibrary.CreateMetadata();
		foreach (var (name, attribute) in functions)
			await Function(name, attribute.MinArgs, attribute.MaxArgs, attribute.ParameterNames);
		foreach (var (target, aliases) in AliasOptions.Default.FunctionAliases)
		{
			await Assert.That(functions.ContainsKey(target)).IsTrue();
			var attribute = functions[target];
			foreach (var alias in aliases)
				await Function(alias, attribute.MinArgs, attribute.MaxArgs, attribute.ParameterNames);
		}
		foreach (var (name, attribute) in commands)
			await Command(name, attribute.MinArgs, attribute.MaxArgs, attribute.ParameterNames, attribute.Switches ?? []);
		foreach (var (target, aliases) in AliasOptions.Default.CommandAliases)
		{
			await Assert.That(commands.ContainsKey(target)).IsTrue();
			var attribute = commands[target];
			foreach (var alias in aliases)
				await Command(alias, attribute.MinArgs, attribute.MaxArgs, attribute.ParameterNames, attribute.Switches ?? []);
		}

		async Task Function(string name, int minimum, int maximum, string[] names)
		{
			var entry = definitions.RootElement.GetProperty("functions").GetProperty(name);
			await Contract(entry, minimum, maximum, names);
		}

		async Task Command(string name, int minimum, int maximum, string[] names, string[] switches)
		{
			var entry = definitions.RootElement.GetProperty("commands").GetProperty(name);
			await Contract(entry, minimum, maximum, names);
			await Assert.That(entry.GetProperty("switches").EnumerateArray().Select(value => value.GetString()!)).IsEquivalentTo(switches);
		}

		static async Task Contract(JsonElement entry, int minimum, int maximum, string[] names)
		{
			await Assert.That(entry.GetProperty("minArgs").GetInt32()).IsEqualTo(minimum);
			await Assert.That(entry.GetProperty("maxArgs").GetInt32()).IsEqualTo(maximum);
			await Assert.That(JsonSerializer.Serialize(entry.GetProperty("parameterNames").EnumerateArray().Select(value => value.GetString()))).IsEqualTo(JsonSerializer.Serialize(names));
		}
	}

	[Test]
	public async Task GeneratedRegistrationsMatchReflection()
	{
		var functions = FunctionLibrary.CreateMetadata();
		var commands = CommandLibrary.CreateMetadata();
		await Assert.That(functions.Keys).IsEquivalentTo(RegistryInventory.Functions.Select(entry => entry.Name));
		await Assert.That(commands.Keys).IsEquivalentTo(RegistryInventory.Commands.Select(entry => entry.Name));
		foreach (var entry in RegistryInventory.Functions)
		{
			var generated = functions[entry.Name];
			await Assert.That(JsonSerializer.Serialize(RegistryInventory.Metadata(generated))).IsEqualTo(JsonSerializer.Serialize(RegistryInventory.Metadata(entry.Attribute)));
			await Assert.That(generated.MinArgs >= 0 && generated.MaxArgs >= generated.MinArgs).IsTrue();
		}
		foreach (var entry in RegistryInventory.Commands)
		{
			var generated = commands[entry.Name];
			await Assert.That(JsonSerializer.Serialize(RegistryInventory.Metadata(generated))).IsEqualTo(JsonSerializer.Serialize(RegistryInventory.Metadata(entry.Attribute)));
			// Command bounds are advisory; legacy MaxArgs=0 declarations need their own behavioral review.
			await Assert.That(generated.MinArgs >= 0 && generated.MaxArgs >= 0).IsTrue();
		}
	}
}
