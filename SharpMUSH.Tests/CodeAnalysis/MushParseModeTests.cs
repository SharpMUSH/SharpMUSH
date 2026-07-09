using SharpMUSH.CodeAnalysis;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.CodeAnalysis;

/// <summary>
/// Unit tests for <see cref="MushParseMode"/> — the extension-based (LSP) and name-based (MCP)
/// resolution of function vs command-list parsing.
/// </summary>
public class MushParseModeTests
{
	[Test]
	[Arguments("file:///home/x/greet.mush", ParseType.CommandList)]
	[Arguments("greet.mu", ParseType.CommandList)]
	[Arguments("file:///home/x/fmt.mushfn", ParseType.Function)]
	[Arguments("fmt.fun", ParseType.Function)]
	[Arguments("file:///x/GREET.MUSH", ParseType.CommandList)]
	[Arguments("notes.txt", ParseType.Function)]
	[Arguments("noextension", ParseType.Function)]
	public async Task ForFileName_MapsExtensionToParseType(string fileName, ParseType expected)
	{
		await Assert.That(MushParseMode.ForFileName(fileName)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("function", ParseType.Function)]
	[Arguments("command", ParseType.Command)]
	[Arguments("commandlist", ParseType.CommandList)]
	[Arguments("command-list", ParseType.CommandList)]
	[Arguments("COMMANDLIST", ParseType.CommandList)]
	[Arguments("nonsense", ParseType.Function)]
	public async Task FromName_MapsNameToParseType(string name, ParseType expected)
	{
		await Assert.That(MushParseMode.FromName(name)).IsEqualTo(expected);
	}

	[Test]
	public async Task FromName_NullOrEmpty_FallsBackToFunction()
	{
		await Assert.That(MushParseMode.FromName(null)).IsEqualTo(ParseType.Function);
		await Assert.That(MushParseMode.FromName("")).IsEqualTo(ParseType.Function);
	}
}
