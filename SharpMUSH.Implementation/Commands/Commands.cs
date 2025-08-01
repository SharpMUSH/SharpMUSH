using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using System.Reflection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands : ILibraryProvider<CommandDefinition>
{
	private readonly CommandLibraryService _commandLibrary = [];

	public LibraryService<string, CommandDefinition> Get() => _commandLibrary;

	public Commands()
	{
		var knownBuiltInMethods =
			typeof(Commands)
				.GetMethods()
				.Select(m => (Method: m,
					Attribute: m.GetCustomAttribute<SharpCommandAttribute>(false)))
				.Where(x => x.Attribute is not null)
				.SelectMany(y =>
					(Configurable.CommandAliases.TryGetValue(y.Attribute!.Name, out var aliases)
						? aliases.Select(alias =>
							new KeyValuePair<string, (MethodInfo Method, SharpCommandAttribute Attribute)>(alias,
								(y.Method, y.Attribute!)))
						: [])
					.Append(new KeyValuePair<string, (MethodInfo Method, SharpCommandAttribute Attribute)>(y.Attribute.Name,
						(y.Method, y.Attribute!))))
				.ToDictionary();
		
		foreach (var knownMethod in knownBuiltInMethods)
		{
			_commandLibrary.Add(knownMethod.Key,
				((knownMethod.Value.Attribute,
					p => (ValueTask<Option<CallState>>)knownMethod.Value.Method.Invoke(null, [p, knownMethod.Value.Attribute])!), true));
		}
	}
}