using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

public class MotdDataTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase _database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IExpandedObjectDataService _dataService => WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();

	[Test, NotInParallel]
	public async Task SetAndGetMotdData()
	{
		var motdData = new MotdData(
			ConnectMotd: "Welcome to the game!",
			WizardMotd: "Wizard message",
			DownMotd: "Server is down",
			FullMotd: "Server is full"
		);

		await _dataService.SetExpandedServerDataAsync(motdData);

		var result = await _dataService.GetExpandedServerDataAsync<MotdData>();

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ConnectMotd).IsEqualTo("Welcome to the game!");
		await Assert.That(result.WizardMotd).IsEqualTo("Wizard message");
		await Assert.That(result.DownMotd).IsEqualTo("Server is down");
		await Assert.That(result.FullMotd).IsEqualTo("Server is full");
	}

	[Test, NotInParallel]
	public async Task UpdateMotdData()
	{
		var initialData = new MotdData(
			ConnectMotd: "Initial message",
			WizardMotd: null,
			DownMotd: null,
			FullMotd: null
		);
		await _dataService.SetExpandedServerDataAsync(initialData);

		var updatedData = new MotdData(
			ConnectMotd: "Updated message",
			WizardMotd: "New wizard message",
			DownMotd: null,
			FullMotd: null
		);
		await _dataService.SetExpandedServerDataAsync(updatedData);

		var result = await _dataService.GetExpandedServerDataAsync<MotdData>();

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ConnectMotd).IsEqualTo("Updated message");
		await Assert.That(result.WizardMotd).IsEqualTo("New wizard message");
	}

	[Test, NotInParallel]
	public async Task ClearMotdData()
	{
		var initialData = new MotdData(
			ConnectMotd: "Message to clear",
			WizardMotd: "Wizard message",
			DownMotd: "Down message",
			FullMotd: "Full message"
		);
		await _dataService.SetExpandedServerDataAsync(initialData);

		var clearedData = new MotdData(
			ConnectMotd: null,
			WizardMotd: "Wizard message",
			DownMotd: "Down message",
			FullMotd: "Full message"
		);
		await _dataService.SetExpandedServerDataAsync(clearedData);

		var result = await _dataService.GetExpandedServerDataAsync<MotdData>();

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ConnectMotd).IsNull();
		await Assert.That(result.WizardMotd).IsEqualTo("Wizard message");
		await Assert.That(result.DownMotd).IsEqualTo("Down message");
		await Assert.That(result.FullMotd).IsEqualTo("Full message");
	}

	[Test, NotInParallel]
	public async Task GetMotdData_CanBeRetrieved()
	{
		var motdData = new MotdData(
			ConnectMotd: "Test message",
			WizardMotd: null,
			DownMotd: null,
			FullMotd: null
		);
		await _dataService.SetExpandedServerDataAsync(motdData);

		var result = await _dataService.GetExpandedServerDataAsync<MotdData>();

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ConnectMotd).IsEqualTo("Test message");
	}
}
