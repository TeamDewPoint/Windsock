using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Windsock
{
    public static class Headlines
    {
        private const string CacheFileName = "CachedAlerts.json";
        private const string ConfigFileName = "config.json";

        public class AlertCacheEntry
        {
            public string AlertKey { get; set; } = string.Empty;
            public long FirstSeenUtc { get; set; }
            public long ExpirationUtc { get; set; }
            public string ActionCode { get; set; } = string.Empty;
        }
        
        // Here is an important switch, that needs to be configured in your config.json, 'domestic' or 'wxscan', as this will control how this script take sin and configures your headlines, so that your i1 can actually get them without a platform mismatch issue... before we were getting wxscan headlines only as bulletins in the domestic system, and it started throwing some crazy errors up there! Now, it doesn't! Isn't is lovely? - Hiroki
        private static bool IsDomesticPlatform()
        {
            try
            {
                if (File.Exists(ConfigFileName))
                {
                    string json = File.ReadAllText(ConfigFileName);
                    using var doc = JsonDocument.Parse(json);
                    if (TryGetPlatformType(doc.RootElement, out string? platformType))
                    {
                        if (string.Equals(platformType, "domestic", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
             // Fallback default on error/missing file, ya know... just in case if you delete the file or something.
            }
            return false;
        }

        private static bool TryGetPlatformType(JsonElement element, out string? platformType)
        {
            platformType = null;
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("platformType", out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    platformType = prop.GetString();
                    return true;
                }
                foreach (var property in element.EnumerateObject())
                {
                    if (TryGetPlatformType(property.Value, out platformType))
                    {
                        return true;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (TryGetPlatformType(item, out platformType))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static List<string> BuildSegments(string areaId, TWC_Alert alert)
        {
            if (IsDomesticPlatform())
            {
                return BuildDomesticSegments(areaId, alert);
            }
            else
            {
                return BuildWxScanSegments(areaId, alert);
            }
        }

        private static List<string> BuildDomesticSegments(string areaId, TWC_Alert alert)
        {
            long expiration = alert.ExpireTimeUTC is > 0 ? alert.ExpireTimeUTC.Value : alert.EndTimeUTC ?? 0;
            if (expiration > 100000000000)
            {
                expiration /= 1000;
            }
            if (expiration <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return new List<string>();
            }

            string headline = Regex.Replace(string.IsNullOrWhiteSpace(alert.HeadlineText) ? alert.EventDescription ?? "" : alert.HeadlineText, @"\s+", " ").Trim();
            if (headline.Length == 0)
            {
                return new List<string>();
            }

            string alertKey = $"{areaId}_{alert.Phenomena}_{alert.Significance}_{headline}";
            string actionCode = DetermineAndUpdateCache(alertKey, expiration);

            string areaLiteral = PythonStringHelper.PyValue(areaId);
            string interest = Regex.IsMatch(areaId, @"^[A-Z]{2}C\d{3}$") ? "county" : "zone";
            string lookup = $"areaList = wxdata.getUGCInterestList({areaLiteral}, '{interest}')\n";
            if (interest == "zone")
            {
                lookup += $"if not areaList:\n    areaList = wxdata.getUGCInterestList({areaLiteral}, 'zone.cwf')\n";
            }

            string phenomena = (alert.Phenomena ?? "").ToUpper();
            string significance = (alert.Significance ?? "").ToUpper();
            string audioCode = ResolveAudioCode(phenomena, significance, headline.ToUpper());

            return new List<string>
            {
                "import twccommon\n" + lookup + "if not areaList:\n    abortMsg()\n",
                lookup + $"""
                twccommon.Log.info("THIS IS WINDSOCK HEADLINE w LOGIC")
                hdlnExp = {PythonStringHelper.PyValue(expiration)}
                headlineText = {PythonStringHelper.PyBytes(headline)}
                audioCode = {PythonStringHelper.PyValue(audioCode)}
                actionCode = {PythonStringHelper.PyValue(actionCode)}
                for area in areaList:
                    d = twc.Data()
                    d.headlines = [headlineText]
                    d.vocal = [[audioCode, actionCode, ""]]
                    wxdata.setData(area, 'hdln', d, hdlnExp)

                """
            };
        }
        // we should NEVER see this on wxscan, unless the user configured their config.json incorrectly...

        private static List<string> BuildWxScanSegments(string areaId, TWC_Alert alert)
        {
            long expiration = alert.ExpireTimeUTC is > 0 ? alert.ExpireTimeUTC.Value : alert.EndTimeUTC ?? 0;
            if (expiration > 100000000000)
            {
                expiration /= 1000;
            }
            if (expiration <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return new List<string>();
            }

            string areaLiteral = PythonStringHelper.PyValue(areaId);
            string interest = Regex.IsMatch(areaId, @"^[A-Z]{2}C\d{3}$") ? "county" : "zone";
            string lookup = $"areaList = wxdata.getUGCInterestList({areaLiteral}, '{interest}')\n";
            if (interest == "zone")
            {
                lookup += $"if not areaList:\n    areaList = wxdata.getUGCInterestList({areaLiteral}, 'zone.cwf')\n";
            }
            string headline = Regex.Replace(string.IsNullOrWhiteSpace(alert.HeadlineText) ? alert.EventDescription ?? "" : alert.HeadlineText, @"\s+", " ").Trim();
            if (headline.Length == 0)
            {
                return new List<string>();
            }

            return new List<string>
            {
                "import twccommon\n" + lookup + "if not areaList:\n    abortMsg()\n",
                lookup + $"""
                twccommon.Log.info("THIS IS WINDSOCK HEADLINE")
                headline = {PythonStringHelper.PyBytes(headline)}
                phenSig = {PythonStringHelper.PyValue((alert.Phenomena ?? "") + "_" + (alert.Significance ?? ""))}
                hdlnExp = {PythonStringHelper.PyValue(expiration)}
                for area in areaList:
                    d = twc.Data()
                    d.headline = headline
                    d.phenSig = phenSig
                    d.expiration = hdlnExp
                    wxdata.setHeadline(area, d, hdlnExp)

                """
            };
        }
        // Likewise, we should NEVER see this one above on domestic. if you do, you need to check your config.json.

        private static string DetermineAndUpdateCache(string alertKey, long expiration)
        {
            long nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Dictionary<string, AlertCacheEntry> cache = LoadCache();

            foreach (var key in new List<string>(cache.Keys))
            {
                if (cache[key].ExpirationUtc <= nowUtc)
                {
                    cache.Remove(key);
                }
            }


            // Here is where the magic resides, this is for domestic, and this controls whether or not we will be hearing action code I or H depending on how long we have had this alert active on the LFs for. Now, TWC does like to throw a rock in things, and they do enjoy preeemptively issuing certain alerts such as a Freeze Warning for example. So it might seem like we are going to have some iffiness with how that is read off, BUT wait, it is intentional! Only after the alert is actually in effect we are going to swap that actionCode, so we can tell that the alert is now active, rather than a pre-alert. Kinda sounds a little complicated, but to me, it makes sense... - Hiroki 
            string actionCode;
            if (cache.TryGetValue(alertKey, out var entry))
            {
                long ageSeconds = nowUtc - entry.FirstSeenUtc;
                actionCode = ageSeconds <= 3600 ? "I" : "H";
                entry.ExpirationUtc = expiration;
            }
            else
            {
                actionCode = "I";
                cache[alertKey] = new AlertCacheEntry
                {
                    AlertKey = alertKey,
                    FirstSeenUtc = nowUtc,
                    ExpirationUtc = expiration,
                    ActionCode = actionCode
                };
            }

            SaveCache(cache);
            return actionCode;
        }

        private static Dictionary<string, AlertCacheEntry> LoadCache()
        // I hate excessive duplicate alerts, don't you? Welp, now those are cached away so they do not get sent again unless they are updated or re-issued! Incroyable!! - Hiroki
        {
            try
            {
                if (File.Exists(CacheFileName))
                {
                    string json = File.ReadAllText(CacheFileName);
                    return JsonSerializer.Deserialize<Dictionary<string, AlertCacheEntry>>(json) ?? new Dictionary<string, AlertCacheEntry>();
                }
            }
            catch
            {
            }
            return new Dictionary<string, AlertCacheEntry>();
        }

        private static void SaveCache(Dictionary<string, AlertCacheEntry> cache)
        {
            try
            {
                string json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(CacheFileName, json);
            }
            catch
            {
            }
        }

        private static string ResolveAudioCode(string phenomena, string significance, string upperHeadline)
        // Once more, some magic happens here where we will be converting the input alerts acquired form the API to codes in which the i1 knows and loves, right off the bat... Again, TWC should've put a Headline table in there though, but oh well. - Hiroki
        {
            return (phenomena, significance) switch
            {
                ("TO", "W") => "SLS001",
                ("TO", "A") => "SLS001",
                ("SV", "W") => "SLS002",
                ("SV", "A") => "SLS002",
                ("FF", "W") => "FFS007",
                ("FL", "W") => "FLW001",
                ("FL", "Y") => "FLS008",
                ("WS", "W") => "WSW005",
                ("WS", "A") => "WSW006",
                ("BZ", "W") => "WSW022",
                ("IS", "W") => "WSW025",
                ("HW", "W") => "NPW014",
                ("HW", "A") => "NPW013",
                ("WI", "Y") => "NPW015",
                ("FZ", "W") => "NPW020",
                ("FR", "Y") => "NPW021",
                ("FZ", "Y") => "NPW024",
                ("CF", "W") => "CFW005",
                ("CF", "A") => "CFW006",
                _ => ResolveByHeadline(upperHeadline)
            };
        }

        private static string ResolveByHeadline(string upperHeadline)
        // This is a fallback, it is important we make double triple quadruple quintuple sure we have the right headline, because if we do not I am gonna loose my mind. Not really, but hey... Either way, this also can be later expanded upon! Isn't this so much fun? - Hiroki
        {
            if (upperHeadline.Contains("TORNADO")) return "SLS001";
            if (upperHeadline.Contains("SEVERE THUNDERSTORM")) return "SLS002";
            if (upperHeadline.Contains("BLIZZARD WARNING")) return "WSW022";
            if (upperHeadline.Contains("WINTER STORM WARNING")) return "WSW005";
            if (upperHeadline.Contains("WINTER STORM WATCH")) return "WSW006";
            if (upperHeadline.Contains("ICE STORM WARNING")) return "WSW025";
            if (upperHeadline.Contains("DENSE FOG ADVISORY")) return "NPW005";
            if (upperHeadline.Contains("DENSE FOG WARNING")) return "NPW042";
            if (upperHeadline.Contains("WIND ADVISORY")) return "NPW015";
            if (upperHeadline.Contains("HIGH WIND WARNING")) return "NPW014";
            if (upperHeadline.Contains("FREEZE WARNING")) return "NPW020";
            if (upperHeadline.Contains("FROST ADVISORY")) return "NPW027";
            if (upperHeadline.Contains("SNOW ADVISORY")) return "WSW020";
            return "WSW020";
        }
    }
}
