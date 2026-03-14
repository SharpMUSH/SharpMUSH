using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

public class DiagnosticTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task GetDiagnostics_ValidInput_ReturnsEmpty()
	{
		var diagnostics = Parser.GetDiagnostics(MModule.single("add(1,2)"), ParseType.Function);

		await Assert.That(diagnostics).IsEmpty();
	}

	[Test]
	public async Task GetDiagnostics_InvalidInput_ReturnsDiagnostics()
	{
		var diagnostics = Parser.GetDiagnostics(MModule.single("add(1,2"), ParseType.Function);

		await Assert.That(diagnostics).IsNotEmpty();
		await Assert.That(diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
	}

	[Test]
	public async Task GetDiagnostics_HasRange()
	{
		var diagnostics = Parser.GetDiagnostics(MModule.single("add(1,2"), ParseType.Function);

		await Assert.That(diagnostics).IsNotEmpty();

		var diagnostic = diagnostics[0];
		await Assert.That(diagnostic.Range).IsNotNull();
		await Assert.That(diagnostic.Range.Start).IsNotNull();
		await Assert.That(diagnostic.Range.End).IsNotNull();
	}

	[Test]
	public async Task GetDiagnostics_RangeSpansToken()
	{
		var diagnostics = Parser.GetDiagnostics(MModule.single("test[unclosed"), ParseType.Function);

		await Assert.That(diagnostics).IsNotEmpty();

		var diagnostic = diagnostics[0];
		// Range should have valid positions
		await Assert.That(diagnostic.Range.Start.Line).IsGreaterThanOrEqualTo(0);
		await Assert.That(diagnostic.Range.Start.Character).IsGreaterThanOrEqualTo(0);
		await Assert.That(diagnostic.Range.End.Character).IsGreaterThanOrEqualTo(diagnostic.Range.Start.Character);
	}

	[Test]
	public async Task GetDiagnostics_IncludesMessage()
	{
		var diagnostics = Parser.GetDiagnostics(MModule.single("add(1,2"), ParseType.Function);

		await Assert.That(diagnostics).IsNotEmpty();
		await Assert.That(diagnostics[0].Message).IsNotEmpty();
	}

	[Test]
	public async Task GetDiagnostics_IncludesSource()
	{
		var diagnostics = Parser.GetDiagnostics(MModule.single("add(1,2"), ParseType.Function);

		await Assert.That(diagnostics).IsNotEmpty();
		await Assert.That(diagnostics[0].Source).IsEqualTo("SharpMUSH Parser");
	}

	[Test]
	public async Task ParseError_ToDiagnostic_ConvertsCorrectly()
	{
		var errors = Parser.ValidateAndGetErrors(MModule.single("add(1,2"), ParseType.Function);

		await Assert.That(errors).IsNotEmpty();

		var diagnostic = errors[0].ToDiagnostic();
		await Assert.That(diagnostic).IsNotNull();
		await Assert.That(diagnostic.Range).IsNotNull();
		await Assert.That(diagnostic.Message).IsEqualTo(errors[0].Message);
	}

	[Test]
	public async Task Range_Contains_Position()
	{
		var range = new SharpMUSH.Library.Models.Range
		{
			Start = new Position(0, 5),
			End = new Position(0, 10)
		};

		var posInside = new Position(0, 7);
		var posOutside = new Position(0, 11);

		await Assert.That(range.Contains(posInside)).IsTrue();
		await Assert.That(range.Contains(posOutside)).IsFalse();
	}

	[Test]
	public async Task Range_IsEmpty_DetectsCorrectly()
	{
		var emptyRange = new SharpMUSH.Library.Models.Range
		{
			Start = new Position(0, 5),
			End = new Position(0, 5)
		};

		var nonEmptyRange = new SharpMUSH.Library.Models.Range
		{
			Start = new Position(0, 5),
			End = new Position(0, 10)
		};

		await Assert.That(emptyRange.IsEmpty).IsTrue();
		await Assert.That(nonEmptyRange.IsEmpty).IsFalse();
	}

	[Test]
	public async Task Range_IsSingleLine_DetectsCorrectly()
	{
		var singleLine = new SharpMUSH.Library.Models.Range
		{
			Start = new Position(0, 5),
			End = new Position(0, 10)
		};

		var multiLine = new SharpMUSH.Library.Models.Range
		{
			Start = new Position(0, 5),
			End = new Position(1, 3)
		};

		await Assert.That(singleLine.IsSingleLine).IsTrue();
		await Assert.That(multiLine.IsSingleLine).IsFalse();
	}

	[Test]
	public async Task Position_Comparison_WorksCorrectly()
	{
		var pos1 = new Position(0, 5);
		var pos2 = new Position(0, 10);
		var pos3 = new Position(1, 0);

		await Assert.That(pos1.IsBefore(pos2)).IsTrue();
		await Assert.That(pos2.IsAfter(pos1)).IsTrue();
		await Assert.That(pos1.IsBefore(pos3)).IsTrue();
		await Assert.That(pos3.IsAfter(pos2)).IsTrue();
	}
}
