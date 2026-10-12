using System.Buffers;
using System.Collections;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pmad.AI.LlamaSharp;

/// <summary>
/// High-performance, zero-reflection JSON formatting utilities designed for Native AOT compatibility.
/// </summary>
internal static class AotJsonUtilities
{
    private static readonly JsonWriterOptions _compactOptions = new() { Indented = false };

    /// <summary>
    /// Writes a tool function definition object directly to the provided <see cref="Utf8JsonWriter"/>.
    /// Produces {"type":"function","function":{"name":"...","description":"...","parameters":...}}
    /// </summary>
    public static void WriteFunctionDefinition(Utf8JsonWriter writer, AIFunction fn)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "function");
        writer.WriteStartObject("function");
        writer.WriteString("name", fn.Name);
        writer.WriteString("description", fn.Description ?? string.Empty);

        writer.WritePropertyName("parameters");
        if (fn.JsonSchema.ValueKind == JsonValueKind.Object)
        {
            fn.JsonSchema.WriteTo(writer);
        }
        else
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteStartObject("properties");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndObject(); // function
        writer.WriteEndObject(); // root
    }

    /// <summary>
    /// Serializes a function definition to a UTF-8 JSON string.
    /// </summary>
    public static string SerializeFunctionDefinition(AIFunction fn)
    {
        var bufferWriter = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(bufferWriter, _compactOptions))
        {
            WriteFunctionDefinition(writer, fn);
        }
        return Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
    }

    /// <summary>
    /// Serializes an arguments dictionary to a UTF-8 JSON string without reflection.
    /// </summary>
    public static string SerializeArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || arguments.Count == 0)
        {
            return "{}";
        }

        var bufferWriter = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(bufferWriter, _compactOptions))
        {
            WriteObject(writer, arguments);
        }
        return Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
    }

    /// <summary>
    /// Writes an arguments dictionary as a JSON object without reflection.
    /// </summary>
    public static void WriteObject(Utf8JsonWriter writer, IDictionary<string, object?> dictionary)
    {
        writer.WriteStartObject();
        foreach (var kvp in dictionary)
        {
            writer.WritePropertyName(kvp.Key);
            WriteValue(writer, kvp.Value);
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a value without reflection by dispatching on supported primitive types, JsonElement, or collections.
    /// </summary>
    public static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case decimal dec:
                writer.WriteNumberValue(dec);
                break;
            case short sh:
                writer.WriteNumberValue(sh);
                break;
            case byte by:
                writer.WriteNumberValue(by);
                break;
            case uint ui:
                writer.WriteNumberValue(ui);
                break;
            case ulong ul:
                writer.WriteNumberValue(ul);
                break;
            case sbyte sb:
                writer.WriteNumberValue(sb);
                break;
            case ushort us:
                writer.WriteNumberValue(us);
                break;
            case JsonElement je:
                je.WriteTo(writer);
                break;
            case IDictionary<string, object?> dict:
                WriteObject(writer, dict);
                break;
            case IEnumerable list when value is not string:
                writer.WriteStartArray();
                foreach (var item in list)
                {
                    WriteValue(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    /// <summary>
    /// Formats a function execution result to string safely for Native AOT.
    /// </summary>
    public static string FormatFunctionResult(object? result)
    {
        return result switch
        {
            null => string.Empty,
            string s => s,
            JsonElement je => je.GetRawText(),
            bool b => b ? "true" : "false",
            int or long or double or float or decimal or short or byte or uint or ulong => Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            IDictionary<string, object?> dict => SerializeArguments(dict),
            _ => FormatComplexResult(result)
        };
    }

    private static string FormatComplexResult(object result)
    {
        if (result is IEnumerable list)
        {
            var bufferWriter = new ArrayBufferWriter<byte>(256);
            using (var writer = new Utf8JsonWriter(bufferWriter, _compactOptions))
            {
                writer.WriteStartArray();
                foreach (var item in list)
                {
                    WriteValue(writer, item);
                }
                writer.WriteEndArray();
            }
            return Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
        }

        return result.ToString() ?? string.Empty;
    }
}
