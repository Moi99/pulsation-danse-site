using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PulsationEventManager;

public static class EventStructuredDataWriter
{
    private const string StartMarker = "<!-- PULSATION_EVENTS_JSONLD_START -->";
    private const string EndMarker = "<!-- PULSATION_EVENTS_JSONLD_END -->";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void UpdateOuDanser(string siteRoot, IEnumerable<EventItem> events)
    {
        var pagePath = Path.Combine(siteRoot, "ou-danser.html");
        if (!File.Exists(pagePath))
        {
            return;
        }

        var html = File.ReadAllText(pagePath, Utf8NoBom);
        var block = BuildBlock(events);

        if (html.Contains(StartMarker, StringComparison.Ordinal))
        {
            var pattern = $"{Regex.Escape(StartMarker)}[\\s\\S]*?{Regex.Escape(EndMarker)}";
            html = Regex.Replace(html, pattern, block, RegexOptions.Singleline);
        }
        else if (TryReplaceExistingEventScript(ref html, block))
        {
            // Existing hand-written Event JSON-LD replaced with managed output.
        }
        else
        {
            html = html.Replace("</head>", $"{block}{Environment.NewLine}</head>", StringComparison.OrdinalIgnoreCase);
        }

        File.WriteAllText(pagePath, html, Utf8NoBom);
    }

    private static bool TryReplaceExistingEventScript(ref string html, string block)
    {
        var matches = Regex.Matches(html, @"<script\s+type=""application/ld\+json"">\s*(?<json>\{[\s\S]*?\})\s*</script>", RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            try
            {
                using var document = JsonDocument.Parse(match.Groups["json"].Value);
                if (ContainsType(document.RootElement, "Event"))
                {
                    html = html.Remove(match.Index, match.Length).Insert(match.Index, block);
                    return true;
                }
            }
            catch
            {
                // Ignore scripts that are not parseable JSON-LD.
            }
        }

        return false;
    }

    private static string BuildBlock(IEnumerable<EventItem> events)
    {
        var graph = new List<object>
        {
            BuildPlace("https://pulsationdanse.ca/#studio-salsa-attitude", "Studio Salsa Attitude", "3188 Chemin Ste-Foy", "G1X 1R4"),
            BuildPlace("https://pulsationdanse.ca/#studio-imperio", "Studio L'Império", "2323 Avenue Nérée-Tremblay", "G1K 3G8")
        };

        foreach (var item in events.OrderBy(item => item.Date).ThenBy(item => item.Title))
        {
            var structuredEvent = BuildStructuredEvent(item);
            if (structuredEvent is not null)
            {
                graph.Add(structuredEvent);
            }
        }

        var payload = new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = graph
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return $"{StartMarker}{Environment.NewLine}  <script type=\"application/ld+json\">{Environment.NewLine}{Indent(json, 4)}{Environment.NewLine}  </script>{Environment.NewLine}  {EndMarker}";
    }

    private static object BuildPlace(string id, string name, string street, string postalCode) =>
        new Dictionary<string, object>
        {
            ["@type"] = "Place",
            ["@id"] = id,
            ["name"] = name,
            ["address"] = new Dictionary<string, object>
            {
                ["@type"] = "PostalAddress",
                ["streetAddress"] = street,
                ["addressLocality"] = "Québec",
                ["addressRegion"] = "QC",
                ["postalCode"] = postalCode,
                ["addressCountry"] = "CA"
            }
        };

    private static Dictionary<string, object>? BuildStructuredEvent(EventItem item)
    {
        if (!DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
        {
            return null;
        }

        var locationId = ResolveLocationId(item);
        if (locationId is null)
        {
            return null;
        }

        var payload = new Dictionary<string, object>
        {
            ["@type"] = "Event",
            ["@id"] = $"https://pulsationdanse.ca/ou-danser.html#event-{Slugify(item.Title)}-{item.Date}",
            ["name"] = item.Title,
            ["description"] = BuildDescription(item),
            ["startDate"] = BuildDateTimeValue(startDate, item.Time),
            ["eventStatus"] = "https://schema.org/EventScheduled",
            ["eventAttendanceMode"] = "https://schema.org/OfflineEventAttendanceMode",
            ["image"] = BuildImageList(item),
            ["location"] = new Dictionary<string, object>
            {
                ["@id"] = locationId
            },
            ["organizer"] = new Dictionary<string, object>
            {
                ["@id"] = "https://pulsationdanse.ca/#organization"
            }
        };

        if (!string.IsNullOrWhiteSpace(item.Url))
        {
            payload["url"] = item.Url;
        }

        if (DateOnly.TryParseExact(item.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
        {
            payload["endDate"] = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return payload;
    }

    private static string BuildDescription(EventItem item)
    {
        var dances = item.Dance.Count > 0
            ? string.Join(", ", item.Dance)
            : "danse";

        return $"{item.Title} a Quebec avec Pulsation Danse. Evenement lie a {dances}.";
    }

    private static string? ResolveLocationId(EventItem item)
    {
        var haystack = $"{item.Title} {item.Location} {string.Join(" ", item.Dance)}".ToLowerInvariant();

        if (haystack.Contains("salsa attitude") || (item.Type == "session" && haystack.Contains("west coast")))
        {
            return "https://pulsationdanse.ca/#studio-salsa-attitude";
        }

        if (haystack.Contains("império") || haystack.Contains("imperio") || (item.Type == "session" && haystack.Contains("zouk")))
        {
            return "https://pulsationdanse.ca/#studio-imperio";
        }

        return null;
    }

    private static List<string> BuildImageList(EventItem item)
    {
        var images = new List<string>();

        if (!string.IsNullOrWhiteSpace(item.Image))
        {
            images.Add($"https://pulsationdanse.ca/{item.Image.Replace('\\', '/')}");
        }

        images.Add("https://pulsationdanse.ca/assets/images/social-preview-pulsation-danse-1200x630.jpg");
        images.Add("https://pulsationdanse.ca/assets/images/logo-pulsation-1200x1200.png");
        return images.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string BuildDateTimeValue(DateOnly date, string time)
    {
        if (!TryParseTime(time, out var hour, out var minute))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var local = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Unspecified);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var offset = zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    private static bool TryParseTime(string value, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;

        var match = Regex.Match(value ?? "", @"(?<h>\d{1,2})\s*h\s*(?<m>\d{2})?", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        hour = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        minute = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        return hour is >= 0 and <= 23 && minute is >= 0 and <= 59;
    }

    private static bool ContainsType(JsonElement element, string type)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("@type", out var typeValue))
            {
                if (typeValue.ValueKind == JsonValueKind.String && typeValue.GetString() == type)
                {
                    return true;
                }

                if (typeValue.ValueKind == JsonValueKind.Array && typeValue.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == type))
                {
                    return true;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (ContainsType(property.Value, type))
                {
                    return true;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(item => ContainsType(item, type));
        }

        return false;
    }

    private static string Indent(string value, int spaces)
    {
        var prefix = new string(' ', spaces);
        return string.Join(Environment.NewLine, value.Split('\n').Select(line => prefix + line.TrimEnd('\r')));
    }

    private static string Slugify(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
            .ToArray();

        var slug = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(slug) ? "evenement" : slug;
    }
}
