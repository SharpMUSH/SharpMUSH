using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// The Scene writes no other test ran: invitations and joining, pose metadata and order, removing a
/// member, personas and plots, through the wizard-only <c>scene…()</c> functions and the matching
/// <c>@scene</c> switches, each read back through the read functions. Every test makes its own scene and
/// players, so nothing here depends on another test's data.
/// </summary>
[NotInParallel]
public class SceneMembershipAndPoseOrderTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IMUSHCodeParser CommandParser => WebAppFactory.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactory.FunctionParser;
	private IConnectionService Connection => WebAppFactory.Services.GetRequiredService<IConnectionService>();

	private const string God = "#1";

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	/// <summary>Runs a command as God and returns what it answered (the scene, pose or plot id, or an error).</summary>
	private async Task<string> Cmd(string command) =>
		(await CommandParser.CommandParse(1, Connection, MarkupText.Plain(command))).Message?.ToPlainText().Trim() ?? string.Empty;

	private async Task<string> CreateSceneAsync(string title) =>
		await Eval($"scenecreate(,{God},{title} {Guid.NewGuid():N})");

	/// <summary>A new player's bare dbref; scenes store members by number.</summary>
	private async Task<string> CreatePlayerAsync(string prefix)
	{
		var name = $"{prefix}{Guid.NewGuid():N}"[..15];
		await Cmd($"@pcreate {name}=pw-{name}");
		var objid = await Eval($"pmatch({name})");
		return objid.Split(':')[0];
	}

	private async Task<string> PoseAsync(string sceneId, string content) =>
		await Eval($"sceneaddpose({sceneId},{God},,{God},ic,pose,,{content})");

	[Test]
	public async Task InviteOnlyScene_TakesOnlyInvitedPlayers()
	{
		var sceneId = await CreateSceneAsync("Invite");
		var player = await CreatePlayerAsync("Invitee");
		await Cmd($"@scene/set {sceneId}/joinpolicy=invite-only");

		await Assert.That(await Eval($"scenejoin({sceneId},{player})"))
			.IsEqualTo("#-1 This scene is invite-only. Ask its owner for an invitation.");
		await Assert.That(await Eval($"scenemembers({sceneId})")).DoesNotContain(player);

		await Assert.That(await Eval($"sceneinvite({sceneId},{player})")).IsEqualTo(sceneId);
		await Assert.That(await Eval($"scenemembers({sceneId})")).DoesNotContain(player)
			.Because("an invitation is not a seat");

		await Assert.That(await Eval($"scenejoin({sceneId},{player})")).IsEqualTo(sceneId);
		await Assert.That(await Eval($"scenemember({sceneId},{player})")).IsEqualTo("participant");
		await Assert.That(await Eval($"sceneinvite({sceneId},{player})")).IsEqualTo("#-1 That player already participates.");
	}

	[Test]
	public async Task PrivateScene_CannotBeJoinedUninvited()
	{
		var sceneId = await CreateSceneAsync("Private");
		var player = await CreatePlayerAsync("Outsider");
		await Cmd($"@scene/set {sceneId}/public=0");

		await Assert.That(await Eval($"scenejoin({sceneId},{player})")).IsEqualTo("#-1 NOT FOUND");

		await Eval($"sceneinvite({sceneId},{player})");
		await Assert.That(await Eval($"scenejoin({sceneId},{player})")).IsEqualTo(sceneId);
	}

	[Test]
	public async Task FullScene_RefusesTheNextJoin()
	{
		var sceneId = await CreateSceneAsync("Capped");
		var first = await CreatePlayerAsync("First");
		var second = await CreatePlayerAsync("Second");

		await Assert.That(await Eval($"scenejoin({sceneId},{first})")).IsEqualTo(sceneId);
		await Cmd($"@scene/set {sceneId}/playercap=1");

		await Assert.That(await Eval($"scenejoin({sceneId},{second})")).IsEqualTo("#-1 This scene has reached its player cap.");
		await Assert.That(await Eval($"scenemembers({sceneId})")).IsEqualTo(first);
	}

	[Test]
	public async Task SceneSetPose_SetsAKeyAndRefusesAnUnknownType()
	{
		var sceneId = await CreateSceneAsync("SetPose");
		var poseId = await PoseAsync(sceneId, "A quiet evening.");

		await Assert.That(await Eval($"scenesetpose({poseId},type,ooc)")).IsEqualTo(poseId);
		await Assert.That(await Eval($"scenepose({sceneId},{poseId},type)")).IsEqualTo("ooc");

		await Assert.That(await Eval($"scenesetpose({poseId},type,not a key)")).StartsWith("#-1");
		await Assert.That(await Eval($"scenepose({sceneId},{poseId},type)")).IsEqualTo("ooc");

		await Assert.That(await Cmd($"@scene/setpose {poseId}/tags=weather")).IsEqualTo(poseId);
		await Assert.That(await Eval($"scenepose({sceneId},{poseId},tags)")).IsEqualTo("weather");
	}

	[Test]
	public async Task MovingAPose_ReordersTheScene()
	{
		var sceneId = await CreateSceneAsync("Move");
		var one = await PoseAsync(sceneId, "one");
		var two = await PoseAsync(sceneId, "two");
		var three = await PoseAsync(sceneId, "three");

		await Assert.That(await Eval($"scenemovepose({three})")).IsEqualTo(three);
		await Assert.That(await Eval($"sceneposes({sceneId})")).IsEqualTo($"{three} {one} {two}");

		await Assert.That(await Eval($"scenemovepose({three},{one})")).IsEqualTo(three);
		await Assert.That(await Eval($"sceneposes({sceneId})")).IsEqualTo($"{one} {three} {two}");

		await Assert.That(await Cmd($"@scene/move {one}={two}")).IsEqualTo(one);
		await Assert.That(await Eval($"sceneposes({sceneId})")).IsEqualTo($"{three} {two} {one}");

		await Assert.That(await Eval($"scenemovepose({one},{one})")).IsEqualTo("Cannot move a pose after itself.");
		await Assert.That(await Eval($"sceneposes({sceneId})")).IsEqualTo($"{three} {two} {one}");
	}

	[Test]
	public async Task DeletingAPose_MarksItDeletedInPlace()
	{
		var sceneId = await CreateSceneAsync("Delete");
		var kept = await PoseAsync(sceneId, "kept");
		var byFunction = await PoseAsync(sceneId, "gone by function");
		var byCommand = await PoseAsync(sceneId, "gone by command");

		await Assert.That(await Eval($"scenedelpose({byFunction})")).IsEqualTo(byFunction);
		await Assert.That(await Cmd($"@scene/delete {byCommand}")).IsEqualTo(byCommand);

		await Assert.That(await Eval($"sceneposes({sceneId})")).IsEqualTo($"{kept} {byFunction} {byCommand}")
			.Because("a deleted pose keeps its place and its history");
		await Assert.That(await Eval($"scenepose({sceneId},{kept},deleted)")).IsEqualTo("0");
		await Assert.That(await Eval($"scenepose({sceneId},{byFunction},deleted)")).IsEqualTo("1");
		await Assert.That(await Eval($"scenepose({sceneId},{byCommand},deleted)")).IsEqualTo("1");
		await Assert.That(await Cmd($"@scene/delete no-such-pose-{Guid.NewGuid():N}")).IsEqualTo("#-1 NO SUCH SCENE OR POSE");
	}

	[Test]
	public async Task RemovingAMember_DropsThemFromTheScene()
	{
		var sceneId = await CreateSceneAsync("Unmember");
		var byFunction = await CreatePlayerAsync("UnmemberF");
		var byCommand = await CreatePlayerAsync("UnmemberC");
		await Eval($"sceneaddmember({sceneId},{byFunction},participant)");
		await Eval($"sceneaddmember({sceneId},{byCommand},gm)");
		await Assert.That(await Eval($"scenemember({sceneId},{byFunction})")).IsEqualTo("participant");
		await Assert.That(await Eval($"scenemember({sceneId},{byCommand})")).IsEqualTo("gm");

		await Assert.That(await Eval($"sceneunmember({sceneId},{byFunction})")).IsEqualTo(sceneId);
		await Assert.That(await Cmd($"@scene/unmember {sceneId}={byCommand}")).IsEqualTo(sceneId);

		await Assert.That(await Eval($"scenemembers({sceneId})")).IsEmpty();
		await Assert.That(await Eval($"sceneunmember(no-such-scene-{Guid.NewGuid():N},{byFunction})")).IsEqualTo("#-1 NOT FOUND");
	}

	[Test]
	public async Task ShowAs_SetsAndClearsAMembersPersona()
	{
		var sceneId = await CreateSceneAsync("ShowAs");
		var player = await CreatePlayerAsync("Persona");
		await Eval($"sceneaddmember({sceneId},{player},participant)");

		await Assert.That(await Cmd($"@scene/showas {sceneId}/{player}=The Masked Stranger")).IsEqualTo(sceneId);
		await Assert.That(await Eval($"scenemember({sceneId},{player},showas)")).IsEqualTo("The Masked Stranger");

		await Assert.That(await Cmd($"@scene/showas {sceneId}/{player}=")).IsEqualTo(sceneId);
		await Assert.That(await Eval($"scenemember({sceneId},{player},showas)")).IsEmpty();
	}

	[Test]
	public async Task Plots_AreCreatedUpdatedAndLinked()
	{
		var sceneId = await CreateSceneAsync("Plot");

		var plotId = await Eval($"sceneplot(create,Harvest {Guid.NewGuid():N}|The long autumn|{God})");
		await Assert.That(plotId).DoesNotStartWith("#-1");
		await Assert.That(await Eval($"sceneplot(update,Harvest renamed|The long winter|{God},{plotId})")).IsEqualTo(plotId);
		await Assert.That(await Cmd($"@scene/plot {plotId}")).IsEqualTo(plotId);

		await Assert.That(await Eval($"sceneplot(link,{plotId},{sceneId})")).IsEqualTo(plotId);
		await Assert.That(await Eval($"sceneplot(unlink,{plotId},{sceneId})")).IsEqualTo(plotId);
		await Assert.That(await Eval($"sceneplot(link,{plotId},no-such-scene-{Guid.NewGuid():N})")).IsEqualTo("#-1 NOT FOUND");
		await Assert.That(await Eval($"sceneplot(rename,{plotId})")).StartsWith("#-1");

		var created = await Cmd($"@scene/plot/create {God},Second plot {Guid.NewGuid():N}");
		await Assert.That(created).DoesNotStartWith("#-1");
		await Assert.That(created).IsNotEqualTo(plotId);
		await Assert.That(await Cmd($"@scene/plot/link {created}={sceneId}")).IsEqualTo(created);
		await Assert.That(await Cmd($"@scene/plot/unlink {created}={sceneId}")).IsEqualTo(created);
		await Assert.That(await Cmd($"@scene/plot no-such-plot-{Guid.NewGuid():N}")).IsEqualTo("#-1 NO SUCH SCENE OR POSE");
	}

	[Test]
	public async Task SceneGet_ReadsOneKey()
	{
		var sceneId = await CreateSceneAsync("Get");
		await Cmd($"@scene/set {sceneId}/status=active");

		await Assert.That(await Cmd($"@scene/get {sceneId}/status")).IsEqualTo("active");
		await Assert.That(await Cmd($"@scene/get {sceneId}")).IsEqualTo(sceneId);
		await Assert.That(await Cmd($"@scene/get no-such-scene-{Guid.NewGuid():N}/status")).IsEqualTo("#-1 NO SUCH SCENE OR POSE");
	}
}
