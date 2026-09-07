using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The engine's configuration lives in the database, so <see cref="OptionsService"/> replaces the
/// framework's <c>IOptionsFactory</c> outright. Replacing it also replaced the validation the
/// framework factory runs, which is why these exist: a stored configuration that cannot be used has
/// to stop the server rather than surface as an exception inside a page render.
/// </summary>
public class OptionsValidationTests
{
	private sealed class StubValidator(ValidateOptionsResult result) : IValidateOptions<SharpMUSHOptions>
	{
		public int Calls { get; private set; }

		public ValidateOptionsResult Validate(string? name, SharpMUSHOptions options)
		{
			Calls++;
			return result;
		}
	}

	private static IExpandedDataStore StoreWithNoSavedOptions()
	{
		var store = Substitute.For<IExpandedDataStore>();
		store.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<SharpMUSHOptions?>((SharpMUSHOptions?)null));
		return store;
	}

	[Test]
	public async Task AStoredConfigurationThatFailsValidationIsRefused()
	{
		var validator = new StubValidator(ValidateOptionsResult.Fail("wiki_default_locale is not a locale"));
		var service = new OptionsService(StoreWithNoSavedOptions(), [validator]);

		var thrown = Assert.Throws<OptionsValidationException>(() => service.Create(Options.DefaultName));

		await Assert.That(validator.Calls).IsEqualTo(1);
		await Assert.That(thrown!.Failures).Contains("wiki_default_locale is not a locale");
	}

	[Test]
	public async Task EveryValidatorRunsAndTheirFailuresAreReportedTogether()
	{
		var first = new StubValidator(ValidateOptionsResult.Fail("first"));
		var second = new StubValidator(ValidateOptionsResult.Fail("second"));
		var service = new OptionsService(StoreWithNoSavedOptions(), [first, second]);

		var thrown = Assert.Throws<OptionsValidationException>(() => service.Create(Options.DefaultName));

		await Assert.That(second.Calls).IsEqualTo(1)
			.Because("the first failure must not short-circuit the rest, or one edit at a time is all you learn");
		await Assert.That(thrown!.Failures).IsEquivalentTo(new[] { "first", "second" });
	}

	[Test]
	public async Task AValidConfigurationIsReturned()
	{
		var validator = new StubValidator(ValidateOptionsResult.Success);
		var service = new OptionsService(StoreWithNoSavedOptions(), [validator]);

		var options = service.Create(Options.DefaultName);

		await Assert.That(options).IsNotNull();
		await Assert.That(validator.Calls).IsEqualTo(1);
	}
}
