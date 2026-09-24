using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>An upload's temporary database and maildb go however the conversion ends.</summary>
public class DatabaseConversionSessionTests
{
	private static (string Database, string Mail) TempFiles()
	{
		var database = Path.Combine(Path.GetTempPath(), $"pennmush_test_{Guid.NewGuid()}.db");
		var mail = Path.Combine(Path.GetTempPath(), $"pennmush_test_{Guid.NewGuid()}.maildb");
		File.WriteAllText(database, "+V");
		File.WriteAllText(mail, "+15");
		return (database, mail);
	}

	private static async Task WaitForDeletionAsync(params string[] paths)
	{
		for (var i = 0; i < 100 && paths.Any(File.Exists); i++)
		{
			await Task.Delay(50);
		}
	}

	[Test]
	public async Task A_failed_conversion_deletes_both_uploads()
	{
		var (database, mail) = TempFiles();
		var converter = Substitute.For<IPennMUSHDatabaseConverter>();
		converter.ConvertDatabaseAsync(database, mail, Arg.Any<IProgress<ConversionProgress>>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException<ConversionResult>(new FormatException("bad maildb")));

		DatabaseConversionSession.StartConversion(Guid.NewGuid().ToString(), converter, database, mail,
			NullLogger.Instance, CancellationToken.None);
		await WaitForDeletionAsync(database, mail);

		await Assert.That(File.Exists(database)).IsFalse();
		await Assert.That(File.Exists(mail)).IsFalse();
	}

	[Test]
	public async Task A_cancelled_conversion_deletes_both_uploads()
	{
		var (database, mail) = TempFiles();
		var converter = Substitute.For<IPennMUSHDatabaseConverter>();
		converter.ConvertDatabaseAsync(database, mail, Arg.Any<IProgress<ConversionProgress>>(), Arg.Any<CancellationToken>())
			.Returns(call => Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>())
				.ContinueWith<ConversionResult>(_ => throw new OperationCanceledException(), TaskScheduler.Default));

		var sessionId = Guid.NewGuid().ToString();
		DatabaseConversionSession.StartConversion(sessionId, converter, database, mail, NullLogger.Instance,
			CancellationToken.None);
		await Assert.That(File.Exists(mail)).IsTrue();

		await Assert.That(DatabaseConversionSession.CancelConversion(sessionId)).IsTrue();
		await WaitForDeletionAsync(database, mail);

		await Assert.That(File.Exists(database)).IsFalse();
		await Assert.That(File.Exists(mail)).IsFalse();
	}
}
