using System.Net;
using System.Net.Http.Json;
using System.Text;
using NSubstitute;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The pose type catalogue the portal draws poses by: read once, an unlisted key drawn in character, and hiding a
/// type as the character (<c>+scene/hide &lt;type&gt;=reading|playing</c>, read back) or, with nobody acting, for the
/// page. Reading a scene and playing in one each keep their own choice.
/// </summary>
public class PoseTypeServiceTests : TrackingTestContext
{
	private readonly TypesApi _api = new();

	private PoseTypeService Service(AccountAuthService.CharacterSummary? acting = null)
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		var auth = Substitute.For<IAccountAuthState>();
		auth.ActiveCharacter.Returns(acting);
		return new PoseTypeService(factory, new GameCommandService(factory), auth);
	}

	[Test]
	public async Task AnUnknownKey_DrawsInCharacter_NamedByItsKey()
	{
		var service = Service();
		await service.LoadAsync();

		var unknown = service.For("telepathy");
		await Assert.That(unknown).IsEqualTo(new PoseTypeInfo("telepathy", "telepathy", "prose", "", "", false, false, 50));
		await Assert.That(service.For("").Key).IsEqualTo("ic").Because("a pose stored before types is in character");
		await Assert.That(service.For("OOC").Presentation).IsEqualTo("band").Because("keys are lower case however they arrive");
	}

	[Test]
	public async Task WithNoCatalogue_EveryPose_IsProse()
	{
		_api.NoPackage = true;
		var service = Service();
		await service.LoadAsync();

		await Assert.That(service.Types).IsEmpty();
		await Assert.That(service.For("ooc").Layout).IsEqualTo("prose");
	}

	[Test]
	public async Task TheCatalogue_IsReadOnce()
	{
		var service = Service();
		await service.LoadAsync();
		await service.LoadAsync();

		await Assert.That(_api.TypeReads).IsEqualTo(1);
		await Assert.That(service.IsHidden("narration", StoryContext.Reading)).IsTrue().Because("the server says this character hides it");
		await Assert.That(service.IsHidden("ooc", StoryContext.Reading)).IsTrue().Because("out of character starts hidden while reading");
		await Assert.That(service.IsHidden("ooc", StoryContext.Playing)).IsFalse().Because("and shown while playing");
		await Assert.That(service.Problems.Single().Key).IsEqualTo("broken");
	}

	[Test]
	public async Task WithNobodyActing_HidingIsThePages_AndRunsNoCommand()
	{
		var service = Service();
		await service.LoadAsync();

		await Assert.That(await service.HideAsync("ooc", StoryContext.Playing) is Success).IsTrue();
		await Assert.That(service.IsHidden("ooc", StoryContext.Playing)).IsTrue();
		await Assert.That(_api.Commands).IsEmpty();
		await service.ShowAsync("ooc", StoryContext.Playing);
		await Assert.That(service.IsHidden("ooc", StoryContext.Playing)).IsFalse();
	}

	[Test]
	public async Task AsACharacter_HidingRunsTheCommand_AndReadsTheSetBack()
	{
		var service = Service(new AccountAuthService.CharacterSummary(12, 1, "Wren", ""));
		await service.LoadAsync();

		var result = await service.HideAsync("ooc", StoryContext.Playing);
		await Assert.That(result is Success).IsTrue();
		await Assert.That(_api.Commands).IsEquivalentTo(new[] { "+scene/hide ooc=playing" });
		await Assert.That(service.IsHidden("ooc", StoryContext.Playing)).IsTrue();
	}

	[Test]
	public async Task ShowingWhileReading_LeavesPlayingAsItWas()
	{
		var service = Service(new AccountAuthService.CharacterSummary(12, 1, "Wren", ""));
		await service.LoadAsync();

		await Assert.That(await service.ShowAsync("narration", StoryContext.Reading) is Success).IsTrue();
		await Assert.That(_api.Commands).IsEquivalentTo(new[] { "+scene/show narration=reading" });
		await Assert.That(service.IsHidden("narration", StoryContext.Reading)).IsFalse();
		await Assert.That(service.IsHidden("narration", StoryContext.Playing)).IsTrue();
	}

	[Test]
	public async Task ARefusedHide_SaysWhatTheGameSaid()
	{
		_api.RefuseHide = true;
		var service = Service(new AccountAuthService.CharacterSummary(12, 1, "Wren", ""));
		await service.LoadAsync();

		var result = await service.HideAsync("ooc", StoryContext.Playing);
		await Assert.That(result is ApiFailure { Message: TypesApi.Refusal }).IsTrue();
		await Assert.That(service.IsHidden("ooc", StoryContext.Playing)).IsFalse();
	}

	/// <summary>A story stays mounted across a character switch, so the service reads the new character's set itself.</summary>
	[Test]
	public async Task SwitchingCharacter_ReadsTheNewCharactersHiddenSet()
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		var auth = Substitute.For<IAccountAuthState>();
		auth.ActiveCharacter.Returns(new AccountAuthService.CharacterSummary(12, 1, "Wren", ""));
		var service = new PoseTypeService(factory, new GameCommandService(factory), auth);
		await service.LoadAsync();

		auth.ActiveCharacter.Returns(new AccountAuthService.CharacterSummary(13, 1, "Tomas", ""));
		auth.ActiveCharacterChanged += Raise.Event<Action>();

		await Assert.That(() => _api.TypeReads).Eventually(reads => reads.IsEqualTo(2), TimeSpan.FromSeconds(5));
		await Assert.That(service.IsHidden("narration", StoryContext.Playing)).IsTrue();
	}

	/// <summary>Serves api/scenes/types and the hide/show commands, keeping the hidden sets the way the scene package does.</summary>
	private sealed class TypesApi : HttpMessageHandler
	{
		public const string Refusal = "SCENE: No pose type called ooc.";

		public bool NoPackage { get; set; }
		public bool RefuseHide { get; set; }
		public int TypeReads { get; private set; }
		public List<string> Commands { get; } = [];
		private readonly Dictionary<string, HashSet<string>> _hidden = new()
		{
			["reading"] = ["narration", "ooc"],
			["playing"] = ["narration"],
		};

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath;
			if (path == "/api/commands")
			{
				var command = (await request.Content!.ReadFromJsonAsync<PortalCommandRequest>(cancellationToken))!.Command;
				Commands.Add(command);
				IReadOnlyList<string> output = [];
				if (RefuseHide) output = [Refusal];
				else if (command.Split(' ', '=') is ["+scene/hide", var hide, var hideIn]) _hidden[hideIn].Add(hide);
				else if (command.Split(' ', '=') is ["+scene/show", var show, var showIn]) _hidden[showIn].Remove(show);
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PortalCommandResponse(output, null, false)) };
			}

			if (path != "/api/scenes/types") return new HttpResponseMessage(HttpStatusCode.NotFound);
			TypeReads++;
			var body = NoPackage
				? """{"types":[],"problems":[],"hiddenReading":[],"hiddenPlaying":[]}"""
				: $$"""
				{"types":[{"key":"ic","label":"In character","presentation":"prose","tone":"","icon":"","hiddenReading":false,"hiddenPlaying":false,"order":10},
				          {"key":"ooc","label":"OOC","presentation":"band","tone":"muted","icon":"","hiddenReading":true,"hiddenPlaying":false,"order":20},
				          {"key":"narration","label":"Narration","presentation":"aside","tone":"","icon":"book","hiddenReading":false,"hiddenPlaying":false,"order":30}],
				 "problems":[{"key":"broken","reason":"not JSON"}],
				 "hiddenReading":{{Keys("reading")}},"hiddenPlaying":{{Keys("playing")}}}
				""";
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
		}

		private string Keys(string context) => System.Text.Json.JsonSerializer.Serialize(_hidden[context]);
	}
}
