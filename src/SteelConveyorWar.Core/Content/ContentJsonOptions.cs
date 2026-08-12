using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteelConveyorWar.Core;

/// <summary>
/// M03: shared System.Text.Json options for content loaders. Unknown JSON properties fail closed
/// (<see cref="JsonUnmappedMemberHandling.Disallow"/>) so typos/extensions cannot silently no-op.
/// </summary>
public static class ContentJsonOptions
{
    public static JsonSerializerOptions CreateStrict(bool includeStringEnums = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        if (includeStringEnums)
        {
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        }

        return options;
    }

    /// <summary>
    /// Parses an enum from a content string, rejecting undefined numeric values
    /// (<see cref="Enum.IsDefined(Type, object)"/>).
    /// </summary>
    public static T ParseDefinedEnum<T>(string raw, string label)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException($"{label} is missing.");
        }

        if (!Enum.TryParse<T>(raw, ignoreCase: true, out var value) || !Enum.IsDefined(value))
        {
            throw new InvalidOperationException($"{label} has undefined enum value '{raw}'.");
        }

        return value;
    }
}
