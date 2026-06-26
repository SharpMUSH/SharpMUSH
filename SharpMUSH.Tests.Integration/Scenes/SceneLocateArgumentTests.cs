using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// Proves the scene functions resolve object-reference arguments (rooms, players) through the engine
/// <c>LocateService</c> — so <c>me</c>, <c>here</c>, and player names work, exactly like every other
/// engine function, not just bare dbrefs. Before the fix the scene functions handed the raw argument
/// straight to storage (which only does <c>DBRef.TryParse</c>), so <c>scenewhere(here)</c> /
/// <c>scenefocus(me)</c> returned <c>#-1 NOT FOUND</c> even with an active, focused scene present.
///
/// Driven over the WIRE via the <c>scene…()</c> functions. The <see cref="ServerWebAppFactory.FunctionParser"/>
/// binds enactor = executor = <c>#1</c> (God), so <c>me</c> must resolve to <c>#1</c> and <c>here</c> to
/// <c>#1</c>'s location. Runs identically on all three providers.
///
/// <para><c>[NotInParallel]</c> for the same reason as <c>SceneDbrefResolutionTests</c>: every test drives
/// the single shared God object (<c>#1</c>), and <c>scenecreate</c> focuses the owner on the new scene, so
/// running the focus round-trip concurrently with another test's create would race on <c>#1</c>'s one
/// focus pointer.</para>
/// </summary>
[NotInParallel]
public class SceneLocateArgumentTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactory.FunctionParser;

	private const string God = "#1";

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText().Trim();

	[Test]
	public async Task SceneCreate_ResolvesOwner_FromMeKeyword()
	{
		var id = await Eval($"scenecreate(,me,Locate owner {Guid.NewGuid():N})");
		await Assert.That(id).DoesNotStartWith("#-1");
		await Eval($"sceneset({id},public,1)");

		await Assert.That(await Eval($"scene({id}, owner)")).IsEqualTo(God);
	}

	[Test]
	public async Task SceneAddMember_ResolvesPlayer_FromMeKeyword()
	{
		var id = await Eval($"scenecreate(,{God},Locate member {Guid.NewGuid():N})");
		await Eval($"sceneset({id},public,1)");

		await Assert.That(await Eval($"sceneaddmember({id},me,participant)")).IsEqualTo(God);
		await Assert.That(await Eval($"scenemembers({id})")).Contains(God);
	}

	[Test]
	public async Task SceneFocus_ResolvesPlayer_FromMeKeyword()
	{
		var id = await Eval($"scenecreate(,{God},Locate focus {Guid.NewGuid():N})");
		await Eval($"sceneset({id},public,1)");

		await Eval($"sceneaddmember({id},me,participant)");
		await Eval($"scenesetfocus(me,{id})");
		await Assert.That(await Eval($"scenefocus(me)")).IsEqualTo(id);
	}

	[Test]
	public async Task SceneCreate_ResolvesOwner_FromPlayerName()
	{
		var id = await Eval($"scenecreate(,God,Locate name {Guid.NewGuid():N})");
		await Assert.That(id).DoesNotStartWith("#-1");
		await Eval($"sceneset({id},public,1)");

		await Assert.That(await Eval($"scene({id}, owner)")).IsEqualTo(God);
	}

	[Test]
	public async Task SceneWhere_ResolvesRoom_FromHereKeyword()
	{
		var id = await Eval($"scenecreate(here,{God},Locate here {Guid.NewGuid():N})");
		await Assert.That(id).DoesNotStartWith("#-1");
		await Eval($"sceneset({id},public,1)");
		await Eval($"sceneset({id},status,active)");

		await Assert.That(await Eval($"scenewhere(here)")).IsEqualTo(id);
	}
}
