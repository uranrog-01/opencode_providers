using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OpenCodeProvidersTool
{
    /// <summary>Provider-agnostic model facts, as served by models.dev ("models.json").</summary>
    public class CatalogModel
    {
        public string Id = "";
        public string Name = "";
        public long Context;
        public long Output;
        public string Description = "";

        public bool HasLimits { get { return Context > 0 || Output > 0; } }
    }

    /// <summary>Provider entry from models.dev ("api.json"), including the models it serves.</summary>
    public class CatalogProvider
    {
        public string Id = "";
        public string Name = "";
        public string Npm = "";
        public string Api = "";
        public Dictionary<string, CatalogModel> Models = new Dictionary<string, CatalogModel>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Result of a smart lookup: provider match, model match and a short source note.</summary>
    public class CatalogMatch
    {
        public CatalogProvider Provider;
        public CatalogModel Model;
        public string Note = "";

        public bool Found { get { return Model != null; } }

        public bool HasLimits { get { return Model != null && Model.HasLimits; } }
    }

    /// <summary>Provider + model list used by the "Smart add" dialog.</summary>
    public class CatalogListing
    {
        public CatalogProvider Provider;
        public List<CatalogModel> Models = new List<CatalogModel>();
        public string Note = "";
    }

    /// <summary>
    /// Reads models.dev and matches it against what the config says about a provider.
    /// Smart configuration uses this to recommend a model's display name and token
    /// limits; the data is cached on disk so the app still works offline.
    ///
    /// Matching uses three things: the provider id, the base URL host, and the API
    /// format (npm package). Provider-specific limits win over provider-agnostic
    /// model metadata, because a relay may serve a smaller window than the lab does.
    /// </summary>
    public static class ModelCatalog
    {
        public const string ApiUrl = "https://models.dev/api.json";
        public const string ModelsUrl = "https://models.dev/models.json";

        private static readonly object Gate = new object();
        private static readonly HttpClient Http = CreateHttp();

        private static List<CatalogProvider> _providers = new List<CatalogProvider>();
        private static List<CatalogModel> _models = new List<CatalogModel>();
        private static Dictionary<string, CatalogModel> _byId = new Dictionary<string, CatalogModel>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, List<CatalogModel>> _bySuffix = new Dictionary<string, List<CatalogModel>>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, List<CatalogModel>> _byNormalized = new Dictionary<string, List<CatalogModel>>(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded;
        private static bool _loading;
        private static Task _loadingTask;
        private static DateTime _loadedUtc;
        private static string _error = "";
        private static string _cacheDirectory;

        /// <summary>Raised on a background thread after new catalog data is available.</summary>
        public static event EventHandler Updated;

        // ------------------------------------------------------------------ state

        public static string CacheDirectory
        {
            get
            {
                if (_cacheDirectory != null) return _cacheDirectory;
                _cacheDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenCodeProviders");
                return _cacheDirectory;
            }
            set { _cacheDirectory = value; }
        }

        public static bool HaveData { get { lock (Gate) return _loaded; } }

        public static bool IsLoading { get { lock (Gate) return _loading; } }

        public static DateTime LoadedUtc { get { lock (Gate) return _loadedUtc; } }

        public static string Error { get { lock (Gate) return _error; } }

        /// <summary>Human-readable state, shown in dialogs and the status bar.</summary>
        public static string Status
        {
            get
            {
                lock (Gate)
                {
                    if (_loaded)
                    {
                        return "models.dev: " + _providers.Count + " providers \u00b7 "
                            + _models.Count + " models";
                    }
                    if (_error.Length > 0) return "models.dev unavailable: " + _error;
                    return _loading ? "Loading models.dev\u2026" : "models.dev not loaded";
                }
            }
        }

        private static string ApiCacheFile { get { return Path.Combine(CacheDirectory, "models.dev.api.json"); } }

        private static string ModelsCacheFile { get { return Path.Combine(CacheDirectory, "models.dev.models.json"); } }

        private static HttpClient CreateHttp()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(25);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenCodeProviders/1.2 (+models.dev smart configuration)");
            return client;
        }

        // ------------------------------------------------------------------ loading

        /// <summary>
        /// Starts loading the catalog in the background: cache first (instant, offline
        /// safe), then a refresh when the cache is missing or older than half a day.
        /// Never blocks the caller and never throws.
        /// </summary>
        public static void BeginLoad()
        {
            lock (Gate)
            {
                if (_loaded && (DateTime.UtcNow - _loadedUtc) < TimeSpan.FromHours(12)) return;
                if (_loadingTask != null && !_loadingTask.IsCompleted) return;
                _loadingTask = Task.Run(() => LoadCore(false));
            }
        }

        /// <summary>Downloads and parses the catalog. Returns true when data is usable.</summary>
        public static Task<bool> RefreshAsync(bool force)
        {
            Task existing;
            lock (Gate)
            {
                if (_loaded && !force && (DateTime.UtcNow - _loadedUtc) < TimeSpan.FromHours(12))
                {
                    return Task.FromResult(true);
                }

                existing = _loadingTask;
                if (existing == null || existing.IsCompleted)
                {
                    Task<bool> started = Task.Run(() => LoadCore(force));
                    _loadingTask = started;
                    return started;
                }
            }

            // A load is already running: wait for it, then force a fresh one if asked.
            return existing.ContinueWith(delegate { return force ? LoadCore(true) : HaveData; });
        }

        /// <summary>
        /// Blocks up to <paramref name="waitMs"/> for catalog data. Used by Save so a first
        /// save can still sync recommendations; the UI shows a status line meanwhile.
        /// </summary>
        public static bool EnsureLoaded(int waitMs)
        {
            if (HaveData) return true;

            Task wait;
            lock (Gate)
            {
                if (_loadingTask == null || _loadingTask.IsCompleted)
                {
                    _loadingTask = Task.Run(() => LoadCore(false));
                }
                wait = _loadingTask;
            }
            try { wait.Wait(waitMs); }
            catch { }
            return HaveData;
        }

        private static bool LoadCore(bool force)
        {
            lock (Gate) _loading = true;
            try
            {
                bool fromCache = false;
                if (File.Exists(ApiCacheFile) && File.Exists(ModelsCacheFile))
                {
                    try
                    {
                        string api = File.ReadAllText(ApiCacheFile);
                        string models = File.ReadAllText(ModelsCacheFile);
                        ParseAndSwap(api, models, false);

                        // The freshness clock follows the cache file, not the parse: an old
                        // cache must still trigger a download below.
                        lock (Gate) _loadedUtc = File.GetLastWriteTimeUtc(ApiCacheFile);
                        fromCache = true;
                    }
                    catch { /* fall through to the download */ }
                }

                bool stale = false;
                lock (Gate)
                {
                    stale = !_loaded || (DateTime.UtcNow - _loadedUtc) > TimeSpan.FromHours(12);
                }
                if (force || !fromCache || stale) DownloadCore();
                return true;
            }
            catch (Exception ex)
            {
                lock (Gate) _error = Short(ex.Message);
                return HaveData;
            }
            finally
            {
                lock (Gate) _loading = false;
            }
        }

        private static void DownloadCore()
        {
            string api = Http.GetStringAsync(ApiUrl).GetAwaiter().GetResult();
            string models = Http.GetStringAsync(ModelsUrl).GetAwaiter().GetResult();
            ParseAndSwap(api, models, true);
        }

        /// <summary>Parses both documents and swaps them in. Public so tests can run offline.</summary>
        public static void LoadFromText(string apiJson, string modelsJson)
        {
            ParseAndSwap(apiJson, modelsJson, false);
        }

        private static void ParseAndSwap(string apiJson, string modelsJson, bool writeCache)
        {
            var providers = new List<CatalogProvider>();
            var models = new List<CatalogModel>();

            JObject apiRoot = JObject.Parse(apiJson);
            foreach (JProperty property in apiRoot.Properties())
            {
                JObject value = property.Value as JObject;
                if (value == null) continue;

                var provider = new CatalogProvider
                {
                    Id = property.Name,
                    Name = Text(value["name"]),
                    Npm = Text(value["npm"]),
                    Api = Text(value["api"])
                };

                JObject providerModels = value["models"] as JObject;
                if (providerModels != null)
                {
                    foreach (JProperty modelProperty in providerModels.Properties())
                    {
                        JObject source = modelProperty.Value as JObject;
                        if (source == null) continue;
                        provider.Models[modelProperty.Name] = ReadModel(source, modelProperty.Name);
                    }
                }
                providers.Add(provider);
            }

            JObject modelsRoot = JObject.Parse(modelsJson);
            foreach (JProperty property in modelsRoot.Properties())
            {
                JObject value = property.Value as JObject;
                if (value == null) continue;
                models.Add(ReadModel(value, property.Name));
            }

            if (providers.Count == 0 && models.Count == 0)
            {
                throw new InvalidDataException("models.dev returned no usable data.");
            }

            var byId = new Dictionary<string, CatalogModel>(StringComparer.OrdinalIgnoreCase);
            var bySuffix = new Dictionary<string, List<CatalogModel>>(StringComparer.OrdinalIgnoreCase);
            var byNormalized = new Dictionary<string, List<CatalogModel>>(StringComparer.OrdinalIgnoreCase);

            foreach (CatalogModel model in models)
            {
                if (model.Id.Length == 0) continue;
                byId[model.Id] = model;
                AddSuffix(bySuffix, Suffix(model.Id), model);
                AddSuffix(byNormalized, Normalize(model.Id), model);
            }

            lock (Gate)
            {
                _providers = providers;
                _models = models;
                _byId = byId;
                _bySuffix = bySuffix;
                _byNormalized = byNormalized;
                _loaded = true;
                _loadedUtc = DateTime.UtcNow;
                _error = "";
            }

            if (writeCache)
            {
                try
                {
                    Directory.CreateDirectory(CacheDirectory);
                    File.WriteAllText(ApiCacheFile, apiJson);
                    File.WriteAllText(ModelsCacheFile, modelsJson);
                }
                catch { /* cache is best effort */ }
            }

            EventHandler handler = Updated;
            if (handler != null)
            {
                try { handler(null, EventArgs.Empty); }
                catch { }
            }
        }

        private static CatalogModel ReadModel(JObject source, string fallbackId)
        {
            var model = new CatalogModel
            {
                Id = Text(source["id"]),
                Name = Text(source["name"]),
                Description = Text(source["description"])
            };
            if (model.Id.Length == 0) model.Id = fallbackId;

            JToken limit = source["limit"];
            if (limit != null)
            {
                model.Context = Number(limit["context"]);
                model.Output = Number(limit["output"]);
            }
            return model;
        }

        private static void AddSuffix(Dictionary<string, List<CatalogModel>> map, string key, CatalogModel model)
        {
            if (key.Length == 0) return;
            List<CatalogModel> list;
            if (!map.TryGetValue(key, out list))
            {
                list = new List<CatalogModel>();
                map[key] = list;
            }
            list.Add(model);
        }

        // ------------------------------------------------------------------ matching

        /// <summary>Best provider for the config's id / base URL / API format, or null.</summary>
        public static CatalogProvider MatchProvider(string providerId, string npm, string baseUrl)
        {
            lock (Gate)
            {
                return MatchProviderLocked(providerId, npm, baseUrl);
            }
        }

        private static CatalogProvider MatchProviderLocked(string providerId, string npm, string baseUrl)
        {
            if (!_loaded || _providers.Count == 0) return null;

            string id = (providerId ?? "").Trim();
            string format = (npm ?? "").Trim();
            string host = HostOf(baseUrl);
            string flatHost = host.Replace(".", "").Replace("-", "");

            CatalogProvider best = null;
            int bestScore = 0;

            foreach (CatalogProvider provider in _providers)
            {
                int score = 0;

                if (id.Length > 0 && string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    score += 100;
                }
                else if (id.Length >= 4 && provider.Id.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 25;
                }

                string apiHost = HostOf(provider.Api);
                if (host.Length > 0 && apiHost.Length > 0)
                {
                    string flatApi = apiHost.Replace(".", "").Replace("-", "");
                    if (string.Equals(host, apiHost, StringComparison.OrdinalIgnoreCase)) score += 80;
                    else if (flatHost == flatApi) score += 60;
                    else if (flatApi.IndexOf(flatHost, StringComparison.OrdinalIgnoreCase) >= 0
                        || flatHost.IndexOf(flatApi, StringComparison.OrdinalIgnoreCase) >= 0) score += 20;
                }

                if (flatHost.Length >= 4 && provider.Id.Length >= 3)
                {
                    string flatId = provider.Id.Replace("-", "");
                    if (flatHost.IndexOf(flatId, StringComparison.OrdinalIgnoreCase) >= 0) score += 40;
                }

                if (format.Length > 0 && string.Equals(provider.Npm, format, StringComparison.OrdinalIgnoreCase))
                {
                    score += 12;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = provider;
                }
            }

            return bestScore >= 40 ? best : null;
        }

        /// <summary>
        /// Looks a model up for smart configuration. Provider-specific limits win;
        /// otherwise provider-agnostic model metadata is used.
        /// </summary>
        public static CatalogMatch FindModel(string providerId, string npm, string baseUrl, string modelId)
        {
            var match = new CatalogMatch();
            string id = (modelId ?? "").Trim();
            if (id.Length == 0)
            {
                match.Note = "Enter a model ID";
                return match;
            }

            lock (Gate)
            {
                if (!_loaded)
                {
                    match.Note = IsLoading ? "Loading models.dev\u2026" : "models.dev data is not available";
                    return match;
                }

                CatalogProvider provider = MatchProviderLocked(providerId, npm, baseUrl);
                match.Provider = provider;

                CatalogModel specific = provider == null ? null : FindInProvider(provider, id);
                CatalogModel global = FindGlobal(id);

                if (specific != null)
                {
                    match.Model = Merge(specific, global);
                    match.Note = NoteFor(provider, match.Model);
                    return match;
                }

                if (global != null)
                {
                    match.Model = global;
                    match.Note = "models.dev \u00b7 " + global.Id;
                    return match;
                }

                // Last resort: some providers list a model models.json does not have.
                CatalogProvider owner;
                CatalogModel found = FindInAnyProvider(id, npm, out owner);
                if (found != null)
                {
                    match.Model = found;
                    match.Provider = owner;
                    match.Note = NoteFor(owner, found);
                }
                else
                {
                    match.Note = "No models.dev match for \"" + id + "\"";
                }
                return match;
            }
        }

        /// <summary>
        /// The provider's models.dev catalog. Returns an empty list when the provider is
        /// not in models.dev: the Smart add list must never become "every known model",
        /// it is only ever what the provider's Base URL or its models.dev entry serves.
        /// </summary>
        public static CatalogListing ListModels(string providerId, string npm, string baseUrl)
        {
            var listing = new CatalogListing();
            lock (Gate)
            {
                if (!_loaded)
                {
                    listing.Note = IsLoading ? "Loading models.dev\u2026" : "models.dev data is not available";
                    return listing;
                }

                CatalogProvider provider = MatchProviderLocked(providerId, npm, baseUrl);
                listing.Provider = provider;

                if (provider != null)
                {
                    foreach (CatalogModel model in provider.Models.Values)
                    {
                        CatalogModel global = FindGlobal(model.Id);
                        listing.Models.Add(Merge(model, global));
                    }
                    listing.Note = "models.dev \u00b7 " + provider.Name + " (" + provider.Id + ")";
                }
                else
                {
                    listing.Note = "No models.dev provider match";
                }

                listing.Models.Sort(delegate (CatalogModel a, CatalogModel b)
                {
                    int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    if (byName != 0) return byName;
                    return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
                });
                return listing;
            }
        }

        private static CatalogModel FindInProvider(CatalogProvider provider, string id)
        {
            CatalogModel exact;
            if (provider.Models.TryGetValue(id, out exact)) return exact;

            foreach (CatalogModel model in provider.Models.Values)
            {
                if (string.Equals(Suffix(model.Id), id, StringComparison.OrdinalIgnoreCase)) return model;
            }
            return null;
        }

        private static CatalogModel FindGlobal(string id)
        {
            CatalogModel exact;
            if (_byId.TryGetValue(id, out exact)) return exact;

            List<CatalogModel> suffix;
            if (_bySuffix.TryGetValue(Suffix(id), out suffix) && suffix.Count > 0) return suffix[0];

            List<CatalogModel> normalized;
            if (_byNormalized.TryGetValue(Normalize(id), out normalized) && normalized.Count == 1) return normalized[0];

            return null;
        }

        private static CatalogModel FindInAnyProvider(string id, string npm, out CatalogProvider owner)
        {
            owner = null;
            CatalogModel best = null;
            string bestNpm = "";
            foreach (CatalogProvider provider in _providers)
            {
                CatalogModel found = FindInProvider(provider, id);
                if (found == null) continue;

                if (owner == null)
                {
                    owner = provider;
                    best = found;
                    bestNpm = provider.Npm;
                }
                else if (!string.IsNullOrEmpty(npm)
                    && !string.Equals(bestNpm, npm, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(provider.Npm, npm, StringComparison.OrdinalIgnoreCase))
                {
                    owner = provider;
                    best = found;
                    bestNpm = provider.Npm;
                }
            }
            return best;
        }

        /// <summary>Provider-specific values win; missing ones come from model metadata.</summary>
        private static CatalogModel Merge(CatalogModel specific, CatalogModel global)
        {
            if (global == null) return specific;
            if (specific == null) return global;

            var merged = new CatalogModel
            {
                Id = specific.Id.Length > 0 ? specific.Id : global.Id,
                Name = specific.Name.Length > 0 ? specific.Name : global.Name,
                Description = specific.Description.Length > 0 ? specific.Description : global.Description,
                Context = specific.Context > 0 ? specific.Context : global.Context,
                Output = specific.Output > 0 ? specific.Output : global.Output
            };
            return merged;
        }

        private static string NoteFor(CatalogProvider provider, CatalogModel model)
        {
            string where = provider != null
                ? "models.dev \u00b7 " + provider.Name + " (" + provider.Id + ")"
                : "models.dev";
            return where + " \u00b7 " + model.Id;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Last path segment of a model id: "deepseek/deepseek-v4-flash" -> "deepseek-v4-flash".</summary>
        private static string Suffix(string id)
        {
            string value = (id ?? "").Trim();
            int slash = value.LastIndexOf('/');
            return slash >= 0 ? value.Substring(slash + 1) : value;
        }

        /// <summary>
        /// Case and punctuation insensitive form used as a last-resort match, so a config
        /// id like "deepseek-v4.1-flash" still finds "deepseek/deepseek-v4.1-flash".
        /// </summary>
        private static string Normalize(string id)
        {
            string value = Suffix(id).ToLowerInvariant();
            value = value.Replace(".", "-").Replace("_", "-").Replace(":", "-");
            if (value.EndsWith("-latest", StringComparison.Ordinal)) value = value.Substring(0, value.Length - 7);

            // Release/date suffixes such as "-0731" or "-20260326".
            int dash = value.LastIndexOf('-');
            if (dash > 0)
            {
                string tail = value.Substring(dash + 1);
                if (tail.Length >= 6 && IsDigits(tail)) value = value.Substring(0, dash);
            }
            return value;
        }

        private static bool IsDigits(string value)
        {
            foreach (char c in value) if (c < '0' || c > '9') return false;
            return value.Length > 0;
        }

        private static string HostOf(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            string value = url.Trim();

            int scheme = value.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) value = value.Substring(scheme + 3);

            int at = value.IndexOf('@');
            if (at >= 0) value = value.Substring(at + 1);

            int slash = value.IndexOf('/');
            if (slash >= 0) value = value.Substring(0, slash);

            int colon = value.IndexOf(':');
            if (colon >= 0) value = value.Substring(0, colon);

            return value.ToLowerInvariant();
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
            if (string.IsNullOrEmpty(message)) return "unknown error";
            return message.Length <= 90 ? message : message.Substring(0, 90) + "\u2026";
        }

        /// <summary>Compact token count for row captions: 1000000 -> "1M".</summary>
        public static string FormatTokens(long value)
        {
            if (value <= 0) return "";
            if (value >= 1000000) return (value / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (value >= 1000) return (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Reset for tests: drops in-memory data and clears any cached error.</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                _providers = new List<CatalogProvider>();
                _models = new List<CatalogModel>();
                _byId = new Dictionary<string, CatalogModel>(StringComparer.OrdinalIgnoreCase);
                _bySuffix = new Dictionary<string, List<CatalogModel>>(StringComparer.OrdinalIgnoreCase);
                _byNormalized = new Dictionary<string, List<CatalogModel>>(StringComparer.OrdinalIgnoreCase);
                _loaded = false;
                _loading = false;
                _loadingTask = null;
                _loadedUtc = default(DateTime);
                _error = "";
            }
        }
    }
}
