using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.ClientState;

public class CharacterStateServiceTests
{
	/// <summary>
	/// Creates a mock IJSRuntime whose localStorage.getItem returns
	/// <paramref name="storedValue"/>.
	/// </summary>
	private static IJSRuntime MakeJs(string? storedValue = null)
	{
		var js = Substitute.For<IJSRuntime>();
		js.InvokeAsync<string?>(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<object?[]?>())
			.Returns(new ValueTask<string?>(storedValue));
		js.InvokeAsync<string?>(Arg.Any<string>(), Arg.Any<object?[]?>())
			.Returns(new ValueTask<string?>(storedValue));
		return js;
	}

	[Test]
	public async Task InitialState_AllPropertiesAreNull()
	{
		var svc = new CharacterStateService(MakeJs());
		await Assert.That(svc.CurrentCharacterDbref).IsNull();
		await Assert.That(svc.CurrentCharacterName).IsNull();
		await Assert.That(svc.CurrentRoomDbref).IsNull();
	}

	[Test]
	public async Task SetCharacterAsync_SetsDbrefAndName()
	{
		var svc = new CharacterStateService(MakeJs());
		await svc.SetCharacterAsync("#5", "Gandalf");
		await Assert.That(svc.CurrentCharacterDbref).IsEqualTo("#5");
		await Assert.That(svc.CurrentCharacterName).IsEqualTo("Gandalf");
	}

	[Test]
	public async Task SetCharacterAsync_FiresOnCharacterChanged()
	{
		var svc = new CharacterStateService(MakeJs());
		var fired = false;
		svc.OnCharacterChanged += () => fired = true;
		await svc.SetCharacterAsync("#5", "Gandalf");
		await Assert.That(fired).IsTrue();
	}

	[Test]
	public async Task SetCharacterAsync_DoesNotChangeRoomDbref()
	{
		var svc = new CharacterStateService(MakeJs());
		await svc.SetCharacterAsync("#5", "Gandalf");
		await Assert.That(svc.CurrentRoomDbref).IsNull();
	}

	[Test]
	public async Task SetRoomAsync_SetsRoomDbref()
	{
		var svc = new CharacterStateService(MakeJs());
		await svc.SetRoomAsync("#100");
		await Assert.That(svc.CurrentRoomDbref).IsEqualTo("#100");
	}

	[Test]
	public async Task SetRoomAsync_FiresOnRoomChanged()
	{
		var svc = new CharacterStateService(MakeJs());
		var fired = false;
		svc.OnRoomChanged += () => fired = true;
		await svc.SetRoomAsync("#100");
		await Assert.That(fired).IsTrue();
	}

	[Test]
	public async Task SetRoomAsync_DoesNotFireOnCharacterChanged()
	{
		var svc = new CharacterStateService(MakeJs());
		var fired = false;
		svc.OnCharacterChanged += () => fired = true;
		await svc.SetRoomAsync("#100");
		await Assert.That(fired).IsFalse();
	}

	[Test]
	public async Task InitializeAsync_WithStoredValue_RestoresDbrefAndName()
	{
		var svc = new CharacterStateService(MakeJs("#5|Gandalf"));
		await svc.InitializeAsync();
		await Assert.That(svc.CurrentCharacterDbref).IsEqualTo("#5");
		await Assert.That(svc.CurrentCharacterName).IsEqualTo("Gandalf");
	}

	[Test]
	public async Task InitializeAsync_WithNullStored_LeavesPropertiesNull()
	{
		var svc = new CharacterStateService(MakeJs(null));
		await svc.InitializeAsync();
		await Assert.That(svc.CurrentCharacterDbref).IsNull();
		await Assert.That(svc.CurrentCharacterName).IsNull();
	}

	[Test]
	public async Task InitializeAsync_WithMalformedStored_LeavesPropertiesNull()
	{
		var svc = new CharacterStateService(MakeJs("nodivider"));
		await svc.InitializeAsync();
		await Assert.That(svc.CurrentCharacterDbref).IsNull();
	}

	[Test]
	public async Task InitializeAsync_CalledTwice_DoesNotThrow()
	{
		var svc = new CharacterStateService(MakeJs("#5|Frodo"));
		await svc.InitializeAsync();
		await svc.InitializeAsync();
		await Assert.That(svc.CurrentCharacterDbref).IsEqualTo("#5");
	}

	[Test]
	public async Task SetCharacterAsync_TwiceDifferentValues_ReturnsLatest()
	{
		var svc = new CharacterStateService(MakeJs());
		await svc.SetCharacterAsync("#5", "Gandalf");
		await svc.SetCharacterAsync("#12", "Frodo");
		await Assert.That(svc.CurrentCharacterDbref).IsEqualTo("#12");
		await Assert.That(svc.CurrentCharacterName).IsEqualTo("Frodo");
	}
}
