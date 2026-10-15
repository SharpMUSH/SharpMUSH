using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Generated;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.LanguageServer;

public class BuiltInMetadataTests
{
	private sealed class DefaultOptions : IOptionsWrapper<SharpMUSHOptions>
	{
		public SharpMUSHOptions CurrentValue { get; } = SharpMUSHOptions.Default();
	}

	private static IEnumerable<T> DeclaredAttributes<T>() where T : Attribute =>
		typeof(MUSHCodeParser).Assembly.GetTypes()
			.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
				BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
			.SelectMany(method => method.GetCustomAttributes<T>());

	[Test]
	public async Task VariadicSignature_AdversarialArgumentCountHasBoundedPresentation()
	{
		using var services = new ServiceCollection().BuildServiceProvider();
		var functions = new FunctionLibraryService();
		functions.Add("add", (new FunctionDefinition(FunctionLibrary.CreateMetadata()["add"], _ => throw new NotSupportedException()), true));
		var parser = new MUSHCodeParser(NullLogger<MUSHCodeParser>.Instance, functions, new CommandLibraryService(), new DefaultOptions(), services);
		var analyzer = new MushCodeAnalyzer(parser);
		var text = "add(" + new string(',', 1000);
		var signature = analyzer.SignatureHelp(text, 0, text.Length)!;
		await Assert.That(signature.Parameters.Count).IsEqualTo(64);
		await Assert.That(signature.Parameters[^1].Label).IsEqualTo("...");
		await Assert.That(signature.ActiveParameter).IsEqualTo(63);
		await Assert.That(signature.Label.Length < 2048).IsTrue();
	}

	[Test]
	public async Task GeneratedMetadata_IncludesEveryDeclaredNameAndParameterList()
	{
		var functions = FunctionLibrary.CreateMetadata();
		var commands = CommandLibrary.CreateMetadata();
		var declaredFunctions = DeclaredAttributes<SharpFunctionAttribute>().ToArray();
		var declaredCommands = DeclaredAttributes<SharpCommandAttribute>().ToArray();
		await Assert.That(functions.Count).IsEqualTo(declaredFunctions.Length);
		await Assert.That(commands.Count).IsEqualTo(declaredCommands.Length);
		foreach (var attribute in declaredFunctions)
		{
			await Assert.That(functions.ContainsKey(attribute.Name)).IsTrue();
			await Assert.That(functions[attribute.Name].ParameterNames.SequenceEqual(attribute.ParameterNames)).IsTrue();
			await Assert.That(functions[attribute.Name].MinArgs).IsEqualTo(attribute.MinArgs);
			await Assert.That(functions[attribute.Name].MaxArgs).IsEqualTo(attribute.MaxArgs);
			await Assert.That(functions[attribute.Name].Flags).IsEqualTo(attribute.Flags);
			await Assert.That(functions[attribute.Name].SideEffectMinArgs).IsEqualTo(attribute.SideEffectMinArgs);
			await Assert.That(functions[attribute.Name].Restrict.SequenceEqual(attribute.Restrict)).IsTrue();
		}
		foreach (var attribute in declaredCommands)
		{
			await Assert.That(commands.ContainsKey(attribute.Name)).IsTrue();
			await Assert.That(commands[attribute.Name].Switches!.SequenceEqual(attribute.Switches!)).IsTrue();
			await Assert.That(commands[attribute.Name].ParameterNames.SequenceEqual(attribute.ParameterNames)).IsTrue();
			await Assert.That(commands[attribute.Name].MinArgs).IsEqualTo(attribute.MinArgs);
			await Assert.That(commands[attribute.Name].MaxArgs).IsEqualTo(attribute.MaxArgs);
			await Assert.That(commands[attribute.Name].Behavior).IsEqualTo(attribute.Behavior);
			await Assert.That(commands[attribute.Name].CommandLock).IsEqualTo(attribute.CommandLock);
			await Assert.That(commands[attribute.Name].Output).IsEqualTo(attribute.Output);
			await Assert.That(commands[attribute.Name].SingleArgumentSwitches.SequenceEqual(attribute.SingleArgumentSwitches)).IsTrue();
		}
	}

	[Test]
	public async Task MetadataFactories_ReturnIndependentAttributes()
	{
		var functions = FunctionLibrary.CreateMetadata();
		var commands = CommandLibrary.CreateMetadata();
		functions["get"].MaxArgs = 99;
		functions["get"].ParameterNames[0] = "changed";
		commands["@EMIT"].Switches![0] = "changed";
		await Assert.That(FunctionLibrary.CreateMetadata()["get"].MaxArgs).IsEqualTo(1);
		await Assert.That(FunctionLibrary.CreateMetadata()["get"].ParameterNames[0]).IsEqualTo("object/attribute");
		await Assert.That(CommandLibrary.CreateMetadata()["@EMIT"].Switches![0]).IsEqualTo("NOEVAL");
	}

	[Test]
	public async Task BuiltInIntelligence_WithoutGameServices_HandlesMixedCaseAndVariadicArguments()
	{
		using var services = new ServiceCollection().BuildServiceProvider();
		var functions = new FunctionLibraryService();
		var commands = new CommandLibraryService();
		foreach (var (name, attribute) in FunctionLibrary.CreateMetadata())
			functions.Add(name, (new FunctionDefinition(attribute,
				static _ => throw new InvalidOperationException("Analysis must never evaluate.")), true));
		foreach (var (name, attribute) in CommandLibrary.CreateMetadata())
			commands.Add(name, (new CommandDefinition(attribute,
				static _ => throw new InvalidOperationException("Analysis must never execute commands.")), true));
		var parser = new MUSHCodeParser(NullLogger<MUSHCodeParser>.Instance,
			functions, commands, new DefaultOptions(), services);
		var analyzer = new MushCodeAnalyzer(parser);
		await Assert.That(analyzer.Hover("GeT(#1/DESC)", 0, 1)!.Markdown).Contains("Maximum: 1");
		await Assert.That(analyzer.Hover("@emit hello", 0, 2)!.Markdown).Contains("NOEVAL");
		await Assert.That(analyzer.Complete("Ge", 0, 2).Any(item => item.Label == "get")).IsTrue();
		var signature = analyzer.SignatureHelp("add(1,2)", 0, 6)!;
		// add() takes one argument, as TinyMUX's does (add(5) is 5), so the second is optional.
		await Assert.That(signature.Label).IsEqualTo("add(number1, [number2], ...)");
		await Assert.That(signature.Parameters.Count).IsEqualTo(2);
		await Assert.That(signature.ActiveParameter).IsEqualTo(1);
		var third = analyzer.SignatureHelp("add(1,2,3)", 0, 8)!;
		await Assert.That(third.Parameters.Count).IsEqualTo(3);
		await Assert.That(third.ActiveParameter).IsEqualTo(2);
	}
}
