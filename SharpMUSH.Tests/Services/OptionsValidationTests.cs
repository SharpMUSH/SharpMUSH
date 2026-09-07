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

	private static IExpandedDataStore StoreWith(SharpMUSHOptions stored)
	{
		var store = Substitute.For<IExpandedDataStore>();
		store.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<SharpMUSHOptions?>(stored));
		return store;
	}

	/// <summary>The document a running game has: whatever was stored last, valid or not.</summary>
	private static SharpMUSHOptions SomeStoredConfiguration()
		=> new OptionsService(StoreWithNoSavedOptions(), []).Create(Options.DefaultName);

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

	/// <summary>
	/// A rejected default must not reach the database. It would be reloaded on the next start, fail the
	/// same validation, and there would be no way to correct it without repairing the document by hand:
	/// the code path that writes a fresh default only runs when there is nothing stored.
	/// </summary>
	[Test]
	public async Task DefaultsAreNotStoredWhenValidationRejectsThem()
	{
		var store = StoreWithNoSavedOptions();
		var service = new OptionsService(store, [new StubValidator(ValidateOptionsResult.Fail("no"))]);

		Assert.Throws<OptionsValidationException>(() => service.Create(Options.DefaultName));

		await store.DidNotReceive().SetExpandedServerData(Arg.Any<string>(), Arg.Any<object>(),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ValidDefaultsAreStored()
	{
		var store = StoreWithNoSavedOptions();
		var service = new OptionsService(store, [new StubValidator(ValidateOptionsResult.Success)]);

		service.Create(Options.DefaultName);

		await store.Received(1).SetExpandedServerData(nameof(SharpMUSHOptions), Arg.Any<object>(),
			Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// The branch a running game actually takes. Every other test here goes through the
	/// nothing-is-stored path, which is only ever hit once in a database's life.
	/// </summary>
	[Test]
	public async Task AStoredConfigurationIsValidatedToo()
	{
		var validator = new StubValidator(ValidateOptionsResult.Fail("wiki_default_locale is not a locale"));
		var store = StoreWith(SomeStoredConfiguration());
		var service = new OptionsService(store, [validator]);

		var thrown = Assert.Throws<OptionsValidationException>(() => service.Create(Options.DefaultName));

		await Assert.That(validator.Calls).IsEqualTo(1);
		await Assert.That(thrown!.Failures).Contains("wiki_default_locale is not a locale");
		await store.DidNotReceive().SetExpandedServerData(Arg.Any<string>(), Arg.Any<object>(),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task AValidStoredConfigurationIsReturnedUnchanged()
	{
		var stored = SomeStoredConfiguration();
		var service = new OptionsService(StoreWith(stored), [new StubValidator(ValidateOptionsResult.Success)]);

		await Assert.That(service.Create(Options.DefaultName)).IsEqualTo(stored);
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
