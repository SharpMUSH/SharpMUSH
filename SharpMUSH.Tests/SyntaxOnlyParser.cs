using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using BuiltInFunctionProvider = SharpMUSH.Implementation.Functions.Functions;

namespace SharpMUSH.Tests;

/// <summary>
/// A real <see cref="MUSHCodeParser"/> over the real built-in function library and the default
/// options, with no host behind it. It answers the tooling half of the parser (<c>Tokenize</c>,
/// <c>ValidateAndGetErrors</c>, <c>GetDiagnostics</c>, <c>GetSemanticTokens</c>) and the
/// <c>FunctionLibrary</c> the formatter classifies against, none of which reads the world.
/// </summary>
/// <remarks>
/// Evaluation (<c>FunctionParse</c>, <c>CommandParse</c>) needs the executor from the database: a
/// test that evaluates uses <see cref="ServerWebAppFactory"/>. The services the parser's constructor
/// resolves are substitutes, so a call that reaches one returns nothing rather than touching state.
/// The function library comes from the engine's own <c>Functions</c> provider, given substitutes
/// for the services it only stores; it adds the configured aliases the way the host does.
/// </remarks>
public static class SyntaxOnlyParser
{
	public static IMUSHCodeParser Instance { get; } = Create();

	private static IMUSHCodeParser Create()
	{
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(SharpMUSHOptions.Default());

		var services = new ServiceCollection()
			.AddSingleton(Substitute.For<IMediator>())
			.AddSingleton(Substitute.For<INotifyService>())
			.AddSingleton(Substitute.For<IConnectionService>())
			.AddSingleton(Substitute.For<ILocateService>())
			.AddSingleton(Substitute.For<ICommandDiscoveryService>())
			.AddSingleton(Substitute.For<IAttributeService>())
			.AddSingleton(Substitute.For<IHookService>())
			.AddSingleton(Substitute.For<ILockService>())
			.BuildServiceProvider();

		return new MUSHCodeParser(NullLogger<MUSHCodeParser>.Instance, BuiltInFunctions(options),
			new LibraryService<string, CommandDefinition>(), options, services);
	}

	private static LibraryService<string, FunctionDefinition> BuiltInFunctions(IOptionsWrapper<SharpMUSHOptions> options)
	{
		var constructor = typeof(BuiltInFunctionProvider).GetConstructors().Single();
		var arguments = constructor.GetParameters()
			.Select(parameter => parameter.ParameterType == typeof(IOptionsWrapper<SharpMUSHOptions>)
				? options
				: Substitute.For([parameter.ParameterType], []))
			.ToArray();

		return ((BuiltInFunctionProvider)constructor.Invoke(arguments)).Get();
	}
}
