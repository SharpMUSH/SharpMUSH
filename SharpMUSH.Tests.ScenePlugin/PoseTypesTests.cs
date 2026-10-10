using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Plugins.Scene.Models;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>Reading a pose type's <c>TYPE`&lt;KEY&gt;</c> JSON, and what a type key may be.</summary>
public class PoseTypesTests
{
	private static T Expect<T>(System.Runtime.CompilerServices.IUnion union) => union.Value switch
	{
		T value => value,
		var other => throw new InvalidOperationException($"Expected a {typeof(T).Name}, but the result was {other}.")
	};

	[Test]
	public async Task EveryFieldIsRead()
	{
		var type = Expect<PoseType>(PoseTypes.Read("RADIO",
			"""{"label":"Radio","presentation":"message","tone":"Info","icon":"radio","hidden":true,"order":40}"""));

		await Assert.That(type).IsEqualTo(new PoseType("radio", "Radio", "message", "info", "radio", true, 40));
	}

	[Test]
	public async Task AToneMayBeAHue()
		=> await Assert.That(Expect<PoseType>(PoseTypes.Read("radio", """{"tone":"Purple"}""")).Tone).IsEqualTo("purple");

	[Test]
	public async Task AnEmptyObjectIsAnInCharacterLookNamedByItsKey()
		=> await Assert.That(Expect<PoseType>(PoseTypes.Read("dice", "{}")))
			.IsEqualTo(new PoseType("dice", "dice", "prose", "", "", false, 50));

	[Test]
	[Arguments("""{"presentation":"bubble"}""", "presentation must be one of prose, band, message, aside, notice")]
	[Arguments("""{"tone":"mauve"}""", "tone must be one of")]
	[Arguments("""{"tone":"highlight"}""", "tone must be one of")]
	[Arguments("""{"icon":"skull"}""", "icon must be one of")]
	[Arguments("""{"hidden":"yes"}""", "hidden must be true or false")]
	[Arguments("""{"order":1.5}""", "order must be a whole number")]
	[Arguments("""{"label":""}""", "label must be text")]
	[Arguments("""{"colour":"red"}""", "'colour' is not a field")]
	[Arguments("""["ic"]""", "not a JSON object")]
	[Arguments("""{"label":""", "not JSON")]
	public async Task AFieldThatDoesNotFitIsRefusedWithWhy(string json, string reason)
		=> await Assert.That(Expect<Error<string>>(PoseTypes.Read("x", json)).Value).StartsWith(reason);

	[Test]
	[Arguments("ic", "ic")]
	[Arguments(" OOC ", "ooc")]
	[Arguments("", "ic")]
	[Arguments("radio-ship_2", "radio-ship_2")]
	public async Task AKeyIsKeptInLowerCase(string key, string kept)
		=> await Assert.That(Expect<string>(PoseTypes.Normalize(key))).IsEqualTo(kept);

	[Test]
	[Arguments("two words")]
	[Arguments("-leading")]
	[Arguments("TYPE`IC")]
	[Arguments("abcdefghijklmnopqrstuvwxyz0123456")]
	public async Task AKeyThatCannotBeOneIsRefused(string key)
		=> await Assert.That(Expect<Error<string>>(PoseTypes.Normalize(key)).Value).IsEqualTo(PoseTypes.InvalidKey);
}
