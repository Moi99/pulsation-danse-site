using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PulsationEventManager;

public sealed class FacebookEventImporter
{
    private static readonly HttpClient Client = CreateHttpClient();

    public async Task<FacebookImportResult> ImportAsync(string eventUrl, string siteRoot, CancellationToken cancellationToken = default)
    {
        var inputUrl = NormalizeInputUrl(eventUrl.Trim());
        if (!Uri.TryCreate(inputUrl, UriKind.Absolute, out var uri))
        {
            return FacebookImportResult.Failed("L'URL Facebook est invalide.");
        }

        uri = UnwrapFacebookRedirect(uri) ?? uri;

        var result = new FacebookImportResult
        {
            SourceUrl = uri.ToString(),
            Item = new EventItem
            {
                Url = uri.ToString()
            }
        };

        var ids = ExtractEventIds(uri.ToString());
        var token = Environment.GetEnvironmentVariable("PULSATION_FACEBOOK_ACCESS_TOKEN")
            ?? Environment.GetEnvironmentVariable("META_FACEBOOK_ACCESS_TOKEN")
            ?? Environment.GetEnvironmentVariable("META_PAGE_ACCESS_TOKEN");

        var graphVersion = NormalizeGraphVersion(Environment.GetEnvironmentVariable("PULSATION_FACEBOOK_GRAPH_VERSION"));

        if (!string.IsNullOrWhiteSpace(token) && ids.Count > 0)
        {
            foreach (var id in ids)
            {
                if (await TryImportFromGraphApiAsync(id, token, graphVersion, result, cancellationToken))
                {
                    result.ImportMode = $"Graph API {graphVersion}";
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(result.Item.Title) || string.IsNullOrWhiteSpace(result.Item.Date))
        {
            if (await TryImportFromPublicHtmlAsync(uri, ids, result, cancellationToken))
            {
                result.ImportMode = string.IsNullOrWhiteSpace(result.ImportMode)
                    ? "HTML public"
                    : $"{result.ImportMode} + HTML public";
            }
        }

        await FinalizeResultAsync(result, siteRoot, allowKnownEventFallback: false, cancellationToken);
        return result;
    }

    public async Task<FacebookImportResult> ImportRenderedHtmlAsync(string eventUrl, string html, string siteRoot, CancellationToken cancellationToken = default)
    {
        return await ImportRenderedPageAsync(eventUrl, html, "", [], "", [], siteRoot, cancellationToken);
    }

    public async Task<FacebookImportResult> ImportRenderedPageAsync(
        string eventUrl,
        string html,
        string browserTitle,
        IEnumerable<string> headings,
        string visibleText,
        IEnumerable<string> imageCandidates,
        string siteRoot,
        CancellationToken cancellationToken = default)
    {
        var inputUrl = NormalizeInputUrl(eventUrl.Trim());
        if (!Uri.TryCreate(inputUrl, UriKind.Absolute, out var uri))
        {
            return FacebookImportResult.Failed("L'URL Facebook est invalide.");
        }

        uri = UnwrapFacebookRedirect(uri) ?? uri;

        var result = new FacebookImportResult
        {
            SourceUrl = uri.ToString(),
            ImportMode = "Navigateur intégré",
            Item = new EventItem
            {
                Url = uri.ToString()
            }
        };

        ApplyPublicHtml(html, result);
        ApplyBrowserImageCandidates(imageCandidates, result);
        ApplyBrowserHints(browserTitle, headings, visibleText, result.Item);
        await FinalizeResultAsync(result, siteRoot, allowKnownEventFallback: true, cancellationToken);
        return result;
    }

    private static async Task FinalizeResultAsync(FacebookImportResult result, string siteRoot, bool allowKnownEventFallback, CancellationToken cancellationToken)
    {
        if (allowKnownEventFallback)
        {
            MergeKnownEventData(result.Item, siteRoot);
        }
        FillInferences(result.Item);
        NormalizeEndDate(result.Item);

        if (!string.IsNullOrWhiteSpace(result.RemoteImageUrl) && Directory.Exists(siteRoot))
        {
            try
            {
                result.Item.Image = await DownloadImageAsync(result.RemoteImageUrl, result.Item.Title, siteRoot, cancellationToken);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Image non téléchargée automatiquement: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(result.Item.ImageAlt) && !string.IsNullOrWhiteSpace(result.Item.Title))
        {
            result.Item.ImageAlt = result.Item.Title;
        }

        if (string.IsNullOrWhiteSpace(result.ImportMode))
        {
            result.ImportMode = "Fallback manuel";
            result.Warnings.Add("Aucune donnée automatique fiable n'a été récupérée. Le formulaire reste disponible pour remplir l'événement à la main.");
        }

        if (string.IsNullOrWhiteSpace(result.Item.Title))
        {
            result.Warnings.Add("Titre manquant.");
        }

        if (string.IsNullOrWhiteSpace(result.Item.Date))
        {
            result.Warnings.Add("Date de début manquante.");
        }

        if (result.Item.Dance.Count == 0)
        {
            result.Warnings.Add("Style de danse non détecté.");
        }
    }

    private static async Task<bool> TryImportFromGraphApiAsync(string eventId, string token, string graphVersion, FacebookImportResult result, CancellationToken cancellationToken)
    {
        try
        {
            var fields = "name,description,start_time,end_time,timezone,place{name,location{street,city,state,zip,country}},cover{source}";
            var url = $"https://graph.facebook.com/{graphVersion}/{eventId}?fields={Uri.EscapeDataString(fields)}&access_token={Uri.EscapeDataString(token)}";
            using var response = await Client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                result.Warnings.Add($"Graph API indisponible pour l'événement {eventId}: {(int)response.StatusCode} {response.ReasonPhrase}");
                return false;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            result.Item.Title = GetString(root, "name");
            ApplyDateTime(GetString(root, "start_time"), true, result.Item);
            ApplyDateTime(GetString(root, "end_time"), false, result.Item);

            if (root.TryGetProperty("place", out var place))
            {
                result.Item.Location = GetString(place, "name");

                if (string.IsNullOrWhiteSpace(result.Item.Location) && place.TryGetProperty("location", out var location))
                {
                    result.Item.Location = string.Join(", ", new[]
                    {
                        GetString(location, "street"),
                        GetString(location, "city"),
                        GetString(location, "state"),
                        GetString(location, "zip")
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));
                }
            }

            if (root.TryGetProperty("cover", out var cover))
            {
                result.RemoteImageUrl = GetString(cover, "source");
            }

            return !string.IsNullOrWhiteSpace(result.Item.Title) || !string.IsNullOrWhiteSpace(result.Item.Date);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Graph API non utilisée: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TryImportFromPublicHtmlAsync(Uri uri, IReadOnlyCollection<string> eventIds, FacebookImportResult result, CancellationToken cancellationToken)
    {
        var attempts = new List<string>();

        foreach (var candidate in BuildPublicHtmlCandidates(uri, eventIds))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125 Safari/537.36");
                request.Headers.Accept.ParseAdd("text/html");
                request.Headers.Accept.ParseAdd("application/xhtml+xml");
                request.Headers.AcceptLanguage.ParseAdd("fr-CA,fr;q=0.9,en;q=0.8");
                request.Headers.Referrer = new Uri("https://www.facebook.com/");

                using var response = await Client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    attempts.Add($"{candidate.Host}: {(int)response.StatusCode} {response.ReasonPhrase}");
                    continue;
                }

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var candidateResult = new FacebookImportResult
                {
                    SourceUrl = result.SourceUrl,
                    ImportMode = result.ImportMode,
                    RemoteImageUrl = result.RemoteImageUrl,
                    Item = CloneItem(result.Item)
                };

                ApplyPublicHtml(html, candidateResult);

                if (HasUsefulHtmlData(candidateResult))
                {
                    MergeImportResult(result, candidateResult);
                    return true;
                }

                attempts.Add($"{candidate.Host}: aucune donnee d'evenement lisible");
            }
            catch (Exception ex)
            {
                attempts.Add($"{candidate.Host}: {ex.Message}");
            }
        }

        result.Warnings.Add($"Lecture publique Facebook impossible. Essais: {string.Join("; ", attempts)}");
        return false;
    }

    private static void ApplyPublicHtml(string html, FacebookImportResult result)
    {
        result.Item.Title = FirstNonEmpty(
            result.Item.Title,
            CleanFacebookTitle(ReadMeta(html, "og:title")),
            CleanFacebookTitle(ReadTitle(html))) ?? "";

        result.RemoteImageUrl = FirstNonEmpty(
            result.RemoteImageUrl,
            ReadMeta(html, "og:image"),
            ReadJsonLdImage(html),
            ReadRegexValue(html, @"""cover_photo""\s*:\s*\{[^}]*?""uri""\s*:\s*""(?<value>https?:\\/\\/[^""]+)""")) ?? "";

        var jsonLd = ReadJsonLdEvent(html);
        if (jsonLd is not null)
        {
            result.Item.Title = FirstNonEmpty(result.Item.Title, GetString(jsonLd.Value, "name")) ?? "";
            ApplyDateTime(FirstNonEmpty(GetString(jsonLd.Value, "startDate"), GetString(jsonLd.Value, "start_time")), true, result.Item);
            ApplyDateTime(FirstNonEmpty(GetString(jsonLd.Value, "endDate"), GetString(jsonLd.Value, "end_time")), false, result.Item);
            result.Item.Location = FirstNonEmpty(result.Item.Location, ReadLocation(jsonLd.Value)) ?? "";
        }

        ApplyDateTime(ReadRegexValue(html, @"""start_time""\s*:\s*""(?<value>[^""]+)"""), true, result.Item);
        ApplyDateTime(ReadRegexValue(html, @"""end_time""\s*:\s*""(?<value>[^""]+)"""), false, result.Item);
        ApplyDateTime(ReadRegexValue(html, @"""startDate""\s*:\s*""(?<value>[^""]+)"""), true, result.Item);
        ApplyDateTime(ReadRegexValue(html, @"""endDate""\s*:\s*""(?<value>[^""]+)"""), false, result.Item);

        if (string.IsNullOrWhiteSpace(result.Item.Location))
        {
            result.Item.Location = FirstNonEmpty(
                ReadRegexValue(html, @"""place_name""\s*:\s*""(?<value>[^""]+)"""),
                ReadRegexValue(html, @"""location_name""\s*:\s*""(?<value>[^""]+)""")) ?? "";
        }
    }

    private static bool HasUsefulHtmlData(FacebookImportResult result) =>
        !string.IsNullOrWhiteSpace(result.Item.Date)
        || !string.IsNullOrWhiteSpace(result.Item.Location)
        || !string.IsNullOrWhiteSpace(result.RemoteImageUrl)
        || IsMeaningfulFacebookTitle(result.Item.Title);

    private static void ApplyBrowserImageCandidates(IEnumerable<string> imageCandidates, FacebookImportResult result)
    {
        var imageUrl = imageCandidates
            .Select(NormalizeRemoteImageUrl)
            .FirstOrDefault(IsDownloadableRemoteImageUrl);

        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            result.RemoteImageUrl = imageUrl;
        }
    }

    private static string NormalizeRemoteImageUrl(string value)
    {
        value = Decode(value ?? "").Replace("&amp;", "&").Trim();
        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            value = $"https:{value}";
        }

        return value;
    }

    private static bool IsDownloadableRemoteImageUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var haystack = $"{uri.Host} {uri.AbsolutePath}".ToLowerInvariant();
        return !haystack.Contains("emoji") &&
            !haystack.Contains("static_map") &&
            !haystack.Contains("profile") &&
            !haystack.Contains("avatar");
    }

    private static void ApplyBrowserHints(string browserTitle, IEnumerable<string> headings, string visibleText, EventItem item)
    {
        var headingList = headings.ToList();
        var combinedText = string.Join(Environment.NewLine, new[]
        {
            browserTitle,
            string.Join(Environment.NewLine, headingList),
            visibleText
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        var visibleTitle = ExtractFacebookVisibleTitle(combinedText, headingList);
        if (!string.IsNullOrWhiteSpace(visibleTitle))
        {
            item.Title = visibleTitle;
        }

        if (string.IsNullOrWhiteSpace(item.Title))
        {
            item.Title = headingList
                .Select(CleanFacebookTitle)
                .FirstOrDefault(IsMeaningfulFacebookTitle) ?? "";
        }

        if (string.IsNullOrWhiteSpace(item.Title))
        {
            item.Title = CleanFacebookTitle(browserTitle);
        }

        if (string.IsNullOrWhiteSpace(item.Date))
        {
            if (!ApplyFacebookVisibleLayoutDate(combinedText, item))
            {
                ApplyVisibleDate(combinedText, item);
            }
        }

        ApplyVisibleTime(combinedText, item, preferFacebookEventLine: true);

        if (string.IsNullOrWhiteSpace(item.Location))
        {
            item.Location = ReadKnownLocation(combinedText);
        }

        ApplyVisibleDanceAndType(combinedText, item);
    }

    private static string ExtractFacebookVisibleTitle(string visibleText, IEnumerable<string> headings)
    {
        var lines = SplitVisibleLines(visibleText);

        for (var index = 0; index < lines.Count; index++)
        {
            if (!IsFacebookVisibleTimeLine(lines[index]))
            {
                continue;
            }

            for (var candidateIndex = index + 1; candidateIndex < Math.Min(lines.Count, index + 6); candidateIndex++)
            {
                if (IsLikelyFacebookEventTitle(lines[candidateIndex]))
                {
                    return CleanFacebookTitle(lines[candidateIndex]);
                }
            }
        }

        return headings
            .Select(CleanFacebookTitle)
            .FirstOrDefault(IsLikelyFacebookEventTitle) ?? "";
    }

    private static bool IsLikelyFacebookEventTitle(string value)
    {
        value = CleanFacebookTitle(value);
        if (value.Length is < 4 or > 160)
        {
            return false;
        }

        var normalized = NormalizeVisibleText(value);
        if (Regex.IsMatch(normalized, @"^\d{1,2}$") ||
            IsFacebookVisibleTimeLine(value) ||
            Regex.IsMatch(normalized, @"^(lun|lundi|mar|mardi|mer|mercredi|jeu|jeudi|ven|vendredi|sam|samedi|dim|dimanche)\.?\s+\d{1,2}\s+"))
        {
            return false;
        }

        var blocked = new[]
        {
            "vous a invite",
            "interesse",
            "je participe",
            "a propos",
            "discussion",
            "inviter",
            "partager",
            "notifications",
            "marie diallo",
            "organise par"
        };

        return !blocked.Any(block => normalized.Contains(block, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ApplyFacebookVisibleLayoutDate(string visibleText, EventItem item)
    {
        var lines = SplitVisibleLines(visibleText);
        if (lines.Count == 0)
        {
            return false;
        }

        var normalizedText = NormalizeVisibleText(visibleText);

        if (TryApplyFacebookDateFromText(normalizedText, item))
        {
            return true;
        }

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!IsFacebookVisibleTimeLine(line))
            {
                continue;
            }

            var day = FindStandaloneDayBefore(lines, index);
            if (day == 0)
            {
                continue;
            }

            var month = FindMonthForDay(normalizedText, day);
            if (month == 0)
            {
                month = FindMonthInTextNearLine(lines, index);
            }

            if (month == 0)
            {
                continue;
            }

            var year = ResolveVisibleDateYear("", month, day);

            try
            {
                item.Date = new DateOnly(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch
            {
                return false;
            }

            ApplyTimeFromLine(line, item);
            return true;
        }

        return false;
    }

    private static bool TryApplyFacebookDateFromText(string normalizedText, EventItem item)
    {
        var dayHint = ReadCalendarDayBeforeTime(normalizedText);
        var candidates = ReadVisibleDateCandidates(normalizedText).ToList();

        var selected = dayHint > 0
            ? candidates.FirstOrDefault(candidate => candidate.Day == dayHint)
            : candidates.FirstOrDefault();

        if (selected.Day == 0 || selected.Month == 0)
        {
            return false;
        }

        if (!ApplyDateParts(selected.Day, selected.Month, selected.Year, item))
        {
            return false;
        }

        TryApplyTimeFromVisibleText(normalizedText, item);
        return true;
    }

    private static int ReadCalendarDayBeforeTime(string normalizedText)
    {
        var weekdayPattern = "lun|lundi|mar|mardi|mer|mercredi|jeu|jeudi|ven|vendredi|sam|samedi|dim|dimanche";
        var match = Regex.Match(
            normalizedText,
            $@"\b(?<day>\d{{1,2}})\s+(?:{weekdayPattern})\.?\s+(?:de\s+)?\d{{1,2}}\s*(?:h|:)\s*\d{{2}}",
            RegexOptions.IgnoreCase);

        return match.Success && int.TryParse(match.Groups["day"].Value, CultureInfo.InvariantCulture, out var day)
            ? day
            : 0;
    }

    private static IEnumerable<(int Day, int Month, int Year)> ReadVisibleDateCandidates(string normalizedText)
    {
        var monthPattern = "janvier|janv|fevrier|fevr|mars|avril|avr|mai|juin|juillet|juil|aout|septembre|sept|octobre|oct|novembre|nov|decembre|dec";
        var weekdayPattern = "lun|lundi|mar|mardi|mer|mercredi|jeu|jeudi|ven|vendredi|sam|samedi|dim|dimanche";

        foreach (Match match in Regex.Matches(
            normalizedText,
            $@"\b(?:{weekdayPattern})\.?\s+(?<day>\d{{1,2}})\s+(?<month>{monthPattern})\s*(?<year>\d{{4}})?\b",
            RegexOptions.IgnoreCase))
        {
            var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
            var month = ParseFrenchMonth(match.Groups["month"].Value);
            var year = ResolveVisibleDateYear(match.Groups["year"].Success ? match.Groups["year"].Value : "", month, day);
            yield return (day, month, year);
        }

        foreach (Match match in Regex.Matches(
            normalizedText,
            $@"\b(?<day>\d{{1,2}})\s+(?<month>{monthPattern})\s*(?<year>\d{{4}})?\b",
            RegexOptions.IgnoreCase))
        {
            var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
            var month = ParseFrenchMonth(match.Groups["month"].Value);
            var year = ResolveVisibleDateYear(match.Groups["year"].Success ? match.Groups["year"].Value : "", month, day);
            yield return (day, month, year);
        }
    }

    private static bool ApplyDateParts(int day, int month, int year, EventItem item)
    {
        if (month is < 1 or > 12 || day is < 1 or > 31)
        {
            return false;
        }

        try
        {
            item.Date = new DateOnly(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int FindStandaloneDayBefore(IReadOnlyList<string> lines, int startIndex)
    {
        for (var index = startIndex - 1; index >= Math.Max(0, startIndex - 4); index--)
        {
            var normalized = NormalizeVisibleText(lines[index]);
            if (Regex.IsMatch(normalized, @"^\d{1,2}$") &&
                int.TryParse(normalized, CultureInfo.InvariantCulture, out var day) &&
                day is >= 1 and <= 31)
            {
                return day;
            }
        }

        return 0;
    }

    private static int FindMonthForDay(string normalizedText, int day)
    {
        var monthPattern = "janvier|janv|fevrier|fevr|mars|avril|avr|mai|juin|juillet|juil|aout|septembre|sept|octobre|oct|novembre|nov|decembre|dec";
        var match = Regex.Match(normalizedText, $@"(?:lun|lundi|mar|mardi|mer|mercredi|jeu|jeudi|ven|vendredi|sam|samedi|dim|dimanche)?\.?\s*\b{day}\s+(?<month>{monthPattern})\b", RegexOptions.IgnoreCase);
        return match.Success ? ParseFrenchMonth(match.Groups["month"].Value) : 0;
    }

    private static int FindMonthInTextNearLine(IReadOnlyList<string> lines, int centerIndex)
    {
        var start = Math.Max(0, centerIndex - 3);
        var end = Math.Min(lines.Count - 1, centerIndex + 8);
        var text = NormalizeVisibleText(string.Join(" ", lines.Skip(start).Take(end - start + 1)));
        var match = Regex.Match(text, @"\b(?<month>janvier|janv|fevrier|fevr|mars|avril|avr|mai|juin|juillet|juil|aout|septembre|sept|octobre|oct|novembre|nov|decembre|dec)\b", RegexOptions.IgnoreCase);
        return match.Success ? ParseFrenchMonth(match.Groups["month"].Value) : 0;
    }

    private static void ApplyVisibleTime(string visibleText, EventItem item, bool preferFacebookEventLine = false)
    {
        var line = SplitVisibleLines(visibleText).FirstOrDefault(IsFacebookVisibleTimeLine);
        if (!string.IsNullOrWhiteSpace(line))
        {
            ApplyTimeFromLine(line, item);
            return;
        }

        if (preferFacebookEventLine && TryApplyTimeFromVisibleText(visibleText, item))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Time))
        {
            TryApplyTimeFromVisibleText(visibleText, item);
        }
    }

    private static bool IsFacebookVisibleTimeLine(string value) =>
        Regex.IsMatch(
            NormalizeVisibleText(value),
            @"^(lun|lundi|mar|mardi|mer|mercredi|jeu|jeudi|ven|vendredi|sam|samedi|dim|dimanche)\.?\s+(?:de\s+)?\d{1,2}\s*(?:h|:)\s*\d{0,2}",
            RegexOptions.IgnoreCase);

    private static void ApplyTimeFromLine(string value, EventItem item)
    {
        var normalized = NormalizeVisibleText(value);
        var match = Regex.Match(normalized, @"(?:de\s+)?(?<hour>\d{1,2})\s*(?:h|:)\s*(?<minute>\d{2})?", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return;
        }

        ApplyTimeMatch(match, item);
    }

    private static bool TryApplyTimeFromVisibleText(string visibleText, EventItem item)
    {
        var normalized = NormalizeVisibleText(visibleText);
        var weekdayPattern = "lun|lundi|mar|mardi|mer|mercredi|jeu|jeudi|ven|vendredi|sam|samedi|dim|dimanche";
        var patterns = new[]
        {
            $@"\b(?:{weekdayPattern})\.?\s+(?:de\s+)?(?<hour>\d{{1,2}})\s*(?:h|:)\s*(?<minute>\d{{2}})(?:\s*(?:a|-|–)\s*\d{{1,2}}\s*(?:h|:)\s*\d{{2}})?",
            @"\bde\s+(?<hour>\d{1,2})\s*(?:h|:)\s*(?<minute>\d{2})\s+(?:a|-|–)\s+\d{1,2}\s*(?:h|:)\s*\d{2}",
            @"\b(?<hour>\d{1,2})\s*(?:h|:)\s*(?<minute>\d{2})\s+(?:a|-|–)\s+\d{1,2}\s*(?:h|:)\s*\d{2}"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                continue;
            }

            ApplyTimeMatch(match, item);
            return true;
        }

        return false;
    }

    private static void ApplyTimeMatch(Match match, EventItem item)
    {
        var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var minute = match.Groups["minute"].Success ? int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture) : 0;
        if (hour is >= 0 and <= 23 && minute is >= 0 and <= 59)
        {
            item.Time = minute == 0 ? $"{hour}h" : $"{hour}h{minute:00}";
        }
    }

    private static void ApplyVisibleDate(string visibleText, EventItem item)
    {
        if (string.IsNullOrWhiteSpace(visibleText))
        {
            return;
        }

        var normalized = NormalizeVisibleText(visibleText);
        var dateMatch = Regex.Match(normalized, @"(?<day>\d{1,2})\s+(?<month>janvier|janv|fevrier|fevr|mars|avril|avr|mai|juin|juillet|juil|aout|septembre|sept|octobre|oct|novembre|nov|decembre|dec)\s*(?<year>\d{4})?", RegexOptions.IgnoreCase);
        if (!dateMatch.Success)
        {
            dateMatch = Regex.Match(normalized, @"(?<day>\d{1,2})[/-](?<month>\d{1,2})(?:[/-](?<year>\d{2,4}))?", RegexOptions.IgnoreCase);
            if (!dateMatch.Success)
            {
                return;
            }
        }

        var month = dateMatch.Groups["month"].Value.All(char.IsDigit)
            ? int.Parse(dateMatch.Groups["month"].Value, CultureInfo.InvariantCulture)
            : ParseFrenchMonth(dateMatch.Groups["month"].Value);
        if (month == 0)
        {
            return;
        }

        var day = int.Parse(dateMatch.Groups["day"].Value, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12 || day is < 1 or > 31)
        {
            return;
        }

        var year = ResolveVisibleDateYear(dateMatch.Groups["year"].Success ? dateMatch.Groups["year"].Value : "", month, day);

        try
        {
            item.Date = new DateOnly(
            year,
            month,
            day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            return;
        }

        var afterDate = normalized[dateMatch.Index..];
        var timeMatch = Regex.Match(afterDate, @"(?:a|@|de|from)?\s*(?<hour>\d{1,2})\s*(?:h|:)\s*(?<minute>\d{2})?", RegexOptions.IgnoreCase);
        if (timeMatch.Success)
        {
            var hour = int.Parse(timeMatch.Groups["hour"].Value, CultureInfo.InvariantCulture);
            var minute = timeMatch.Groups["minute"].Success ? int.Parse(timeMatch.Groups["minute"].Value, CultureInfo.InvariantCulture) : 0;
            if (hour is >= 0 and <= 23 && minute is >= 0 and <= 59)
            {
                item.Time = minute == 0 ? $"{hour}h" : $"{hour}h{minute:00}";
            }
        }

        ApplyVisibleEndDate(normalized[dateMatch.Index..], item, year);
    }

    private static void ApplyVisibleEndDate(string textAfterStartDate, EventItem item, int fallbackYear)
    {
        if (!string.IsNullOrWhiteSpace(item.EndDate))
        {
            return;
        }

        var endMatch = Regex.Match(textAfterStartDate, @"(?:-|–|au|jusqu.au|to)\s*(?<day>\d{1,2})\s+(?<month>janvier|janv|fevrier|fevr|mars|avril|avr|mai|juin|juillet|juil|aout|septembre|sept|octobre|oct|novembre|nov|decembre|dec)\s*(?<year>\d{4})?", RegexOptions.IgnoreCase);
        if (!endMatch.Success)
        {
            return;
        }

        var month = ParseFrenchMonth(endMatch.Groups["month"].Value);
        var day = int.Parse(endMatch.Groups["day"].Value, CultureInfo.InvariantCulture);
        var year = ResolveVisibleDateYear(endMatch.Groups["year"].Success ? endMatch.Groups["year"].Value : fallbackYear.ToString(CultureInfo.InvariantCulture), month, day);

        try
        {
            item.EndDate = new DateOnly(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            // Keep the start date only if the visible end date cannot be parsed.
        }
    }

    private static int ResolveVisibleDateYear(string value, int month, int day)
    {
        if (int.TryParse(value, CultureInfo.InvariantCulture, out var parsedYear))
        {
            return parsedYear < 100 ? 2000 + parsedYear : parsedYear;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var candidate = new DateOnly(today.Year, month, Math.Min(day, DateTime.DaysInMonth(today.Year, month)));

        if (candidate < today.AddMonths(-6))
        {
            candidate = candidate.AddYears(1);
        }

        return candidate.Year;
    }

    private static void ApplyVisibleDanceAndType(string visibleText, EventItem item)
    {
        var normalized = NormalizeVisibleText(visibleText);

        AddDanceIfSeen(item, "WCS", normalized.Contains("west coast swing") || normalized.Contains("wcs") || normalized.Contains("westi"));
        AddDanceIfSeen(item, "Zouk", normalized.Contains("zouk"));
        AddDanceIfSeen(item, "Blues", normalized.Contains("blues"));

        if (normalized.Contains("cours") || normalized.Contains("session") || normalized.Contains("classes") || normalized.Contains("course"))
        {
            item.Type = "session";
        }
        else if (normalized.Contains("fest") || normalized.Contains("weekender") || normalized.Contains("stage") || normalized.Contains("workshop") || normalized.Contains("atelier"))
        {
            item.Type = "weekender";
        }
    }

    private static void NormalizeEndDate(EventItem item)
    {
        if (string.IsNullOrWhiteSpace(item.EndDate))
        {
            return;
        }

        if (!DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate) ||
            !DateOnly.TryParseExact(item.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
        {
            item.EndDate = "";
            return;
        }

        if (endDate <= startDate)
        {
            item.EndDate = "";
            return;
        }

        if (!ShouldKeepEndDate(item, startDate, endDate))
        {
            item.EndDate = "";
        }
    }

    private static bool ShouldKeepEndDate(EventItem item, DateOnly startDate, DateOnly endDate)
    {
        var haystack = NormalizeVisibleText($"{item.Title} {item.Type}");
        var duration = endDate.DayNumber - startDate.DayNumber;

        if (duration <= 0)
        {
            return false;
        }

        return item.Type == "weekender" ||
            haystack.Contains("weekend") ||
            haystack.Contains("week-end") ||
            haystack.Contains("weekender") ||
            haystack.Contains("fest") ||
            haystack.Contains("festival") ||
            haystack.Contains("stage") ||
            haystack.Contains("workshop") ||
            haystack.Contains("atelier");
    }

    private static void AddDanceIfSeen(EventItem item, string dance, bool seen)
    {
        if (seen && !item.Dance.Any(value => string.Equals(value, dance, StringComparison.OrdinalIgnoreCase)))
        {
            item.Dance.Add(dance);
        }
    }

    private static int ParseFrenchMonth(string value)
    {
        value = NormalizeVisibleText(value).Trim('.');
        return value switch
        {
            "janvier" or "janv" => 1,
            "fevrier" or "fevr" => 2,
            "mars" => 3,
            "avril" or "avr" => 4,
            "mai" => 5,
            "juin" => 6,
            "juillet" or "juil" => 7,
            "aout" => 8,
            "septembre" or "sept" => 9,
            "octobre" or "oct" => 10,
            "novembre" or "nov" => 11,
            "decembre" or "dec" => 12,
            _ => 0
        };
    }

    private static string ReadKnownLocation(string visibleText)
    {
        var normalized = RemoveDiacritics(visibleText).ToLowerInvariant();
        if (normalized.Contains("salsa attitude"))
        {
            return "Studio Salsa Attitude";
        }

        if (normalized.Contains("imperio"))
        {
            return "Studio Império";
        }

        return "";
    }

    private static void MergeKnownEventData(EventItem item, string siteRoot)
    {
        try
        {
            var eventsPath = Path.Combine(siteRoot, "data", "evenements.json");
            if (!File.Exists(eventsPath))
            {
                return;
            }

            var store = JsonSerializer.Deserialize<EventStore>(File.ReadAllText(eventsPath));
            var known = store?.Events.FirstOrDefault(existing => IsSameFacebookEvent(existing.Url, item.Url));
            if (known is null)
            {
                return;
            }

            item.Title = FirstNonEmpty(item.Title, known.Title) ?? "";
            item.Date = FirstNonEmpty(item.Date, known.Date) ?? "";
            item.Time = FirstNonEmpty(item.Time, known.Time) ?? "";
            item.Location = FirstNonEmpty(item.Location, known.Location) ?? "";
            item.Type = FirstNonEmpty(item.Type, known.Type) ?? "soiree-locale";
            item.EndDate = FirstNonEmpty(item.EndDate, known.EndDate) ?? "";
            item.Image = FirstNonEmpty(item.Image, known.Image) ?? "";
            item.ImageAlt = FirstNonEmpty(item.ImageAlt, known.ImageAlt) ?? "";

            foreach (var dance in known.Dance)
            {
                AddDanceIfSeen(item, dance, true);
            }
        }
        catch
        {
            // Existing event data is only a fallback; ignore read errors.
        }
    }

    private static bool IsSameFacebookEvent(string firstUrl, string secondUrl)
    {
        var firstIds = ExtractEventIds(firstUrl);
        var secondIds = ExtractEventIds(secondUrl);
        if (firstIds.Count > 0 && secondIds.Count > 0)
        {
            return firstIds.Intersect(secondIds, StringComparer.OrdinalIgnoreCase).Any();
        }

        return string.Equals(
            NormalizeInputUrl(firstUrl).TrimEnd('/'),
            NormalizeInputUrl(secondUrl).TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void MergeImportResult(FacebookImportResult target, FacebookImportResult source)
    {
        target.Item.Title = source.Item.Title;
        target.Item.Url = source.Item.Url;
        target.Item.Date = source.Item.Date;
        target.Item.EndDate = source.Item.EndDate;
        target.Item.Time = source.Item.Time;
        target.Item.Location = source.Item.Location;
        target.Item.Dance = source.Item.Dance;
        target.Item.Type = source.Item.Type;
        target.Item.Image = source.Item.Image;
        target.Item.ImageAlt = source.Item.ImageAlt;
        target.RemoteImageUrl = source.RemoteImageUrl;
    }

    private static EventItem CloneItem(EventItem item) =>
        new()
        {
            Title = item.Title,
            Url = item.Url,
            Date = item.Date,
            EndDate = item.EndDate,
            Time = item.Time,
            Location = item.Location,
            Dance = item.Dance.ToList(),
            Type = item.Type,
            Image = item.Image,
            ImageAlt = item.ImageAlt
        };

    private static string? ReadMeta(string html, string property)
    {
        var escaped = Regex.Escape(property);
        var patterns = new[]
        {
            $"""<meta\s+[^>]*(?:property|name)=[""']{escaped}[""'][^>]*content=[""'](?<value>.*?)[""'][^>]*>""",
            $"""<meta\s+[^>]*content=[""'](?<value>.*?)[""'][^>]*(?:property|name)=[""']{escaped}[""'][^>]*>"""
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                return Decode(match.Groups["value"].Value);
            }
        }

        return null;
    }

    private static IReadOnlyList<Uri> BuildPublicHtmlCandidates(Uri uri, IReadOnlyCollection<string> eventIds)
    {
        var candidates = new List<string>();

        AddCandidate(uri.ToString());

        if (uri.Host.Contains("facebook.com", StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(WithHost(uri, "www.facebook.com"));
            AddCandidate(WithHost(uri, "m.facebook.com"));
            AddCandidate(WithHost(uri, "mbasic.facebook.com"));
            AddCandidate(WithHost(uri, "touch.facebook.com"));
        }

        foreach (var id in eventIds)
        {
            AddCandidate($"https://www.facebook.com/events/{id}/");
            AddCandidate($"https://m.facebook.com/events/{id}/");
            AddCandidate($"https://mbasic.facebook.com/events/{id}/");
            AddCandidate($"https://touch.facebook.com/events/{id}/");
        }

        return candidates
            .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var candidate) ? candidate : null)
            .OfType<Uri>()
            .ToList();

        void AddCandidate(string value)
        {
            if (!candidates.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(value);
            }
        }

        static string WithHost(Uri source, string host)
        {
            var builder = new UriBuilder(source)
            {
                Host = host
            };

            return builder.Uri.ToString();
        }
    }

    private static string? ReadTitle(string html)
    {
        var match = Regex.Match(html, @"<title[^>]*>(?<value>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? Decode(Regex.Replace(match.Groups["value"].Value, @"\s+", " ").Trim()) : null;
    }

    private static JsonElement? ReadJsonLdEvent(string html)
    {
        foreach (Match match in Regex.Matches(html, @"<script[^>]+type=[""']application/ld\+json[""'][^>]*>(?<json>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            try
            {
                using var document = JsonDocument.Parse(Decode(match.Groups["json"].Value));
                var clone = document.RootElement.Clone();
                var found = FindEventNode(clone);
                if (found is not null)
                {
                    return found.Value.Clone();
                }
            }
            catch
            {
                // Ignore invalid third-party script blocks.
            }
        }

        return null;
    }

    private static string? ReadJsonLdImage(string html)
    {
        var node = ReadJsonLdEvent(html);
        if (node is null)
        {
            return null;
        }

        if (!node.Value.TryGetProperty("image", out var image))
        {
            return null;
        }

        if (image.ValueKind == JsonValueKind.String)
        {
            return image.GetString();
        }

        if (image.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in image.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    return item.GetString();
                }
            }
        }

        return null;
    }

    private static JsonElement? FindEventNode(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("@type", out var type) && ContainsType(type, "Event"))
            {
                return element;
            }

            if (element.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in graph.EnumerateArray())
                {
                    var found = FindEventNode(node);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var found = FindEventNode(property.Value);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindEventNode(item);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static bool ContainsType(JsonElement type, string expected)
    {
        if (type.ValueKind == JsonValueKind.String)
        {
            return string.Equals(type.GetString(), expected, StringComparison.OrdinalIgnoreCase);
        }

        if (type.ValueKind == JsonValueKind.Array)
        {
            return type.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), expected, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static string? ReadLocation(JsonElement element)
    {
        if (!element.TryGetProperty("location", out var location))
        {
            return null;
        }

        if (location.ValueKind == JsonValueKind.String)
        {
            return location.GetString();
        }

        if (location.ValueKind == JsonValueKind.Object)
        {
            return FirstNonEmpty(
                GetString(location, "name"),
                GetString(location, "address"),
                GetString(location, "streetAddress"),
                ReadAddress(location));
        }

        return null;
    }

    private static string? ReadAddress(JsonElement location)
    {
        if (!location.TryGetProperty("address", out var address))
        {
            return null;
        }

        if (address.ValueKind == JsonValueKind.String)
        {
            return address.GetString();
        }

        if (address.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return string.Join(", ", new[]
        {
            GetString(address, "streetAddress"),
            GetString(address, "addressLocality"),
            GetString(address, "addressRegion"),
            GetString(address, "postalCode")
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string? ReadRegexValue(string html, string pattern)
    {
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? DecodeJsonValue(match.Groups["value"].Value) : null;
    }

    private static void ApplyDateTime(string? value, bool isStart, EventItem item)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        value = DecodeJsonValue(value);

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var offset))
        {
            var localOffset = ToEasternTime(offset);

            if (isStart)
            {
                item.Date = localOffset.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                item.Time = FormatTime(localOffset);
            }
            else
            {
                item.EndDate = localOffset.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
        {
            if (isStart)
            {
                item.Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                item.EndDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }
    }

    private static DateTimeOffset ToEasternTime(DateTimeOffset value)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        return TimeZoneInfo.ConvertTime(value, zone);
    }

    private static string FormatTime(DateTimeOffset value) =>
        value.Minute == 0
            ? $"{value.Hour}h"
            : $"{value.Hour}h{value.Minute:00}";

    private static void FillInferences(EventItem item)
    {
        var haystack = $"{item.Title} {item.Location} {item.Url}".ToLowerInvariant();

        if (haystack.Contains("west coast") || haystack.Contains("wcs") || haystack.Contains("westi"))
        {
            AddDanceIfSeen(item, "WCS", true);
        }

        if (haystack.Contains("zouk"))
        {
            AddDanceIfSeen(item, "Zouk", true);
        }

        if (haystack.Contains("blues"))
        {
            AddDanceIfSeen(item, "Blues", true);
        }

        if (haystack.Contains("cours") || haystack.Contains("session"))
        {
            item.Type = "session";
        }
        else if (haystack.Contains("fest") || haystack.Contains("weekender") || haystack.Contains("stage") || haystack.Contains("workshop"))
        {
            item.Type = "weekender";
        }
        else
        {
            item.Type = "soiree-locale";
        }

        if (string.IsNullOrWhiteSpace(item.Location))
        {
            if (item.Type == "session" && item.Dance.Contains("WCS"))
            {
                item.Location = "Studio Salsa Attitude";
            }
            else if (item.Type == "session" && item.Dance.Contains("Zouk"))
            {
                item.Location = "Studio Império";
            }
        }
    }

    private static async Task<string> DownloadImageAsync(string imageUrl, string title, string siteRoot, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125 Safari/537.36");
        request.Headers.Accept.ParseAdd("image/avif");
        request.Headers.Accept.ParseAdd("image/webp");
        request.Headers.Accept.ParseAdd("image/apng");
        request.Headers.Accept.ParseAdd("image/*");
        request.Headers.Referrer = new Uri("https://www.facebook.com/");

        using var response = await Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"La ressource trouvée n'est pas une image ({contentType}).");
        }

        var extension = GetExtension(contentType, imageUrl);
        var folder = Path.Combine(siteRoot, "assets", "images", "events");
        Directory.CreateDirectory(folder);

        var baseName = Slugify(string.IsNullOrWhiteSpace(title) ? "evenement-facebook" : title);
        var fileName = $"{baseName}{extension}";
        var destination = Path.Combine(folder, fileName);
        var counter = 2;

        while (File.Exists(destination))
        {
            fileName = $"{baseName}-{counter}{extension}";
            destination = Path.Combine(folder, fileName);
            counter++;
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken);

        return $"assets/images/events/{fileName}";
    }

    private static string GetExtension(string contentType, string imageUrl)
    {
        if (contentType.Contains("png", StringComparison.OrdinalIgnoreCase))
        {
            return ".png";
        }

        if (contentType.Contains("webp", StringComparison.OrdinalIgnoreCase))
        {
            return ".webp";
        }

        var uriPath = Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ? uri.AbsolutePath : imageUrl;
        var extension = Path.GetExtension(uriPath);
        return string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension;
    }

    private static List<string> ExtractEventIds(string url) =>
        Regex.Matches(url, @"/events/(?:[^/?#]+/)*(\d{8,})|facebook\.com/events/(?:[^/?#]+/)*(\d{8,})|[?&]eid=(\d{8,})|(?<!\d)(\d{10,})(?!\d)", RegexOptions.IgnoreCase)
            .Select(match => match.Groups.Cast<Group>().Skip(1).FirstOrDefault(group => group.Success)?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct()
            .ToList()!;

    private static string NormalizeInputUrl(string value)
    {
        value = value.Trim().Trim('<', '>', '"', '\'');

        if (!Regex.IsMatch(value, @"^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase) &&
            (value.Contains("facebook.com", StringComparison.OrdinalIgnoreCase) ||
             value.Contains("fb.me", StringComparison.OrdinalIgnoreCase)))
        {
            value = $"https://{value.TrimStart('/')}";
        }

        return value;
    }

    private static Uri? UnwrapFacebookRedirect(Uri uri)
    {
        if (!uri.Host.Contains("facebook.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!uri.AbsolutePath.Equals("/l.php", StringComparison.OrdinalIgnoreCase) &&
            !uri.AbsolutePath.Equals("/l", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || !pair[0].Equals("u", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var decoded = WebUtility.UrlDecode(pair[1]);
            if (Uri.TryCreate(decoded, UriKind.Absolute, out var unwrapped))
            {
                return unwrapped;
            }
        }

        return null;
    }

    private static string GetString(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? "";
        }

        return "";
    }

    private static string CleanFacebookTitle(string? value)
    {
        value = Decode(value ?? "");
        value = Regex.Replace(value, @"\s*\|\s*Facebook\s*$", "", RegexOptions.IgnoreCase).Trim();
        return IsMeaningfulFacebookTitle(value) ? value : "";
    }

    private static bool IsMeaningfulFacebookTitle(string? value)
    {
        value = Decode(value ?? "");
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = Regex.Replace(value, @"\s+", " ").Trim().ToLowerInvariant();
        var genericTitles = new[]
        {
            "facebook",
            "log in or sign up to view",
            "log into facebook",
            "facebook - log in or sign up",
            "connectez-vous ou inscrivez-vous pour voir",
            "connectez-vous à facebook",
            "se connecter à facebook"
        };

        return !genericTitles.Any(title => normalized.Contains(title, StringComparison.OrdinalIgnoreCase));
    }

    private static string Decode(string value) =>
        WebUtility.HtmlDecode(value).Replace("\\u0025", "%").Trim();

    private static string RemoveDiacritics(string value) =>
        new(value.Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray());

    private static string NormalizeVisibleText(string value) =>
        Regex.Replace(RemoveDiacritics(value), @"[\s\u00a0]+", " ")
            .Replace("À", "a", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToLowerInvariant();

    private static List<string> SplitVisibleLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Replace(line, @"[\s\u00a0]+", " ").Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

    private static string DecodeJsonValue(string value)
    {
        value = value.Replace("\\/", "/");

        try
        {
            var decoded = JsonSerializer.Deserialize<string>($"\"{value}\"");
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                value = decoded;
            }
        }
        catch
        {
            // Keep the original value if it is not a valid JSON string fragment.
        }

        return Decode(value);
    }

    private static string NormalizeGraphVersion(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "v25.0" : value.Trim();
        return value.StartsWith('v') ? value : $"v{value}";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

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

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}

public sealed class FacebookImportResult
{
    public string SourceUrl { get; init; } = "";
    public string ImportMode { get; set; } = "";
    public string RemoteImageUrl { get; set; } = "";
    public EventItem Item { get; init; } = new();
    public List<string> Warnings { get; } = [];

    public bool HasEssentialData =>
        !string.IsNullOrWhiteSpace(Item.Title) &&
        !string.IsNullOrWhiteSpace(Item.Date) &&
        Item.Dance.Count > 0;

    public static FacebookImportResult Failed(string warning)
    {
        var result = new FacebookImportResult
        {
            ImportMode = "Fallback manuel"
        };
        result.Warnings.Add(warning);
        return result;
    }
}
