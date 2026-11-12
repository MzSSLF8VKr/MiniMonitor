using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiniMonitor.Web.WebTodo;

/// <summary>WebTodo 时间工具：本地 naive 时间 + NTP 校正偏移，与 Python 版语义一致。</summary>
public static class TodoClock
{
    private static double _offsetSeconds;

    public static void SetOffset(double seconds) => _offsetSeconds = seconds;

    public static double OffsetSeconds => _offsetSeconds;

    public static DateTime LocalNow()
    {
        DateTime now = DateTime.Now;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Unspecified);
    }

    public static DateTime Now()
    {
        return LocalNow().AddSeconds(_offsetSeconds);
    }

    public static string Iso(DateTime? value)
    {
        return value == null ? "" : value.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    }

    public static string IsoNow() => Iso(Now());

    public static DateTime? ParseDt(JsonNode? node, bool endOfDay = false)
    {
        if (node == null)
        {
            return null;
        }
        JsonValueKind kind = node.GetValueKind();
        if (kind == JsonValueKind.Null)
        {
            return null;
        }
        if (kind == JsonValueKind.Number)
        {
            double ts = node.GetValue<double>();
            return DateTimeOffset.FromUnixTimeSeconds((long)ts).LocalDateTime;
        }
        string text = node.GetValue<string>().Trim();
        return ParseDt(text, endOfDay);
    }

    public static DateTime? ParseDt(object? value, bool endOfDay = false)
    {
        if (value == null)
        {
            return null;
        }
        string text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        return ParseDt(text, endOfDay);
    }

    public static DateTime? ParseDt(string? text, bool endOfDay = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        text = text.Trim();
        if (text.Length == 10 && DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime dateOnly))
        {
            return endOfDay ? dateOnly.AddHours(23).AddMinutes(59) : dateOnly;
        }
        // 带 Z / +08:00 偏移的 ISO 时间：换算到本地后去掉时区
        if (text.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(text, "([+-]\\d{2}:?\\d{2})$"))
        {
            if (DateTimeOffset.TryParse(text.Replace("Z", "+00:00"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out DateTimeOffset offsetTime))
            {
                return offsetTime.LocalDateTime;
            }
        }
        string[] formats =
        {
            "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm",
            "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd"
        };
        if (DateTime.TryParseExact(text.Replace(' ', 'T'), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime parsed))
        {
            return parsed;
        }
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            return parsed;
        }
        throw new ArgumentException($"无法识别的时间格式：'{text}'");
    }

    public static int ToInt(object? value, int fallback, int? min = null, int? max = null)
    {
        if (value == null || value is string s0 && s0 == "")
        {
            return fallback;
        }
        int number;
        if (value is JsonNode node)
        {
            if (node.GetValueKind() == JsonValueKind.Number && node is JsonValue jv)
            {
                if (jv.TryGetValue<int>(out int intValue)) number = intValue;
                else if (jv.TryGetValue<long>(out long longValue)) number = (int)longValue;
                else number = (int)jv.GetValue<double>();
            }
            else
            {
                try { number = (int)double.Parse(node.GetValue<string>(), CultureInfo.InvariantCulture); }
                catch { return fallback; }
            }
        }
        else
        {
            try { number = (int)double.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
                CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }
        if (min.HasValue) number = Math.Max(min.Value, number);
        if (max.HasValue) number = Math.Min(max.Value, number);
        return number;
    }

    public static bool ToBool(object? value, bool fallback = false)
    {
        if (value == null)
        {
            return fallback;
        }
        if (value is JsonNode node)
        {
            return node.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => node is JsonValue nv && (nv.TryGetValue<int>(out int ni) ? ni != 0
                    : nv.TryGetValue<long>(out long nl) ? nl != 0 : nv.GetValue<double>() != 0),
                JsonValueKind.String => IsTrueText(node.GetValue<string>()),
                _ => fallback
            };
        }
        if (value is bool b) return b;
        if (value is string s) return IsTrueText(s);
        if (value is int or long or double or float)
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0;
        }
        return fallback;
    }

    private static bool IsTrueText(string text)
    {
        return text.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on" or "y";
    }

    public static string Clip(string? text, int limit)
    {
        if (text == null)
        {
            return "";
        }
        return text.Length > limit ? text[..limit] : text;
    }

    public static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 1 + 8)];

    public static (string Name, int OffsetMinutes) TzLabel()
    {
        DateTime now = DateTime.Now;
        TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(now);
        string name = TimeZoneInfo.Local.StandardName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "local";
        }
        return (name, (int)(offset.TotalMinutes));
    }

    public static object? JsonToObject(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonObject obj => obj.ToDictionary(pair => pair.Key, pair => JsonToObject(pair.Value)),
            JsonArray arr => arr.Select(JsonToObject).ToList(),
            JsonValue value => value.GetValueKind() switch
            {
                JsonValueKind.String => value.GetValue<string>(),
                JsonValueKind.Number => value.TryGetValue<int>(out int iv) ? iv
                    : value.TryGetValue<long>(out long l) ? l : value.GetValue<double>(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            },
            _ => null
        };
    }

    public static string JsonString(object? value)
    {
        if (value is string s) return s;
        if (value is JsonNode node) return node.ToJsonString(JsonOptions);
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };
}
