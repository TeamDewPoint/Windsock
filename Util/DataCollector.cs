using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;
using ImageMagick;

namespace Windsock
{
    public static class DataCollector
    {
        private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        public static async Task<Dictionary<string, TWC_DailyForecast>> CollectDailyForecasts(List<LFRecordLocation> locations, List<string> coopIds, ApiConfig api)
        {
            string? apiKey = api.TwcForecastsKey;
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "your_api_key_here")
            {
                throw new InvalidOperationException("You didn't set the TWC Forecasts API key.");
            }

            Dictionary<string, TWC_DailyForecast> forecasts = new Dictionary<string, TWC_DailyForecast>();
            foreach (string coopId in coopIds.Distinct())
            {
                LFRecordLocation? location = locations.Find(item => item.CoopID == coopId);
                if (location == null || !double.TryParse(location.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude) || !double.TryParse(location.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
                {
                    Console.WriteLine("No valid coordinates found for forecast location: " + coopId);
                    continue;
                }

                string url = "https://api.weather.com/v1/geocode/" + latitude.ToString(CultureInfo.InvariantCulture) + "/" + longitude.ToString(CultureInfo.InvariantCulture) + "/forecast/daily/10day.json?units=e&language=en-US&apiKey=" + Uri.EscapeDataString(apiKey);

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Daily forecast failed for " + coopId + ": HTTP " + (int)response.StatusCode);
                        continue;
                    }

                    using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (!json.RootElement.TryGetProperty("forecasts", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
                    {
                        throw new JsonException("The response does not contain daily forecasts.");
                    }

                    TWC_DailyForecast forecast = json.RootElement.Deserialize<TWC_DailyForecast>() ?? throw new JsonException("The daily forecast response is empty.");
                    if (forecast.Forecasts?.Count > 0)
                    {
                        // Remove the "F" after degrees in the temperature narratives
                        string tempNarrativePattern = @"(?<=\d)F\b";
                        foreach (var dailyForecast in forecast.Forecasts)
                        {
                            if (!string.IsNullOrWhiteSpace(dailyForecast.Day?.Narrative))
                            {
                                dailyForecast.Day.Narrative = Regex.Replace(dailyForecast.Day.Narrative, tempNarrativePattern, "");
                            }
                            if (!string.IsNullOrWhiteSpace(dailyForecast.Night?.Narrative))
                            {
                                dailyForecast.Night.Narrative = Regex.Replace(dailyForecast.Night.Narrative, tempNarrativePattern, "");
                            }
                        }

                        string DAVocalKeyPattern = @"(?:^DA\d+:?|:DA\d+(?=:|$))";
                        foreach (var dailyForecast in forecast.Forecasts)
                        {
                            if (!string.IsNullOrWhiteSpace(dailyForecast.Day?.VocalKey))
                            {
                                dailyForecast.Day.VocalKey = Regex.Replace(dailyForecast.Day.VocalKey, DAVocalKeyPattern, "");
                            }
                            if (!string.IsNullOrWhiteSpace(dailyForecast.Night?.VocalKey))
                            {
                                dailyForecast.Night.VocalKey = Regex.Replace(dailyForecast.Night.VocalKey, DAVocalKeyPattern, "");
                            }
                        }

                        forecasts[coopId] = forecast;
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException)
                {
                    Console.WriteLine("Could not collect daily forecast for " + coopId + " (" + exception.GetType().Name + ").");
                }
            }

            return forecasts;
        }

        public static async Task<Dictionary<string, TWC_HourlyForecast>> CollectHourlyForecasts(List<LFRecordLocation> locations, List<string> coopIds, ApiConfig api)
        {
            string? apiKey = api.TwcForecastsKey;
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "your_api_key_here")
            {
                throw new InvalidOperationException("You didn't set the TWC Forecasts API key.");
            }

            Dictionary<string, TWC_HourlyForecast> forecasts = new Dictionary<string, TWC_HourlyForecast>();
            foreach (string coopId in coopIds.Distinct())
            {
                LFRecordLocation? location = locations.Find(item => item.CoopID == coopId);
                if (location == null || !double.TryParse(location.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude) || !double.TryParse(location.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
                {
                    Console.WriteLine("No valid coordinates found for forecast location: " + coopId);
                    continue;
                }

                string url = "https://api.weather.com/v1/geocode/" + latitude.ToString(CultureInfo.InvariantCulture) + "/" + longitude.ToString(CultureInfo.InvariantCulture) + "/forecast/hourly/48hour.json?units=e&language=en-US&apiKey=" + Uri.EscapeDataString(apiKey);

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Hourly forecast failed for " + coopId + ": HTTP " + (int)response.StatusCode);
                        continue;
                    }

                    using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (!json.RootElement.TryGetProperty("forecasts", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
                    {
                        throw new JsonException("The response does not contain hourly forecasts.");
                    }

                    TWC_HourlyForecast forecast = json.RootElement.Deserialize<TWC_HourlyForecast>() ?? throw new JsonException("The hourly forecast response is empty.");
                    if (forecast.Forecasts?.Count > 0)
                    {
                        forecasts[coopId] = forecast;
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException)
                {
                    Console.WriteLine("Could not collect hourly forecast for " + coopId + " (" + exception.GetType().Name + ").");
                }
            }

            return forecasts;
        }

        public static async Task<Dictionary<string, TWC_CurrentObservation>> CollectCurrentConditions(List<LFRecordLocation> locations, List<string> stationIds, ApiConfig api)
        {
            string? apiKey = api.TwcForecastsKey;

            if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "your_api_key_here")
            {
                throw new InvalidOperationException("You didn't set the TWC Forecasts API key.");
            }

            Dictionary<string, TWC_CurrentObservation> observations = new Dictionary<string, TWC_CurrentObservation>();

            foreach (string stationId in stationIds.Distinct())
            {
                LFRecordLocation? location = locations.Find(location => location.ObservationStation == stationId || (location.CoopID != null && "T" + location.CoopID == stationId));
                if (location == null || string.IsNullOrWhiteSpace(location.Latitude) || string.IsNullOrWhiteSpace(location.Longitude))
                {
                    Console.WriteLine("No coordinates found for observation station: " + stationId);
                    continue;
                }

                string geocode = Uri.EscapeDataString(location.Latitude + "," + location.Longitude);
                string url = "https://api.weather.com/v3/wx/observations/current?geocode=" + geocode + "&units=e&language=en-US&format=json&apiKey=" + Uri.EscapeDataString(apiKey);

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Current conditions failed for " + stationId + ": HTTP " + (int)response.StatusCode);
                        continue;
                    }

                    using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (!json.RootElement.TryGetProperty("temperature", out _) || !json.RootElement.TryGetProperty("validTimeUtc", out _))
                    {
                        throw new JsonException("The response does not contain current conditions? How did that happen?");
                    }

                    TWC_CurrentObservation observation = json.RootElement.Deserialize<TWC_CurrentObservation>() ?? throw new JsonException("The current conditions response is empty.");
                    observations[stationId] = observation;
                }
                catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException)
                {
                    Console.WriteLine("Could not collect current conditions for " + stationId + " (" + exception.GetType().Name + ").");
                }
            }

            return observations;
        }

