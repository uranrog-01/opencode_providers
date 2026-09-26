using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenCodeProvidersTool
{
    /// <summary>One entry of a provider's "models" object.</summary>
    public class ModelEntry
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";

        /// <summary>limit.context as text, so the editor can round-trip it exactly.</summary>
        public string ContextWindow { get; set; } = "";

        /// <summary>limit.output as text.</summary>
        public string MaxOutputTokens { get; set; } = "";

        /// <summary>True when the loaded definition had the limit, so clearing removes it.</summary>
        public bool ContextWasSet { get; set; }

        public bool OutputWasSet { get; set; }

        /// <summary>Smart configuration toggle for this model (state lives outside the config).</summary>
        public bool SmartConfiguration { get; set; } = true;

        /// <summary>Set by the UI to draw the smart indicator beside the model row.</summary>
        public bool SmartIndicator { get; set; }

        /// <summary>The original model value, kept so limit/modalities/variants survive a save.</summary>
        public JObject Definition { get; set; }

        public bool IsNew { get; set; }
    }

    /// <summary>One provider block, as edited in the UI.</summary>
    public class ProviderEntry
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Npm { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public List<ModelEntry> Models { get; set; } = new List<ModelEntry>();

        /// <summary>
        /// The effective state: true when OpenCode would skip this provider, either because
        /// it is listed in "disabled_providers" or because an "enabled_providers" whitelist
        /// leaves it out.
        /// </summary>
        public bool IsDisabled { get; set; }

        /// <summary>
        /// The state as loaded, so a save can tell "the user switched this off" from
        /// "this was already off". Without it, a provider merely missing from the whitelist
        /// would be written into disabled_providers on every save.
        /// </summary>
        public bool WasDisabled { get; set; }

        public bool IsNew { get; set; }

        /// <summary>Enough set up to be usable: a base URL or at least one model.</summary>
        public bool IsConfigured
        {
            get { return Models.Count > 0 || BaseUrl.Trim().Length > 0; }
        }
    }

    public class ConfigDocument
    {
        public string Path { get; set; } = "";

        /// <summary>The whole file, so top-level keys ($schema, plugin, ...) survive.</summary>
        public JObject Root { get; set; }

        public List<ProviderEntry> Providers { get; set; } = new List<ProviderEntry>();

        /// <summary>The "disabled_providers" array exactly as read, including ids with no provider block.</summary>
        public List<string> DisabledProviders { get; set; } = new List<string>();

        /// <summary>The "enabled_providers" whitelist as read. Only meaningful when the key exists.</summary>
        public List<string> EnabledProviders { get; set; } = new List<string>();

        /// <summary>
        /// True when the file had an "enabled_providers" key. OpenCode then treats it as a
        /// whitelist, so the UI's enable toggle has to maintain both lists to stay truthful.
        /// </summary>
        public bool HasEnabledProviders { get; set; }

        /// <summary>
        /// Models whose token limits could not be written because only one half was set.
        /// OpenCode requires limit.context and limit.output together, so a half limit is
        /// dropped rather than written as an invalid config.
        /// </summary>
        public List<string> LimitWarnings { get; set; } = new List<string>();

        /// <summary>
        /// The comments of the original .jsonc text. Newtonsoft drops them from the tree,
        /// so they are held here and re-emitted on save.
        /// </summary>
        public ConfigComments Comments { get; set; } = new ConfigComments();

        /// <summary>Provider ids the user deleted in the UI. Applied on the next save.</summary>
        public HashSet<string> DeletedProviderIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Top-level "model" / "small_model" defaults, as edited in the UI.</summary>
        public string DefaultModel { get; set; } = "";

        /// <summary>Top-level "small_model" default, as edited in the UI.</summary>
        public string SmallModel { get; set; } = "";

        public List<McpServer> McpServers { get; set; } = new List<McpServer>();

        /// <summary>MCP server names the user deleted in the UI. Applied on the next save.</summary>
        public HashSet<string> DeletedMcpIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public List<PermissionRule> Permissions { get; set; } = new List<PermissionRule>();

        /// <summary>Permission paths the user deleted in the UI. Applied on the next save.</summary>
        public HashSet<string> DeletedPermissionPaths { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>One entry of the top-level "mcp" object.</summary>
    public class McpServer
    {
        public string Name { get; set; } = "";

        /// <summary>"local" (stdio command) or "remote" (url).</summary>
        public string Type { get; set; } = "local";

        /// <summary>Command + args for local servers, space-joined for editing.</summary>
        public string Command { get; set; } = "";

        public string Url { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string Timeout { get; set; } = "";

        /// <summary>The original server value, kept so environment/headers survive a save.</summary>
        public JObject Definition { get; set; }

        public bool IsNew { get; set; }
    }

    /// <summary>One flattened permission rule. Path is "tool" or "tool › sub".</summary>
    public class PermissionRule
    {
        public string Path { get; set; } = "";
        public string Effect { get; set; } = "ask";
        public bool IsNew { get; set; }
    }

    /// <summary>
    /// Reads and writes an OpenCode config while leaving everything the user did not
    /// touch exactly as it was. Edits are applied to the parsed tree in place; the
    /// provider block is never rebuilt from scratch, so unknown or unedited keys
    /// (name, npm, options.*, rich model definitions) cannot be dropped.
    /// </summary>
    public static class ConfigStore
    {
        /// <summary>Config locations OpenCode actually uses, most specific first.</summary>
        public static List<string> DiscoverCandidates()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var list = new List<string>();

            AddPair(list, AppDomain.CurrentDomain.BaseDirectory);
            AddPair(list, Path.Combine(home, ".config", "opencode"));
            AddPair(list, Path.Combine(home, ".opencode"));
            AddPair(list, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opencode"));
            return list;
        }

        private static void AddPair(List<string> list, string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            list.Add(Path.Combine(directory, "opencode.jsonc"));
            list.Add(Path.Combine(directory, "opencode.json"));
        }

        /// <summary>First config that exists on disk, or "" when none does.</summary>
        public static string FindExistingConfig()
        {
            foreach (string candidate in DiscoverCandidates())
            {
                if (File.Exists(candidate)) return candidate;
            }
            return "";
        }

        /// <summary>
        /// Whether OpenCode would skip this provider: listed in disabled_providers, or left
        /// out of an enabled_providers whitelist.
        /// </summary>
        public static bool EffectiveDisabled(ConfigDocument doc, string id)
        {
            if (doc == null || string.IsNullOrEmpty(id)) return false;
            if (Contains(doc.DisabledProviders, id)) return true;
            if (doc.HasEnabledProviders && !Contains(doc.EnabledProviders, id)) return true;
            return false;
        }

        private static bool Contains(List<string> ids, string id)
        {
            foreach (string candidate in ids)
            {
                if (string.Equals(candidate, id, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string Text(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "";
            return token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
        }

        /// <summary>Whole positive token count parsed from user text; 0 when blank or invalid.</summary>
        public static long ParseCount(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            string clean = text.Replace(",", "").Replace("_", "").Replace(" ", "").Trim();
            long value;
            if (long.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out value) && value > 0) return value;
            return 0;
        }

        /// <summary>Token count as canonical text; "" when missing or not a whole number.</summary>
        private static string CountText(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "";
            long value;
            if (long.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value) && value > 0)
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }
            return "";
        }

        private static void LoadMcp(ConfigDocument doc, JObject root)
        {
            JObject mcpBlock = root["mcp"] as JObject;
            if (mcpBlock == null) return;

            foreach (JProperty property in mcpBlock.Properties())
            {
                JObject server = property.Value as JObject;
                if (server == null) continue;

                var entry = new McpServer { Name = property.Name, IsNew = false, Definition = server };
                entry.Type = Text(server["type"]);
                if (entry.Type.Length == 0) entry.Type = server["url"] != null ? "remote" : "local";
                entry.Url = Text(server["url"]);
                entry.Timeout = Text(server["timeout"]);

                if (server["command"] is JArray command)
                {
                    var parts = new List<string>();
                    foreach (JToken part in command)
                    {
                        string text = Text(part);
                        if (text.Length > 0) parts.Add(text);
                    }
                    entry.Command = string.Join(" ", parts.ToArray());
                }

                JToken enabled = server["enabled"];
                entry.Enabled = !(enabled != null && enabled.Type == JTokenType.Boolean && !enabled.Value<bool>());

                doc.McpServers.Add(entry);
            }
        }

        private static void LoadPermissions(ConfigDocument doc, JObject root)
        {
            JObject permissionBlock = root["permission"] as JObject;
            if (permissionBlock == null) return;

            foreach (JProperty property in permissionBlock.Properties())
            {
                if (property.Value.Type == JTokenType.String)
                {
                    doc.Permissions.Add(new PermissionRule
                    {
                        Path = property.Name,
                        Effect = property.Value.Value<string>(),
                        IsNew = false
                    });
                }
                else if (property.Value is JObject nested)
                {
                    foreach (JProperty sub in nested.Properties())
                    {
                        if (sub.Value.Type != JTokenType.String) continue;
                        doc.Permissions.Add(new PermissionRule
                        {
                            Path = property.Name + " › " + sub.Name,
                            Effect = sub.Value.Value<string>(),
                            IsNew = false
                        });
                    }
                }
            }
        }

        public static ConfigDocument Load(string path)
        {
            string contents = File.ReadAllText(path);

            // Newtonsoft skips // and /* */ comments, so a commented .jsonc reads fine.
            // The comments are captured separately, because the parse throws them away.
            JObject root = JObject.Parse(contents);

            var doc = new ConfigDocument { Path = path, Root = root };
            doc.Comments = ConfigComments.Capture(contents);

            if (root["disabled_providers"] is JArray disabled)
            {
                foreach (JToken item in disabled)
                {
                    string id = Text(item);
                    if (id.Length > 0) doc.DisabledProviders.Add(id);
                }
            }

            // An "enabled_providers" list turns OpenCode's selection into a whitelist, so the
            // enable toggle has to be read against both keys to show the truth.
            if (root["enabled_providers"] is JArray enabled)
            {
                doc.HasEnabledProviders = true;
                foreach (JToken item in enabled)
                {
                    string id = Text(item);
                    if (id.Length > 0) doc.EnabledProviders.Add(id);
                }
            }

            doc.DefaultModel = Text(root["model"]);
            doc.SmallModel = Text(root["small_model"]);

            LoadMcp(doc, root);
            LoadPermissions(doc, root);

            JObject providerBlock = root["provider"] as JObject;
            if (providerBlock == null) return doc;
            foreach (JProperty property in providerBlock.Properties())
            {
                JObject provider = property.Value as JObject;
                if (provider == null) continue;

                var entry = new ProviderEntry { Id = property.Name, IsNew = false };
                entry.DisplayName = Text(provider["name"]);
                entry.Npm = Text(provider["npm"]);
                entry.IsDisabled = EffectiveDisabled(doc, entry.Id);
                entry.WasDisabled = entry.IsDisabled;

                JObject options = provider["options"] as JObject;
                if (options != null)
                {
                    entry.BaseUrl = Text(options["baseURL"]);
                    entry.ApiKey = Text(options["apiKey"]);
                }

                JObject models = provider["models"] as JObject;
                if (models != null)
                {
                    foreach (JProperty modelProperty in models.Properties())
                    {
                        string name = Text(modelProperty.Value["name"]);
                        var model = new ModelEntry
                        {
                            Id = modelProperty.Name,
                            DisplayName = string.IsNullOrEmpty(name) ? modelProperty.Name : name,
                            Definition = modelProperty.Value as JObject,
                            IsNew = false
                        };

                        JObject limit = modelProperty.Value["limit"] as JObject;
                        if (limit != null)
                        {
                            model.ContextWindow = CountText(limit["context"]);
                            model.MaxOutputTokens = CountText(limit["output"]);
                            model.ContextWasSet = limit["context"] != null;
                            model.OutputWasSet = limit["output"] != null;
                        }

                        entry.Models.Add(model);
                    }
                }

                doc.Providers.Add(entry);
            }

            return doc;
        }

        /// <summary>
        /// Applies the edited values onto the loaded tree and writes the file. A backup
        /// of the previous contents is written next to it first.
        /// </summary>
        public static string Save(ConfigDocument doc, string path)
        {
            JObject root = doc.Root ?? new JObject();
            doc.LimitWarnings.Clear();

            JObject providerBlock = root["provider"] as JObject;
            if (providerBlock == null)
            {
                providerBlock = new JObject();
                root["provider"] = providerBlock;
            }

            var managed = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProviderEntry entry in doc.Providers) managed.Add(entry.Id);

            // Providers the user deleted in the UI: drop their block so a save
            // actually removes them instead of leaving the old JSON behind.
            if (doc.DeletedProviderIds != null && doc.DeletedProviderIds.Count > 0)
            {
                foreach (string deletedId in doc.DeletedProviderIds) providerBlock.Remove(deletedId);
            }

            foreach (ProviderEntry entry in doc.Providers)
            {
                if (string.IsNullOrWhiteSpace(entry.Id)) continue;

                JObject provider = providerBlock[entry.Id] as JObject;
                if (provider == null)
                {
                    provider = new JObject();
                    providerBlock[entry.Id] = provider;
                }

                if (!string.IsNullOrWhiteSpace(entry.DisplayName)) provider["name"] = entry.DisplayName.Trim();
                if (!string.IsNullOrWhiteSpace(entry.Npm)) provider["npm"] = entry.Npm.Trim();

                // options is edited in place so sibling keys (apiKey, headers, ...) survive.
                bool touchesOptions = !string.IsNullOrWhiteSpace(entry.BaseUrl) || !string.IsNullOrWhiteSpace(entry.ApiKey);
                if (touchesOptions)
                {
                    JObject options = provider["options"] as JObject;
                    if (options == null)
                    {
                        options = new JObject();
                        provider["options"] = options;
                    }
                    if (!string.IsNullOrWhiteSpace(entry.BaseUrl)) options["baseURL"] = entry.BaseUrl.Trim();
                    if (!string.IsNullOrWhiteSpace(entry.ApiKey)) options["apiKey"] = entry.ApiKey.Trim();
                }

                JObject existing = provider["models"] as JObject;

                // Hold on to the original values so anything we did not edit (rich model
                // metadata, or a value that is not even an object) is written back verbatim.
                var original = new Dictionary<string, JToken>();
                if (existing != null)
                {
                    foreach (JProperty modelProperty in existing.Properties())
                    {
                        original[modelProperty.Name] = modelProperty.Value;
                    }
                    existing.RemoveAll();
                }

                var models = new JObject();
                foreach (ModelEntry model in entry.Models)
                {
                    if (string.IsNullOrWhiteSpace(model.Id)) continue;

                    string id = model.Id.Trim();
                    JToken value;
                    if (original.ContainsKey(id)) value = original[id].DeepClone();
                    else if (model.Definition != null) value = model.Definition.DeepClone();
                    else value = new JObject();

                    JObject definition = value as JObject;
                    if (definition != null)
                    {
                        if (!string.IsNullOrWhiteSpace(model.DisplayName) && model.DisplayName != model.Id)
                        {
                            definition["name"] = model.DisplayName.Trim();
                        }

                        ApplyLimit(definition, "context", model.ContextWindow, model.ContextWasSet);
                        ApplyLimit(definition, "output", model.MaxOutputTokens, model.OutputWasSet);

                        // The schema requires limit.context and limit.output together, so a
                        // half limit would make the whole config invalid. Drop it and tell the
                        // caller, rather than writing something OpenCode will reject.
                        if (LimitIsHalf(definition))
                        {
                            definition.Remove("limit");
                            doc.LimitWarnings.Add(entry.Id + " / " + model.Id);
                        }

                        model.ContextWasSet = ParseCount(model.ContextWindow) > 0;
                        model.OutputWasSet = ParseCount(model.MaxOutputTokens) > 0;
                    }

                    models[id] = value;
                }

                if (models.Count > 0 || existing != null) provider["models"] = models;
            }

            // Rebuild disabled_providers: keep every entry we do not manage (built-in
            // providers, ids with no block) in its original order, then append providers
            // the user just switched off. Dropping unrelated ids would silently re-enable
            // providers this tool knows nothing about.
            //
            // A managed provider is only (re)added when it was already listed or the user
            // just switched it off. One that an enabled_providers whitelist merely leaves
            // out is already handled by that list, so writing it here too would be noise.
            var stillDisabled = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProviderEntry entry in doc.Providers)
            {
                if (!entry.IsDisabled || !managed.Contains(entry.Id)) continue;
                if (Contains(doc.DisabledProviders, entry.Id) || !entry.WasDisabled) stillDisabled.Add(entry.Id);
            }

            var rebuilt = new List<string>();
            foreach (string id in doc.DisabledProviders)
            {
                if (doc.DeletedProviderIds != null && doc.DeletedProviderIds.Contains(id)) continue;
                if (!managed.Contains(id)) rebuilt.Add(id);
                else if (stillDisabled.Contains(id)) rebuilt.Add(id);
            }
            foreach (string id in stillDisabled)
            {
                if (!rebuilt.Contains(id)) rebuilt.Add(id);
            }

            bool hadKey = root["disabled_providers"] != null;
            if (rebuilt.Count > 0 || hadKey)
            {
                var array = new JArray();
                foreach (string id in rebuilt) array.Add(id);
                root["disabled_providers"] = array;
            }

            MaintainEnabledProviders(doc, root);

            SaveModels(doc, root);
            SaveMcp(doc, root);
            SavePermissions(doc, root);

            string backup = "";
            if (File.Exists(path))
            {
                backup = path + ".bak";
                File.Copy(path, backup, overwrite: true);
            }

            // Comments are re-emitted, so saving no longer wipes the notes in a .jsonc file.
            string written = doc.Comments == null
                ? root.ToString(Formatting.Indented)
                : doc.Comments.Write(root);
            File.WriteAllText(path, written);

            // Re-read them from what was just written, so later edits stay anchored to the
            // file as it now stands.
            doc.Comments = ConfigComments.Capture(written);

            if (doc.DeletedProviderIds != null) doc.DeletedProviderIds.Clear();
            if (doc.DeletedMcpIds != null) doc.DeletedMcpIds.Clear();
            if (doc.DeletedPermissionPaths != null) doc.DeletedPermissionPaths.Clear();
            return backup;
        }

        /// <summary>
        /// Keeps "enabled_providers" in step with the toggle. Under that whitelist a provider
        /// is off simply by being absent, so switching one on means adding it here as well as
        /// clearing it from disabled_providers — otherwise OpenCode would still skip it.
        /// Only entries the user actually changed, plus new providers, are touched.
        /// </summary>
        private static void MaintainEnabledProviders(ConfigDocument doc, JObject root)
        {
            if (!doc.HasEnabledProviders) return;

            var list = new List<string>();
            foreach (string id in doc.EnabledProviders)
            {
                if (doc.DeletedProviderIds != null && doc.DeletedProviderIds.Contains(id)) continue;
                if (!list.Contains(id)) list.Add(id);
            }

            foreach (ProviderEntry entry in doc.Providers)
            {
                if (string.IsNullOrWhiteSpace(entry.Id)) continue;
                if (!entry.IsNew && entry.IsDisabled == entry.WasDisabled) continue;

                if (entry.IsDisabled) list.Remove(entry.Id);
                else if (!list.Contains(entry.Id)) list.Add(entry.Id);
            }

            var array = new JArray();
            foreach (string id in list) array.Add(id);
            root["enabled_providers"] = array;
        }

        /// <summary>True when a model has a "limit" that is missing context or output.</summary>
        private static bool LimitIsHalf(JObject definition)
        {
            JObject limit = definition["limit"] as JObject;
            if (limit == null) return false;
            return limit["context"] == null || limit["output"] == null;
        }

        /// <summary>
        /// Writes one token limit, creating or removing the "limit" object as needed.
        /// Other limit keys (input, ...) and non-numeric originals are left alone; blank
        /// text only removes a limit that was actually there when the file was loaded.
        /// </summary>
        private static void ApplyLimit(JObject definition, string key, string text, bool wasSet)
        {
            long parsed = ParseCount(text);
            JObject limit = definition["limit"] as JObject;
            JToken existing = limit == null ? null : limit[key];

            if (parsed <= 0)
            {
                if (!wasSet || limit == null || existing == null) return;
                limit.Remove(key);
                if (limit.Count == 0) definition.Remove("limit");
                return;
            }

            string canonical = parsed.ToString(CultureInfo.InvariantCulture);
            if (existing != null && string.Equals(Text(existing), canonical, StringComparison.Ordinal)) return;

            if (limit == null)
            {
                limit = new JObject();
                definition["limit"] = limit;
            }
            limit[key] = parsed;
        }

        /// <summary>Writes "model" / "small_model", preserving every other top-level key.</summary>
        private static void SaveModels(ConfigDocument doc, JObject root)
        {
            bool hadModel = root["model"] != null;
            bool hadSmall = root["small_model"] != null;

            // A deleted default is represented by an empty string: drop the key so the
            // blank in the UI does not linger as an empty string in the file.
            if (doc.DefaultModel == null) { /* untouched */ }
            else if (doc.DefaultModel.Trim().Length == 0) { if (hadModel) root.Remove("model"); }
            else root["model"] = doc.DefaultModel.Trim();

            if (doc.SmallModel == null) { /* untouched */ }
            else if (doc.SmallModel.Trim().Length == 0) { if (hadSmall) root.Remove("small_model"); }
            else root["small_model"] = doc.SmallModel.Trim();
        }

        private static void SaveMcp(ConfigDocument doc, JObject root)
        {
            bool touched = doc.McpServers.Count > 0
                || (doc.DeletedMcpIds != null && doc.DeletedMcpIds.Count > 0);
            if (!touched) return;

            JObject mcpBlock = root["mcp"] as JObject;
            if (mcpBlock == null)
            {
                mcpBlock = new JObject();
                root["mcp"] = mcpBlock;
            }

            if (doc.DeletedMcpIds != null)
            {
                foreach (string deleted in doc.DeletedMcpIds) mcpBlock.Remove(deleted);
            }

            foreach (McpServer entry in doc.McpServers)
            {
                if (string.IsNullOrWhiteSpace(entry.Name)) continue;

                JObject server = mcpBlock[entry.Name] as JObject;
                if (server == null)
                {
                    server = entry.Definition != null
                        ? (JObject)entry.Definition.DeepClone()
                        : new JObject();
                    mcpBlock[entry.Name] = server;
                }

                // Type is edited in place so environment/headers/timeout survive.
                // Untouched servers keep their original shape: no inferred keys added.
                string type = entry.Type.Trim().ToLowerInvariant();
                if (type != "local" && type != "remote") type = "local";
                bool hadType = entry.Definition != null && entry.Definition["type"] != null;
                if (entry.IsNew || hadType || type == "remote") server["type"] = type;

                if (type == "remote")
                {
                    if (!string.IsNullOrWhiteSpace(entry.Url))
                    {
                        server["url"] = entry.Url.Trim();
                        server.Remove("command");
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(entry.Command))
                    {
                        var array = new JArray();
                        foreach (string part in entry.Command.Split(new[] { ' ' },
                            StringSplitOptions.RemoveEmptyEntries)) array.Add(part);
                        server["command"] = array;
                        server.Remove("url");
                    }
                }

                if (!entry.Enabled) server["enabled"] = false;
                else if (entry.IsNew || (entry.Definition != null && entry.Definition["enabled"] != null))
                    server.Remove("enabled");

                if (!string.IsNullOrWhiteSpace(entry.Timeout))
                {
                    int timeout;
                    if (int.TryParse(entry.Timeout.Trim(), out timeout)) server["timeout"] = timeout;
                    else server["timeout"] = entry.Timeout.Trim();
                }
            }

            if (mcpBlock.Count == 0) root.Remove("mcp");
        }

        private static void SavePermissions(ConfigDocument doc, JObject root)
        {
            bool touched = doc.Permissions.Count > 0
                || (doc.DeletedPermissionPaths != null && doc.DeletedPermissionPaths.Count > 0);
            if (!touched) return;

            // Rebuild from the flattened rules; untouched nested shapes are preserved
            // because every loaded rule round-trips through this same flattening.
            var tools = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (PermissionRule rule in doc.Permissions)
            {
                if (string.IsNullOrWhiteSpace(rule.Path)) continue;
                if (doc.DeletedPermissionPaths != null && doc.DeletedPermissionPaths.Contains(rule.Path)) continue;

                string tool = rule.Path;
                string sub = null;
                int split = rule.Path.IndexOf(" › ", StringComparison.Ordinal);
                if (split >= 0)
                {
                    tool = rule.Path.Substring(0, split).Trim();
                    sub = rule.Path.Substring(split + 3).Trim();
                }
                else tool = tool.Trim();

                if (tool.Length == 0) continue;
                if (!tools.ContainsKey(tool)) { tools[tool] = new Dictionary<string, string>(StringComparer.Ordinal); order.Add(tool); }
                tools[tool][sub ?? ""] = rule.Effect.Trim().Length > 0 ? rule.Effect.Trim() : "ask";
            }

            // Drop tools the user deleted wholesale.
            if (doc.DeletedPermissionPaths != null)
            {
                foreach (string deleted in new List<string>(order))
                {
                    if (doc.DeletedPermissionPaths.Contains(deleted))
                    {
                        tools.Remove(deleted);
                        order.Remove(deleted);
                    }
                }
            }

            if (order.Count == 0)
            {
                if (root["permission"] != null && doc.Permissions.Count == 0) root.Remove("permission");
                return;
            }

            var block = new JObject();
            foreach (string tool in order)
            {
                var subs = tools[tool];
                if (subs.Count == 1 && subs.ContainsKey(""))
                {
                    block[tool] = subs[""];
                }
                else
                {
                    var nested = new JObject();
                    foreach (KeyValuePair<string, string> sub in subs)
                    {
                        if (sub.Key.Length == 0) continue;
                        nested[sub.Key] = sub.Value;
                    }
                    // A lone "" alongside subs collapses to "*".
                    if (subs.ContainsKey("")) nested["*"] = subs[""];
                    block[tool] = nested;
                }
            }
            root["permission"] = block;
        }
    }
}
