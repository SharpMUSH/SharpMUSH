using System.Text;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;

namespace SharpMUSH.Tests.Services;

public class RhostStrictReaderTests
{
	private const string Empty = "+V7\n+S0\n+N256\n***END OF DUMP***\n";

	[Test]
	[Arguments("+V7\n+V7\n+S0\n+N256\n***END OF DUMP***\n")]
	[Arguments("+V7\n+S0\n+N256\n+Q1\n***END OF DUMP***\n")]
	[Arguments("+V7\n+N256\n***END OF DUMP***\n")]
	[Arguments("+V7\n+S-1\n+N256\n***END OF DUMP***\n")]
	[Arguments("+V7\n+S0\n+N256\n***END OF DUMP***\ntrailing")]
	[Arguments("+V7\n+S0\n+N257\n+A256\n0:TEST\n+A256\n0:TEST\n***END OF DUMP***\n")]
	public void RejectsInvalidFlatStructure(string text)
		=> Assert.Throws<FormatException>(() => RhostFlatfileReader.ReadStrict(text));

	[Test]
	[Arguments("C1/1\x01\ninvalid\x01\n")]
	[Arguments("C1/1\x01\n1/0/1/3/NNNsubject\x01\n")]
	[Arguments("E1/1\x01\nbody\x01\nE1/1\x01\nbody\x01\n")]
	[Arguments("E1/1\x01\nbody")]
	[Arguments("Awrong\x01\n1/0/0\x01\n")]
	[Arguments("/\x01\nbody\x01\n")]
	[Arguments("E01/1\x01\nbody\x01\nE1/1\x01\nbody\x01\n")]
	[Arguments("C1/1\x01\n1/0/1/not-size/NNNsubject\x01\nE1/1\x01\nbody\x01\n")]
	[Arguments("C1/1\x01\n1/9223372036854775807/1/4/NNNsubject\x01\nE1/1\x01\nbody\x01\n")]
	[Arguments("C1/1\x01\n1/0/1/4/NNNsubject\x01\nC1/1\x01\n1/0/1/4/NNNsubject\x01\nE1/1\x01\nbody\x01\n")]
	public void RejectsInvalidMail(string text)
		=> Assert.Throws<FormatException>(() => RhostMailReader.ReadStrict(text, null));

	[Test]
	public async Task ReadsNameFromAttributeWhenHeaderRequiresIt()
	{
		var world = "+V2055\n+S1\n+N256\n!0\n-1\n-1\n-1\n-1\n\n0\n0\n3\n>43\nWizard\n<\n***END OF DUMP***\n";
		await Assert.That(RhostFlatfileReader.ReadStrict(world).Objects[0].Name).IsEqualTo("Wizard");
		Assert.Throws<FormatException>(() => RhostFlatfileReader.ReadStrict(world.Replace(">43\nWizard\n", "")));
	}

	[Test]
	public async Task AcceptsEmptyWorld()
		=> await Assert.That(RhostFlatfileReader.ReadStrict(Empty).Objects.Count).IsEqualTo(0);
	[Test]
	[Arguments("duplicate-object")]
	[Arguments("duplicate-attribute")]
	[Arguments("missing-object")]
	[Arguments("bad-owner")]
	[Arguments("bad-attribute-owner")]
	[Arguments("undefined-attribute")]
	[Arguments("bad-type")]
	[Arguments("bad-object-number")]
	[Arguments("bad-attribute-prefix")]
	public void RejectsInvalidObjectRelationships(string defect)
	{
		var room = Object(0, "Limbo", 0, 1);
		var player = Object(1, "Wizard", 3, 1);
		var world = "+V7\n+S2\n+N257\n+A256\n0:TEST\n" + room + player + RhostFlatfileReader.EndOfDump + "\n";
		world = defect switch
		{
			"duplicate-object" => world.Replace(player, room),
			"duplicate-attribute" => world.Replace(room, Object(0, "Limbo", 0, 1, ">256\nfirst\n>256\nsecond\n")),
			"missing-object" => world.Replace(room, Object(0, "Limbo", 0, 1, location: 99)),
			"bad-owner" => world.Replace(room, Object(0, "Limbo", 0, 99)),
			"bad-attribute-owner" => world.Replace(room, Object(0, "Limbo", 0, 1, ">256\n\x01" + "99:0:value\n")),
			"undefined-attribute" => world.Replace(room, Object(0, "Limbo", 0, 1, ">257\nvalue\n")),
			"bad-type" => world.Replace(room, Object(0, "Limbo", 6, 1)),
			"bad-object-number" => world.Replace(room, Object(2, "Limbo", 0, 1)),
			_ => world.Replace(room, Object(0, "Limbo", 0, 1, ">256\n\x01" + "broken:value\n"))
		};
		Assert.Throws<FormatException>(() => RhostFlatfileReader.ReadStrict(world));
	}

