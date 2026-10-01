using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Gallery;

/// <summary>
/// A gallery write is the gallery attribute plus its three image mirrors. When any step fails the
/// steps already taken are put back, so the portal's gallery and softcode's <c>IMAGE</c> attributes
/// never disagree about which picture is the character's.
/// </summary>
public class GalleryWriterTests
{
	/// <summary>Attributes in memory; a named attribute refuses its next write.</summary>
	private sealed class Store(Dictionary<string, string> attributes) : GalleryWriter.IStore
	{
		public Dictionary<string, string> Attributes { get; } = attributes;

		public string? Refuse { get; set; }

		public ValueTask<MString?> ReadAsync(string attribute) =>
			ValueTask.FromResult(Attributes.TryGetValue(attribute, out var value) ? MString.Plain(value) : null);

		public ValueTask<Result<Success>> SetAsync(string attribute, MString value) => Write(attribute, value.ToPlainText());

		public ValueTask<Result<Success>> ClearAsync(string attribute) => Write(attribute, null);

		private ValueTask<Result<Success>> Write(string attribute, string? value)
		{
			if (attribute == Refuse)
			{
				Refuse = null;
				return ValueTask.FromResult<Result<Success>>(new Error<string>($"{attribute} refused"));
			}

			if (value is null) Attributes.Remove(attribute);
			else Attributes[attribute] = value;
			return ValueTask.FromResult<Result<Success>>(new Success());
		}
	}

	private static GalleryEntry Entry(string id, bool icon = false, bool banner = false, string? caption = null) =>
		new(id, $"{id}.jpg", $"/api/wiki-assets/{id}/{id}.jpg", caption, 0, icon, banner);

	private static Dictionary<string, string> Before() => new()
	{
		[GalleryWriter.GalleryAttribute] = "[old]",
		["IMAGE"] = "/old.jpg",
		["IMAGE`BANNER"] = "/old-banner.jpg",
	};

	[Test]
	public async Task AWrite_SetsTheGalleryAndItsMirrors()
	{
		var store = new Store(Before());
		var result = await GalleryWriter.WriteAsync(store, [Entry("a", icon: true, caption: "Tomas")], NullLogger.Instance);

		result.Expect<Success>();
		await Assert.That(store.Attributes["IMAGE"]).IsEqualTo("/api/wiki-assets/a/a.jpg");
		await Assert.That(store.Attributes["IMAGE`ALT"]).IsEqualTo("Tomas");
		await Assert.That(store.Attributes.ContainsKey("IMAGE`BANNER")).IsFalse();
		await Assert.That(store.Attributes[GalleryWriter.GalleryAttribute]).Contains("\"a\"");
	}

	[Test]
	[Arguments("IMAGE")]
	[Arguments("IMAGE`ALT")]
	[Arguments("IMAGE`BANNER")]
	[Arguments(GalleryWriter.GalleryAttribute)]
	public async Task AFailedStep_PutsBackEveryStepAlreadyTaken(string refused)
	{
		var store = new Store(Before()) { Refuse = refused };
		var result = await GalleryWriter.WriteAsync(store, [Entry("a", icon: true, caption: "Tomas")], NullLogger.Instance);

		result.Expect<Error<string>>();
		await Assert.That(store.Attributes).IsEquivalentTo(Before())
			.Because($"{refused} failed, so the gallery and its mirrors are as they were");
	}
}
