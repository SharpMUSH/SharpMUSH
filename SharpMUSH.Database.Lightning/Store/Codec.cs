using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SharpMUSH.Database.Lightning.Records;

namespace SharpMUSH.Database.Lightning.Store;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false)]
[JsonSerializable(typeof(ObjectRecord))]
[JsonSerializable(typeof(LockRecord))]
[JsonSerializable(typeof(AttrMetaRecord))]
[JsonSerializable(typeof(FlagRecord))]
[JsonSerializable(typeof(PowerRecord))]
[JsonSerializable(typeof(AttributeFlagRecord))]
[JsonSerializable(typeof(AttributeEntryRecord))]
[JsonSerializable(typeof(ChannelRecord))]
[JsonSerializable(typeof(ChannelMemberRecord))]
[JsonSerializable(typeof(MailRecord))]
[JsonSerializable(typeof(AccountRecord))]
[JsonSerializable(typeof(SessionRecord))]
[JsonSerializable(typeof(ServerStateRecord))]
[JsonSerializable(typeof(WikiPageRecord))]
[JsonSerializable(typeof(WikiRevisionRecord))]
[JsonSerializable(typeof(WikiTranslationRecord))]
[JsonSerializable(typeof(ApplicationRecord))]
[JsonSerializable(typeof(RoleRecord))]
[JsonSerializable(typeof(InstalledPackageRecord))]
[JsonSerializable(typeof(PackageObjectRecord))]
[JsonSerializable(typeof(ManagedAttributeRecord))]
[JsonSerializable(typeof(ManagedStructureRecord))]
[JsonSerializable(typeof(PackageDependencyRecord))]
[JsonSerializable(typeof(PackageRemoteRecord))]
[JsonSerializable(typeof(PackageRevisionRecord))]
[JsonSerializable(typeof(MigrationRecord))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal partial class LightningJsonContext : JsonSerializerContext;

public static class Codec
{
	public static byte[] Serialize<T>(T value)
		=> JsonSerializer.SerializeToUtf8Bytes(value, (JsonTypeInfo<T>)LightningJsonContext.Default.GetTypeInfo(typeof(T))!);

	public static T Deserialize<T>(ReadOnlySpan<byte> bytes)
		=> JsonSerializer.Deserialize(bytes, (JsonTypeInfo<T>)LightningJsonContext.Default.GetTypeInfo(typeof(T))!)!;
}