	[Test]
	public async Task AcceptsRhostHomeAndDefaultAttributeOwnerSentinels()
	{
		var world = "+V7\n+S2\n+N257\n+A256\n0:TEST\n"
			+ Object(0, "Limbo", 0, 1) + Object(1, "Wizard", 3, 1, ">256\n\x01-1:0:value\n", home: -3)
			+ RhostFlatfileReader.EndOfDump + "\n";
		await Assert.That(RhostFlatfileReader.ReadStrict(world).Objects.Count).IsEqualTo(2);
	}

	[Test]
	public async Task AcceptsNonPlayerOwnershipAllowedByRhostImmortals()
	{
		var world = "+V7\n+S1\n+N257\n+A256\n0:TEST\n"
			+ Object(0, "Widget", 1, 0, ">256\n\x01" + "0:0:value\n")
			+ RhostFlatfileReader.EndOfDump + "\n";
		await Assert.That(RhostFlatfileReader.ReadStrict(world).Objects.Single().Owner).IsEqualTo(0);
	}

	[Test]
	public async Task AcceptsHomeInExplicitLinkField()
	{
		var world = "+V519\n+S2\n+N256\n"
			+ "!0\nLimbo\n-1\n-1\n-1\n-3\n-1\n\n1\n0\n0\n<\n"
			+ "!1\nWizard\n0\n-1\n-1\n0\n-1\n\n1\n0\n3\n<\n"
			+ RhostFlatfileReader.EndOfDump + "\n";
		await Assert.That(RhostFlatfileReader.ReadStrict(world).Objects.Count).IsEqualTo(2);
	}

	[Test]
	public async Task ReadsExistingPortableSamplesAndPreservesLossNotes()
	{
		var flat = await RhostFlatfileReader.ReadFileStrictAsync(RhostFlatfileReaderTests.FlatfilePath);
		// This handmade sample's multiline body is 42 raw bytes, but its header says 25.
		await Assert.ThrowsAsync<FormatException>(() => RhostMailReader.ReadFilesStrictAsync(
			RhostFlatfileReaderTests.MailPath, RhostFlatfileReaderTests.FolderPath));
		var text = (await File.ReadAllTextAsync(RhostFlatfileReaderTests.MailPath)).Replace("/1/25/MnF", "/1/42/MnF");
		var mail = RhostMailReader.ReadStrict(text, await File.ReadAllTextAsync(RhostFlatfileReaderTests.FolderPath));
		await Assert.That(flat.Objects.Count).IsEqualTo(12);
		await Assert.That(mail.Mail.Messages.Count).IsEqualTo(3);
		await Assert.That(mail.Notes.Any(n => n.Contains("anonymously"))).IsTrue();
	}

	[Test]
	public async Task PermissiveReadersRetainTheirExistingBehavior()
	{
		await Assert.That(RhostFlatfileReader.Read(Empty + "trailing").Objects.Count).IsEqualTo(0);
		await Assert.That(RhostMailReader.Read("C1/1\x01\ninvalid\x01\n", null).Notes.Count).IsGreaterThan(0);
	}

	[Test]
	[Arguments("B1/Inbox\x01\n1/not-a-number\x01\n")]
	[Arguments("B1/Inbox\x01\n1/1")]
	[Arguments("B1/Inbox\x01\n2/1\x01\n")]
	[Arguments("B1/Inbox\x01\n1/2\x01\n")]
	[Arguments("B1/Inbox\x01\n1/1\x01\nB1/Archive\x01\n1/1\x01\n")]
	public void RejectsMalformedFolders(string folders)
		=> Assert.Throws<FormatException>(() => RhostMailReader.ReadStrict("C1/1\x01\n1/0/1/4/NNNsubject\x01\nE1/1\x01\nbody\x01\n", folders));

