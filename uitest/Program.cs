using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCodeProvidersTool;

namespace OpenCodeUiTest
{
    /// <summary>
    /// Builds the real Smart configuration dialogs off-screen and checks them: the layout
    /// fits, the models.dev lookup fills fields, manual edits stick to their own field,
    /// and the catalogue check list drives "add + sync". Also writes screenshots so the
    /// design can be reviewed without opening the app.
    /// </summary>
    internal static class Program
    {
        private static int _failures;
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private const string ApiFixture = @"{
  ""deepseek"": {
    ""name"": ""DeepSeek"",
    ""npm"": ""@ai-sdk/openai-compatible"",
    ""api"": ""https://api.deepseek.com"",
    ""models"": {
      ""deepseek-v4-flash"": { ""name"": ""DeepSeek V4 Flash"", ""limit"": { ""context"": 1000000, ""output"": 393216 } },
      ""deepseek-v4-pro"": { ""name"": ""DeepSeek V4 Pro"", ""limit"": { ""context"": 1000000, ""output"": 393216 } }
    }
  }
}";

        private const string ModelsFixture = @"{
  ""deepseek/deepseek-v4-flash"": { ""id"": ""deepseek/deepseek-v4-flash"", ""name"": ""DeepSeek V4 Flash"", ""limit"": { ""context"": 1000000, ""output"": 393216 } },
  ""deepseek/deepseek-v4-pro"": { ""id"": ""deepseek/deepseek-v4-pro"", ""name"": ""DeepSeek V4 Pro"", ""limit"": { ""context"": 1000000, ""output"": 393216 } }
}";

        [STAThread]
        private static void Main(string[] args)
        {
            // Live preview: "OpenCodeUiTest <config path>" prints what smart configuration
            // would do for a real config, reading models.dev over the network. Nothing is
            // written to the config and the smart state file is redirected to temp.
            if (args.Length > 0 && File.Exists(args[0]))
            {
                RunLivePreview(args[0]);
                return;
            }

            Application.EnableVisualStyles();

            string temp = Path.Combine(Path.GetTempPath(), "OpenCodeUiTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            SmartStateStore.FilePath = Path.Combine(temp, "smart-models.json");
            SmartStateStore.Reload();
            ModelCatalog.CacheDirectory = temp;
            ModelCatalog.LoadFromText(ApiFixture, ModelsFixture);

            try
            {
                RunModelDialogChecks();
                RunCatalogDialogChecks();
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }

            Console.WriteLine();
            if (_failures == 0)
            {
                Console.WriteLine("RESULT: ALL CHECKS PASSED");
                Environment.Exit(0);
            }
            Console.WriteLine("RESULT: " + _failures + " CHECK(S) FAILED");
            Environment.Exit(1);
        }

        // ------------------------------------------------------------------ live preview

        private static void RunLivePreview(string path)
        {
            string temp = Path.Combine(Path.GetTempPath(), "OpenCodeUiTest-live-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            SmartStateStore.FilePath = Path.Combine(temp, "smart-models.json");
            SmartStateStore.Reload();
            ModelCatalog.CacheDirectory = temp;

            Console.WriteLine("config: " + path);
            bool loaded = ModelCatalog.RefreshAsync(true).GetAwaiter().GetResult();
            Console.WriteLine("catalog: " + ModelCatalog.Status + (loaded ? "" : "  (unavailable)"));
            if (!loaded) return;

            ConfigDocument document = ConfigStore.Load(path);
            foreach (ProviderEntry provider in document.Providers)
            {
                CatalogProvider match = ModelCatalog.MatchProvider(provider.Id, provider.Npm, provider.BaseUrl);
                int matched = 0;
                var samples = new List<string>();
                foreach (ModelEntry model in provider.Models)
                {
                    CatalogMatch result = ModelCatalog.FindModel(provider.Id, provider.Npm, provider.BaseUrl, model.Id);
                    if (!result.Found) continue;
                    matched++;
                    if (samples.Count < 3)
                    {
                        samples.Add(model.Id + " -> " + result.Model.Name
                            + " [" + ModelCatalog.FormatTokens(result.Model.Context) + " ctx / "
                            + ModelCatalog.FormatTokens(result.Model.Output) + " out]");
                    }
                }
                Console.WriteLine();
                Console.WriteLine("provider " + provider.Id
                    + "  |  catalog match: " + (match == null ? "none" : match.Id)
                    + "  |  models matched: " + matched + "/" + provider.Models.Count);
                foreach (string sample in samples) Console.WriteLine("    " + sample);

                // What Smart add would show: the provider's own /models endpoint.
                if (ModelDiscovery.Endpoint(provider.BaseUrl).Length == 0)
                {
                    Console.WriteLine("    live catalog: no Base URL \u2014 Smart add falls back to models.dev");
                    continue;
                }

                DiscoveryResult discovery = ModelDiscovery.DiscoverAsync(
                    provider.BaseUrl, provider.ApiKey, provider.Npm).GetAwaiter().GetResult();
                if (!discovery.Ok)
                {
                    Console.WriteLine("    live catalog: unavailable \u2014 " + discovery.Error);
                    continue;
                }

                int missing = 0;
                var newIds = new List<string>();
                foreach (DiscoveredModel found in discovery.Models)
                {
                    bool exists = provider.Models.Exists(delegate (ModelEntry m)
                    {
                        return string.Equals(m.Id, found.Id, StringComparison.OrdinalIgnoreCase);
                    });
                    if (exists) continue;
                    missing++;
                    if (newIds.Count < 4) newIds.Add(found.Id);
                }
                Console.WriteLine("    live catalog: " + discovery.Models.Count + " models at " + discovery.Endpoint
                    + "  |  not in config: " + missing
                    + (newIds.Count > 0 ? "  e.g. " + string.Join(", ", newIds.ToArray()) : ""));
            }

            SmartSync.SeedDocument(document);
            int synced = SmartSync.Apply(document, false);
            Console.WriteLine();
            Console.WriteLine("smart sync would update " + synced + " model(s); nothing was written.");
            foreach (ProviderEntry provider in document.Providers)
            {
                foreach (ModelEntry model in provider.Models)
                {
                    if (model.SmartConfiguration == false) continue;
                    if (model.ContextWindow.Length == 0 && model.MaxOutputTokens.Length == 0) continue;
                    SmartModelState state = SmartStateStore.Get(provider.Id, model.Id);
                    if (state == null) continue;
                    if (!state.ManualContext && !state.ManualOutput) continue;
                    Console.WriteLine("    manual keeps: " + provider.Id + "/" + model.Id
                        + (state.ManualContext ? "  context=" + model.ContextWindow : "")
                        + (state.ManualOutput ? "  output=" + model.MaxOutputTokens : ""));
                }
            }
            Console.WriteLine();
            Console.WriteLine("RESULT: live preview complete");
            try { Directory.Delete(temp, true); } catch { }
        }

        // ------------------------------------------------------------------ model dialog

        private static void RunModelDialogChecks()
        {
            Console.WriteLine("== model dialog · smart configuration ==");

            var provider = new ProviderEntry
            {
                Id = "deepseek",
                DisplayName = "DeepSeek",
                Npm = "@ai-sdk/openai-compatible",
                BaseUrl = "https://api.deepseek.com/v1"
            };
            var existing = new ModelEntry { Id = "deepseek-v4-flash", DisplayName = "My custom name" };
            SmartModelState state = SmartSync.Seed(provider, existing);

            using (var dialog = new ModelDialog(provider, existing, state))
            {
                dialog.Show();
                Application.DoEvents();

                Panel body = FindBody(dialog);
                Check("dialog body found", body != null);
                if (body == null) return;

                Check("controls stay inside the dialog", AllInside(body));
                Check("controls do not overlap", !AnyOverlap(body));

                var context = (FieldBox)typeof(ModelDialog).GetField("_contextField", Private).GetValue(dialog);
                var output = (FieldBox)typeof(ModelDialog).GetField("_outputField", Private).GetValue(dialog);
                var name = (FieldBox)typeof(ModelDialog).GetField("_nameField", Private).GetValue(dialog);

                Check("lookup fills empty limits from models.dev",
                    context.Value == "1000000" && output.Value == "393216", context.Value + " / " + output.Value);
                Check("user display name is treated as manual",
                    name.Value == "My custom name" && dialog.ManualName && !dialog.ManualContext && !dialog.ManualOutput);

                output.Value = "2000";
                Check("editing one limit marks only that field manual",
                    dialog.ManualOutput && !dialog.ManualContext && dialog.ManualName);

                context.Value = "";
                Check("clearing a limit keeps the field manual", dialog.ManualContext);

                Button reset = FindButton(body, "Reset form");
                Check("reset button found", reset != null);
                if (reset != null)
                {
                    reset.PerformClick();
                    Application.DoEvents();
                    Check("reset follows the recommendation again",
                        context.Value == "1000000" && output.Value == "393216" && name.Value == "DeepSeek V4 Flash"
                        && !dialog.ManualContext && !dialog.ManualOutput && !dialog.ManualName);
                }

                Save(dialog, "smart-model-dialog-opencode.png");
                dialog.Close();
            }
        }

        // ------------------------------------------------------------------ catalog dialog

        private static void RunCatalogDialogChecks()
        {
            Console.WriteLine("== smart add dialog · provider Base URL catalog ==");

            var server = new TcpListener(IPAddress.Loopback, 0);
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
                    const string json = "{\"data\":["
                        + "{\"id\":\"deepseek-v4-pro\",\"display_name\":\"DeepSeek V4 Pro\",\"context_length\":1000000,\"max_output_tokens\":393216},"
                        + "{\"id\":\"local-only-model\",\"display_name\":\"Local Only Model\"}]}";
                    byte[] body = Encoding.UTF8.GetBytes(json);
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "
                        + body.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(header, 0, header.Length);
                    stream.Write(body, 0, body.Length);
                }
            });

            var provider = new ProviderEntry
            {
                Id = "deepseek",
                DisplayName = "DeepSeek",
                Npm = "@ai-sdk/openai-compatible",
                BaseUrl = "http://127.0.0.1:" + port + "/v1",
                ApiKey = "test-key"
            };
            provider.Models.Add(new ModelEntry { Id = "deepseek-v4-pro", DisplayName = "DeepSeek V4 Pro" });
            SmartSync.Seed(provider, provider.Models[0]);

            try
            {
                using (var dialog = new CatalogDialog(provider))
                {
                    dialog.Show();
                    var list = (CatalogList)typeof(CatalogDialog).GetField("_list", Private).GetValue(dialog);
                    var apply = (FlatButton)typeof(CatalogDialog).GetField("_apply", Private).GetValue(dialog);
                    var count = (Label)typeof(CatalogDialog).GetField("_countLabel", Private).GetValue(dialog);
                    var note = (Label)typeof(CatalogDialog).GetField("_note", Private).GetValue(dialog);

                    bool ready = WaitFor(delegate { return list != null && list.Items.Count >= 2; }, 8000);
                    Check("provider catalog rows listed", ready, "rows=" + (list == null ? -1 : list.Items.Count));

                    Panel body = FindBody(dialog);
                    Check("controls stay inside the dialog", body != null && AllInside(body));
                    Check("controls do not overlap", body != null && !AnyOverlap(body));

                    responder.GetAwaiter().GetResult();
                    Check("requests the Base URL models endpoint", requestLine == "GET /v1/models HTTP/1.1", requestLine ?? "<none>");
                    Check("sends the API key", authorization == "Authorization: Bearer test-key", authorization ?? "<none>");
                    Check("counts only the provider's models", count.Text.Contains("2 models"), count.Text);
                    Check("source is the live provider catalog",
                        note.Text.Contains("Live catalog") && note.Text.Contains("/v1/models"), note.Text);

                    Save(dialog, "smart-add-dialog-opencode.png");

                    if (apply != null)
                    {
                        apply.PerformClick();
                        Application.DoEvents();
                    }
                    Check("apply returns the new provider model",
                        dialog.SelectedNew.Count == 1 && dialog.SelectedNew[0].Id == "local-only-model",
                        "new=" + dialog.SelectedNew.Count);
                    Check("apply returns the existing model for sync",
                        dialog.SelectedExisting.Count == 1 && dialog.SelectedExisting[0].Id == "deepseek-v4-pro",
                        "existing=" + dialog.SelectedExisting.Count);
                    dialog.Close();
                }
            }
            finally
            {
                server.Stop();
            }

            // The provider endpoint is unreachable: models.dev's catalog for the matched
            // provider is the fallback, and the whole models.dev catalog is never listed.
            Console.WriteLine("== smart add dialog · models.dev fallback ==");
            var offline = new ProviderEntry
            {
                Id = "deepseek",
                DisplayName = "DeepSeek",
                Npm = "@ai-sdk/openai-compatible",
                BaseUrl = "http://127.0.0.1:9/v1"
            };
            using (var dialog = new CatalogDialog(offline))
            {
                dialog.Show();
                var list = (CatalogList)typeof(CatalogDialog).GetField("_list", Private).GetValue(dialog);
                var note = (Label)typeof(CatalogDialog).GetField("_note", Private).GetValue(dialog);

                bool ready = WaitFor(delegate { return list != null && list.Items.Count >= 2; }, 10000);
                Check("falls back to the provider's models.dev catalog",
                    ready && note.Text.Contains("models.dev") && note.Text.Contains("DeepSeek"), note.Text);
                dialog.Close();
            }

            // A relay that is in neither place must show an error, not the whole catalog.
            Console.WriteLine("== smart add dialog · no source ==");
            var unknown = new ProviderEntry
            {
                Id = "private-relay",
                DisplayName = "Private Relay",
                Npm = "@ai-sdk/openai-compatible",
                BaseUrl = "http://127.0.0.1:9/v1"
            };
            using (var dialog = new CatalogDialog(unknown))
            {
                dialog.Show();
                var list = (CatalogList)typeof(CatalogDialog).GetField("_list", Private).GetValue(dialog);
                var apply = (FlatButton)typeof(CatalogDialog).GetField("_apply", Private).GetValue(dialog);

                // Let discovery fail, then make sure nothing is offered.
                for (int i = 0; i < 40; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(100); if (list.Items.Count > 0 || (apply != null && apply.Enabled)) break; }
                Application.DoEvents();
                Check("no catalog is offered when there is no source",
                    list.Items.Count == 0 && apply != null && !apply.Enabled,
                    "rows=" + list.Items.Count);
                dialog.Close();
            }
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMs)
        {
            for (int waited = 0; waited < timeoutMs; waited += 50)
            {
                Application.DoEvents();
                if (condition()) return true;
                System.Threading.Thread.Sleep(50);
            }
            Application.DoEvents();
            return condition();
        }

        // ------------------------------------------------------------------ helpers

        private static Panel FindBody(DarkDialog dialog)
        {
            foreach (Control child in dialog.Controls)
            {
                if (child is Panel) return child as Panel;
            }
            return null;
        }

        private static bool AllInside(Panel body)
        {
            foreach (Control child in body.Controls)
            {
                if (!body.ClientRectangle.Contains(child.Bounds))
                {
                    Console.WriteLine("      outside: " + child.GetType().Name + " " + child.Bounds);
                    return false;
                }
            }
            return true;
        }

        private static bool AnyOverlap(Panel body)
        {
            foreach (Control a in body.Controls)
            {
                if (!a.Visible) continue;
                foreach (Control b in body.Controls)
                {
                    if (ReferenceEquals(a, b) || !b.Visible) continue;
                    Rectangle intersection = Rectangle.Intersect(a.Bounds, b.Bounds);
                    if (intersection.Width > 1 && intersection.Height > 1)
                    {
                        Console.WriteLine("      overlap: " + a.GetType().Name + " " + a.Bounds
                            + " / " + b.GetType().Name + " " + b.Bounds);
                        return true;
                    }
                }
            }
            return false;
        }

        private static Button FindButton(Control parent, string text)
        {
            foreach (Control child in parent.Controls)
            {
                var button = child as Button;
                if (button != null && button.Text == text) return button;
                Button nested = FindButton(child, text);
                if (nested != null) return nested;
            }
            return null;
        }

        private static void Save(Form form, string name)
        {
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(Path.Combine(Environment.CurrentDirectory, name));
                Console.WriteLine("      screenshot: " + name);
            }
        }

        private static void Check(string name, bool condition, string detail = "")
        {
            if (condition) Console.WriteLine("  ok   " + name);
            else
            {
                _failures++;
                Console.WriteLine("  FAIL " + name + (detail.Length > 0 ? "  -- " + detail : ""));
            }
        }
    }
}
