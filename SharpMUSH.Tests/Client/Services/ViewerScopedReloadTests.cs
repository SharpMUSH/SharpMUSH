using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// The reload a component holds for data that depends on the acting character: it runs on a switch of
/// character and, when watching scenes, on a reported scene change; it stops when disposed; and a read
/// overtaken by a newer one is not applied.
/// </summary>
public class ViewerScopedReloadTests
{
	private static SceneService Scenes(IAccountAuthState auth) => new(Substitute.For<IHttpClientFactory>(), auth);

	[Test]
	public async Task ASwitchOfCharacter_AndAReportedSceneChange_EachRunTheReload()
	{
		var auth = Substitute.For<IAccountAuthState>();
		var scenes = Scenes(auth);
		using var reload = new ViewerScopedReload();
		var runs = 0;
		reload.Watch(auth, () => runs++, scenes);

		auth.ActiveCharacterChanged += Raise.Event<Action>();
		scenes.ReportChanged();

		await Assert.That(runs).IsEqualTo(2);
	}

	[Test]
	public async Task WithoutScenes_ASceneChangeDoesNotRunIt()
	{
		var auth = Substitute.For<IAccountAuthState>();
		var scenes = Scenes(auth);
		using var reload = new ViewerScopedReload();
		var runs = 0;
		reload.Watch(auth, () => runs++);

		scenes.ReportChanged();

		await Assert.That(runs).IsEqualTo(0);
	}

	[Test]
	public async Task OnceDisposed_NothingRunsIt()
	{
		var auth = Substitute.For<IAccountAuthState>();
		var scenes = Scenes(auth);
		var reload = new ViewerScopedReload();
		var runs = 0;
		reload.Watch(auth, () => runs++, scenes);

		reload.Dispose();
		auth.ActiveCharacterChanged += Raise.Event<Action>();
		scenes.ReportChanged();

		await Assert.That(runs).IsEqualTo(0);
	}

	[Test]
	public async Task AReadOvertakenByANewerOne_IsNotApplied()
	{
		using var reload = new ViewerScopedReload();
		var (older, newer) = (new TaskCompletionSource<string>(), new TaskCompletionSource<string>());
		var applied = new List<string>();

		var first = reload.LoadAsync(() => older.Task, applied.Add);
		var second = reload.LoadAsync(() => newer.Task, applied.Add);
		newer.SetResult("Wren's");
		older.SetResult("Ilsa's");
		await Task.WhenAll(first, second);

		await Assert.That(applied).IsEquivalentTo(["Wren's"]);
	}
}
