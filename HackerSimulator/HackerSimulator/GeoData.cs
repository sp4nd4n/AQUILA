using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HackerSimulator;

public sealed class City
{
    public string Name { get; }
    public string Code { get; }
    public double Lat { get; }
    public double Lon { get; }
    public string[] Aliases { get; }

    /// <summary>True when the position came from real coordinates or a geocoded address (no random jitter).</summary>
    public bool Exact { get; init; }

    public IEnumerable<string> AllNames => Aliases.Prepend(Name);

    public City(string name, string code, double lat, double lon, params string[] aliases)
    {
        Name = name;
        Code = code;
        Lat = lat;
        Lon = lon;
        Aliases = aliases;
    }
}

public static class GeoData
{
    public const int Cols = 120;   // 3 degrees per column, lon -180..180
    public const int Rows = 48;    // 3 degrees per row, lat 84..-60

    // proxy hop cities (also the last-resort fallback target pool)
    public static readonly City[] Hubs =
    {
        new("London", "LHR", 51.51, -0.13),
        new("Frankfurt", "FRA", 50.11, 8.68),
        new("Amsterdam", "AMS", 52.37, 4.90),
        new("Stockholm", "ARN", 59.33, 18.07),
        new("Reykjavik", "KEF", 64.15, -21.94),
        new("Moscow", "SVO", 55.76, 37.62),
        new("Istanbul", "IST", 41.01, 28.98),
        new("Dubai", "DXB", 25.20, 55.27),
        new("Cairo", "CAI", 30.04, 31.24),
        new("Lagos", "LOS", 6.52, 3.38),
        new("Nairobi", "NBO", -1.29, 36.82),
        new("Johannesburg", "JNB", -26.20, 28.05),
        new("Singapore", "SIN", 1.35, 103.82),
        new("Hong Kong", "HKG", 22.32, 114.17),
        new("Tokyo", "NRT", 35.68, 139.69),
        new("Seoul", "ICN", 37.57, 126.98),
        new("Sydney", "SYD", -33.87, 151.21),
        new("New York", "JFK", 40.71, -74.01),
        new("Toronto", "YYZ", 43.65, -79.38),
        new("Los Angeles", "LAX", 34.05, -118.24),
        new("San Francisco", "SFO", 37.77, -122.42),
        new("Mexico City", "MEX", 19.43, -99.13),
        new("Sao Paulo", "GRU", -23.55, -46.63),
        new("Buenos Aires", "EZE", -34.60, -58.38),
        new("Bogota", "BOG", 4.71, -74.07),
        new("Zurich", "ZRH", 47.38, 8.54),
        new("Paris", "CDG", 48.86, 2.35),
        new("Madrid", "MAD", 40.42, -3.70),
        new("Helsinki", "HEL", 60.17, 24.94),
        new("Bucharest", "OTP", 44.43, 26.10),
        new("Beijing", "PEK", 39.90, 116.41),
        new("Shanghai", "PVG", 31.23, 121.47),
        new("Bangkok", "BKK", 13.76, 100.50),
        new("Jakarta", "CGK", -6.21, 106.85),
        new("Auckland", "AKL", -36.85, 174.76),
        new("Santiago", "SCL", -33.45, -70.67),
        new("Anchorage", "ANC", 61.22, -149.90),
        new("Honolulu", "HNL", 21.31, -157.86),
        new("Perth", "PER", -31.95, 115.86),
        new("Lima", "LIM", -12.05, -77.04),
        new("Chicago", "ORD", 41.88, -87.63),
        new("Seattle", "SEA", 47.61, -122.33),
    };

