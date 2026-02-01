using System.Net.Http.Json;
using SharpMUSH.Client.Models;
using SharpMUSH.Configuration;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

public class ConfigSchemaService(IHttpClientFactory httpClientFactory)
{
	public async Task<ConfigSchema?> GetSchemaAsync()
	{
		try
		{
			// Use the named "api" client which is configured to point to port 8080
			var client = httpClientFactory.CreateClient("api");
			var response = await client.GetFromJsonAsync<ConfigurationResponse>("/api/configuration");
			
			if (response == null)
			{
				return null;
			}

			return BuildSchemaFromMetadata(response.Metadata);
		}
		catch (Exception)
		{
			// TODO: Add logging
			return null;
		}
	}

	private static ConfigSchema BuildSchemaFromMetadata(Dictionary<string, SharpConfigAttribute> metadata)
	{
		var schema = new ConfigSchema();
		var categoriesDict = new Dictionary<string, ConfigCategory>();

		foreach (var kvp in metadata)
		{
			var propertyPath = kvp.Key;
			var attr = kvp.Value;

			// Get or create category
			if (!categoriesDict.TryGetValue(attr.Category, out var category))
			{
				category = new ConfigCategory
				{
					Name = attr.Category,
					DisplayName = FormatCategoryDisplayName(attr.Category),
					Description = null
				};
				categoriesDict[attr.Category] = category;
				schema.Categories.Add(category);
			}

			// Extract property name from path (e.g., "NetOptions.Port" -> "Port")
			var propertyName = propertyPath.Contains('.') 
				? propertyPath.Split('.').Last() 
				: propertyPath;

			// Determine type from the attribute or property name conventions
			var propertyType = DeterminePropertyType(attr, propertyName);

			category.Properties.Add(new ConfigProperty
			{
				Name = propertyName,
				DisplayName = attr.Name,
				Description = attr.Description,
				Type = propertyType,
				DefaultValue = null // Could be enhanced to include defaults
			});
		}

		return schema;
	}

	private static string FormatCategoryDisplayName(string categoryName)
	{
		// Remove "Options" suffix if present
		if (categoryName.EndsWith("Options"))
		{
			categoryName = categoryName.Substring(0, categoryName.Length - 7);
		}

		// Add spaces before capital letters
		return System.Text.RegularExpressions.Regex.Replace(categoryName, "([A-Z])", " $1").Trim();
	}

	private static string DeterminePropertyType(SharpConfigAttribute attr, string propertyName)
	{
		// Try to infer type from name patterns
		var lowerName = propertyName.ToLower();

		if (lowerName.Contains("enable") || lowerName.Contains("allow") || 
		    lowerName.Contains("use") || lowerName.StartsWith("is"))
		{
			return "boolean";
		}

		if (lowerName.Contains("port") || lowerName.Contains("max") || 
		    lowerName.Contains("min") || lowerName.Contains("limit") ||
		    lowerName.Contains("count") || lowerName.Contains("size"))
		{
			return "integer";
		}

		// Default to string
		return "string";
	}
}
