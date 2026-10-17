using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel;

/// <summary>
/// Reads and writes an enum as the exact JSON name of one member. The default enum converter also takes a number, so <c>7</c> would bind
/// to no member and <c>0</c> to the first, a list of names with commas, so <c>"tr, ar"</c> would bind to no member and <c>"en, tr"</c> to
/// the second, and a name with spaces, so <c>" Admin"</c> would bind to Admin. <see cref="Language"/> and the built-in role of a member use it.
/// </summary>
public sealed class ExactNameEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        (reader.TokenType == JsonTokenType.String ? JsonEnum<TEnum>.Find(reader.GetString()!) : null)
            ?? throw new JsonException($"A {typeof(TEnum).Name} is one name of the list.");

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) => writer.WriteStringValue(JsonEnum<TEnum>.NameOf(value));
}

// The name of each member of an enum in JSON, as System.Text.Json writes it: the name the member declares, else its own name in camel
// case. A sort field is so the name of its property in the response. The names are compared exactly.
internal static class JsonEnum<T>
    where T : struct, Enum
{
    private static readonly (string Name, T Value)[] Members =
    [
        .. typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static).Select(member => (
            member.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(member.Name),
            (T)member.GetValue(null)!)),
    ];

    public static IReadOnlyList<string> Names { get; } = [.. Members.Select(member => member.Name)];

    public static T First => Members[0].Value;

    public static T? Find(string name) => Members.Where(member => member.Name == name).Select(member => (T?)member.Value).FirstOrDefault();

    public static string NameOf(T value) => Members.Single(member => member.Value.Equals(value)).Name;
}
