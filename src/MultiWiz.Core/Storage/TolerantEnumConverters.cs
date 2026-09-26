using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiWiz.Core.Storage;

/// <summary>
/// Reads an enum written as its name (or number) without failing on a name this build does not know. A newer build
/// may have added the value, and the standard converter would throw, which makes the whole file unreadable (it is then
/// set aside and every other setting is lost), for example after switching from a beta back to the stable channel.
/// An unknown name reads as <see cref="UnknownValue"/>, which the stores replace with a default or drop. Defined values
/// are written as their names, exactly like the standard string enum converter.
/// </summary>
public sealed class TolerantEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    /// <summary>The value an unknown name reads as. No MultiWiz enum defines -1.</summary>
    public static TEnum UnknownValue => (TEnum)Enum.ToObject(typeof(TEnum), -1);

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return TryParseName(reader.GetString(), out var value) ? value : UnknownValue;
            case JsonTokenType.Number when reader.TryGetInt32(out var number):
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            default:
                throw new JsonException($"Expected a {typeof(TEnum).Name} name.");
        }
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (Enum.IsDefined(value))
        {
            writer.WriteStringValue(value.ToString());
        }
        else
        {
            writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Parses a defined value by name (ignoring case, as the standard converter does).</summary>
    internal static bool TryParseName(string? name, out TEnum value) =>
        Enum.TryParse(name, ignoreCase: true, out value) && Enum.IsDefined(value);
}

/// <summary>
/// Reads a map keyed by enum names, skipping keys this build does not know (a newer build may have added them, see
/// <see cref="TolerantEnumConverter{TEnum}"/>) and entries whose value is not a string, instead of failing the whole
/// file. Writes the keys as their names.
/// </summary>
public sealed class TolerantEnumKeyDictionaryConverter<TEnum> : JsonConverter<IReadOnlyDictionary<TEnum, string>>
    where TEnum : struct, Enum
{
    public override IReadOnlyDictionary<TEnum, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected an object keyed by {typeof(TEnum).Name} names.");
        }

        var result = new Dictionary<TEnum, string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return result;
            }

            var name = reader.GetString();
            if (!reader.Read())
            {
                break;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (value is not null && TolerantEnumConverter<TEnum>.TryParseName(name, out var key))
                {
                    result[key] = value;
                }
            }
            else
            {
                // Converters always get the complete value, so skipping cannot run out of data.
                reader.Skip();
            }
        }

        throw new JsonException("The JSON object ended unexpectedly.");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<TEnum, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, text) in value)
        {
            writer.WriteString(key.ToString(), text);
        }

        writer.WriteEndObject();
    }
}
