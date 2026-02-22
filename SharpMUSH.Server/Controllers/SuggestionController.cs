using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SuggestionController(
	IExpandedObjectDataService objectDataService,
	ILogger<SuggestionController> logger)
	: ControllerBase
{
	/// <summary>
	/// Get all suggestion categories and their words
	/// </summary>
	[HttpGet]
	public async Task<ActionResult<SuggestionData>> GetSuggestions()
	{
		try
		{
			var suggestionData = await objectDataService.GetExpandedServerDataAsync<SuggestionData>()
				?? new SuggestionData();

			return Ok(suggestionData);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error retrieving suggestion data");
			return StatusCode(500, "Error retrieving suggestion data");
		}
	}

	/// <summary>
	/// Get words for a specific category
	/// </summary>
	[HttpGet("{category}")]
	public async Task<ActionResult<IEnumerable<string>>> GetCategory(string category)
	{
		try
		{
			var suggestionData = await objectDataService.GetExpandedServerDataAsync<SuggestionData>()
				?? new SuggestionData();

			if (suggestionData.Categories == null || !suggestionData.Categories.ContainsKey(category.ToLower()))
			{
				return NotFound($"Category '{category}' not found");
			}

			return Ok(suggestionData.Categories[category.ToLower()].OrderBy(w => w));
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error retrieving category {Category}", SanitizeUserLogInput(category));
			return StatusCode(500, $"Error retrieving category '{category}'");
		}
	}

	/// <summary>
	/// Add a word to a category
	/// </summary>
	[HttpPost("{category}")]
	public async Task<ActionResult> AddWord(string category, [FromBody] string word)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(word))
			{
				return BadRequest("Word cannot be empty");
			}

			var suggestionData = await objectDataService.GetExpandedServerDataAsync<SuggestionData>()
				?? new SuggestionData();

			if (suggestionData.Categories == null)
			{
				suggestionData = suggestionData with { Categories = [] };
			}

			var categoryKey = category.ToLower();
			if (!suggestionData.Categories.ContainsKey(categoryKey))
			{
				suggestionData.Categories[categoryKey] = [];
			}

			var normalizedWord = word.ToLower().Trim();
			if (suggestionData.Categories[categoryKey].Add(normalizedWord))
			{
				await objectDataService.SetExpandedServerDataAsync(suggestionData, ignoreNull: true);
				logger.LogInformation("Added word '{Word}' to category '{Category}'", normalizedWord, SanitizeUserLogInput(categoryKey));
				return Ok(new { message = $"Added '{normalizedWord}' to category '{categoryKey}'" });
			}
			else
			{
				return Conflict($"Word '{normalizedWord}' already exists in category '{categoryKey}'");
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error adding word to category {Category}", SanitizeUserLogInput(category));
			return StatusCode(500, $"Error adding word to category '{category}'");
		}
	}

	/// <summary>
	/// Delete a word from a category
	/// </summary>
	[HttpDelete("{category}/{word}")]
	public async Task<ActionResult> DeleteWord(string category, string word)
	{
		try
		{
			var suggestionData = await objectDataService.GetExpandedServerDataAsync<SuggestionData>()
				?? new SuggestionData();

			var categoryKey = category.ToLower();
			if (suggestionData.Categories == null || !suggestionData.Categories.ContainsKey(categoryKey))
			{
				return NotFound($"Category '{category}' not found");
			}

			var normalizedWord = word.ToLower().Trim();
			if (suggestionData.Categories[categoryKey].Remove(normalizedWord))
			{
				// Remove empty categories
				if (suggestionData.Categories[categoryKey].Count == 0)
				{
					suggestionData.Categories.Remove(categoryKey);
				}

				await objectDataService.SetExpandedServerDataAsync(suggestionData, ignoreNull: true);
				logger.LogInformation("Removed word '{Word}' from category '{Category}'", normalizedWord, SanitizeUserLogInput(categoryKey));
				return Ok(new { message = $"Removed '{normalizedWord}' from category '{categoryKey}'" });
			}
			else
			{
				return NotFound($"Word '{normalizedWord}' not found in category '{categoryKey}'");
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error deleting word from category {Category}", SanitizeUserLogInput(category));
			return StatusCode(500, $"Error deleting word from category '{category}'");
		}
	}

	/// <summary>
	/// Delete an entire category
	/// </summary>
	[HttpDelete("{category}")]
	public async Task<ActionResult> DeleteCategory(string category)
	{
		try
		{
			var suggestionData = await objectDataService.GetExpandedServerDataAsync<SuggestionData>()
				?? new SuggestionData();

			var categoryKey = category.ToLower();
			if (suggestionData.Categories == null || !suggestionData.Categories.ContainsKey(categoryKey))
			{
				return NotFound($"Category '{category}' not found");
			}

			suggestionData.Categories.Remove(categoryKey);
			await objectDataService.SetExpandedServerDataAsync(suggestionData, ignoreNull: true);
			logger.LogInformation("Deleted category '{Category}'", SanitizeUserLogInput(categoryKey));
			return Ok(new { message = $"Deleted category '{categoryKey}'" });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error deleting category {Category}", SanitizeUserLogInput(category));
			return StatusCode(500, $"Error deleting category '{category}'");
		}
	}

	/// <summary>
	/// Update entire suggestion data (bulk import)
	/// </summary>
	[HttpPut]
	public async Task<ActionResult> UpdateSuggestions([FromBody] SuggestionData suggestionData)
	{
		try
		{
			if (suggestionData == null)
			{
				return BadRequest("Suggestion data cannot be null");
			}

			await objectDataService.SetExpandedServerDataAsync(suggestionData, ignoreNull: true);
			logger.LogInformation("Updated suggestion data with {CategoryCount} categories",
				suggestionData.Categories?.Count ?? 0);
			return Ok(new { message = "Suggestion data updated successfully" });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error updating suggestion data");
			return StatusCode(500, "Error updating suggestion data");
		}
	}

	private string SanitizeUserLogInput(string input)
	{
		return input.Replace("\r", "").Replace("\n", "");
	}
}
