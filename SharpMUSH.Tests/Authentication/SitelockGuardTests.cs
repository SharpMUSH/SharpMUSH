using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// Unit tests for <see cref="SitelockGuard"/>: the thin DI wrapper that reads the live
/// <see cref="IOptionsWrapper{SharpMUSHOptions}"/>.<c>CurrentValue.SitelockRules</c> on every call
/// and delegates the actual matching to <see cref="SharpMUSH.Library.Services.SitelockMatcher.IsBlocked"/>.
///
/// <see cref="SharpMUSHOptions"/> is a record with many <c>required</c> properties, so rather than
/// hand-constructing one, tests load the checked-in minimal config fixture (same pattern as
/// <c>SqlProviderSelectionTests</c>) and override only <c>SitelockRules</c> via <c>with</c>.
/// </summary>
public class SitelockGuardTests
{
	private static readonly SharpMUSHOptions BaseConfig = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");

	private static SitelockGuard Build(Dictionary<string, string[]> rules)
	{
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(BaseConfig with { SitelockRules = new SitelockRulesOptions(rules) });
		return new SitelockGuard(options);
	}

	[Test]
	public async Task IsBlocked_NoRules_ReturnsFalse()
	{
		var guard = Build(new Dictionary<string, string[]>());

		await Assert.That(guard.IsBlocked("1.2.3.4", "host", SitelockGuard.Connect)).IsFalse();
	}

	[Test]
	public async Task IsBlocked_MatchingIpAndFlag_ReturnsTrue()
	{
		var guard = Build(new Dictionary<string, string[]> { ["203.0.113.5"] = [SitelockGuard.Connect] });

		await Assert.That(guard.IsBlocked("203.0.113.5", "host", SitelockGuard.Connect)).IsTrue();
	}

	[Test]
	public async Task IsBlocked_MatchingIpWrongFlag_ReturnsFalse()
	{
		var guard = Build(new Dictionary<string, string[]> { ["203.0.113.5"] = [SitelockGuard.Create] });

		await Assert.That(guard.IsBlocked("203.0.113.5", "host", SitelockGuard.Connect)).IsFalse();
	}

	[Test]
	public async Task IsBlocked_ReadsCurrentValueLive_ReflectsSubsequentChange()
	{
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(BaseConfig with { SitelockRules = new SitelockRulesOptions(new Dictionary<string, string[]>()) });
		var guard = new SitelockGuard(options);

		await Assert.That(guard.IsBlocked("203.0.113.5", "host", SitelockGuard.Guest)).IsFalse();

		options.CurrentValue.Returns(BaseConfig with
		{
			SitelockRules = new SitelockRulesOptions(new Dictionary<string, string[]> { ["203.0.113.5"] = [SitelockGuard.Guest] })
		});

		await Assert.That(guard.IsBlocked("203.0.113.5", "host", SitelockGuard.Guest)).IsTrue();
	}
}
