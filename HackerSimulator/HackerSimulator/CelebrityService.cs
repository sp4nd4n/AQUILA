using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HackerSimulator;

/// <summary>
/// Looks up well-known, living public figures on Wikidata (public data) and turns them into a Profile.
/// Only exact label/alias matches of humans with many Wikipedia language editions are accepted.
/// </summary>
public static class CelebrityService
{
    private const string Api = "https://www.wikidata.org/w/api.php";
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GhostlinkPrankApp/1.0 (personal hobby project)");
        return client;
    }

    public static async Task<Profile?> LookupAsync(string name, CancellationToken ct)
    {
        try
        {
            string query = name.Trim();
            if (query.Length < 3) return null;

            // 1) candidates whose label or alias is EXACTLY what was typed
            string searchUrl = Api + "?action=wbsearchentities&format=json&language=en&uselang=en&type=item&limit=10&search="
                               + Uri.EscapeDataString(query);
            using var searchDoc = JsonDocument.Parse(await Http.GetStringAsync(searchUrl, ct));

            string wanted = Normalize(query);
            var ids = new List<string>();
            if (searchDoc.RootElement.TryGetProperty("search", out var results))
            {
                foreach (var r in results.EnumerateArray())
                {
                    string? id = r.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    string? matched = r.TryGetProperty("match", out var mEl) && mEl.TryGetProperty("text", out var tEl)
                        ? tEl.GetString()
                        : null;
                    if (id != null && Normalize(matched) == wanted) ids.Add(id);
                }
            }
            if (ids.Count == 0) return null;

            // 2) keep living humans with lots of Wikipedia editions (= actually famous)
            string props = Uri.EscapeDataString("claims|sitelinks|labels|descriptions");
            string entUrl = Api + "?action=wbgetentities&format=json&languages=en&props=" + props
                            + "&ids=" + Uri.EscapeDataString(string.Join("|", ids));
            using var entDoc = JsonDocument.Parse(await Http.GetStringAsync(entUrl, ct));
            if (!entDoc.RootElement.TryGetProperty("entities", out var entities)) return null;

            JsonElement best = default;
            int bestLinks = 0;
            bool found = false;
            foreach (var prop in entities.EnumerateObject())
            {
                var ent = prop.Value;
                if (!IsHuman(ent) || Values(ent, "P570").Any()) continue;   // humans only, alive only
                int links = ent.TryGetProperty("sitelinks", out var sl) ? sl.EnumerateObject().Count() : 0;
                if (links >= 12 && links > bestLinks)
                {
                    best = ent;
                    bestLinks = links;
                    found = true;
                }
            }
            if (!found) return null;

            string qid = best.TryGetProperty("id", out var qEl) ? qEl.GetString() ?? "x" : "x";
            string label = Label(best) ?? query;
            string description = best.TryGetProperty("descriptions", out var ds) &&
                                 ds.TryGetProperty("en", out var de) &&
                                 de.TryGetProperty("value", out var dv)
                ? dv.GetString() ?? ""
                : "";

            // 3) resolve readable names for birthplace / residence / country / occupations
            string? birthPlaceId = Ids(best, "P19").FirstOrDefault();
            string? residenceId = Ids(best, "P551").FirstOrDefault();
            string? countryId = Ids(best, "P27").FirstOrDefault();
            var occupationIds = Ids(best, "P106").Take(3).ToList();

            var need = new List<string>();
            if (birthPlaceId != null) need.Add(birthPlaceId);
            if (residenceId != null) need.Add(residenceId);
            if (countryId != null) need.Add(countryId);
            need.AddRange(occupationIds);
            var labels = await LabelsAsync(need, ct);

            string Name(string? id) => id != null && labels.TryGetValue(id, out var l) ? l : "";

            string place = Name(residenceId).Length > 0 ? Name(residenceId) : Name(birthPlaceId);
            string country = Name(countryId);
            string address = place;
            if (country.Length > 0 && !place.Contains(country, StringComparison.OrdinalIgnoreCase))
                address = place.Length > 0 ? place + ", " + country : country;

            string? Str(string prop) => Values(best, prop)
                .Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : null)
                .FirstOrDefault(s => !string.IsNullOrEmpty(s));

            string? instagram = Str("P2003");
            string? twitter = Str("P2002");
            string? facebook = Str("P2013");
            string? youtube = Str("P2397");
            string? website = Str("P856");

            // 4) photo from Wikimedia Commons (freely licensed images)
            string photo = "";
            string? image = Str("P18");
            if (image != null)
            {
                try
                {
                    string imgUrl = "https://commons.wikimedia.org/wiki/Special:FilePath/" +
                                    Uri.EscapeDataString(image) + "?width=480";
                    byte[] bytes = await Http.GetByteArrayAsync(imgUrl, ct);
                    if (bytes.Length > 0 && bytes.Length < 6_000_000)
                    {
                        photo = "celeb_" + qid + ".jpg";
                        string path = ProfileStore.PhotoPath(photo);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await File.WriteAllBytesAsync(path, bytes, ct);
                    }
                }
                catch { photo = ""; }
            }

            var extra = new List<string>();
            string occupations = string.Join(", ", occupationIds.Select(Name).Where(s => s.Length > 0));
            if (occupations.Length > 0) extra.Add("Occupation: " + occupations);
            if (country.Length > 0) extra.Add("Nationality: " + country);
            if (website != null) extra.Add("Website: " + website);
            if (youtube != null) extra.Add("YouTube: youtube.com/channel/" + youtube);

            return new Profile
            {
                Name = label,
                Alias = description,
                Birthday = FormatBirth(best),
                Address = address,
                Instagram = instagram != null ? "@" + instagram : "",
                Twitter = twitter != null ? "@" + twitter : "",
                Facebook = facebook != null ? "facebook.com/" + facebook : "",
                Photo = photo,
                Extra = string.Join("\n", extra)
            };
        }
        catch
        {
            return null;   // offline, blocked, timed out...
        }
    }

    // ---------- helpers ----------

    private static bool IsHuman(JsonElement ent) =>
        Ids(ent, "P31").Any(id => id == "Q5");

    private static IEnumerable<JsonElement> Values(JsonElement ent, string prop)
    {
        if (!ent.TryGetProperty("claims", out var claims) || !claims.TryGetProperty(prop, out var arr))
            yield break;

        foreach (var claim in arr.EnumerateArray())
        {
            if (claim.TryGetProperty("mainsnak", out var snak) &&
                snak.TryGetProperty("datavalue", out var dv) &&
                dv.TryGetProperty("value", out var value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> Ids(JsonElement ent, string prop)
    {
        foreach (var v in Values(ent, prop))
        {
            if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("id", out var id) && id.GetString() is string s)
                yield return s;
        }
    }

    private static string? Label(JsonElement ent) =>
        ent.TryGetProperty("labels", out var l) &&
        l.TryGetProperty("en", out var en) &&
        en.TryGetProperty("value", out var v)
            ? v.GetString()
            : null;

    private static async Task<Dictionary<string, string>> LabelsAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var result = new Dictionary<string, string>();
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return result;

        string url = Api + "?action=wbgetentities&format=json&props=labels&languages=en&ids="
                     + Uri.EscapeDataString(string.Join("|", list));
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
        if (doc.RootElement.TryGetProperty("entities", out var ents))
        {
            foreach (var p in ents.EnumerateObject())
                if (Label(p.Value) is string l) result[p.Name] = l;
        }
        return result;
    }

    private static string FormatBirth(JsonElement ent)
    {
        string? time = Values(ent, "P569")
            .Select(v => v.ValueKind == JsonValueKind.Object && v.TryGetProperty("time", out var tm) ? tm.GetString() : null)
            .FirstOrDefault(s => s != null);
        if (time == null) return "";

        string raw = time.TrimStart('+');
        string date = raw.Length >= 10 ? raw[..10] : raw;
        if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            int age = DateTime.Today.Year - dt.Year;
            if (dt.Date > DateTime.Today.AddYears(-age)) age--;
            return $"{dt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)} (age {age})";
        }
        return date.Length >= 4 ? date[..4] : date;   // year-only precision
    }

    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder();
        foreach (char c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}