using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>An upload's temporary database, maildb and chatdb go however the conversion ends.</summary>
public class DatabaseConversionSessionTests
{
	private static (string Database, string Mail, string Chat) TempFiles()
	{
		var database = Path.Join(Path.GetTempPath(), $"pennmush_test_{Guid.NewGuid()}.db");
		var mail = Path.Join(Path.GetTempPath(), $"pennmush_test_{Guid.NewGuid()}.maildb");
		var chat = Path.Join(Path.GetTempPath(), $"pennmush_test_{Guid.NewGuid()}.chatdb");
		File.WriteAllText(database, "+V");
		File.WriteAllText(mail, "+15");
		File.WriteAllText(chat, "+V1");
		return (database, mail, chat);
	}

	private static async Task WaitForDeletionAsync(params string[] paths)
	{
		for (var i = 0; i < 100 && paths.Any(File.Exists); i++)
		{
			await Task.Delay(50);
		}
	}

	[Test]
	public async Task A_failed_conversion_deletes_every_upload()
	{
		var (database, mail, chat) = TempFiles();
		var converter = Substitute.For<IPennMUSHDatabaseConverter>();
		converter.ConvertDatabaseAsync(database, mail, chat, Arg.Any<IProgress<ConversionProgress>>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException<ConversionResult>(new FormatException("bad maildb")));

		DatabaseConversionSession.StartConversion(Guid.NewGuid().ToString(), converter, database, mail, chat,
			NullLogger.Instance, CancellationToken.None);
		await WaitForDeletionAsync(database, mail, chat);

		await Assert.That(File.Exists(database)).IsFalse();
		await Assert.That(File.Exists(mail)).IsFalse();
		await Assert.That(File.Exists(chat)).IsFalse();
	}

	[Test]
	public async Task A_cancelled_conversion_deletes_every_upload()
	{
		var (database, mail, chat) = TempFiles();
		var converter = Substitute.For<IPennMUSHDatabaseConverter>();
		converter.ConvertDatabaseAsync(database, mail, chat, Arg.Any<IProgress<ConversionProgress>>(), Arg.Any<CancellationToken>())
			.Returns(call => Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>())
				.ContinueWith<ConversionResult>(_ => throw new OperationCanceledException(), TaskScheduler.Default));

		var sessionId = Guid.NewGuid().ToString();
		DatabaseConversionSession.StartConversion(sessionId, converter, database, mail, chat, NullLogger.Instance,
			CancellationToken.None);
		await Assert.That(File.Exists(mail)).IsTrue();

		await Assert.That(DatabaseConversionSession.CancelConversion(sessionId)).IsTrue();
		await WaitForDeletionAsync(database, mail, chat);

		await Assert.That(File.Exists(database)).IsFalse();
		await Assert.That(File.Exists(mail)).IsFalse();
		await Assert.That(File.Exists(chat)).IsFalse();
	}

	/// <summary>The request limit is the whole form, so it has to hold a database, a maildb and a chatdb all at the file limit.</summary>
	[Test]
	public async Task The_upload_limit_fits_every_file_at_its_limit()
	{
		var upload = typeof(DatabaseConversionController).GetMethod(nameof(DatabaseConversionController.UploadDatabase))!;
		IRequestSizeLimitMetadata request = upload.GetCustomAttribute<RequestSizeLimitAttribute>()!;
		var form = upload.GetCustomAttribute<RequestFormLimitsAttribute>()!;
		var limit = request.MaxRequestBodySize!.Value;

		await Assert.That(form.MultipartBodyLengthLimit).IsEqualTo(100L * 1024 * 1024);
		await Assert.That(limit).IsGreaterThan(3 * form.MultipartBodyLengthLimit);
	}
}
