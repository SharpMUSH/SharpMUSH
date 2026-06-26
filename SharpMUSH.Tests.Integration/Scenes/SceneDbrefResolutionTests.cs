using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// Proves the live <c>*Dbref</c> values on scene records — resolved from the object edges — round-trip
/// back to the real object, not just the <c>*Name</c> snapshot. Driven over the WIRE (the host can no
/// longer name <c>ISceneService</c> now that it lives inside the Scene plugin's ALC): writes go through the
/// wizard-only <c>scene…()</c> side-effect functions and the resolved dbref is read back through the
/// <c>scene…()</c> read functions. Object references are <c>#1</c> (the seeded God object), so a correct
/// resolution must come back as <c>#1</c>. Runs identically on all three providers.
/// </summary>
[NotInParallel]
public class SceneDbrefResolutionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactory.FunctionParser;

	private const string God = "#1";

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText().Trim();

	private async Task<string> NewPublicSceneAsync(string title)
	{
		var id = await Eval($"scenecreate(,{God},{title} {Guid.NewGuid():N})");
		await Eval($"sceneset({id},public,1)");
		return id;
	}

	[Test]
	public async Task CreateScene_ResolvesOwnerDbref_BackToGod()
	{
		var id = await NewPublicSceneAsync("Dbref owner");

		await Assert.That(await Eval($"scene({id}, ownername)")).IsNotEmpty();
		await Assert.That(await Eval($"scene({id}, owner)")).IsEqualTo(God);
	}

	[Test]
	public async Task AddPose_ResolvesAuthorDbref_BackToGod()
	{
		var id = await NewPublicSceneAsync("Dbref author");
		var poseId = await Eval($"sceneaddpose({id},{God},,{God},pose,,dbref author check)");
		await Assert.That(poseId).DoesNotStartWith("#-1");

		await Assert.That(await Eval($"scenepose({id}, {poseId}, authorname)")).IsNotEmpty();
		await Assert.That(await Eval($"scenepose({id}, {poseId}, author)")).IsEqualTo(God);
	}

	[Test]
	public async Task AddMember_ResolvesMemberDbref_BackToGod()
	{
		var id = await NewPublicSceneAsync("Dbref member");

		await Assert.That(await Eval($"sceneaddmember({id},{God},participant)")).IsEqualTo(God);

		await Assert.That(await Eval($"scenemembers({id})")).Contains(God);
	}
}
