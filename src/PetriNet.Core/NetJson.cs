using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetriNet.Core;

public static class NetJson
{
    private sealed record PlaceDocument(string Id, BigInteger Initial, Position? Position = null);
    private sealed record TransitionDocument(string Id, Dictionary<string, BigInteger>? Inputs,
        Dictionary<string, BigInteger>? Outputs, Position? Position = null);
    private sealed record NetDocument(string Name, PlaceDocument[] Places, TransitionDocument[] Transitions);

    public static Net Load(string path) => Parse(File.ReadAllText(path));

    public static Net Parse(string json)
    {
        // Reject duplicate keys, including duplicate arc references, rather than silently overwriting them.
        using var document = JsonDocument.Parse(json);
        RejectDuplicates(document.RootElement);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true
        };
        options.Converters.Add(new IntegerConverter());
        var data = JsonSerializer.Deserialize<NetDocument>(json, options)
            ?? throw new JsonException("A net object is required.");
        if (data.Places is null || data.Transitions is null || data.Places.Any(p => p is null) || data.Transitions.Any(t => t is null))
            throw new JsonException("Places and transitions must be arrays of objects.");
        return new Net(data.Name, data.Places.Select(p => new Place(p.Id, p.Initial, p.Position)),
            data.Transitions.Select(t => new Transition(t.Id,
                t.Inputs ?? throw new JsonException("Transition inputs are required."),
                t.Outputs ?? throw new JsonException("Transition outputs are required."), t.Position)));
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new JsonException($"Duplicate property '{property.Name}'.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }

    private sealed class IntegerConverter : JsonConverter<BigInteger>
    {
        public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number) throw new JsonException("Expected an integer number.");
            using var value = JsonDocument.ParseValue(ref reader);
            if (!BigInteger.TryParse(value.RootElement.GetRawText(), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var integer)) throw new JsonException("Expected an integer number.");
            return integer;
        }
        public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
