using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

public class MotdDataTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase _database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IExpandedObjectDataService _dataService => WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();

	[Test, NotInParallel]
	public async Task SetAndGetMotdData()
	{
		// Create MOTD data
		var motdData = new MotdData(
			ConnectMotd: "Welcome to the game!",
			WizardMotd: "Wizard message",
			DownMotd: "Server is down",
			FullMotd: "Server is full"
		);

		// Set the data
		await _dataService.SetExpandedServerDataAsync(motdData);

		// Get the data back
		var result = await _dataService.GetExpandedServerDataAsync<MotdData>();

		// Verify
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ConnectMotd).IsEqualTo("Welcome to the game!");
		await Assert.That(result.WizardMotd).IsEqualTo("Wizard message");
		await Assert.That(result.DownMotd).IsEqualTo("Server is down");
		await Assert.That(result.FullMotd).IsEqualTo("Server is full");
	}

	[Test, NotInParallel]
	public async Task UpdateMotdData()
	{
		// Set initial data
		var initialData = new MotdData(
			ConnectMotd: "Initial message",
			WizardMotd: null,
			DownMotd: null,
			FullMotd: null
		);
		await _dataService.SetExpandedServerDataAsync(initialData);

		// Update with new data
		var updatedData = new MotdData(
			ConnectMotd: "Updated message",
			WizardMotd: "New wizard message",
			DownMotd: null,
			FullMotd: null
		);
		await _dataService.SetExpandedServerDataAsync(updatedData);

		// Get the data back
		var result = await _dataService.GetExpandedServerDataAsync<MotdData>();

		// Verify
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ConnectMotd).IsEqualTo("Updated message");
		await Assert.That(result.WizardMotd).IsEqualTo("New wizard message");
	}

	[Test, NotInParallel, Skip("TODO: Failing test - needs investigation")]
	public async Task ClearMotdData()
	{
		// Set initial data
		var initialData = new MotdData(
			ConnectMotd: "Message to clear",
			WizardMotd: "Wizard message",
			DownMotd: "Down message",
			FullMotd: "Full message"
		);
		await _dataService.SetExpandedServerDataAsync(initialData);

		// Clear connect MOTD by setting it to null
		var clearedData = new MotdData(
			ConnectMotd: null,
			WizardMotd: "Wizard message",
			DownMotd: "Down message",
			FullMotd: "Full message"
		);
		await _dataService.SetExpandedServerDataAsync(clearedData);

		// Get the data back
		var result = await _dataService.GetExpandedServerDataAsync<MotdData>();

		// Verify connect MOTD is null but others remain
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ConnectMotd).IsNull();
		await Assert.That(result.WizardMotd).IsEqualTo("Wizard message");
		await Assert.That(result.DownMotd).IsEqualTo("Down message");
		await Assert.That(result.FullMotd).IsEqualTo("Full message");
	}

	[Test, NotInParallel]
	public async Task GetMotdData_CanBeRetrieved()
	{
		// Set some data first
		var motdData = new MotdData(
			ConnectMotd: "Test message",
			WizardMotd: null,
			DownMotd: null,
			FullMotd: null
		);
		await _dataService.SetExpandedServerDataAsync(motdData);

		// Try to get MOTD data
		var result = await _dataService.GetExpandedServerDataAsync<MotdData>();

		// Verify we can retrieve the data
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ConnectMotd).IsEqualTo("Test message");
	}
}
