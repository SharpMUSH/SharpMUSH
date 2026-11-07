using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;

namespace SharpMUSH.Tests.Services;

public class RhostBccMailImportTests
{
	[Test]
	[Arguments(-32767)]
	[Arguments(1)]
	public async Task StrictValidatedCompanionRetainsBodyThroughOrdinaryDatabaseImport(int routingKey)
	{
		var scratch = Environment.GetEnvironmentVariable("TMPDIR") ?? Path.Join(AppContext.BaseDirectory, "work");
		var directory = Path.Join(scratch, "rhost-bcc-import-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			var mailPath = Path.Join(directory, "portable.mail");
			var original = $"C4/1\x01\n1/1792036695/{routingKey}/12/NNNBCC subject\x01\nE1/1\x01\nprivate body\x01\n";
			await File.WriteAllTextAsync(mailPath, original, RhostFlatfileReader.FileEncoding);
			var validated = await RhostMailReader.ReadFilesStrictAsync(mailPath, null);
			await Assert.That(validated.Mail.Messages.Single().Body).IsEqualTo("private body");

			await using var world = await IsolatedImportWorld.CreateAsync();
			var flatfile = Path.Join(AppContext.BaseDirectory, "Services", "TestData", "RhostNative", "seed.flat");
			var result = await world.Converter.ConvertDatabaseAsync(new DatabaseImportFiles(flatfile, mailPath, null, null)
			{ Format = DatabaseImportFormat.RhostMUSH }, new Progress<ConversionProgress>());
			await Assert.That(result.Errors).IsEmpty();
			var key = (await world.Database.GetAllObjectsAsync().FirstAsync(o => o.Name == "Bob")).Key;
			var bob = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(key)))).Expect<SharpPlayer>();
			var mailbox = await world.Mediator.CreateStream(new GetAllMailListQuery(bob)).ToListAsync();
			await Assert.That(mailbox.Single().Content.ToPlainText()).IsEqualTo("private body");
			await Assert.That(await File.ReadAllTextAsync(mailPath)).IsEqualTo(original);
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	/// <summary>
	/// RhostMUSH marks an anonymous message with a lower-case second status letter. It arrives anonymous: the
	/// recipient's read and listing name no sender, while the row keeps it for the sender's own sent list.
	/// </summary>
	[Test]
	public async Task AnonymousMailStaysAnonymous()
	{
		var scratch = Environment.GetEnvironmentVariable("TMPDIR") ?? Path.Join(AppContext.BaseDirectory, "work");
		var directory = Path.Join(scratch, "rhost-anon-import-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			var mailPath = Path.Join(directory, "portable.mail");
			await File.WriteAllTextAsync(mailPath, "C4/1\x01\n1/1792036695/1/12/NnNsecret admirer\x01\nE1/1\x01\nguess who\x01\n",
				RhostFlatfileReader.FileEncoding);

			await using var world = await IsolatedImportWorld.CreateAsync();
			var flatfile = Path.Join(AppContext.BaseDirectory, "Services", "TestData", "RhostNative", "seed.flat");
			var result = await world.Converter.ConvertDatabaseAsync(new DatabaseImportFiles(flatfile, mailPath, null, null)
			{ Format = DatabaseImportFormat.RhostMUSH }, new Progress<ConversionProgress>());
			await Assert.That(result.Errors).IsEmpty();
			var key = (await world.Database.GetAllObjectsAsync().FirstAsync(o => o.Name == "Bob")).Key;
			var bob = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(key)))).Expect<SharpPlayer>();

			var mail = (await world.Mediator.CreateStream(new GetAllMailListQuery(bob)).ToListAsync()).Single();
			await Assert.That(mail.Anonymous).IsTrue();
			await Assert.That(await mail.From.WithCancellation(CancellationToken.None) is None).IsTrue();

			var summary = (await world.Mediator.CreateStream(new GetMailSummaryListQuery(bob, mail.Folder)).ToListAsync()).Single();
			await Assert.That(summary.Anonymous).IsTrue();
			await Assert.That(summary.SenderName).IsNull();
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}
}
