using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Configuration.Generated;

namespace SharpMUSH.Configuration;

/// <summary>
/// Validates SharpMUSH configuration options by delegating to the code-generated validator.
/// This class provides a stable API in the SharpMUSH.Configuration namespace for external use,
/// while the actual validation logic is implemented via code generation for performance.
/// </summary>
public class ValidateSharpOptions : IValidateOptions<SharpMUSHOptions>
{
	private readonly ValidateSharpMUSHOptions _generatedValidator = new();

	public ValidateOptionsResult Validate(string? name, SharpMUSHOptions options)
		=> _generatedValidator.Validate(name, options);
}