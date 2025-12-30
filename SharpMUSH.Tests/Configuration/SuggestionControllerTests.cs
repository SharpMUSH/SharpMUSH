using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Configuration;

public class SuggestionControllerTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task GetSuggestions_EmptyData_ReturnsEmptyCategories()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		
		// Clear any existing data
		await objectDataService.SetExpandedServerDataAsync(new SuggestionData(), ignoreNull: true);
		
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		var result = await controller.GetSuggestions();

		// Assert
		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
		
		var okResult = (OkObjectResult)result.Result!;
		var response = (SuggestionData)okResult.Value!;
		
		await Assert.That(response.Categories ?? new Dictionary<string, HashSet<string>>()).IsNotNull();
		await Assert.That((response.Categories ?? new Dictionary<string, HashSet<string>>()).Count).IsEqualTo(0);
	}

	[Test]
	public async Task AddWord_NewCategory_CreatesCategory()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		
		// Clear any existing data
		await objectDataService.SetExpandedServerDataAsync(new SuggestionData(), ignoreNull: true);
		
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		var result = await controller.AddWord("testcategory", "testword");

		// Assert
		await Assert.That(result).IsTypeOf<OkObjectResult>();
		
		// Verify the category was created
		var getSuggestions = await controller.GetSuggestions();
		var okGetResult = (OkObjectResult)getSuggestions.Result!;
		var data = (SuggestionData)okGetResult.Value!;
		
		await Assert.That(data.Categories ?? new Dictionary<string, HashSet<string>>()).IsNotNull();
		await Assert.That((data.Categories ?? new Dictionary<string, HashSet<string>>()).ContainsKey("testcategory")).IsTrue();
		await Assert.That(data.Categories!["testcategory"].Contains("testword")).IsTrue();
	}

	[Test]
	public async Task AddWord_ExistingCategory_AddsWord()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		
		// Setup initial data with a category
		var initialData = new SuggestionData
		{
			Categories = new Dictionary<string, HashSet<string>>
			{
				{ "commands", new HashSet<string> { "@create", "@destroy" } }
			}
		};
		await objectDataService.SetExpandedServerDataAsync(initialData, ignoreNull: true);
		
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		var result = await controller.AddWord("commands", "@dig");

		// Assert
		await Assert.That(result).IsTypeOf<OkObjectResult>();
		
		// Verify the word was added
		var getSuggestions = await controller.GetSuggestions();
		var okGetResult = (OkObjectResult)getSuggestions.Result!;
		var data = (SuggestionData)okGetResult.Value!;
		
		await Assert.That(data.Categories!["commands"].Count).IsEqualTo(3);
		await Assert.That(data.Categories["commands"].Contains("@dig")).IsTrue();
	}

	[Test]
	public async Task AddWord_DuplicateWord_ReturnsConflict()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		
		// Setup initial data with a category and word
		var initialData = new SuggestionData
		{
			Categories = new Dictionary<string, HashSet<string>>
			{
				{ "commands", new HashSet<string> { "@create" } }
			}
		};
		await objectDataService.SetExpandedServerDataAsync(initialData, ignoreNull: true);
		
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		var result = await controller.AddWord("commands", "@create");

		// Assert
		await Assert.That(result).IsTypeOf<ConflictObjectResult>();
	}

	[Test]
	public async Task AddWord_EmptyWord_ReturnsBadRequest()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		var result = await controller.AddWord("commands", "");

		// Assert
		await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
	}

	[Test]
	public async Task GetCategory_ExistingCategory_ReturnsWords()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		
		// Setup initial data
		var initialData = new SuggestionData
		{
			Categories = new Dictionary<string, HashSet<string>>
			{
				{ "functions", new HashSet<string> { "add", "sub", "mul" } }
			}
		};
		await objectDataService.SetExpandedServerDataAsync(initialData, ignoreNull: true);
		
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		var result = await controller.GetCategory("functions");

		// Assert
		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
		
		var okResult = (OkObjectResult)result.Result!;
		var words = (IEnumerable<string>)okResult.Value!;
		
		await Assert.That(words.Count()).IsEqualTo(3);
		await Assert.That(words).Contains("add");
	}

	[Test]
	public async Task GetCategory_NonExistentCategory_ReturnsNotFound()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		
		await objectDataService.SetExpandedServerDataAsync(new SuggestionData(), ignoreNull: true);
		
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		var result = await controller.GetCategory("nonexistent");

		// Assert
		await Assert.That(result.Result).IsTypeOf<NotFoundObjectResult>();
	}

	[Test]
	public async Task DeleteWord_ExistingWord_RemovesWord()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		
		// Setup initial data
		var initialData = new SuggestionData
		{
			Categories = new Dictionary<string, HashSet<string>>
			{
				{ "commands", new HashSet<string> { "@create", "@destroy", "@dig" } }
			}
		};
		await objectDataService.SetExpandedServerDataAsync(initialData, ignoreNull: true);
		
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		var result = await controller.DeleteWord("commands", "@destroy");

		// Assert
		await Assert.That(result).IsTypeOf<OkObjectResult>();
		
		// Verify the word was removed
		var getSuggestions = await controller.GetSuggestions();
		var okGetResult = (OkObjectResult)getSuggestions.Result!;
		var data = (SuggestionData)okGetResult.Value!;
		
		await Assert.That(data.Categories!["commands"].Count).IsEqualTo(2);
		await Assert.That(data.Categories["commands"].Contains("@destroy")).IsFalse();
	}

	[Test]
	public async Task DeleteWord_LastWordInCategory_RemovesCategory()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		
		// Setup initial data with only one word
		var initialData = new SuggestionData
		{
			Categories = new Dictionary<string, HashSet<string>>
			{
				{ "temp", new HashSet<string> { "onlyword" } }
			}
		};
		await objectDataService.SetExpandedServerDataAsync(initialData, ignoreNull: true);
		
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		var result = await controller.DeleteWord("temp", "onlyword");

		// Assert
		await Assert.That(result).IsTypeOf<OkObjectResult>();
		
		// Verify the category was removed
		var getSuggestions = await controller.GetSuggestions();
		var okGetResult = (OkObjectResult)getSuggestions.Result!;
		var data = (SuggestionData)okGetResult.Value!;
		
		await Assert.That(data.Categories!.ContainsKey("temp")).IsFalse();
	}

	[Test]
	public async Task DeleteWord_NonExistentWord_ReturnsNotFound()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		
		var initialData = new SuggestionData
		{
			Categories = new Dictionary<string, HashSet<string>>
			{
				{ "commands", new HashSet<string> { "@create" } }
			}
		};
		await objectDataService.SetExpandedServerDataAsync(initialData, ignoreNull: true);
		
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		var result = await controller.DeleteWord("commands", "@nonexistent");

		// Assert
		await Assert.That(result).IsTypeOf<NotFoundObjectResult>();
	}

	[Test]
	public async Task DeleteCategory_ExistingCategory_RemovesCategory()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		
		var initialData = new SuggestionData
		{
			Categories = new Dictionary<string, HashSet<string>>
			{
				{ "commands", new HashSet<string> { "@create" } },
				{ "functions", new HashSet<string> { "add" } }
			}
		};
		await objectDataService.SetExpandedServerDataAsync(initialData, ignoreNull: true);
		
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		var result = await controller.DeleteCategory("commands");

		// Assert
		await Assert.That(result).IsTypeOf<OkObjectResult>();
		
		// Verify the category was removed
		var getSuggestions = await controller.GetSuggestions();
		var okGetResult = (OkObjectResult)getSuggestions.Result!;
		var data = (SuggestionData)okGetResult.Value!;
		
		await Assert.That(data.Categories!.ContainsKey("commands")).IsFalse();
		await Assert.That(data.Categories.ContainsKey("functions")).IsTrue();
	}

	[Test]
	public async Task DeleteCategory_NonExistentCategory_ReturnsNotFound()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		
		await objectDataService.SetExpandedServerDataAsync(new SuggestionData(), ignoreNull: true);
		
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		var result = await controller.DeleteCategory("nonexistent");

		// Assert
		await Assert.That(result).IsTypeOf<NotFoundObjectResult>();
	}

	[Test]
	public async Task UpdateSuggestions_ValidData_UpdatesStorage()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		var controller = new SuggestionController(objectDataService, logger);

		var newData = new SuggestionData
		{
			Categories = new Dictionary<string, HashSet<string>>
			{
				{ "bulk1", new HashSet<string> { "word1", "word2" } },
				{ "bulk2", new HashSet<string> { "word3" } }
			}
		};

		// Act
		var result = await controller.UpdateSuggestions(newData);

		// Assert
		await Assert.That(result).IsTypeOf<OkObjectResult>();
		
		// Verify the data was updated
		var getSuggestions = await controller.GetSuggestions();
		var okGetResult = (OkObjectResult)getSuggestions.Result!;
		var data = (SuggestionData)okGetResult.Value!;
		
		await Assert.That(data.Categories!.Count).IsEqualTo(2);
		await Assert.That(data.Categories.ContainsKey("bulk1")).IsTrue();
		await Assert.That(data.Categories.ContainsKey("bulk2")).IsTrue();
	}

	[Test]
	public async Task UpdateSuggestions_NullData_ReturnsBadRequest()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		var result = await controller.UpdateSuggestions(null!);

		// Assert
		await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
	}

	[Test]
	public async Task AddWord_NormalizesToLowerCase()
	{
		// Arrange
		var objectDataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
		var logger = WebAppFactoryArg.Services.GetRequiredService<ILogger<SuggestionController>>();
		
		await objectDataService.SetExpandedServerDataAsync(new SuggestionData(), ignoreNull: true);
		
		var controller = new SuggestionController(objectDataService, logger);

		// Act
		await controller.AddWord("MyCategory", "MyWord");

		// Assert - Verify both category and word are lowercased
		var getSuggestions = await controller.GetSuggestions();
		var okGetResult = (OkObjectResult)getSuggestions.Result!;
		var data = (SuggestionData)okGetResult.Value!;
		
		await Assert.That(data.Categories!.ContainsKey("mycategory")).IsTrue();
		await Assert.That(data.Categories["mycategory"].Contains("myword")).IsTrue();
	}
}
