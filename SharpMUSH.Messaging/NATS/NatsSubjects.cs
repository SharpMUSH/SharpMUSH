using System.Collections.Concurrent;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// The subject naming the publishing and consuming sides share: a message type's name without its
/// <c>Message</c> suffix, kebab-cased, under the stream's subject prefix.
/// </summary>
internal static class NatsSubjects
{
	private const string MessageSuffix = "Message";
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
			var name = type.Name.EndsWith(MessageSuffix, StringComparison.Ordinal)
				? type.Name[..^MessageSuffix.Length]
				: type.Name;

			var dashed = string.Concat(name.Select((character, index) =>
				index > 0 && char.IsUpper(character) ? $"-{character}" : character.ToString()));

			return dashed.ToLowerInvariant();
		});
}
