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

	/// <summary>The mush.cnf that came with the upload is undone when the conversion it was applied for fails.</summary>
	[Test]
	public async Task A_failed_conversion_puts_the_configuration_back()
	{
		var (database, mail, chat) = TempFiles();
		var converter = Substitute.For<IPennMUSHDatabaseConverter>();
		converter.ConvertDatabaseAsync(database, mail, chat, Arg.Any<IProgress<ConversionProgress>>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException<ConversionResult>(new FormatException("bad maildb")));
		var restored = new TaskCompletionSource();

		DatabaseConversionSession.StartConversion(Guid.NewGuid().ToString(), converter, database, mail, chat,
			NullLogger.Instance, CancellationToken.None, () =>
			{
				restored.TrySetResult();
				return Task.CompletedTask;
			});

		await restored.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(restored.Task.IsCompletedSuccessfully).IsTrue();
	}

	/// <summary>
	/// The converter answers a cancellation or a fatal error with an aborted result rather than a faulted task;
	/// that conversion did not finish either, so the configuration goes back.
	/// </summary>
	[Test]
	public async Task An_aborted_conversion_puts_the_configuration_back()
	{
		var (database, mail, chat) = TempFiles();
		var converter = Substitute.For<IPennMUSHDatabaseConverter>();
		converter.ConvertDatabaseAsync(database, mail, chat, Arg.Any<IProgress<ConversionProgress>>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new ConversionResult { Errors = ["Fatal error: cancelled"], Aborted = true }));
		var restored = new TaskCompletionSource();

		DatabaseConversionSession.StartConversion(Guid.NewGuid().ToString(), converter, database, mail, chat,
			NullLogger.Instance, CancellationToken.None, () =>
			{
				restored.TrySetResult();
				return Task.CompletedTask;
			});

		await restored.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(restored.Task.IsCompletedSuccessfully).IsTrue();
	}

	/// <summary>A conversion that reached the end keeps the configuration, even with errors along the way.</summary>
	[Test]
	public async Task A_conversion_with_errors_that_finished_keeps_the_configuration()
	{
		var (database, mail, chat) = TempFiles();
		var converter = Substitute.For<IPennMUSHDatabaseConverter>();
		converter.ConvertDatabaseAsync(database, mail, chat, Arg.Any<IProgress<ConversionProgress>>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new ConversionResult { Errors = ["#12: number already taken"] }));
		var restores = 0;

		var sessionId = Guid.NewGuid().ToString();
		DatabaseConversionSession.StartConversion(sessionId, converter, database, mail, chat, NullLogger.Instance,
			CancellationToken.None, () =>
			{
				Interlocked.Increment(ref restores);
				return Task.CompletedTask;
			});

		ConversionResult? result = null;
		for (var i = 0; i < 100 && result is null; i++)
		{
			await Task.Delay(50);
			result = await DatabaseConversionSession.GetResult(sessionId);
		}

		await Assert.That(result).IsNotNull();
		await Assert.That(restores).IsEqualTo(0);
	}

	/// <summary>A conversion that finished keeps the configuration that came with it.</summary>
	[Test]
	public async Task A_finished_conversion_keeps_the_configuration()
	{
		var (database, mail, chat) = TempFiles();
		var converter = Substitute.For<IPennMUSHDatabaseConverter>();
		converter.ConvertDatabaseAsync(database, mail, chat, Arg.Any<IProgress<ConversionProgress>>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new ConversionResult()));
		var restores = 0;

		var sessionId = Guid.NewGuid().ToString();
		DatabaseConversionSession.StartConversion(sessionId, converter, database, mail, chat, NullLogger.Instance,
			CancellationToken.None, () =>
			{
				Interlocked.Increment(ref restores);
				return Task.CompletedTask;
			});

		ConversionResult? result = null;
		for (var i = 0; i < 100 && result is null; i++)
		{
			await Task.Delay(50);
			result = await DatabaseConversionSession.GetResult(sessionId);
		}

		await Assert.That(result).IsNotNull();
		await Assert.That(restores).IsEqualTo(0);
	}

	/// <summary>
	/// The converter reports nothing until it has parsed the dump. The import page reads a 404 from the
	/// progress endpoint as "the session is gone", so a session in that gap must still answer.
	/// </summary>
	[Test]
	public async Task A_conversion_that_has_not_reported_yet_still_has_progress()
	{
		var (database, mail, chat) = TempFiles();
		var converter = Substitute.For<IPennMUSHDatabaseConverter>();
		converter.ConvertDatabaseAsync(database, mail, chat, Arg.Any<IProgress<ConversionProgress>>(), Arg.Any<CancellationToken>())
			.Returns(call => Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>())
				.ContinueWith<ConversionResult>(_ => throw new OperationCanceledException(), TaskScheduler.Default));

		var sessionId = Guid.NewGuid().ToString();
		DatabaseConversionSession.StartConversion(sessionId, converter, database, mail, chat, NullLogger.Instance,
			CancellationToken.None);

		try
		{
			var progress = DatabaseConversionSession.GetProgress(sessionId);

			await Assert.That(progress).IsNotNull();
			await Assert.That(progress!.PercentageComplete).IsEqualTo(0);
			await Assert.That(DatabaseConversionSession.GetProgress(Guid.NewGuid().ToString())).IsNull();
		}
		finally
		{
			DatabaseConversionSession.CancelConversion(sessionId);
			await WaitForDeletionAsync(database, mail, chat);
		}
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
