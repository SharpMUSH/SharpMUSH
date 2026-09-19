using Mediator;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <c>@attribute/limit</c> through <c>valid(attrvalue, …)</c>'s service. PennMUSH compiles the limit with
/// <c>PCRE2_CASELESS</c> (<c>src/atr_tab.c:540</c>) and reads it from the attribute table on every check.
/// </summary>
public class AttributeLimitValidationTests
{
	private static readonly SharpMUSHOptions BaseConfig =
		ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");

	private static ValidateService Service()
	{
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(BaseConfig);
		return new ValidateService(Substitute.For<IMediator>(), options, Substitute.For<ILockService>());
	}

	private static SharpAttributeEntry Limited(string limit) =>
		new() { Name = "LIMITED", DefaultFlags = [], Limit = limit };

	private static async Task<bool> Valid(ValidateService service, string value, SharpAttributeEntry entry) =>
		await service.Valid(IValidateService.ValidationType.AttributeValue, MarkupText.Plain(value), entry);

	[Test]
	[Arguments("abc", true)]
	[Arguments("ABC", true)]
	[Arguments("abc1", false)]
	public async Task LimitIsCaseless(string value, bool expected)
		=> await Assert.That(await Valid(Service(), value, Limited("^[a-z]+$"))).IsEqualTo(expected);

	[Test]
	public async Task AChangedLimitTakesEffect()
	{
		var service = Service();
		await Valid(service, "abc", Limited("^[a-z]+$"));

		await Assert.That(await Valid(service, "abc", Limited("^[0-9]+$"))).IsFalse();
		await Assert.That(await Valid(service, "123", Limited("^[0-9]+$"))).IsTrue();
	}

	[Test]
	public async Task ALimitThatDoesNotCompileLimitsNothing()
		=> await Assert.That(await Valid(Service(), "anything", Limited("(unclosed"))).IsTrue();
}
