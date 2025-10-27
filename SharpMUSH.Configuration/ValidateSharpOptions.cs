using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Configuration.Generated;

namespace SharpMUSH.Configuration;

public class ValidateSharpOptions : IValidateOptions<SharpMUSHOptions>
{	
	private readonly PropertyInfo[] properties = typeof(SharpMUSHOptions).GetProperties();

	public ValidateOptionsResult Validate(string? name, SharpMUSHOptions options)
	{
		foreach (var category in properties)
		{
			var categoryValue = category.GetValue(options);

			foreach (var property in category.PropertyType.GetProperties())
			{
				var customAttributes = property.GetCustomAttribute<SharpConfigAttribute>()!;
				var propertyValue = property.GetValue(categoryValue);
				if (customAttributes.ValidationPattern is null) continue;

				if (!Regex.IsMatch(propertyValue?.ToString() ?? string.Empty, customAttributes.ValidationPattern))
				{
					return ValidateOptionsResult.Fail($"Configuration option {category.Name}:{property.Name} with value '{propertyValue}' is invalid.");
				}
			}
		}

		return ValidateOptionsResult.Success;
	}
}