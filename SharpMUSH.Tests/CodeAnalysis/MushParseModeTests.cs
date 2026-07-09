using SharpMUSH.CodeAnalysis;

namespace SharpMUSH.Tests.CodeAnalysis;

/// <summary>
/// Unit tests for <see cref="MushParseMode"/> — the extension-based (LSP) and name-based (MCP)
/// resolution of the analysis mode (function / command-list / command / commands-per-line).
/// </summary>
public class MushParseModeTests
{
	[Test]
	[Arguments("file:///home/x/greet.mush", MushAnalysisMode.CommandsPerLine)]
	[Arguments("greet.mu", MushAnalysisMode.CommandsPerLine)]
	[Arguments("file:///home/x/fmt.mushfn", MushAnalysisMode.Function)]
	[Arguments("fmt.fun", MushAnalysisMode.Function)]
	[Arguments("file:///x/GREET.MUSH", MushAnalysisMode.CommandsPerLine)]
	[Arguments("notes.txt", MushAnalysisMode.Function)]
	[Arguments("noextension", MushAnalysisMode.Function)]
	public async Task ForFileName_MapsExtensionToMode(string fileName, MushAnalysisMode expected)
	{
		await Assert.That(MushParseMode.ForFileName(fileName)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("function", MushAnalysisMode.Function)]
	[Arguments("command", MushAnalysisMode.Command)]
	[Arguments("commandlist", MushAnalysisMode.CommandList)]
	[Arguments("command-list", MushAnalysisMode.CommandList)]
	[Arguments("commandsperline", MushAnalysisMode.CommandsPerLine)]
	[Arguments("commands-per-line", MushAnalysisMode.CommandsPerLine)]
	[Arguments("mushfile", MushAnalysisMode.CommandsPerLine)]
	[Arguments("COMMANDLIST", MushAnalysisMode.CommandList)]
	[Arguments("nonsense", MushAnalysisMode.Function)]
	public async Task FromName_MapsNameToMode(string name, MushAnalysisMode expected)
	{
		await Assert.That(MushParseMode.FromName(name)).IsEqualTo(expected);
	}

	[Test]
	public async Task FromName_NullOrEmpty_FallsBackToFunction()
	{
		await Assert.That(MushParseMode.FromName(null)).IsEqualTo(MushAnalysisMode.Function);
		await Assert.That(MushParseMode.FromName("")).IsEqualTo(MushAnalysisMode.Function);
	}
}
