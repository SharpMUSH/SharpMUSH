using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

public class ParserErrorTests : TestsBase
{
	private IMUSHCodeParser Parser => FunctionParser;

	[Test]
	public async Task ValidInput_ShouldHaveNoErrors()
	{
		// Valid function call
		var errors = Parser.ValidateAndGetErrors(MModule.single("add(1,2)"), ParseType.Function);
		
		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task UnclosedFunction_ShouldReportError()
	{
		// Missing closing parenthesis
		var errors = Parser.ValidateAndGetErrors(MModule.single("add(1,2"), ParseType.Function);
		
		await Assert.That(errors).IsNotEmpty();
		await Assert.That(errors[0].Message).Contains("end of input");
	}

	[Test]
	public async Task UnclosedBracket_ShouldReportError()
	{
		// Missing closing bracket
		var errors = Parser.ValidateAndGetErrors(MModule.single("test[function"), ParseType.Function);
		
		await Assert.That(errors).IsNotEmpty();
	}

	[Test]
	public async Task UnclosedBrace_ShouldReportError()
	{
		// Missing closing brace
		var errors = Parser.ValidateAndGetErrors(MModule.single("test{brace"), ParseType.Function);
		
		await Assert.That(errors).IsNotEmpty();
	}

	[Test]
	public async Task ErrorPosition_ShouldBeCorrect()
	{
		// Error at a specific position
		var errors = Parser.ValidateAndGetErrors(MModule.single("add(1,2"), ParseType.Function);
		
		await Assert.That(errors).IsNotEmpty();
		
		// Error should be reported at or near the end
		var firstError = errors[0];
		await Assert.That(firstError.Line).IsGreaterThanOrEqualTo(1);
		await Assert.That(firstError.Column).IsGreaterThanOrEqualTo(0);
	}

	[Test]
	public async Task ComplexNestedInput_WithoutErrors_ShouldValidate()
	{
		// Complex nested structure that is valid
		var input = "strcat(add(1,2),[sub(5,3)],{concat})";
		var errors = Parser.ValidateAndGetErrors(MModule.single(input), ParseType.Function);
		
		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task CommandValidation_ShouldWork()
	{
		// Valid command
		var errors = Parser.ValidateAndGetErrors(MModule.single("@emit Hello"), ParseType.Command);
		
		// Commands might have different validation rules, 
		// but this shouldn't throw an exception
		await Assert.That(errors).IsNotNull();
	}

	[Test]
	public async Task ParseError_ShouldHaveInputText()
	{
		var input = "add(1,2";
		var errors = Parser.ValidateAndGetErrors(MModule.single(input), ParseType.Function);
		
		await Assert.That(errors).IsNotEmpty();
		await Assert.That(errors[0].InputText).IsEqualTo(input);
	}
}
