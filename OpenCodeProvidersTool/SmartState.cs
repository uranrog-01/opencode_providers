using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenCodeProvidersTool
{
    /// <summary>
    /// Per-model smart-configuration bookkeeping. Kept in a local file instead of the
    /// opencode config: OpenCode validates its config strictly, so keys it does not
    /// know (manual flags, sync stamps) would be rejected or dropped.
    /// </summary>
    public class SmartModelState
    {
        /// <summary>Follow models.dev recommendations for this model.</summary>
        public bool Smart = true;

        public bool ManualName;
        public bool ManualContext;
        public bool ManualOutput;

        public long SyncedContext;
        public long SyncedOutput;
        public string SyncedName = "";
        public string Updated = "";

        public SmartModelState Copy()
        {
            return new SmartModelState
            {
                Smart = Smart,
                ManualName = ManualName,
                ManualContext = ManualContext,
                ManualOutput = ManualOutput,
                SyncedContext = SyncedContext,
                SyncedOutput = SyncedOutput,
                SyncedName = SyncedName,
                Updated = Updated
            };
        }
    }

    /// <summary>Loads and saves smart-model state under %LOCALAPPDATA%\OpenCodeProviders.</summary>
    public static class SmartStateStore
    {
        private static readonly object Gate = new object();
        private static Dictionary<string, SmartModelState> _states;
        private static string _filePath;

        public static string FilePath
        {
            get
            {
                if (_filePath != null) return _filePath;
                _filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenCodeProviders", "smart-models.json");
                return _filePath;
            }
            set { _filePath = value; }
        }

        public static string Key(string providerId, string modelId)
        {
            return (providerId ?? "").Trim() + "/" + (modelId ?? "").Trim();
        }

        /// <summary>The saved state, or null when the tool has never managed this model.</summary>
        public static SmartModelState Get(string providerId, string modelId)
        {
            EnsureLoaded();
            string key = Key(providerId, modelId);
            lock (Gate)
            {
                SmartModelState state;
                return _states.TryGetValue(key, out state) ? state : null;
            }
        }

        public static void Set(string providerId, string modelId, SmartModelState state)
        {
            if (state == null) return;
            EnsureLoaded();
            lock (Gate) _states[Key(providerId, modelId)] = state;
        }

        public static void Remove(string providerId, string modelId)
        {
            EnsureLoaded();
            lock (Gate) _states.Remove(Key(providerId, modelId));
        }

        public static void RemoveProvider(string providerId)
        {
            EnsureLoaded();
            string prefix = (providerId ?? "").Trim() + "/";
            lock (Gate)
            {
                var doomed = new List<string>();
                foreach (string key in _states.Keys)
                {
                    if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) doomed.Add(key);
                }
                foreach (string key in doomed) _states.Remove(key);
            }
        }

        /// <summary>Carries the state over when a model is renamed in the editor.</summary>
        public static void Rename(string providerId, string oldModelId, string newModelId)
        {
            if (string.Equals(oldModelId, newModelId, StringComparison.Ordinal)) return;
            EnsureLoaded();
            string oldKey = Key(providerId, oldModelId);
            string newKey = Key(providerId, newModelId);
            lock (Gate)
            {
                SmartModelState state;
                if (_states.TryGetValue(oldKey, out state))
                {
                    _states.Remove(oldKey);
                    _states[newKey] = state;
                }
            }
        }

        public static int Count
        {
            get
            {
                EnsureLoaded();
                lock (Gate) return _states.Count;
            }
        }

        /// <summary>Best effort: a read-only or missing profile folder must never fail a save.</summary>
        public static void Save()
        {
            try
            {
                EnsureLoaded();
                var payload = new JObject();
                payload["version"] = 1;
                payload["models"] = JObject.Parse(JsonConvert.SerializeObject(_states));
                payload["updated"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, payload.ToString(Formatting.Indented));
            }
            catch { }
        }

        public static void Reload()
        {
            lock (Gate) _states = null;
        }

        private static void EnsureLoaded()
        {
            if (_states != null) return;
            lock (Gate)
            {
                if (_states != null) return;
                _states = LoadFile();
            }
        }

        private static Dictionary<string, SmartModelState> LoadFile()
        {
            var states = new Dictionary<string, SmartModelState>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return states;
                JObject root = JObject.Parse(File.ReadAllText(FilePath));
                JObject models = root["models"] as JObject;
                if (models == null) return states;

                foreach (JProperty property in models.Properties())
                {
                    JObject value = property.Value as JObject;
                    if (value == null) continue;
                    SmartModelState state = JsonConvert.DeserializeObject<SmartModelState>(value.ToString());
                    if (state == null) continue;
                    if (state.SyncedName == null) state.SyncedName = "";
                    if (state.Updated == null) state.Updated = "";
                    states[property.Name] = state;
                }
            }
            catch { }
            return states;
        }
    }

    /// <summary>
    /// Bridges the config document and the models.dev recommendations. Seeding happens
    /// when a config loads so an existing model with no limits gets them on the next
    /// save, while any value the user already set stays put until they edit it.
    /// </summary>
    public static class SmartSync
    {
        /// <summary>Number of characters in a model's definition that smart config manages.</summary>
        public const string FieldName = "name";
        public const string FieldContext = "context";
        public const string FieldOutput = "output";

        /// <summary>Seeds state for every model that has none. Does not change the config.</summary>
        public static void SeedDocument(ConfigDocument document)
        {
            if (document == null) return;
            foreach (ProviderEntry provider in document.Providers)
            {
                foreach (ModelEntry model in provider.Models) Seed(provider, model);
            }
        }

        /// <summary>
        /// Returns the smart state for a model, creating it from the model's current
        /// values when this is the first time the tool sees it: a value that is already
        /// in the config counts as manually managed, an empty one as auto.
        /// </summary>
        public static SmartModelState Seed(ProviderEntry provider, ModelEntry model)
        {
            if (provider == null || model == null) return new SmartModelState();

            SmartModelState state = SmartStateStore.Get(provider.Id, model.Id);
            if (state != null) return state;

            state = new SmartModelState
            {
                Smart = model.SmartConfiguration,
                ManualName = !string.IsNullOrEmpty(model.DisplayName) && model.DisplayName != model.Id,
                ManualContext = !string.IsNullOrEmpty(model.ContextWindow),
                ManualOutput = !string.IsNullOrEmpty(model.MaxOutputTokens)
            };
            SmartStateStore.Set(provider.Id, model.Id, state);
            return state;
        }

        /// <summary>
        /// Applies fresh recommendations to all smart-managed models. Returns how many
        /// models changed. <paramref name="forceLimits"/> overwrites limits the user had
        /// set manually (used by the "Smart add" dialog's explicit overwrite switch).
        /// </summary>
        public static int Apply(ConfigDocument document, bool forceLimits)
        {
            if (document == null || !ModelCatalog.HaveData) return 0;

            int changed = 0;
            foreach (ProviderEntry provider in document.Providers)
            {
                foreach (ModelEntry model in provider.Models)
                {
                    SmartModelState state = SmartStateStore.Get(provider.Id, model.Id);
                    if (state == null || !state.Smart) continue;

                    CatalogMatch match = ModelCatalog.FindModel(provider.Id, provider.Npm, provider.BaseUrl, model.Id);
                    if (!match.Found) continue;

                    CatalogModel found = match.Model;
                    bool touched = false;

                    if (found.Name.Length > 0 && !state.ManualName && model.DisplayName != found.Name)
                    {
                        model.DisplayName = found.Name;
                        state.SyncedName = found.Name;
                        touched = true;
                    }

                    touched |= ApplyLimit(model, found.Context, state, true, forceLimits);
                    touched |= ApplyLimit(model, found.Output, state, false, forceLimits);

                    if (touched)
                    {
                        state.Updated = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                        changed++;
                    }
                }
            }

            if (changed > 0) SmartStateStore.Save();
            return changed;
        }

        private static bool ApplyLimit(ModelEntry model, long recommended, SmartModelState state, bool context, bool force)
        {
            if (recommended <= 0) return false;
            bool manual = context ? state.ManualContext : state.ManualOutput;
            if (manual && !force) return false;

            string text = recommended.ToString(CultureInfo.InvariantCulture);
            bool touched = false;
            if (context)
            {
                if (model.ContextWindow != text)
                {
                    model.ContextWindow = text;
                    model.ContextWasSet = true;
                    touched = true;
                }
                state.ManualContext = false;
                state.SyncedContext = recommended;
            }
            else
            {
                if (model.MaxOutputTokens != text)
                {
                    model.MaxOutputTokens = text;
                    model.OutputWasSet = true;
                    touched = true;
                }
                state.ManualOutput = false;
                state.SyncedOutput = recommended;
            }
            return touched;
        }
    }
}