    // extra cities that can be matched from the profile text when the online lookup isn't available
    public static readonly City[] Regional =
    {
        new("Kolkata", "CCU", 22.57, 88.36, "calcutta"),
        new("Delhi", "DEL", 28.61, 77.21, "new delhi"),
        new("Mumbai", "BOM", 19.08, 72.88, "bombay"),
        new("Bengaluru", "BLR", 12.97, 77.59, "bangalore"),
        new("Chennai", "MAA", 13.08, 80.27, "madras"),
        new("Hyderabad", "HYD", 17.39, 78.49),
        new("Pune", "PNQ", 18.52, 73.86),
        new("Ahmedabad", "AMD", 23.02, 72.57),
        new("Surat", "STV", 21.17, 72.83),
        new("Jaipur", "JAI", 26.91, 75.79),
        new("Lucknow", "LKO", 26.85, 80.95),
        new("Kanpur", "KNU", 26.45, 80.33),
        new("Varanasi", "VNS", 25.32, 82.97),
        new("Patna", "PAT", 25.59, 85.14),
        new("Ranchi", "IXR", 23.34, 85.31),
        new("Guwahati", "GAU", 26.14, 91.74),
        new("Bhubaneswar", "BBI", 20.30, 85.82),
        new("Siliguri", "IXB", 26.72, 88.43),
        new("Durgapur", "RDP", 23.52, 87.31),
        new("Howrah", "HWH", 22.59, 88.31),
        new("Darjeeling", "DJ", 27.04, 88.26),
        new("Chandigarh", "IXC", 30.73, 76.78),
        new("Indore", "IDR", 22.72, 75.86),
        new("Bhopal", "BHO", 23.26, 77.41),
        new("Nagpur", "NAG", 21.15, 79.09),
        new("Kochi", "COK", 9.93, 76.27, "cochin"),
        new("Thiruvananthapuram", "TRV", 8.52, 76.94, "trivandrum"),
        new("Coimbatore", "CJB", 11.02, 76.96),
        new("Visakhapatnam", "VTZ", 17.69, 83.22, "vizag"),
        new("Dhaka", "DAC", 23.81, 90.41),
        new("Kathmandu", "KTM", 27.72, 85.32),
        new("Colombo", "CMB", 6.93, 79.85),
        new("Karachi", "KHI", 24.86, 67.01),
    };

    public static readonly City[] All = Hubs.Concat(Regional).ToArray();

