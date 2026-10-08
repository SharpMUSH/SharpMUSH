using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Kit;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class CharacterAvatarTests : TrackingBunitContext
{
	/// <summary>A directory of pictures by objid, dbref or name, as the test gives them.</summary>
	private sealed class Pictures : ICharacterPictures
	{
		public Dictionary<string, string> ByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
		public List<string> Asked { get; } = [];
		public event Action? Changed;

		/// <summary>When set, each lookup waits on its own source, answered by the test.</summary>
		public Queue<TaskCompletionSource<string?>>? Pending { get; set; }

		public Task<string?> PictureOfAsync(string character, CancellationToken cancellationToken = default)
		{
			Asked.Add(character);
			if (Pending is { } pending)
			{
				var answer = new TaskCompletionSource<string?>();
				pending.Enqueue(answer);
				return answer.Task;
			}

			return Task.FromResult(ByKey.GetValueOrDefault(character));
		}

		public void Change() => Changed?.Invoke();
	}

	private readonly Pictures _pictures = new();

	public CharacterAvatarTests() => Services.AddSingleton<ICharacterPictures>(_pictures);

	[Test]
	public async Task AKnownPicture_IsShown_WithoutAskingTheDirectory()
	{
		var cut = Render<CharacterAvatar>(p => p.Add(x => x.Name, "Wren Halloway").Add(x => x.ImageUrl, "/w.jpg")
			.Add(x => x.Character, "Wren Halloway").Add(x => x.Class, "rail-avatar"));
		var img = cut.Find("img.rail-avatar");
		await Assert.That(img.GetAttribute("src")).EndsWith("/w.jpg");
		await Assert.That(img.GetAttribute("alt")).IsEqualTo("");
		await Assert.That(_pictures.Asked).IsEmpty();
	}

	[Test]
	[Arguments("#7:1700000000000")]
	[Arguments("#7")]
	[Arguments("Wren Halloway")]
	public async Task GivenOnlyTheCharacter_TheDirectorysPictureIsShown(string character)
	{
		_pictures.ByKey[character] = "/w.jpg";
		var cut = Render<CharacterAvatar>(p => p.Add(x => x.Name, "Wren Halloway").Add(x => x.Character, character));
		cut.WaitForAssertion(() => cut.Find("img"));
		await Assert.That(cut.Find("img").GetAttribute("src")).EndsWith("/w.jpg");
	}

	[Test]
	public async Task WithoutAPicture_TheInitialsShow_OnTheNamesHue()
	{
		var cut = Render<CharacterAvatar>(p => p.Add(x => x.Name, "Dace Kellan").Add(x => x.Character, "Dace Kellan")
			.Add(x => x.Class, "rail-avatar").Add(x => x.FallbackClass, "rail-initials"));
		var initials = cut.Find("span.rail-avatar.rail-initials");
		await Assert.That(initials.TextContent).IsEqualTo("DK");
		await Assert.That(initials.GetAttribute("aria-hidden")).IsEqualTo("true");
		await Assert.That(initials.GetAttribute("style")).Contains($"hsl({NameHue.Of("Dace Kellan")} 30% 24%)");
	}

	[Test]
	public async Task WithoutACharacter_NothingIsLookedUp()
	{
		var cut = Render<CharacterAvatar>(p => p.Add(x => x.Name, "A crate"));
		await Assert.That(cut.Find("span").TextContent).IsEqualTo("AC");
		await Assert.That(_pictures.Asked).IsEmpty().Because("what an avatar stands for may not be a character");
	}

	[Test]
	public async Task AnUnsafePicture_FallsBackToTheDirectory_ThenTheInitials()
	{
		var cut = Render<CharacterAvatar>(p => p.Add(x => x.Name, "Ilsa Varn").Add(x => x.ImageUrl, "http://evil/x.jpg")
			.Add(x => x.Character, "Ilsa Varn"));
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);
		await Assert.That(cut.Find("span").TextContent).IsEqualTo("IV");
	}

	[Test]
	public async Task APictureChangedElsewhere_IsShownWithoutAReload()
	{
		var cut = Render<CharacterAvatar>(p => p.Add(x => x.Name, "Ilsa Varn").Add(x => x.Character, "#3"));
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);

		_pictures.ByKey["#3"] = "/new.jpg";
		_pictures.Change();

		cut.WaitForAssertion(() => cut.Find("img"));
		await Assert.That(cut.Find("img").GetAttribute("src")).EndsWith("/new.jpg");
	}

	[Test]
	public async Task AnotherCharacter_IsNotShownThePreviousOnesPicture()
	{
		_pictures.ByKey["#3"] = "/ilsa.jpg";
		var cut = Render<CharacterAvatar>(p => p.Add(x => x.Name, "Ilsa Varn").Add(x => x.Character, "#3"));
		cut.WaitForAssertion(() => cut.Find("img"));

		cut.Render(p => p.Add(x => x.Name, "Dace Kellan").Add(x => x.Character, "#4"));
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);
		await Assert.That(cut.Find("span").TextContent).IsEqualTo("DK");
	}

	[Test]
	public async Task APictureChangedElsewhere_ReplacesTheOneTheCallerGave()
	{
		var cut = Render<CharacterAvatar>(p => p.Add(x => x.Name, "Ilsa Varn").Add(x => x.ImageUrl, "/old.jpg").Add(x => x.Character, "#3"));
		await Assert.That(cut.Find("img").GetAttribute("src")).EndsWith("/old.jpg");

		_pictures.ByKey["#3"] = "/new.jpg";
		_pictures.Change();
		cut.WaitForState(() => cut.Find("img").GetAttribute("src")?.EndsWith("/new.jpg") == true);

		_pictures.ByKey.Remove("#3");
		_pictures.Change();
		cut.WaitForAssertion(() => cut.Find("span"));
		await Assert.That(cut.Find("span").TextContent).IsEqualTo("IV").Because("a removed avatar is not drawn from the caller's stale copy");
	}

	[Test]
	public async Task ALookupOvertakenByALaterOne_IsNotTheAnswer()
	{
		_pictures.Pending = new();
		var cut = Render<CharacterAvatar>(p => p.Add(x => x.Name, "Ilsa Varn").Add(x => x.Character, "#3"));
		var before = _pictures.Pending.Dequeue();

		_pictures.Change();
		cut.WaitForState(() => _pictures.Pending.Count == 1);
		var after = _pictures.Pending.Dequeue();

		await cut.InvokeAsync(() => after.SetResult("/new.jpg"));
		await cut.InvokeAsync(() => before.SetResult("/old.jpg"));

		cut.WaitForAssertion(() => cut.Find("img"));
		await Assert.That(cut.Find("img").GetAttribute("src")).EndsWith("/new.jpg");
	}
}
