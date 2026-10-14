using System.Text.Json;

namespace App.Common.Errors;

/// <summary>Reads an enum from a query string or body field by its API name, such as preliminary_pass.</summary>
public static class EnumQuery
{
    /// <summary>The enum value for the snake_case name, or null. Numbers and other spellings are refused.</summary>
    public static T? Parse<T>(string? value) where T : struct, Enum =>
        value is null
            ? null
            : Enum.GetValues<T>().Cast<T?>().FirstOrDefault(v =>
                JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()!) == value);

    /// <summary>The names a client may use, for an error message.</summary>
    public static string Names<T>(params T[] allowed) where T : struct, Enum =>
        string.Join(", ", (allowed.Length > 0 ? allowed : Enum.GetValues<T>())
            .Select(v => JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()!)));
}
