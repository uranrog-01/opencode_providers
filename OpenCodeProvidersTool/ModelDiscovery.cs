using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OpenCodeProvidersTool
{
    /// <summary>A model the provider's own catalog endpoint returned.</summary>
    public class DiscoveredModel
    {
        public string Id = "";
        public string Name = "";
        public long Context;
        public long Output;
    }

    /// <summary>Outcome of asking a provider's Base URL for its model list.</summary>
    public class DiscoveryResult
    {
        public List<DiscoveredModel> Models = new List<DiscoveredModel>();
        public string Endpoint = "";
        public string Error = "";

        public bool Ok { get { return Error.Length == 0 && Models.Count > 0; } }
    }

    /// <summary>
    /// Reads the model catalog from the provider itself: GET {baseURL}/models with the
    /// configured key. Smart add uses this so the list is what that endpoint actually
    /// serves; models.dev is only used afterwards to fill in metadata the provider omits.
    /// </summary>
    public static class ModelDiscovery
    {
        private static readonly HttpClient Http = CreateHttp();

        private static HttpClient CreateHttp()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenCodeProviders/1.2 (+smart add)");
            return client;
        }

        /// <summary>
        /// The models URL for a provider Base URL. The Base URL is used the way the AI
        /// SDK uses it, so "/models" is appended to the configured base; a configured
        /// request suffix ("/responses", "/chat/completions", ...) is stripped first.
        /// Returns "" when no usable URL can be derived.
        /// </summary>
        public static string Endpoint(string baseUrl)
        {
            string url = (baseUrl ?? "").Trim();
            if (url.Length == 0) return "";
            if (url.IndexOf("${", StringComparison.Ordinal) >= 0) return "";
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            url = url.TrimEnd('/');
            string[] suffixes = { "/chat/completions", "/responses", "/messages", "/completions" };
            foreach (string suffix in suffixes)
            {
                if (url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    url = url.Substring(0, url.Length - suffix.Length);
                    break;
                }
            }
            if (url.EndsWith("/models", StringComparison.OrdinalIgnoreCase)) return url;
            return url + "/models";
        }

        /// <summary>Fetches and parses the provider catalog; never throws.</summary>
        public static Task<DiscoveryResult> DiscoverAsync(string baseUrl, string apiKey, string npm)
        {
            return Task.Run(() => Discover(baseUrl, apiKey, npm));
        }

        private static DiscoveryResult Discover(string baseUrl, string apiKey, string npm)
        {
            var result = new DiscoveryResult();
            result.Endpoint = Endpoint(baseUrl);
            if (result.Endpoint.Length == 0)
            {
                result.Error = "No usable Base URL \u2014 set one to read the provider's models.";
                return result;
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, result.Endpoint);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    string key = apiKey.Trim();
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                    if (IsAnthropicFormat(npm)) request.Headers.TryAddWithoutValidation("x-api-key", key);
                }

                using (HttpResponseMessage response = Http.SendAsync(request).GetAwaiter().GetResult())
                {
                    string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode)
                    {
                        result.Error = DescribeFailure((int)response.StatusCode, body);
                        return result;
                    }
                    Parse(body, result);
                }
            }
            catch (Exception ex)
            {
                result.Error = Short(ex.Message);
            }
            return result;
        }

        private static void Parse(string body, DiscoveryResult result)
        {
            JToken root;
            try { root = JToken.Parse(body); }
            catch
            {
                result.Error = "The provider returned a non-JSON catalog.";
                return;
            }

            var items = new List<KeyValuePair<string, JToken>>();
            Collect(root, items);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, JToken> entry in items)
            {
                DiscoveredModel model = Read(entry.Value, entry.Key);
                if (model == null || model.Id.Length == 0) continue;
                if (!seen.Add(model.Id)) continue;
                result.Models.Add(model);
            }

            if (result.Models.Count == 0)
            {
                result.Error = "The provider returned no models.";
                return;
            }

            result.Models.Sort(delegate (DiscoveredModel a, DiscoveredModel b)
            {
                return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// Accepts the shapes catalogs come in: a bare array, data/models/result arrays,
        /// and object maps keyed by model id.
        /// </summary>
        private static void Collect(JToken token, List<KeyValuePair<string, JToken>> items)
        {
            var array = token as JArray;
            if (array != null)
            {
                foreach (JToken item in array) items.Add(new KeyValuePair<string, JToken>("", item));
                return;
            }

            var obj = token as JObject;
            if (obj == null) return;

            JToken data = obj["data"] ?? obj["models"] ?? obj["result"];
            if (data != null)
            {
                var nested = data as JArray;
                if (nested != null)
                {
                    foreach (JToken item in nested) items.Add(new KeyValuePair<string, JToken>("", item));
                    return;
                }
                var map = data as JObject;
                if (map != null)
                {
                    foreach (JProperty property in map.Properties())
                    {
                        items.Add(new KeyValuePair<string, JToken>(property.Name, property.Value));
                    }
                    return;
                }
            }

            // Unknown object shape: treat each property as a model entry.
            foreach (JProperty property in obj.Properties())
            {
                if (property.Value is JObject) items.Add(new KeyValuePair<string, JToken>(property.Name, property.Value));
            }
        }

        private static DiscoveredModel Read(JToken item, string fallbackId)
        {
            if (item == null) return null;
            var obj = item as JObject;

            string id = obj == null ? "" : FirstText(obj, "id", "model", "name");
            if (id.Length == 0) id = Text(item);
            if (id.Length == 0) id = fallbackId;
            if (id.Length == 0) return null;
            if (id.StartsWith("models/", StringComparison.OrdinalIgnoreCase)) id = id.Substring(7);

            var model = new DiscoveredModel { Id = id };

            if (obj != null)
            {
                string name = FirstText(obj, "display_name", "displayName", "title", "name");
                if (name.StartsWith("models/", StringComparison.OrdinalIgnoreCase)) name = name.Substring(7);
                if (!string.Equals(name, id, StringComparison.OrdinalIgnoreCase)) model.Name = name;

                model.Context = FirstNumber(obj,
                    "context_length", "context_window", "max_context_length",
                    "inputTokenLimit", "input_token_limit");
                model.Output = FirstNumber(obj,
                    "max_output_tokens", "max_completion_tokens", "max_tokens",
                    "outputTokenLimit", "output_token_limit");

                JToken limit = obj["limit"];
                if (limit != null)
                {
                    if (model.Context <= 0) model.Context = Number(limit["context"]);
                    if (model.Output <= 0) model.Output = Number(limit["output"]);
                }
            }
            return model;
        }

        private static bool IsAnthropicFormat(string npm)
        {
            return !string.IsNullOrEmpty(npm)
                && npm.IndexOf("anthropic", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DescribeFailure(int status, string body)
        {
            string hint = Short(body);
            if (status == 401 || status == 403)
            {
                return "The provider rejected the API key (" + status + ")." + Suffix(hint);
            }
            if (status == 404)
            {
                return "The provider has no /models endpoint at this Base URL (404)." + Suffix(hint);
            }
            if (status == 429)
            {
                return "The provider rate-limited the catalog request (429)." + Suffix(hint);
            }
            return "The provider answered HTTP " + status + "." + Suffix(hint);
        }

        private static string Suffix(string detail)
        {
            return detail.Length == 0 ? "" : "  " + detail;
        }

        private static string FirstText(JObject obj, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = Text(obj[key]);
                if (value.Length > 0) return value;
            }
            return "";
        }

        private static long FirstNumber(JObject obj, params string[] keys)
        {
            foreach (string key in keys)
            {
                long value = Number(obj[key]);
                if (value > 0) return value;
            }
            return 0;
        }

        private static string Text(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "";
            return token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
        }

        private static long Number(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return 0;
            long value;
            if (long.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return value;
            double floating;
            if (double.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out floating)) return (long)floating;
            return 0;
        }

        private static string Short(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";
            string clean = message.Replace("\r", " ").Replace("\n", " ").Trim();
            return clean.Length <= 120 ? clean : clean.Substring(0, 120) + "\u2026";
        }
    }
}
