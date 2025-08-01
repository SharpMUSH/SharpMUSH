using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using System.Reflection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Functions;

// TODO: FunctionProvider Interface.
public partial class Functions : ILibraryProvider<FunctionDefinition>
{
	private readonly FunctionLibraryService _functionLibrary = [];
	
	public LibraryService<string, FunctionDefinition> Get() => _functionLibrary;

	public Functions()
	{
		var knownBuiltInMethods = typeof(Functions)
			.GetMethods()
			.Select(m => (Method: m,
				Attribute: m.GetCustomAttribute<SharpFunctionAttribute>(false)))
			.Where(x => x.Attribute is not null)
			.SelectMany(y =>
				(Configurable.FunctionAliases.TryGetValue(y.Attribute!.Name, out var aliases)
					? aliases.Select(alias =>
						new KeyValuePair<string, (MethodInfo Method, SharpFunctionAttribute Attribute)>(alias,
							(y.Method, y.Attribute!)))
					: [])
				.Append(new KeyValuePair<string, (MethodInfo Method, SharpFunctionAttribute Attribute)>(y.Attribute.Name,
					(y.Method, y.Attribute!))))
			.ToDictionary();
		
		foreach (var knownMethod in knownBuiltInMethods)
		{
			_functionLibrary.Add(knownMethod.Key,
				((knownMethod.Value.Attribute,
					p => (ValueTask<CallState>)knownMethod.Value.Method.Invoke(null, [p, knownMethod.Value.Attribute])!), true));
		}
	}
}