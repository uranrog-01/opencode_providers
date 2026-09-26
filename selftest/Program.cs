using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCodeProvidersTool;

internal static class SelfTest
{
    private static int Main(string[] args)
    {
        string source = args.Length > 0 ? args[0] : ConfigStore.FindExistingConfig();
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
        {
            Console.WriteLine("FAIL: no source config found");
            return 1;
        }

        Console.WriteLine("source: " + source);
        string originalText = File.ReadAllText(source);
        JToken originalTree = JToken.Parse(originalText);

        string work = Path.Combine(Path.GetTempPath(), "occ_selftest_" + Guid.NewGuid().ToString("N") + ".jsonc");
        string temp = Path.GetTempPath();
        File.Copy(source, work, true);

        int failures = 0;

        // ---- Pass 1: load and save with no edits at all. Nothing may change. ----
        ConfigDocument doc = ConfigStore.Load(work);
        Console.WriteLine($"loaded providers: {doc.Providers.Count}");
        int models = 0;
        foreach (ProviderEntry p in doc.Providers) models += p.Models.Count;
        Console.WriteLine($"loaded models: {models}");

        ConfigStore.Save(doc, work);

        JToken afterTree = JToken.Parse(File.ReadAllText(work));
        bool lossless = JToken.DeepEquals(originalTree, afterTree);
        Console.WriteLine("PASS1 lossless round-trip (no edits): " + (lossless ? "PASS" : "FAIL"));
        if (!lossless) { failures++; ReportDiff(originalTree, afterTree); }

        // ---- Pass 2: realistic edits. Only the intended keys may change. ----
        ConfigDocument edit = ConfigStore.Load(work);
        ProviderEntry target = edit.Providers[0];
        string targetId = target.Id;
        Console.WriteLine($"editing provider: {targetId}  baseURL={target.BaseUrl}  models={target.Models.Count}");

        target.BaseUrl = "https://example.invalid/v1";
        target.ApiKey = "sk-test-key";

        string secondId = null;
        if (edit.Providers.Count > 1)
        {
            secondId = edit.Providers[1].Id;
            edit.Providers[1].Models.Add(new ModelEntry { Id = "zz-selftest-model", DisplayName = "Self Test Model", IsNew = true });
        }

        // The later steps need a third provider. A smaller config is still worth checking,
        // so those edits and their assertions are skipped instead of indexing off the end.
        string renamedId = null;
        string renameProvider = null;
        if (edit.Providers.Count > 2)
        {
            renameProvider = edit.Providers[2].Id;
            if (edit.Providers[2].Models.Count > 0)
            {
                renamedId = edit.Providers[2].Models[0].Id;
                edit.Providers[2].Models[0].DisplayName = "Renamed By SelfTest";
            }
        }

        string removedId = null;
        ProviderEntry withModels = null;
        foreach (ProviderEntry p in edit.Providers)
        {
            if (p.Models.Count > 2) { withModels = p; break; }
        }
        if (withModels != null)
        {
            removedId = withModels.Models[withModels.Models.Count - 1].Id;
            withModels.Models.RemoveAt(withModels.Models.Count - 1);
        }

        edit.Providers.Add(new ProviderEntry
        {
            Id = "zz-selftest-provider",
            DisplayName = "Self Test Provider",
            Npm = "@ai-sdk/openai-compatible",
            BaseUrl = "https://selftest.invalid",
            IsNew = true
        });

        ConfigStore.Save(edit, work);

        JToken edited = JToken.Parse(File.ReadAllText(work));
        failures += Check(edited, "zz-selftest-provider", "BaseURL applied", n => (string)n["options"]?["baseURL"] == "https://selftest.invalid");
        failures += Check(edited, "zz-selftest-provider", "npm applied", n => (string)n["npm"] == "@ai-sdk/openai-compatible");
        failures += Check(edited, "zz-selftest-provider", "display name applied", n => (string)n["name"] == "Self Test Provider");
        failures += Check(edited, targetId, "baseURL changed", n => (string)n["options"]?["baseURL"] == "https://example.invalid/v1");
        failures += Check(edited, targetId, "apiKey added", n => (string)n["options"]?["apiKey"] == "sk-test-key");
        if (secondId != null)
        {
            failures += Check(edited, secondId, "model added", n => n["models"]?["zz-selftest-model"] != null);
            failures += Check(edited, secondId, "model display name set", n => (string)n["models"]?["zz-selftest-model"]?["name"] == "Self Test Model");
        }
        if (renamedId != null)
        {
            failures += Check(edited, renameProvider, "model renamed", n => (string)n["models"]?[renamedId]?["name"] == "Renamed By SelfTest");
        }
        if (removedId != null && withModels != null)
        {
            failures += Check(edited, withModels.Id, "model removed", n => n["models"]?[removedId] == null);
        }

        // Untouched providers must be semantically identical.
        JObject origProviders = (JObject)originalTree["provider"];
        JObject newProviders = (JObject)edited["provider"];
        var touched = new HashSet<string> { targetId, secondId, "zz-selftest-provider" };
        if (withModels != null) touched.Add(withModels.Id);
        if (renamedId != null) touched.Add(renameProvider);

        int survived = 0, altered = 0;
        foreach (JProperty pair in origProviders.Properties())
        {
            if (touched.Contains(pair.Name)) continue;
            if (newProviders[pair.Name] == null) { Console.WriteLine("  LOST provider: " + pair.Name); altered++; continue; }
            if (JToken.DeepEquals(pair.Value, newProviders[pair.Name])) survived++;
            else { Console.WriteLine("  ALTERED provider: " + pair.Name); altered++; }
        }
        Console.WriteLine($"PASS2 untouched providers preserved: {survived}   altered/lost: {altered}");
        if (altered > 0) failures++;

        // Top-level keys must be untouched.
        foreach (string key in new[] { "$schema", "plugin", "disabled_providers" })
        {
            bool same = JToken.DeepEquals(originalTree[key], edited[key]);
            Console.WriteLine($"PASS2 top-level \"{key}\" preserved: {(same ? "PASS" : "FAIL")}");
            if (!same) failures++;
        }

        // Rich model metadata must survive (limits/modalities/variants).
        string richProvider = null, richModel = null;
        foreach (JProperty pair in origProviders.Properties())
        {
            JObject m = pair.Value["models"] as JObject;
            if (m == null) continue;
            foreach (JProperty model in m.Properties())
            {
                if (model.Value["variants"] != null || model.Value["limit"] != null)
                {
                    richProvider = pair.Name;
                    richModel = model.Name;
                    break;
                }
            }
            if (richProvider != null) break;
        }
        if (richProvider != null)
        {
            bool same = JToken.DeepEquals(
                origProviders[richProvider]?["models"]?[richModel],
                newProviders[richProvider]?["models"]?[richModel]);
            Console.WriteLine($"PASS2 rich model metadata preserved ({richProvider} / {richModel}): {(same ? "PASS" : "FAIL")}");
            if (!same) failures++;
        }

        // ---- Pass 3: the enable/disable toggle driving disabled_providers. ----
        ConfigDocument toggle = ConfigStore.Load(work);
        int disabledBefore = toggle.DisabledProviders.Count;

        // An id with no provider block must survive untouched: this is what stops the tool
        // from silently re-enabling providers it knows nothing about.
        const string unrelated = "some-builtin-provider";
        if (!toggle.DisabledProviders.Contains(unrelated)) toggle.DisabledProviders.Add(unrelated);

        // enable one that is currently disabled, disable one that is not
        ProviderEntry toEnable = null;
        ProviderEntry toDisable = null;
        foreach (ProviderEntry p in toggle.Providers)
        {
            if (toEnable == null && p.IsDisabled) toEnable = p;
            if (toDisable == null && !p.IsDisabled) toDisable = p;
        }

        if (toEnable != null) toEnable.IsDisabled = false;
        if (toDisable != null) toDisable.IsDisabled = true;
        ConfigStore.Save(toggle, work);

        JToken saved = JToken.Parse(File.ReadAllText(work));
        JArray disabledList = saved["disabled_providers"] as JArray;

        bool listPresent = disabledList != null;
        Console.WriteLine($"PASS3 disabled_providers written: {(listPresent ? "PASS" : "FAIL")}");
        if (!listPresent) failures++;

        if (listPresent)
        {
            var ids = new List<string>();
            foreach (JToken item in disabledList) ids.Add(item.ToString());

            if (toEnable != null)
            {
                bool gone = !ids.Contains(toEnable.Id);
                Console.WriteLine($"PASS3 enabled provider removed from list [{toEnable.Id}]: {(gone ? "PASS" : "FAIL")}");
                if (!gone) failures++;
            }
            if (toDisable != null)
            {
                bool added = ids.Contains(toDisable.Id);
                Console.WriteLine($"PASS3 disabled provider added to list [{toDisable.Id}]: {(added ? "PASS" : "FAIL")}");
                if (!added) failures++;
            }

            bool kept = ids.Contains(unrelated);
            Console.WriteLine($"PASS3 unrelated id preserved [{unrelated}]: {(kept ? "PASS" : "FAIL")}");
            if (!kept) failures++;

            Console.WriteLine($"PASS3 list size {disabledBefore} -> {ids.Count} (one enabled, one disabled)");
        }

        // ---- Pass 4: MCP / default models / permissions round-trip and edits. ----
        ConfigDocument tools = ConfigStore.Load(work);
        JToken beforeTools = JToken.Parse(File.ReadAllText(work));
        ConfigStore.Save(tools, work);
        JToken afterTools = JToken.Parse(File.ReadAllText(work));
        bool toolsLossless = JToken.DeepEquals(beforeTools, afterTools);
        Console.WriteLine("PASS4 untouched mcp/model/permission round-trip: " + (toolsLossless ? "PASS" : "FAIL"));
        if (!toolsLossless) failures++;

        tools.DefaultModel = "local-relay/gpt-4o";
        tools.SmallModel = "local-relay/gpt-4o";
        tools.McpServers.Add(new McpServer
        {
            Name = "zz-selftest-mcp",
            Type = "local",
            Command = "npx -y zz-mcp@latest",
            Enabled = true,
            IsNew = true
        });
        tools.Permissions.Add(new PermissionRule { Path = "bash", Effect = "ask", IsNew = true });
        tools.Permissions.Add(new PermissionRule { Path = "bash › rm -rf *", Effect = "deny", IsNew = true });
        ConfigStore.Save(tools, work);

        JToken editedTools = JToken.Parse(File.ReadAllText(work));
        bool okModel = (string)editedTools["model"] == "local-relay/gpt-4o";
        Console.WriteLine("PASS4 default model written: " + (okModel ? "PASS" : "FAIL"));
        if (!okModel) failures++;
        JToken mcpNode = editedTools["mcp"]?["zz-selftest-mcp"];
        bool okMcp = mcpNode != null && (string)mcpNode["type"] == "local"
            && mcpNode["command"] is JArray cmds && cmds.Count == 3;
        Console.WriteLine("PASS4 mcp server written: " + (okMcp ? "PASS" : "FAIL"));
        if (!okMcp) failures++;
        JToken permNode = editedTools["permission"]?["bash"];
        bool okPerm = permNode is JObject nested && (string)nested["*"] == "ask"
            && (string)nested["rm -rf *"] == "deny";
        Console.WriteLine("PASS4 permission written: " + (okPerm ? "PASS" : "FAIL"));
        if (!okPerm) failures++;

        // Delete what Pass 4 added: the keys must disappear again.
        ConfigDocument cleanup = ConfigStore.Load(work);
        McpServer addedMcp = cleanup.McpServers.Find(m => m.Name == "zz-selftest-mcp");
        if (addedMcp != null) { cleanup.McpServers.Remove(addedMcp); cleanup.DeletedMcpIds.Add(addedMcp.Name); }
        cleanup.DefaultModel = "";
        cleanup.SmallModel = "";
        foreach (PermissionRule rule in new List<PermissionRule>(cleanup.Permissions))
        {
            if (rule.Path == "bash" || rule.Path.StartsWith("bash › "))
            {
                cleanup.Permissions.Remove(rule);
                cleanup.DeletedPermissionPaths.Add(rule.Path);
            }
        }
        cleanup.DeletedPermissionPaths.Add("bash");
        ConfigStore.Save(cleanup, work);
        JToken cleaned = JToken.Parse(File.ReadAllText(work));
        bool okClean = cleaned["mcp"]?["zz-selftest-mcp"] == null
            && cleaned["permission"]?["bash"] == null;
        Console.WriteLine("PASS4 deleted mcp/permission removed: " + (okClean ? "PASS" : "FAIL"));
        if (!okClean) failures++;

        // ---- Pass 5: model limits (limit.context / limit.output) are written and removed. ----
        ConfigDocument limits = ConfigStore.Load(work);
        ProviderEntry limitTarget = limits.Providers.Count > 1 ? limits.Providers[1] : limits.Providers[0];
        var limited = new ModelEntry
        {
            Id = "zz-limit-model",
            DisplayName = "Limit Model",
            ContextWindow = "1048576",
            MaxOutputTokens = "65536",
            IsNew = true
        };
        limitTarget.Models.Add(limited);
        ConfigStore.Save(limits, work);

        JToken limitSaved = JToken.Parse(File.ReadAllText(work));
        JToken limitNode = limitSaved["provider"]?[limitTarget.Id]?["models"]?["zz-limit-model"]?["limit"];
        bool limitWritten = limitNode != null
            && (long?)limitNode["context"] == 1048576
            && (long?)limitNode["output"] == 65536;
        Console.WriteLine("PASS5 model limits written as numbers: " + (limitWritten ? "PASS" : "FAIL"));
        if (!limitWritten) failures++;

        ConfigDocument limitReload = ConfigStore.Load(work);
        ModelEntry reloadedLimit = null;
        foreach (ProviderEntry p in limitReload.Providers)
        {
            ModelEntry found = p.Models.Find(m => m.Id == "zz-limit-model");
            if (found != null) { reloadedLimit = found; break; }
        }
        bool limitLoaded = reloadedLimit != null
            && reloadedLimit.ContextWindow == "1048576"
            && reloadedLimit.MaxOutputTokens == "65536"
            && reloadedLimit.ContextWasSet;
        Console.WriteLine("PASS5 model limits load back into the editor: " + (limitLoaded ? "PASS" : "FAIL"));
        if (!limitLoaded) failures++;

        if (reloadedLimit != null)
        {
            reloadedLimit.ContextWindow = "";
            reloadedLimit.MaxOutputTokens = "";
            ConfigStore.Save(limitReload, work);

            JToken cleared = JToken.Parse(File.ReadAllText(work));
            JToken clearedModel = cleared["provider"]?[limitTarget.Id]?["models"]?["zz-limit-model"];
            bool limitCleared = clearedModel != null && clearedModel["limit"] == null
                && (string)clearedModel["name"] == "Limit Model";
            Console.WriteLine("PASS5 cleared limits removed, name kept: " + (limitCleared ? "PASS" : "FAIL"));
            if (!limitCleared) failures++;
        }

        // ---- Pass 6: models.dev matching (offline fixture, no network). ----
        ModelCatalog.LoadFromText(CatalogApiFixture, CatalogModelsFixture);
        bool catalogLoaded = ModelCatalog.HaveData;
        Console.WriteLine("PASS6 catalog fixture loaded: " + (catalogLoaded ? "PASS" : "FAIL"));
        if (!catalogLoaded) failures++;

        CatalogProvider byHost = ModelCatalog.MatchProvider("deepseek", "@ai-sdk/openai-compatible", "https://api.deepseek.com/v1");
        Console.WriteLine("PASS6 provider matched by base URL host: " + (byHost != null && byHost.Id == "deepseek" ? "PASS" : "FAIL"));
        if (byHost == null || byHost.Id != "deepseek") failures++;

        CatalogProvider byId = ModelCatalog.MatchProvider("zai", "", "");
        Console.WriteLine("PASS6 provider matched by id: " + (byId != null && byId.Id == "zai" ? "PASS" : "FAIL"));
        if (byId == null || byId.Id != "zai") failures++;

        CatalogMatch exact = ModelCatalog.FindModel("deepseek", "@ai-sdk/openai-compatible", "https://api.deepseek.com/v1", "deepseek-v4-flash");
        bool exactOk = exact.Found && exact.Model.Name == "DeepSeek V4 Flash"
            && exact.Model.Context == 1000000 && exact.Model.Output == 393216;
        Console.WriteLine("PASS6 model matched with limits: " + (exactOk ? "PASS" : "FAIL"));
        if (!exactOk) failures++;

        CatalogMatch relay = ModelCatalog.FindModel("openrouter", "@openrouter/ai-sdk-provider", "https://openrouter.ai/api/v1", "deepseek/deepseek-v4-flash");
        bool relayOk = relay.Found && relay.Model.Context == 163840 && relay.Model.Output == 32768;
        Console.WriteLine("PASS6 provider-specific limits win over model metadata: " + (relayOk ? "PASS" : "FAIL"));
        if (!relayOk) failures++;

        CatalogMatch suffix = ModelCatalog.FindModel("relay-example", "@ai-sdk/openai-compatible", "https://api.relay-example.dev/v1", "z-ai/glm-5.3-flash");
        bool suffixOk = suffix.Found && suffix.Model.Name == "GLM-5.3-Flash" && suffix.Model.Context == 1000000;
        Console.WriteLine("PASS6 relay model matched by id suffix: " + (suffixOk ? "PASS" : "FAIL"));
        if (!suffixOk) failures++;

        CatalogMatch dotted = ModelCatalog.FindModel("relay-example", "@ai-sdk/openai-compatible", "https://api.relay-example.dev/v1", "kimi.k3");
        bool dottedOk = dotted.Found && dotted.Model.Name == "Kimi K3";
        Console.WriteLine("PASS6 punctuation-insensitive fallback match: " + (dottedOk ? "PASS" : "FAIL"));
        if (!dottedOk) failures++;

        CatalogMatch missing = ModelCatalog.FindModel("deepseek", "", "https://api.deepseek.com/v1", "zz-not-a-real-model");
        Console.WriteLine("PASS6 unknown model reports no match: " + (!missing.Found ? "PASS" : "FAIL"));
        if (missing.Found) failures++;

        CatalogListing listing = ModelCatalog.ListModels("deepseek", "@ai-sdk/openai-compatible", "https://api.deepseek.com/v1");
        bool listingOk = listing.Provider != null && listing.Provider.Id == "deepseek" && listing.Models.Count == 2;
        Console.WriteLine("PASS6 provider catalog listed for Smart add: " + (listingOk ? "PASS" : "FAIL"));
        if (!listingOk) failures++;

        CatalogListing fallbackListing = ModelCatalog.ListModels("relay-example", "@ai-sdk/openai-compatible", "https://api.relay-example.dev/v1");
        bool fallbackOk = fallbackListing.Provider == null && fallbackListing.Models.Count == 0;
        Console.WriteLine("PASS6 unknown provider lists nothing: " + (fallbackOk ? "PASS" : "FAIL"));
        if (!fallbackOk) failures++;

        // ---- Pass 7: smart sync fills what is empty, keeps what the user set. ----
        string smartPath = Path.Combine(temp, "smart-config.json");
        File.WriteAllText(smartPath,
            "{\n" +
            "  \"provider\": {\n" +
            "    \"deepseek\": {\n" +
            "      \"npm\": \"@ai-sdk/openai-compatible\",\n" +
            "      \"options\": { \"baseURL\": \"https://api.deepseek.com/v1\" },\n" +
            "      \"models\": {\n" +
            "        \"deepseek-v4-flash\": { \"name\": \"My Custom Name\" },\n" +
            "        \"glm-5.3-flash\": { \"name\": \"GLM\" , \"limit\": { \"context\": 64000 } }\n" +
            "      }\n" +
            "    }\n" +
            "  }\n" +
            "}\n");

        SmartStateStore.FilePath = Path.Combine(temp, "smart-models.json");
        SmartStateStore.Reload();

        ConfigDocument smartDoc = ConfigStore.Load(smartPath);
        SmartSync.SeedDocument(smartDoc);
        int syncedCount = SmartSync.Apply(smartDoc, false);
        SmartStateStore.Save();

        ProviderEntry smartProvider = smartDoc.Providers[0];
        ModelEntry flash = smartProvider.Models.Find(m => m.Id == "deepseek-v4-flash");
        ModelEntry glm = smartProvider.Models.Find(m => m.Id == "glm-5.3-flash");
        bool flashOk = flash != null && flash.DisplayName == "My Custom Name"
            && flash.ContextWindow == "1000000" && flash.MaxOutputTokens == "393216";
        bool glmOk = glm != null && glm.ContextWindow == "64000" && glm.MaxOutputTokens == "131072";
        Console.WriteLine("PASS7 fills empty limits, keeps display name: " + (flashOk ? "PASS" : "FAIL"));
        Console.WriteLine("PASS7 keeps manual context, fills missing output: " + (glmOk ? "PASS" : "FAIL"));
        if (!flashOk) failures++;
        if (!glmOk) failures++;
        if (syncedCount != 2) { Console.WriteLine("PASS7 expected 2 synced models, got " + syncedCount + ": FAIL"); failures++; }

        ConfigStore.Save(smartDoc, smartPath);
        JToken smartSaved = JToken.Parse(File.ReadAllText(smartPath));
        JToken flashSaved = smartSaved["provider"]?["deepseek"]?["models"]?["deepseek-v4-flash"];
        bool smartWritten = (long?)flashSaved?["limit"]?["context"] == 1000000
            && (long?)flashSaved?["limit"]?["output"] == 393216
            && (string)flashSaved?["name"] == "My Custom Name";
        Console.WriteLine("PASS7 smart limits land in the config: " + (smartWritten ? "PASS" : "FAIL"));
        if (!smartWritten) failures++;

        SmartStateStore.Reload();
        SmartModelState flashState = SmartStateStore.Get("deepseek", "deepseek-v4-flash");
        SmartModelState glmState = SmartStateStore.Get("deepseek", "glm-5.3-flash");
        bool stateOk = flashState != null && flashState.Smart && !flashState.ManualContext && !flashState.ManualOutput
            && glmState != null && glmState.ManualContext && !glmState.ManualOutput;
        Console.WriteLine("PASS7 smart/manual flags persist locally: " + (stateOk ? "PASS" : "FAIL"));
        if (!stateOk) failures++;

        // A resync must not overwrite the manual context again.
        int resynced = SmartSync.Apply(ConfigStore.Load(smartPath), false);
        Console.WriteLine("PASS7 resync leaves manual values alone: " + (resynced == 0 ? "PASS" : "FAIL"));
        if (resynced != 0) failures++;

        // ---- Pass 8: Smart add reads the provider's own /models endpoint. ----
        bool endpointsOk = ModelDiscovery.Endpoint("https://example.com/v1") == "https://example.com/v1/models"
            && ModelDiscovery.Endpoint("https://example.com/v1/responses") == "https://example.com/v1/models"
            && ModelDiscovery.Endpoint("https://example.com/api/v2/") == "https://example.com/api/v2/models"
            && ModelDiscovery.Endpoint("https://example.com/v1/models") == "https://example.com/v1/models"
            && ModelDiscovery.Endpoint("") == ""
            && ModelDiscovery.Endpoint("not-a-url") == ""
            && ModelDiscovery.Endpoint("https://api.example.com/client/v4/accounts/${ACCOUNT}/ai/v1") == "";
        Console.WriteLine("PASS8 /models endpoint derived from the Base URL: " + (endpointsOk ? "PASS" : "FAIL"));
        if (!endpointsOk) failures++;

        var server = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            server.Start();
            int port = ((IPEndPoint)server.LocalEndpoint).Port;
            string requestLine = "", authorization = "";
            Task responder = Task.Run(delegate
            {
                using (TcpClient client = server.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                {
                    requestLine = reader.ReadLine();
                    string line;
                    while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                    {
                        if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)) authorization = line;
                    }
                    byte[] body = Encoding.UTF8.GetBytes("{\"data\":["
                        + "{\"id\":\"glm-test\",\"display_name\":\"Test model\",\"context_length\":200000,\"max_output_tokens\":8000},"
                        + "{\"id\":\"plain-model\"}]}");
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "
                        + body.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(header, 0, header.Length);
                    stream.Write(body, 0, body.Length);
                }
            });