	[Test]
	public async Task StrictFileReadersRejectInvalidUtf8WhilePermissiveReadersRemainCompatible()
	{
		var directory = Path.Combine(AppContext.BaseDirectory, "work", $"rhost-strict-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		try
		{
			var path = Path.Combine(directory, "invalid.flat");
			await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(Empty).Concat(new byte[] { 0xff }).ToArray());
			await Assert.ThrowsAsync<DecoderFallbackException>(() => RhostFlatfileReader.ReadFileStrictAsync(path));
			await Assert.That((await RhostFlatfileReader.ReadFileAsync(path)).Objects.Count).IsEqualTo(0);
			await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("E1/1\x01\n").Concat(new byte[] { 0xff, 1, 10 }).ToArray());
			await Assert.ThrowsAsync<DecoderFallbackException>(() => RhostMailReader.ReadFilesStrictAsync(path, null));
			await Assert.That((await RhostMailReader.ReadFilesAsync(path, null)).Mail.Messages.Count).IsEqualTo(0);
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	[Test]
	[Arguments("seed.flat")]
	[Arguments("legacy/export.flat")]
	[Arguments("mdbx/export.flat")]
	public async Task ReadsActualUpstreamExports(string file)
	{
		var path = Path.Combine(AppContext.BaseDirectory, "Services", "TestData", "RhostNative", file);
		var flat = await RhostFlatfileReader.ReadFileStrictAsync(path);
		await Assert.That(flat.Objects.Count).IsEqualTo(12);
		await Assert.That(flat.Objects.Single(o => o.DBRef == 1).Name).IsEqualTo("Wizard");
	}

	[Test]
	public async Task StrictMailResolvesSignedBccRoutingKeyWithoutChangingPermissiveReader()
	{
		const string mail = "C1/1\x01\n1/0/-32767/4/NNNsubject\x01\nE1/1\x01\nbody\x01\n";
		await Assert.That(RhostMailReader.ReadStrict(mail, null).Mail.Messages.Single().Body).IsEqualTo("body");
		await Assert.That(RhostMailReader.Read(mail, null).Mail.Messages.Single().Body).IsEqualTo("");
	}

	[Test]
	public async Task StrictMailVerifiesSignedShortBodySizeFromUpstream()
	{
		var body = new string('x', 32768);
		var mail = RhostMailReader.ReadStrict($"C1/1\x01\n1/0/1/-32768/NNNsubject\x01\nE1/1\x01\n{body}\x01\n", null);
		await Assert.That(mail.Mail.Messages.Single().Body.Length).IsEqualTo(32768);
	}

	[Test]
	[Arguments("01", "1")]
	[Arguments("Team", "team")]
	public async Task StrictMailPreservesDistinctLiteralAliasNames(string first, string second)
	{
		var mail = RhostMailReader.ReadStrict($"K{first}\x01\n1/1\x01\nK{second}\x01\n1/1\x01\nL{first}\x01\nlock\x01\nL{second}\x01\nlock\x01\n", null);
		await Assert.That(mail.Mail.Messages.Count).IsEqualTo(0);
		await Assert.That(mail.Notes.Any(n => n.Contains("aliases"))).IsTrue();
	}

	[Test]
	[Arguments("400", "body")]
	[Arguments("3", "éé")]
	[Arguments("3", "a\r\nb")]
	public void StrictMailRejectsBodyByteLengthMismatch(string size, string body)
		=> Assert.Throws<FormatException>(() => RhostMailReader.ReadStrict($"C1/1\x01\n1/0/1/{size}/NNNsubject\x01\nE1/1\x01\n{body}\x01\n", null));

	[Test]
	[Arguments("4", "éé", "éé")]
	[Arguments("4", "a\r\nb", "a\nb")]
	[Arguments("3", "a\nb", "a\nb")]
	public async Task StrictMailVerifiesUtf8BytesBeforeNewlineNormalization(string size, string body, string imported)
	{
		var mail = RhostMailReader.ReadStrict($"C1/1\x01\n1/0/1/{size}/NNNsubject\x01\nE1/1\x01\n{body}\x01\n", null);
		await Assert.That(mail.Mail.Messages.Single().Body).IsEqualTo(imported);
	}

	private static string Object(int id, string name, int flags, int owner, string attributes = "", int location = -1, int home = -1)
		=> $"!{id}\n{name}\n{location}\n-1\n{home}\n-1\n\n{owner}\n0\n{flags}\n{attributes}<\n";

}
