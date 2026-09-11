using SharpMUSH.Library;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Internal;

/// <summary>
/// The three <c>object/attribute</c> splitters return the halves as written, or
/// <see langword="null"/> when the text is not a spec of their shape.
/// </summary>
public class ObjectAttributeSplitTests
{
	[Test]
	[Arguments("#1/FOO", "#1", "FOO")]
	[Arguments("me/foo`bar", "me", "foo`bar")]
	[Arguments("#12:345/A.B", "#12:345", "A.B")]
	public async Task SplitObjectAndAttrSplitsBothHalves(string text, string obj, string attribute)
		=> await Assert.That(HelperFunctions.SplitObjectAndAttr(text)).IsEqualTo(new ObjectAttribute(obj, attribute));

	[Test]
	[Arguments("")]
	[Arguments("FOO")]
	[Arguments("#1/")]
	[Arguments("/FOO")]
	[Arguments("#1/FOO BAR")]
	[Arguments("a/b/c")]
	public async Task SplitObjectAndAttrRejects(string text)
		=> await Assert.That(HelperFunctions.SplitObjectAndAttr(text)).IsNull();

	[Test]
	[Arguments("#1/FOO", "#1", "FOO")]
	[Arguments("me/foo`bar", "me", "foo`bar")]
	[Arguments("FOO", null, "FOO")]
	public async Task SplitOptionalObjectAndAttrSplitsBothHalves(string text, string? obj, string attribute)
		=> await Assert.That(HelperFunctions.SplitOptionalObjectAndAttr(text))
			.IsEqualTo(new AttributeWithOptionalObject(obj, attribute));

	[Test]
	[Arguments("")]
	[Arguments("#1/")]
	[Arguments("FOO BAR")]
	[Arguments("a/b/c")]
	public async Task SplitOptionalObjectAndAttrRejects(string text)
		=> await Assert.That(HelperFunctions.SplitOptionalObjectAndAttr(text)).IsNull();

	[Test]
	[Arguments("#1/FOO", "#1", "FOO")]
	[Arguments("me/foo`bar", "me", "foo`bar")]
	[Arguments("#1", "#1", null)]
	[Arguments("here", "here", null)]
	public async Task SplitDbRefAndOptionalAttrSplitsBothHalves(string text, string obj, string? attribute)
		=> await Assert.That(HelperFunctions.SplitDbRefAndOptionalAttr(text))
			.IsEqualTo(new ObjectWithOptionalAttribute(obj, attribute));

	[Test]
	[Arguments("")]
	[Arguments("#1/")]
	[Arguments("/FOO")]
	[Arguments("#1/FOO BAR")]
	public async Task SplitDbRefAndOptionalAttrRejects(string text)
		=> await Assert.That(HelperFunctions.SplitDbRefAndOptionalAttr(text)).IsNull();
}
