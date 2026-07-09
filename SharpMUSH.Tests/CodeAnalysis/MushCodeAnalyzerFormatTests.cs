using NSubstitute;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.CodeAnalysis;

/// <summary>
/// Unit tests for <see cref="MushCodeAnalyzer.Format"/>. Formatting is a pure text transform
/// (it does not consult the parser), so these are fully deterministic without a DB.
/// </summary>
public class MushCodeAnalyzerFormatTests
{
	private static MushCodeAnalyzer Analyzer() => new(Substitute.For<IMUSHCodeParser>());

	[Test]
	public async Task Format_InsertsSpaceAfterCommas()
	{
		var result = Analyzer().Format("add(1,2,3)");
		await Assert.That(result).IsEqualTo("add(1, 2, 3)");
	}

	[Test]
	public async Task Format_TrimsLeadingAndTrailingWhitespace()
	{
		var result = Analyzer().Format("   think foo   ");
		await Assert.That(result).IsEqualTo("think foo");
	}

	[Test]
	public async Task Format_InsertsSpaceBetweenCommandAndFirstArgument()
	{
		var result = Analyzer().Format("@pemit#1=hi");
		await Assert.That(result).IsEqualTo("@pemit #1=hi");
	}

	[Test]
	public async Task Format_PreservesLineCount()
	{
		var result = Analyzer().Format("think a,b\nthink c,d");
		await Assert.That(result).IsEqualTo("think a, b\nthink c, d");
	}

	[Test]
	public async Task Format_LeavesAlreadyCleanCodeUnchanged()
	{
		var result = Analyzer().Format("add(1, 2)");
		await Assert.That(result).IsEqualTo("add(1, 2)");
	}

	[Test]
	public async Task Format_PreservesCrlfLineEndings()
	{
		var result = Analyzer().Format("a,b\r\nc,d");
		await Assert.That(result).IsEqualTo("a, b\r\nc, d");
	}
}