        public static async Task<Dictionary<string, List<TWC_Alert>>> CollectHeadlines(List<string> areaIds, ApiConfig api, string product = "alerts")
        {
            string? apiKey = api.TwcForecastsKey;
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "your_api_key_here")
            {
                throw new InvalidOperationException("You didn't set the TWC Forecasts API key.");
            }

            List<string> areas = areaIds.Where(area => Regex.IsMatch(area, @"^[A-Z]{2}[CZ]\d{3}$")).Distinct().ToList();
            List<string> states = product == "alerts" ? areas.Where(area => area[2] == 'C').Select(area => area.Substring(0, 2)).Distinct().ToList() : new List<string>();
            Dictionary<string, List<TWC_Alert>> headlines = new Dictionary<string, List<TWC_Alert>>();
            foreach (string query in areas.Concat(states))
            {
                headlines[query] = new List<TWC_Alert>();
                long? next = null;
                HashSet<long> cursors = new HashSet<long>();
                try
                {
                    for (int page = 0; page < 100; page++)
                    {
                        string parameter = query.Length == 2 ? "adminDistrictCode" : "areaId";
                        string url = "https://api.weather.com/v3/" + product + "/headlines?" + parameter + "=" + Uri.EscapeDataString(query + ":US") + "&language=en-US&format=json&apiKey=" + Uri.EscapeDataString(apiKey);
                        if (next.HasValue)
                        {
                            url += "&next=" + next.Value.ToString(CultureInfo.InvariantCulture);
                        }

                        using HttpResponseMessage response = await client.GetAsync(url);
                        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                        {
                            break;
                        }
                        if (!response.IsSuccessStatusCode)
                        {
                            Console.WriteLine(product + " headlines failed for " + query + ": HTTP " + (int)response.StatusCode);
                            break;
                        }

                        TWC_AlertHeadlines result = JsonSerializer.Deserialize<TWC_AlertHeadlines>(await response.Content.ReadAsStringAsync()) ?? throw new JsonException("The headlines response is empty.");
                        if (result.Alerts == null)
                        {
                            throw new JsonException("The response does not contain alerts.");
                        }
                        foreach (TWC_Alert alert in result.Alerts)
                        {
                            if (query.Length == 2 || alert.AreaId == query)
                            {
                                alert.Product = product;
                                headlines[query].Add(alert);
                            }
                        }
                        next = result.Metadata?.Next;
                        if (!next.HasValue)
                        {
                            break;
                        }
                        if (!cursors.Add(next.Value) || page == 99 || headlines[query].Count > 2000)
                        {
                            throw new JsonException("The headlines pagination did not finish.");
                        }
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException)
                {
                    Console.WriteLine("Could not collect " + product + " headlines for " + query + " (" + exception.GetType().Name + ").");
                }
            }

            Dictionary<string, List<string>> countiesByZone = new Dictionary<string, List<string>>();
            foreach (string county in areas.Where(area => area[2] == 'C'))
            {
                string state = county.Substring(0, 2);
                if (!states.Contains(state))
                {
                    continue;
                }
                foreach (TWC_Alert alert in headlines[state])
                {
                    string zone = alert.AreaId ?? "";
                    if (!Regex.IsMatch(zone, "^" + state + @"Z\d{3}$"))
                    {
                        continue;
                    }
                    if (!countiesByZone.TryGetValue(zone, out List<string>? counties))
                    {
                        try
                        {
                            string url = "https://api.weather.com/v3/location/boundary?zoneId=" + Uri.EscapeDataString(zone) + "&product=county&format=json&apiKey=" + Uri.EscapeDataString(apiKey);
                            using HttpResponseMessage response = await client.GetAsync(url);
                            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                            {
                                counties = new List<string>();
                            }
                            else
                            {
                                response.EnsureSuccessStatusCode();
                                using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                                counties = new List<string>();
                                foreach (JsonElement feature in json.RootElement.GetProperty("features").EnumerateArray())
                                {
                                    JsonElement properties = feature.GetProperty("properties");
                                    if (properties.GetProperty("product").GetString() == "county" && properties.TryGetProperty("key", out JsonElement key) && key.GetString() is string countyId)
                                    {
                                        counties.Add(countyId);
                                    }
                                }
                            }
                            countiesByZone[zone] = counties;
                        }
                        catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException || exception is KeyNotFoundException)
                        {
                            Console.WriteLine("Could not map alert zone " + zone + " to counties (" + exception.GetType().Name + ").");
                            continue;
                        }
                    }
                    if (counties.Contains(county))
                    {
                        headlines[county].Add(alert);
                    }
                }
            }

