using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Configuration;

public class ValidateSharpOptions : IValidateOptions<SharpMUSHOptions>
{
	public ValidateOptionsResult Validate(string? name, SharpMUSHOptions options)
	{
		throw new NotImplementedException();
	}
}