            DiscoveryResult discovery = ModelDiscovery.DiscoverAsync(
                "http://127.0.0.1:" + port + "/v1", "test-key", "@ai-sdk/openai-compatible").GetAwaiter().GetResult();
            responder.GetAwaiter().GetResult();

            bool discoveryOk = discovery.Ok && discovery.Models.Count == 2
                && discovery.Models[0].Id == "glm-test" && discovery.Models[0].Name == "Test model"
                && discovery.Models[0].Context == 200000 && discovery.Models[0].Output == 8000
                && discovery.Models[1].Id == "plain-model";
            Console.WriteLine("PASS8 provider catalog parsed (limits included): " + (discoveryOk ? "PASS" : "FAIL"));
            if (!discoveryOk) failures++;

            bool requestOk = requestLine == "GET /v1/models HTTP/1.1"
                && authorization == "Authorization: Bearer test-key";
            Console.WriteLine("PASS8 requests the provider path with the key: " + (requestOk ? "PASS" : "FAIL"));
            if (!requestOk) failures++;

            DiscoveryResult noBase = ModelDiscovery.DiscoverAsync("", "k", "").GetAwaiter().GetResult();
            Console.WriteLine("PASS8 missing Base URL reports an error: " + (!noBase.Ok && noBase.Error.Length > 0 ? "PASS" : "FAIL"));
            if (noBase.Ok) failures++;
        }
        finally
        {
            server.Stop();
        }

        File.Delete(work);
        if (File.Exists(work + ".bak")) File.Delete(work + ".bak");

        // ---- Pass 9: the rules that keep a written config valid for OpenCode. ----
        failures += Pass9();

        Console.WriteLine(failures == 0 ? "RESULT: ALL CHECKS PASSED" : $"RESULT: {failures} CHECK(S) FAILED");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Three save rules that were each a way to corrupt or lose data:
    /// a limit must carry context and output together, comments must survive, and an
    /// enabled_providers whitelist has to be maintained or the toggle lies.
    /// </summary>
    private static int Pass9()
    {
        int failures = 0;
        string dir = Path.Combine(Path.GetTempPath(), "occ_pass9_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        // --- 9a: half a limit is never written. ---
        string half = Path.Combine(dir, "half.json");
        File.WriteAllText(half, "{\n  \"provider\": {\n    \"p\": {\n      \"models\": {\n"
            + "        \"m\": { \"name\": \"M\" }\n      }\n    }\n  }\n}\n");
        ConfigDocument halfDoc = ConfigStore.Load(half);
        ModelEntry halfModel = halfDoc.Providers[0].Models[0];
        halfModel.ContextWindow = "200000";
        halfModel.ContextWasSet = true;   // output deliberately left blank
        ConfigStore.Save(halfDoc, half);
        JToken halfNode = JToken.Parse(File.ReadAllText(half))["provider"]["p"]["models"]["m"];
        bool halfDropped = halfNode["limit"] == null && halfDoc.LimitWarnings.Count == 1;
        Console.WriteLine("PASS9 half limit dropped, not written: " + (halfDropped ? "PASS" : "FAIL"));
        if (!halfDropped) failures++;

        // --- 9b: both halves still write. ---
        string both = Path.Combine(dir, "both.json");
        File.WriteAllText(both, File.ReadAllText(half).Replace("\"name\": \"M\"", "\"name\": \"M\""));
        ConfigDocument bothDoc = ConfigStore.Load(both);
        ModelEntry bothModel = bothDoc.Providers[0].Models[0];
        bothModel.ContextWindow = "200000";
        bothModel.ContextWasSet = true;
        bothModel.MaxOutputTokens = "8192";
        bothModel.OutputWasSet = true;
        ConfigStore.Save(bothDoc, both);
        JToken bothNode = JToken.Parse(File.ReadAllText(both))["provider"]["p"]["models"]["m"];
        bool bothWritten = (long?)bothNode["limit"]?["context"] == 200000
            && (long?)bothNode["limit"]?["output"] == 8192
            && bothDoc.LimitWarnings.Count == 0;
        Console.WriteLine("PASS9 complete limit written: " + (bothWritten ? "PASS" : "FAIL"));
        if (!bothWritten) failures++;

        // --- 9c: comments survive a load/edit/save cycle. ---
        string jsonc = Path.Combine(dir, "commented.jsonc");
        File.WriteAllText(jsonc,
            "{\n"
            + "  // note above the schema\n"
            + "  \"$schema\": \"https://opencode.ai/config.json\",\n"
            + "  \"provider\": {\n"
            + "    // note above the provider\n"
            + "    \"p\": { \"name\": \"P\" } // note beside the provider\n"
            + "  }\n"
            + "}\n");
        ConfigDocument commentDoc = ConfigStore.Load(jsonc);
        commentDoc.Providers[0].DisplayName = "P edited";
        ConfigStore.Save(commentDoc, jsonc);
        string commented = File.ReadAllText(jsonc);
        bool commentsKept = commented.Contains("note above the schema")
            && commented.Contains("note above the provider")
            && commented.Contains("note beside the provider")
            && commented.Contains("P edited");
        Console.WriteLine("PASS9 .jsonc comments survive a save: " + (commentsKept ? "PASS" : "FAIL"));
        if (!commentsKept) failures++;

        // --- 9d: an enabled_providers whitelist is read and maintained. ---
        string white = Path.Combine(dir, "white.json");
        File.WriteAllText(white, "{\n  \"enabled_providers\": [\"alpha\"],\n  \"provider\": {\n"
            + "    \"alpha\": { \"name\": \"Alpha\" },\n    \"beta\": { \"name\": \"Beta\" }\n  }\n}\n");
        ConfigDocument whiteDoc = ConfigStore.Load(white);
        ProviderEntry alpha = whiteDoc.Providers.Find(p => p.Id == "alpha");
        ProviderEntry beta = whiteDoc.Providers.Find(p => p.Id == "beta");
        bool whiteRead = alpha != null && beta != null && !alpha.IsDisabled && beta.IsDisabled;

        beta.IsDisabled = false;                  // the user switches beta on
        ConfigStore.Save(whiteDoc, white);
        JToken afterWhite = JToken.Parse(File.ReadAllText(white));
        bool whiteMaintained = afterWhite["enabled_providers"].ToString(Formatting.None).Contains("beta")
            && afterWhite["disabled_providers"] == null;   // nothing added needlessly
        bool whiteOk = whiteRead && whiteMaintained;
        Console.WriteLine("PASS9 enabled_providers whitelist honoured: " + (whiteOk ? "PASS" : "FAIL"));
        if (!whiteOk) failures++;

        try { Directory.Delete(dir, true); } catch { }
        return failures;
    }

    private static int Check(JToken root, string provider, string label, Func<JToken, bool> test)
    {
        JToken node = root["provider"]?[provider];
        bool ok = node != null && test(node);
        Console.WriteLine($"PASS2 {label} [{provider}]: {(ok ? "PASS" : "FAIL")}");
        return ok ? 0 : 1;
    }

    /// <summary>Trimmed models.dev api.json used by the offline catalog checks.</summary>
    private const string CatalogApiFixture = @"{
  ""deepseek"": {
    ""name"": ""DeepSeek"",
    ""npm"": ""@ai-sdk/openai-compatible"",
    ""api"": ""https://api.deepseek.com"",
    ""models"": {
      ""deepseek-v4-flash"": { ""name"": ""DeepSeek V4 Flash"", ""limit"": { ""context"": 1000000, ""output"": 393216 } },
      ""deepseek-v4-pro"": { ""name"": ""DeepSeek V4 Pro"", ""limit"": { ""context"": 1000000, ""output"": 393216 } }
    }
  },
  ""zai"": {
    ""name"": ""Z.AI"",
    ""npm"": ""@ai-sdk/openai-compatible"",
    ""api"": ""https://api.z.ai/api/paas/v4"",
    ""models"": {
      ""glm-5.3-flash"": { ""name"": ""GLM-5.3-Flash"", ""limit"": { ""context"": 1000000, ""output"": 131072 } }
    }
  },
  ""openrouter"": {
    ""name"": ""OpenRouter"",
    ""npm"": ""@openrouter/ai-sdk-provider"",
    ""api"": ""https://openrouter.ai/api/v1"",
    ""models"": {
      ""deepseek/deepseek-v4-flash"": { ""name"": ""DeepSeek V4 Flash (OpenRouter)"", ""limit"": { ""context"": 163840, ""output"": 32768 } }
    }
  }
}";

    /// <summary>Trimmed models.dev models.json (provider-agnostic metadata).</summary>
    private const string CatalogModelsFixture = @"{
  ""deepseek/deepseek-v4-flash"": { ""id"": ""deepseek/deepseek-v4-flash"", ""name"": ""DeepSeek V4 Flash"", ""limit"": { ""context"": 1000000, ""output"": 393216 } },
  ""zhipuai/glm-5.3-flash"": { ""id"": ""zhipuai/glm-5.3-flash"", ""name"": ""GLM-5.3-Flash"", ""limit"": { ""context"": 1000000, ""output"": 131072 } },
  ""moonshotai/kimi-k3"": { ""id"": ""moonshotai/kimi-k3"", ""name"": ""Kimi K3"", ""limit"": { ""context"": 262144, ""output"": 32768 } },
  ""anthropic/claude-opus-5"": { ""id"": ""anthropic/claude-opus-5"", ""name"": ""Claude Opus 5"", ""limit"": { ""context"": 200000, ""output"": 64000 } }
}";

    private static void ReportDiff(JToken a, JToken b)
    {
        string[] la = a.ToString(Formatting.Indented).Split('\n');
        string[] lb = b.ToString(Formatting.Indented).Split('\n');
        int shown = 0;
        for (int i = 0; i < Math.Max(la.Length, lb.Length) && shown < 12; i++)
        {
            string x = i < la.Length ? la[i].Trim() : "<eof>";
            string y = i < lb.Length ? lb[i].Trim() : "<eof>";
            if (x != y)
            {
                Console.WriteLine($"  line {i + 1}: before={x}  after={y}");
                shown++;
            }
        }
    }
}