            foreach (string state in states)
            {
                headlines.Remove(state);
            }
            foreach (string area in areas)
            {
                headlines[area] = headlines[area].DistinctBy(alert => (alert.DetailKey, alert.AreaId))
                    .OrderBy(alert => alert.SeverityCode is > 0 ? alert.SeverityCode.Value : 99)
                    .ThenBy(alert => alert.Significance switch { "W" => 0, "A" => 1, "Y" => 2, "S" => 3, _ => 4 })
                    .ThenByDescending(alert => alert.ProcessTimeUTC > 100000000000 ? alert.ProcessTimeUTC / 1000 : alert.ProcessTimeUTC)
                    .ThenBy(alert => alert.DetailKey, StringComparer.Ordinal).Take(20).ToList();
            }
            return headlines;
        }

        public static async Task<Dictionary<string, List<TWC_Alert>>> CollectBulletins(List<string> areaIds, List<string> coastalAreaIds, ApiConfig api)
        {
            Dictionary<string, List<TWC_Alert>> bulletins = await CollectHeadlines(areaIds, api);
            Dictionary<string, List<TWC_Alert>> coastalBulletins = await CollectHeadlines(coastalAreaIds, api, "bulletins");
            foreach (KeyValuePair<string, List<TWC_Alert>> area in coastalBulletins)
            {
                if (!bulletins.ContainsKey(area.Key))
                {
                    bulletins[area.Key] = new List<TWC_Alert>();
                }
                bulletins[area.Key].AddRange(area.Value);
            }

            Dictionary<(string Product, string Key), TWC_Alert> details = new Dictionary<(string Product, string Key), TWC_Alert>();
            foreach (List<TWC_Alert> alerts in bulletins.Values)
            {
                foreach (TWC_Alert alert in alerts)
                {
                    if (string.IsNullOrWhiteSpace(alert.DetailKey))
                    {
                        continue;
                    }
                    if (!details.TryGetValue((alert.Product, alert.DetailKey), out TWC_Alert? detail))
                    {
                        try
                        {
                            string url = "https://api.weather.com/v3/" + alert.Product + "/detail?alertId=" + Uri.EscapeDataString(alert.DetailKey) + "&language=en-US&format=json&apiKey=" + Uri.EscapeDataString(api.TwcForecastsKey);
                            using HttpResponseMessage response = await client.GetAsync(url);
                            if (!response.IsSuccessStatusCode)
                            {
                                Console.WriteLine("Bulletin detail failed for " + alert.AreaId + ": HTTP " + (int)response.StatusCode);
                                continue;
                            }
                            using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                            JsonElement entry = json.RootElement.TryGetProperty("alertDetail", out JsonElement wrapped) ? wrapped : json.RootElement;
                            detail = entry.Deserialize<TWC_Alert>() ?? throw new JsonException("The bulletin detail response is empty.");
                            details[(alert.Product, alert.DetailKey)] = detail;
                        }
                        catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException)
                        {
                            Console.WriteLine("Could not collect bulletin detail for " + alert.AreaId + " (" + exception.GetType().Name + ").");
                            continue;
                        }
                    }
                    alert.Detail = detail;
                }
            }
            return bulletins;
        }

        public static async Task<TWC_RadarTimeStamp> CollectRadarTimeStamps(ApiConfig api, string product = "twcRadarMosaic")
        {

            string? apiKey = api.TwcRadarKey;

            if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "your_api_key_here")
            {
                throw new InvalidOperationException("You didn't set the TWC Radar API key.");
            }

            TWC_RadarTimeStamp radarTimeStamp = new TWC_RadarTimeStamp();

            try
            {
                string url = $"https://api.weather.com/v3/TileServer/series/product/{product}?apiKey={Uri.EscapeDataString(apiKey)}";
                using HttpResponseMessage response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine(product + " time stamps collection failed: HTTP " + (int)response.StatusCode);
                    throw new HttpRequestException(product + " time stamps collection failed: HTTP " + (int)response.StatusCode);
                }

                using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (!json.RootElement.TryGetProperty("series", out _))
                {
                    throw new JsonException("The response does not contain " + product + " time stamps? Check key?");
                }

                radarTimeStamp = json.RootElement.Deserialize<TWC_RadarTimeStamp>() ?? throw new JsonException("The " + product + " time stamps response is empty.");
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException)
            {
                Console.WriteLine("Could not collect " + product + " time stamps (" + exception.GetType().Name + ").");
            }


            return radarTimeStamp;
        }

        public static async Task<List<string>> DownloadMapCutRange(I1Config config, string destinationPath, ApiConfig api, int frames = 0, long? afterTimestamp = null)
        {
            List<I1Map> cuts = new List<I1Map>();
            foreach (I1Map map in config.Maps.Values)
            {
                if (map.DatacutType != "radar.us")
                {
                    continue;
                }
                if (map.DatacutCoordinate is not { Length: >= 2 } || map.DatacutSize is not { Length: >= 2 }
                    || map.MapName != "mercator.us.bfg"
                    || map.MapcutCoordinate is not { Length: >= 2 } || map.MapcutSize is not { Length: >= 2 }
                    || map.DatacutCoordinate[0] < 0 || map.DatacutCoordinate[1] < 0
                    || map.DatacutSize[0] <= 0 || map.DatacutSize[1] <= 0
                    || map.MapcutSize[0] <= 0 || map.MapcutSize[1] <= 0)
                {
                    throw new InvalidDataException("Invalid radar cut: " + map.Name);
                }
                cuts.Add(map);
            }
            if (cuts.Count == 0)
            {
                throw new InvalidDataException("The i1 config does not contain any radar.us data cuts.");
            }

            int left = cuts.Min(cut => cut.DatacutCoordinate![0]);
            int lower = cuts.Min(cut => cut.DatacutCoordinate![1]);
            int right = cuts.Max(cut => cut.DatacutCoordinate![0] + cut.DatacutSize![0]);
            int upper = cuts.Max(cut => cut.DatacutCoordinate![1] + cut.DatacutSize![1]);
            int canvasWidth = Math.Max(3400, right + 64);
            int canvasHeight = Math.Max(1600, upper + 64);

            TWC_RadarTimeStamp radar = await CollectRadarTimeStamps(api);
            if (radar.Series.Count == 0)
            {
                throw new InvalidOperationException("No radar frames were returned.");
            }

            int frameCount = frames > 0 ? Math.Min(frames, radar.Series.Count) : radar.Series.Count;
            List<long> timestamps = radar.Series.Take(frameCount).Select(frame => frame.Timestamp).Where(timestamp => !afterTimestamp.HasValue || timestamp > afterTimestamp.Value).Distinct().OrderBy(timestamp => timestamp).ToList();
            
            if (timestamps.Count == 0)
            {
                Console.WriteLine("No new radar frames to send.");
                return new List<string>();
            }

            const int zoom = 7;

            MapCutRange range = MapUtils.ConvertMercatorRange(cuts);
            int minX = range.minX;
            int maxX = range.maxX;
            int minY = range.minY;
            int maxY = range.maxY;
            int mosaicWidth = (maxX - minX + 1) * 256;
            int mosaicHeight = (maxY - minY + 1) * 256;

            double scaleX = (right - left) / (range.Right - range.Left);
            double scaleY = (upper - lower) / (range.Bottom - range.Top);
            int imageWidth = (int)Math.Ceiling(mosaicWidth * scaleX);
            int imageHeight = (int)Math.Ceiling(mosaicHeight * scaleY);
            int imageX = (int)Math.Round(left + (minX * 256 - range.Left) * scaleX);
            // i1 cuts use a bottom-left origin; TIFF images use top-left.
            int imageY = (int)Math.Round(canvasHeight - upper + (minY * 256 - range.Top) * scaleY);

            long expiration = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 43200;
            string apiKey = Uri.EscapeDataString(api.TwcRadarKey);
            Directory.CreateDirectory(destinationPath);
            List<string> framePaths = new List<string>();

            MagickColor[] palette =
            {
                new MagickColor("#000000"), new MagickColor("#40CC55"), new MagickColor("#009900"),
                new MagickColor("#006600"), new MagickColor("#BFCC55"), new MagickColor("#BF9900"),
                new MagickColor("#FF3300"), new MagickColor("#BF3300"), new MagickColor("#800000"),
                new MagickColor("#400000")
            };

            foreach (long timestamp in timestamps)
            {
                using MagickImage mosaic = new MagickImage(MagickColors.Transparent, (uint)mosaicWidth, (uint)mosaicHeight);
                bool complete = true;
                for (int y = minY; y <= maxY && complete; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        string tileUrl = $"https://api.weather.com/v3/TileServer/tile/twcRadarMosaic?ts={timestamp}&xyz={x}:{y}:{zoom}&apiKey={apiKey}";
                        try
                        {
                            byte[] tileData = await client.GetByteArrayAsync(tileUrl);
                            using MagickImage tile = new MagickImage(tileData, MagickFormat.Png);
                            // TWC returns a 1x1 transparent PNG for an empty radar tile.
                            if (tile.Width == 1 && tile.Height == 1)
                            {
                                tile.Resize(256, 256, FilterType.Point);
                            }
                            else if (tile.Width != 256 || tile.Height != 256)
                            {
                                throw new InvalidDataException("Radar tiles must be 256 by 256 pixels.");
                            }
                            mosaic.Composite(tile, (x - minX) * 256, (y - minY) * 256, CompositeOperator.Copy);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Skipping radar frame {timestamp}: tile ({x}, {y}) failed ({ex.GetType().Name}).");
                            complete = false;
                            break;
                        }
                    }
                }
                if (!complete)
                {
                    continue;
                }

                mosaic.Resize(new MagickGeometry((uint)imageWidth, (uint)imageHeight) { IgnoreAspectRatio = true }, FilterType.Triangle);
                using IMagickImage<byte> mask = mosaic.Clone();
                mask.Alpha(AlphaOption.Extract);
                mask.Threshold(new Percentage(63 * 100.0 / 255));
                mosaic.Alpha(AlphaOption.Off);
                mosaic.Remap(palette, new QuantizeSettings { DitherMethod = DitherMethod.No });
                mosaic.Composite(mask, CompositeOperator.Multiply);

                using MagickImage canvas = new MagickImage(MagickColors.Black, (uint)canvasWidth, (uint)canvasHeight);
                canvas.Composite(mosaic, imageX, imageY, CompositeOperator.Copy);
                canvas.Alpha(AlphaOption.Off);
                canvas.ColorType = ColorType.TrueColor;
                canvas.Depth = 8;
                canvas.Settings.ColorType = ColorType.TrueColor;
                canvas.Settings.Compression = CompressionMethod.RLE;
                string framePath = Path.Combine(destinationPath, $"{timestamp}.{expiration}.tif");
                await canvas.WriteAsync(framePath, MagickFormat.Tiff);
                framePaths.Add(framePath);
                Console.WriteLine("Saved radar frame: " + Path.GetFileName(framePath));
            }

            return framePaths;
        }

        public static async Task<List<string>> DownloadSatRadMapCutRange(I1Config config, string destinationPath, ApiConfig api, int frames = 0, long? afterTimestamp = null)
        {
            List<I1Map> cuts = new List<I1Map>();
            foreach (I1Map map in config.Maps.Values)
            {
                if (map.DatacutType != "radarSatellite.us")
                {
                    continue;
                }
                if (map.DatacutCoordinate is not { Length: >= 2 } || map.DatacutSize is not { Length: >= 2 }
                    || map.MapName != "lambert.us.tif"
                    || map.MapcutCoordinate is not { Length: >= 2 } || map.MapcutSize is not { Length: >= 2 }
                    || map.DatacutCoordinate[0] < 0 || map.DatacutCoordinate[1] < 0
                    || map.DatacutSize[0] <= 0 || map.DatacutSize[1] <= 0
                    || map.MapcutSize[0] <= 0 || map.MapcutSize[1] <= 0)
                {
                    throw new InvalidDataException("Invalid satrad cut: " + map.Name);
                }
                cuts.Add(map);
            }
            if (cuts.Count == 0)
            {
                throw new InvalidDataException("The i1 config does not contain any radarSatellite.us data cuts.");
            }

            TWC_RadarTimeStamp satRad = await CollectRadarTimeStamps(api, "satrad");
            if (satRad.Series.Count == 0)
            {
                throw new InvalidOperationException("No satrad frames were returned.");
            }

            int frameCount = frames > 0 ? Math.Min(frames, satRad.Series.Count) : satRad.Series.Count;
            List<long> timestamps = satRad.Series.Take(frameCount).Select(frame => frame.Timestamp).Where(timestamp => !afterTimestamp.HasValue || timestamp > afterTimestamp.Value).Distinct().OrderBy(timestamp => timestamp).ToList();

            if (timestamps.Count == 0)
            {
                Console.WriteLine("No new satrad frames to send.");
                return new List<string>();
            }

            int left = cuts.Min(cut => cut.DatacutCoordinate![0]);
            int lower = cuts.Min(cut => cut.DatacutCoordinate![1]);
            int right = cuts.Max(cut => cut.DatacutCoordinate![0] + cut.DatacutSize![0]);
            int upper = cuts.Max(cut => cut.DatacutCoordinate![1] + cut.DatacutSize![1]);
            int canvasWidth = Math.Max(2400, right + 64);
            int canvasHeight = Math.Max(1600, upper + 64);
            int width = right - left;
            int height = upper - lower;
            const int zoom = 5;

            (double X, double Y)[] coordinates = new (double X, double Y)[width * height];
            Array.Fill(coordinates, (double.NaN, double.NaN));
            foreach (I1Map cut in cuts)
            {
                for (int y = 0; y < cut.DatacutSize![1]; y++)
                {
                    for (int x = 0; x < cut.DatacutSize![0]; x++)
                    {
                        double mapX = cut.MapcutCoordinate![0] + (x + 0.5) * cut.MapcutSize![0] / cut.DatacutSize[0];
                        double mapY = cut.MapcutCoordinate![1] + (y + 0.5) * cut.MapcutSize![1] / cut.DatacutSize[1];
                        int index = (cut.DatacutCoordinate![1] + y - lower) * width + cut.DatacutCoordinate[0] + x - left;
                        coordinates[index] = MapUtils.ConvertLambertCoordinate(mapX, mapY, zoom);
                    }
                }
            }

            int minX = (int)Math.Floor(coordinates.Where(coordinate => !double.IsNaN(coordinate.X)).Min(coordinate => coordinate.X) / 256);
            int maxX = (int)Math.Floor(coordinates.Where(coordinate => !double.IsNaN(coordinate.X)).Max(coordinate => coordinate.X) / 256);
            int minY = (int)Math.Floor(coordinates.Where(coordinate => !double.IsNaN(coordinate.Y)).Min(coordinate => coordinate.Y) / 256);
            int maxY = (int)Math.Floor(coordinates.Where(coordinate => !double.IsNaN(coordinate.Y)).Max(coordinate => coordinate.Y) / 256);
            int mosaicWidth = (maxX - minX + 1) * 256;
            int mosaicHeight = (maxY - minY + 1) * 256;

            long expiration = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 43200;
            string apiKey = Uri.EscapeDataString(api.TwcRadarKey);
            Directory.CreateDirectory(destinationPath);
            List<string> framePaths = new List<string>();

            MagickColor[] palette =
            {
                new MagickColor("#000000"), new MagickColor("#40CC55"), new MagickColor("#009900"),
                new MagickColor("#006600"), new MagickColor("#BFCC55"), new MagickColor("#BF9900"),
                new MagickColor("#FF3300"), new MagickColor("#BF3300"), new MagickColor("#800000"),
                new MagickColor("#400000")
            };

            foreach (long timestamp in timestamps)
            {
                using MagickImage mosaic = new MagickImage(MagickColors.Transparent, (uint)mosaicWidth, (uint)mosaicHeight);
                bool complete = true;
                for (int y = minY; y <= maxY && complete; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        string tileUrl = $"https://api.weather.com/v3/TileServer/tile/satrad?ts={timestamp}&xyz={x}:{y}:{zoom}&apiKey={apiKey}";
                        try
                        {
                            byte[] tileData = await client.GetByteArrayAsync(tileUrl);
                            using MagickImage tile = new MagickImage(tileData, MagickFormat.Png);
                            if (tile.Width == 1 && tile.Height == 1)
                            {
                                tile.Resize(256, 256, FilterType.Point);
                            }
                            else if (tile.Width != 256 || tile.Height != 256)
                            {
                                throw new InvalidDataException("SatRad tiles must be 256 by 256 pixels.");
                            }
                            mosaic.Composite(tile, (x - minX) * 256, (y - minY) * 256, CompositeOperator.Copy);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Skipping satrad frame {timestamp}: tile ({x}, {y}) failed ({ex.GetType().Name}).");
                            complete = false;
                            break;
                        }
                    }
                }
                if (!complete)
                {
                    continue;
                }

                mosaic.BackgroundColor = MagickColors.Black;
                mosaic.Alpha(AlphaOption.Remove);
                mosaic.Depth = 8;
                byte[] sourceData = mosaic.ToByteArray(MagickFormat.Rgb);
                using IMagickImage<byte> radar = mosaic.Clone();
                radar.Remap(palette, new QuantizeSettings { DitherMethod = DitherMethod.No });
                byte[] radarData = radar.ToByteArray(MagickFormat.Rgb);
                byte[] canvasData = new byte[canvasWidth * canvasHeight * 3];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        (double X, double Y) coordinate = coordinates[y * width + x];
                        if (double.IsNaN(coordinate.X))
                        {
                            continue;
                        }
                        int sourceX = (int)Math.Floor(coordinate.X) - minX * 256;
                        int sourceY = (int)Math.Floor(coordinate.Y) - minY * 256;
                        int sourceIndex = (sourceY * mosaicWidth + sourceX) * 3;
                        int destinationIndex = ((canvasHeight - 1 - lower - y) * canvasWidth + left + x) * 3;
                        bool cloud = sourceData[sourceIndex] == sourceData[sourceIndex + 1] && sourceData[sourceIndex + 1] == sourceData[sourceIndex + 2];
                        Buffer.BlockCopy(cloud ? sourceData : radarData, sourceIndex, canvasData, destinationIndex, 3);
                    }
                }

                using MagickImage canvas = new MagickImage(canvasData, new MagickReadSettings { Width = (uint)canvasWidth, Height = (uint)canvasHeight, Depth = 8, Format = MagickFormat.Rgb });
                canvas.ColorType = ColorType.TrueColor;
                canvas.Settings.ColorType = ColorType.TrueColor;
                canvas.Settings.Compression = CompressionMethod.RLE;
                string framePath = Path.Combine(destinationPath, $"{timestamp}.{expiration}.tif");
                await canvas.WriteAsync(framePath, MagickFormat.Tiff);
                framePaths.Add(framePath);
                Console.WriteLine("Saved satrad frame: " + Path.GetFileName(framePath));
            }

            return framePaths;
        }
    }
}
