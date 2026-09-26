using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MultiWiz.Core.Storage;

/// <summary>
/// The values a JSON document gets for the properties it lacks, taken from a default instance of the model.
/// </summary>
/// <remarks>
/// Source-generated System.Text.Json metadata creates records with init-only properties through an object initializer
/// that assigns every property, so a property missing from a file would become <c>default(T)</c> instead of keeping its
/// C# initializer: a settings file written before a setting existed, or edited by hand, would read <c>AutoLogin</c> as
/// false and <c>WindowTimeoutSeconds</c> as 0. <see cref="JsonFileStore.Load"/> therefore writes the missing properties
/// into the document before deserializing it. Instances are meant for a single load and are not thread-safe.
/// </remarks>
public sealed class JsonDefaults
{
    private static readonly HashSet<string> NoRequiredNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, JsonDefaults> NoArrayElements = new(StringComparer.OrdinalIgnoreCase);

    private readonly JsonElement _values;
    private readonly HashSet<string> _required;
    private readonly Dictionary<string, JsonDefaults> _arrayElements;

    private JsonDefaults(JsonElement values, HashSet<string> required, Dictionary<string, JsonDefaults> arrayElements)
    {
        _values = values;
        _required = required;
        _arrayElements = arrayElements;
    }

    /// <summary>
    /// Defaults read from <paramref name="instance"/>, normally a freshly constructed object. Nested objects are filled
    /// in recursively. Properties the type marks as <c>required</c> are never filled in, so a document without them is
    /// still rejected.
    /// </summary>
    public static JsonDefaults From<T>(T instance, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in typeInfo.Properties)
        {
            if (property.IsRequired)
            {
                required.Add(property.Name);
            }
        }

        return new JsonDefaults(
            JsonSerializer.SerializeToElement(instance, typeInfo),
            required,
            new Dictionary<string, JsonDefaults>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns a copy that also fills in every object in the array property <paramref name="propertyName"/> (matched
    /// ignoring case, as the serializer matches names) with <paramref name="elementDefaults"/>.
    /// </summary>
    public JsonDefaults WithArrayElements(string propertyName, JsonDefaults elementDefaults)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        ArgumentNullException.ThrowIfNull(elementDefaults);

        var arrayElements = new Dictionary<string, JsonDefaults>(_arrayElements, StringComparer.OrdinalIgnoreCase)
        {
            [propertyName] = elementDefaults,
        };
        return new JsonDefaults(_values, _required, arrayElements);
    }

    /// <summary>Deserializes <paramref name="document"/> after adding the default properties it lacks.</summary>
    internal T? Deserialize<T>(JsonElement document, JsonTypeInfo<T> typeInfo)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            if (document.ValueKind == JsonValueKind.Object)
            {
                WriteObject(writer, document, _values, _required, _arrayElements);
            }
            else
            {
                document.WriteTo(writer);
            }
        }

        return JsonSerializer.Deserialize(buffer.WrittenSpan, typeInfo);
    }

    // Writes the object's own properties as they are (duplicates included, so the serializer resolves them exactly as it
    // would for the original file), merging nested objects and array elements that have defaults, then appends the
    // default properties the object lacks.
    private static void WriteObject(
        Utf8JsonWriter writer,
        JsonElement source,
        JsonElement defaults,
        HashSet<string> required,
        Dictionary<string, JsonDefaults> arrayElements)
    {
        writer.WriteStartObject();
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in source.EnumerateObject())
        {
            present.Add(property.Name);
            writer.WritePropertyName(property.Name);
            var value = property.Value;
            if (value.ValueKind == JsonValueKind.Array && arrayElements.TryGetValue(property.Name, out var elementDefaults))
            {
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        WriteObject(writer, item, elementDefaults._values, elementDefaults._required, elementDefaults._arrayElements);
                    }
                    else
                    {
                        item.WriteTo(writer);
                    }
                }

                writer.WriteEndArray();
            }
            else if (value.ValueKind == JsonValueKind.Object
                && TryGetProperty(defaults, property.Name, out var nestedDefaults)
                && nestedDefaults.ValueKind == JsonValueKind.Object)
            {
                WriteObject(writer, value, nestedDefaults, NoRequiredNames, NoArrayElements);
            }
            else
            {
                value.WriteTo(writer);
            }
        }

        if (defaults.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in defaults.EnumerateObject())
            {
                if (!present.Contains(property.Name) && !required.Contains(property.Name))
                {
                    property.WriteTo(writer);
                }
            }
        }

        writer.WriteEndObject();
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