    // ---------- target resolution ----------

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GhostlinkPrankApp/1.0 (personal hobby project)");
        return client;
    }

    /// <summary>
    /// Where the final tracking ping lands. Priority:
    /// 1) a custom field like  "Coords: 22.57, 88.36"
    /// 2) the Address, looked up online (OpenStreetMap), trying less specific versions if needed
    /// 3) a known city name in Address / Workplace / custom fields (works offline)
    /// 4) a random hub city
    /// </summary>
    public static async Task<City> ResolveTargetAsync(Profile p, Random rng)
    {
        var coords = TryCoords(p);
        if (coords != null) return coords;

        var online = await GeocodeAsync(p.Address);
        if (online != null) return online;

        return FindTarget(p, rng);
    }

    private static async Task<City?> GeocodeAsync(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;

        string[] parts = address.Split(new[] { ',', ';', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(14));
        try
        {
            // "a, b, c" -> "a, b, c" -> "b, c" -> "c"  (most specific first, then nearer and nearer)
            for (int skip = 0; skip < parts.Length; skip++)
            {
                string query = string.Join(", ", parts.Skip(skip));
                var hit = await QueryAsync(query, cts.Token);
                if (hit != null) return hit;

                if (skip < parts.Length - 1)
                    await Task.Delay(1100, cts.Token);   // the free geocoder allows ~1 request per second
            }
        }
        catch
        {
            // offline, blocked, timed out... fall back to the built-in city list
        }
        return null;
    }

    private static async Task<City?> QueryAsync(string query, CancellationToken ct)
    {
        string url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q=" + Uri.EscapeDataString(query);
        string json = await Http.GetStringAsync(url, ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;

        var item = root[0];
        if (!double.TryParse(item.GetProperty("lat").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) ||
            !double.TryParse(item.GetProperty("lon").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
            return null;

        string display = item.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? "" : "";
        string label = string.Join(", ",
            display.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(3));
        if (label.Length > 40) label = label[..40] + "…";

        return new City(label, "TGT", lat, lon) { Exact = true };
    }

    private static City? TryCoords(Profile p)
    {
        foreach (string raw in p.Extra.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int idx = raw.IndexOf(':');
            if (idx <= 0) continue;
            string label = raw[..idx].Trim().ToLowerInvariant();
            if (label is not ("coords" or "coordinates" or "map" or "gps")) continue;

            var m = Regex.Match(raw[(idx + 1)..], @"(-?\d+(?:\.\d+)?)\s*[,;\s]\s*(-?\d+(?:\.\d+)?)");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
            {
                return new City("", "TGT", lat, lon) { Exact = true };
            }
        }
        return null;
    }

    /// <summary>Offline fallback: custom coordinates, then a known city name in the text, then a random hub.</summary>
    public static City FindTarget(Profile p, Random rng)
    {
        var coords = TryCoords(p);
        if (coords != null) return coords;

        string text = $"{p.Address} {p.Workplace} {p.Extra}";
        City? best = null;
        int bestIdx = int.MaxValue;
        foreach (var c in All)
        {
            foreach (string n in c.AllNames)
            {
                int i = text.IndexOf(n, StringComparison.OrdinalIgnoreCase);
                if (i >= 0 && i < bestIdx) { best = c; bestIdx = i; }
            }
        }
        return best ?? Hubs[rng.Next(Hubs.Length)];
    }

    // '#' = land. Generated from Natural Earth 110m coastlines.
    public static readonly string[] MapRows =
    {
        "..............................##########################................................................................",
        ".........................#.###########################..........####........#..............####.........................",
        "....................####..#.######...#################...........#.............##.#.......########........##............",
        "..................#####.#.###.####.......############.........................#....#...################...##............",
        "......#######..#####.#####.##########.....###########.............#####.........#.##################################.##.",
        "###..############################..####...########..............##########.#############################################",
        ".##.#############################.#####...#####.....###........#########################################################",
        ".....########################.....###......###................####.####################################################.",
        "......####....###############.....#####...................#...####..#########################################....#......",
        "................################..#######................###...###.#######################################......##......",
        ".................#########################...............#.#################################################....#.......",
        "..................########################................##################################################............",
        "..................######################...................###############################################.#............",
        "..................###################....................#############...###.############################..#............",
        "..................##################.....................####...#############.#########################.................",
        "...................################......................###.##....#.################################.#...#.............",
        "....................###############......................#######........############################..#####.............",
        ".....................############........................############################################..#................",
        "......................######....#.......................####################.########################...................",
        "......................#.####.....#.....................######################.######################....................",
        "........................####.....#....................##################.#######...################.....................",
        ".........................###.##....##.................##########################....#####..######.......................",
        "...........................#####......................###################.####......####...#####....#...................",
        "..............................##......................######################.........##.....#####...#...................",
        "...............................##..#####...............######################........##.......##........................",
        "................................#.#######...............#####################.........#......#.......#..................",
        "..................................#########..............###..##############................###...##....................",
        ".................................##########....................############..................##.#####...................",
        ".................................############..................###########...................##.#####...#...............",
        ".................................###############................#########.....................##....#...#####...........",
        ".................................################...............#########......................##.........###...........",
        "..................................##############................##########..............................................",
        "..................................#############.................##########..#..........................###.#............",
        "...................................############.................#########..##........................#####.##...........",
        "....................................###########.................########..###.......................#########...........",
        "....................................##########..................########..##......................############..........",
        "....................................########.....................######....#......................#############.........",
        "....................................########.....................######...........................#############.........",
        "....................................#######.......................####............................#############.........",
        "....................................######........................###.............................###....#####..........",
        "...................................######..................................................................###........#.",
        "...................................####...............................................................................#.",
        "...................................####.....................................................................#.......##..",
        "...................................###..............................................................................#...",
        "...................................###..................................................................................",
        "...................................##...................................................................................",
        "....................................##..................................................................................",
        "........................................................................................................................",
    };
}