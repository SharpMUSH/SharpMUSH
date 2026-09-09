using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// The subject naming the publishing and consuming sides share: a message type's name without its
/// <c>Message</c> suffix, kebab-cased, under the stream's subject prefix.
/// </summary>
internal static class NatsSubjects
{
	private static readonly ConcurrentDictionary<Type, string> KebabNames = new();

	/// <summary>The full subject for <paramref name="messageType"/> under <paramref name="subjectPrefix"/>.</summary>
	public static string For(Type messageType, string subjectPrefix) =>
		$"{subjectPrefix}.{KebabName(messageType)}";

	/// <summary>
	/// The type name, minus a trailing <c>Message</c>, in kebab-case — <c>TelnetInputMessage</c> is
	/// <c>telnet-input</c>. Computed once per type.
	/// </summary>
	public static string KebabName(Type messageType) =>
		KebabNames.GetOrAdd(messageType, static type =>
		{
			var name = type.Name.AsSpan();
			if (name.EndsWith("Message", StringComparison.Ordinal))
				name = name[..^7];

			var kebab = new StringBuilder(name.Length + 4);
			for (var index = 0; index < name.Length; index++)
			{
				if (index > 0 && char.IsUpper(name[index]))
					kebab.Append('-');
				kebab.Append(char.ToLowerInvariant(name[index]));
			}

			return kebab.ToString();
		});
}
