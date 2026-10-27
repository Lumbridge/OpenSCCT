using System;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;

namespace OpenScct.Content.Json;

/// <summary>
/// Strict SVMap JSON reader (conventions.md): RFC 8259, UTF-8 without a byte order mark, no duplicate keys, no
/// comments or trailing commas, finite floats rounded to binary32.
/// </summary>
public static class SvJson
{
    /// <summary>Deeper nesting is rejected; SVMap documents stay far below it.</summary>
    public const int MaxDepth = 256;

    private static readonly JsonReaderOptions s_options = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaxDepth,
    };

    /// <summary>Parses one JSON document. Throws <see cref="SvMapFormatException"/> when it breaks the rules.</summary>
    public static SvJsonNode Parse(ReadOnlySpan<byte> utf8, string? file = null)
    {
        if (utf8.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            throw new SvMapFormatException(file, "a byte order mark is not allowed");
        }

        if (!Utf8.IsValid(utf8))
        {
            throw new SvMapFormatException(file, "the file is not valid UTF-8");
        }

        var reader = new Utf8JsonReader(utf8, isFinalBlock: true, state: new JsonReaderState(s_options));
        try
        {
            if (!reader.Read())
            {
                throw new SvMapFormatException(file, "the file holds no JSON value");
            }

            SvJsonNode root = ReadValue(ref reader, file);
            if (reader.Read())
            {
                throw new SvMapFormatException(file, "extra data after the JSON value");
            }

            return root;
        }
        catch (JsonException e)
        {
            throw new SvMapFormatException(file, $"invalid JSON: {e.Message}", e);
        }
        catch (InvalidOperationException e)
        {
            throw new SvMapFormatException(file, $"invalid JSON: {e.Message}", e);
        }
    }

    /// <summary>Parses JSON text (encoded to UTF-8 first).</summary>
    public static SvJsonNode Parse(string text, string? file = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Parse(Encoding.UTF8.GetBytes(text), file);
    }

    private static SvJsonNode ReadValue(ref Utf8JsonReader reader, string? file)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
            {
                var obj = new SvJsonObject();
                while (true)
                {
                    Next(ref reader, file);
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return obj;
                    }

                    string key = reader.GetString()!;
                    Next(ref reader, file);
                    SvJsonNode value = ReadValue(ref reader, file);
                    if (!obj.TryAdd(key, value))
                    {
                        throw new SvMapFormatException(file, $"duplicate key '{key}'");
                    }
                }
            }

            case JsonTokenType.StartArray:
            {
                var array = new SvJsonArray();
                while (true)
                {
                    Next(ref reader, file);
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        return array;
                    }

                    array.Add(ReadValue(ref reader, file));
                }
            }

            case JsonTokenType.String:
                return new SvJsonString(reader.GetString()!);

            case JsonTokenType.Number:
                return ReadNumber(reader.ValueSpan, file);

            case JsonTokenType.True:
                return SvJsonNode.True;

            case JsonTokenType.False:
                return SvJsonNode.False;

            case JsonTokenType.Null:
                return SvJsonNode.Null;

            default:
                throw new SvMapFormatException(file, $"unexpected JSON token {reader.TokenType}");
        }
    }

    private static void Next(ref Utf8JsonReader reader, string? file)
    {
        if (!reader.Read())
        {
            throw new SvMapFormatException(file, "unexpected end of JSON");
        }
    }

    private static SvJsonNumber ReadNumber(ReadOnlySpan<byte> token, string? file)
    {
        // Number tokens are ASCII and never escaped.
        Span<char> chars = token.Length <= 256 ? stackalloc char[token.Length] : new char[token.Length];
        for (int i = 0; i < token.Length; i++)
        {
            chars[i] = (char)token[i];
        }

        if (chars.IndexOfAny('.', 'e', 'E') < 0)
        {
            return SvJsonNumber.FromIntegerToken(chars);
        }

        try
        {
            return SvJsonNumber.FromFloat(Float32Text.Parse(chars));
        }
        catch (OverflowException)
        {
            throw new SvMapFormatException(file, $"float {chars.ToString()} overflows binary32");
        }
    }
}
