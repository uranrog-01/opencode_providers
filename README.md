# OpenCode Providers Tool

**A free, open-source tool for the OpenCode app, by zer0ne.** Version 1.0.0. MIT licensed.

A Windows GUI patch for OpenCode: edits the `provider` block of an OpenCode config —
names, provider ids, base URLs, API formats, API keys, models, and the
enabled/disabled state. It finds the OpenCode installation and every config file on
the machine, then opens the global config for you. The window is fixed-size —
no resizing, no maximizing.

Models carry **Smart configuration**: recommendations (display name, context
window, max output tokens) are looked up on [models.dev](https://models.dev) and
kept in sync on every save, with per-field manual overrides. A **Smart add**
dialog reads the provider's own model catalog from its Base URL, so a whole
endpoint's models can be added at once instead of one row at a time.

## Download

Either grab `release\OpenCodeProvidersTool.exe` from this repository, or take the exe
from the [Releases](../../releases) page. It is a single file — no installer, no
runtime to install, nothing to put beside it.

There is also a product page, built from [`site\`](site) and published to GitHub Pages:
<https://uranrog-01.github.io/opencode_providers/>

## Run it

```
release\OpenCodeProvidersTool.exe
```

One file, ~870 KB, no installer and no runtime to download — it targets
**.NET Framework 4.8**, already present on Windows 10 (1903+) and Windows 11.
`Newtonsoft.Json` is embedded inside the exe, so nothing needs to sit next to it.

Optional argument: a config path to open instead of the global one.

```
OpenCodeProvidersTool.exe C:\path\opencode.jsonc
```

## The Providers page

The whole app is one focused screen, styled after OpenCode's own provider settings:
a searchable list on the left (logo tile per provider, status dot: green configured,
amber unset, grey disabled), and the detail pane on the right — logo tile, name,
`Get API Key` link (opens the OpenCode provider docs), id, enable/disable toggle,
base URL, API format dropdown, masked API key with reveal, and the model list with
add / edit / remove.

The header carries the two actions that change the document — `Add provider` and
`Save changes`. The secondary actions sit in a row under the card: `Open config…`
and `Reload` on the left, `About` on the right.

Switching a provider off adds its id to the top-level `disabled_providers`, which
is how OpenCode is told not to load it.

Every popup (About, the config picker, message boxes, the model editor) floats above
the window on a soft drop shadow, so it never blends into the panel behind it.

## About

`About` shows the app version, the author, and what was detected on this machine —
the OpenCode installation it found and how many others are present.

## Smart configuration (models.dev)

The model editor has a **Smart configuration** toggle. When it is on, the model
you are editing is looked up on models.dev using three things:

- the **model ID**, matched exactly, by id suffix (`z-ai/glm-5.3-flash` against
  `zhipuai/glm-5.3-flash`), or with punctuation folded (`kimi.k3` matches
  `moonshotai/kimi-k3`);
- the provider's **Base URL** host (`https://api.z.ai/...` → the `zai` provider);
- the provider's **API format** (the `npm` package, e.g. `@ai-sdk/anthropic`).

Provider-specific limits win over provider-agnostic model metadata, because a
relay may serve a smaller window than the lab does. When nothing matches, the
dialog says so and the values stay as you typed them.

- **Context window** is written as `limit.context`, **Max output tokens** as
  `limit.output`, and the display name as `name` — nothing else is touched, so
  `modalities`, `variants`, `cost` and friends survive.
- Every field is labelled **auto** or **manual**. Editing a field makes only that
  field manual; the others keep following recommendations. **Reset form** clears
  the manual flags and re-applies the recommendation (`auto` again).
- **Save changes** re-checks models.dev and refreshes every smart-managed model
  before writing the file. Models that have no limits yet get them; values you
  set yourself are never overwritten.

### Smart add

The provider's model list has a **Smart add** button next to **Add model**. The
list is what that provider's **Base URL** serves, never a global model dump:

1. It calls `GET {baseURL}/models` with the provider's API key (a configured
   request suffix such as `/responses` or `/chat/completions` is stripped first).
   The parser accepts the usual catalog shapes — `data`/`models` arrays, a bare
   array, object maps keyed by id, Google-style `models/...` entries — and reads
   `context_length` / `max_output_tokens` when the provider reports them.
2. models.dev is then used only to fill what the provider omitted: display name
   first, then limits (provider-specific limits before generic model metadata).
3. If the endpoint cannot be read (missing key, wrong URL, HTTP 404), models.dev's
   catalog for the provider matched by id / Base URL / API format is the
   fallback. A provider that matches neither shows the error with a **Retry**
   button; the full models.dev catalog is never listed.

- rows marked **new** are added with their recommended name and limits;
- rows marked **added** are already in your config; checking them refreshes their
  smart-managed fields;
- if a checked model has manually set limits, an **Overwrite limits that were set
  manually** switch appears — off by default, so a bulk add cannot silently
  replace values you chose.

The source line under the list names the exact endpoint used (or the models.dev
entry when the fallback is active). Nothing is written until you press **Save
changes**; a `.bak` copy is still written first.

### Where the data lives

| What | Where |
| --- | --- |
| models.dev catalog cache (12 h) | `%LOCALAPPDATA%\OpenCodeProviders\models.dev.api.json`, `models.dev.models.json` |
| Smart/manual flags per model | `%LOCALAPPDATA%\OpenCodeProviders\smart-models.json` |

The smart state is deliberately **not** stored in the opencode config: OpenCode
validates its config strictly, so unknown keys would be rejected or dropped.
First time the tool sees an existing model, a value already in the file counts as
manual and an empty one as auto — so opening a config, pressing Save, and
reopening it can only add missing limits, never rewrite your own.

models.dev is fetched with plain GETs; no config contents are sent. Offline, the
last cache is used, and if there is no cache the dialog and save simply skip
recommendations.

## What it finds automatically

**The installation** — checks `PATH` (resolving npm shims to the real package), npm, bun
and pnpm global roots, scoop, chocolatey, winget, `%LOCALAPPDATA%\Programs`
(including the Electron Desktop app: `@opencode-aidesktop`, `OpenCode`, channel
variants, `ZCode`) and `C:\Program Files`, reading the version from the package
manifest or the exe metadata. The result appears in `About` and at the top of the
config picker.

**The config file** — first hit among: next to the exe (portable),
`%USERPROFILE%\.config\opencode\` (the global config OpenCode loads by default),
`%USERPROFILE%\.opencode\`, `%APPDATA%\opencode\`. That one is opened on startup.

`Open config…` lists **every** config found, not just those four: it also honours
`XDG_CONFIG_HOME`, `%LOCALAPPDATA%\opencode`, `~/.local/share/opencode`, configs
inside a detected installation, and project folders walking up from the working
directory. Each row shows the provider count, the kind, the path, when it was last
written, and whether it is the global config. The one already open is preselected;
`Browse…` still falls back to the system file dialog.

## What it edits, and what it never touches

Editable: provider display `name`, `npm` package, `options.baseURL`,
`options.apiKey`, the `models` map (add, remove, rename via the row dialog), each
model's display `name`, `limit.context` and `limit.output` (via Smart
configuration), and the enabled/disabled state. Providers can be deleted outright.

Preserved exactly: `$schema`, `plugin`, every unedited provider, and per-model
metadata other than the two limits and the name — `modalities`, `variants`, `cost`,
an extra `limit.input`, and any key this tool does not know survive a save.
`disabled_providers` entries that belong to no provider block — built-in providers
this tool knows nothing about — are kept as they are rather than dropped.

**Comments survive a save.** Newtonsoft's JSON tree cannot hold comments (an object's
children must be properties, so `CommentHandling.Load` parses them and then drops
them), which used to mean any note in a `.jsonc` file was wiped on the first save.
They are now captured separately, keyed by the property they belong to, and written
back in the same place. A comment inside something the save deletes — a removed
model, a deleted provider — goes with it, and one sitting inside an array is written
above that array.

**Context and Max output always travel together.** OpenCode's schema requires
`limit.context` and `limit.output` on any model that declares a `limit`, so a value
for only one of them would produce a config OpenCode rejects. The model editor asks
for both or neither, and as a backstop a half limit is dropped rather than written —
the status line then reports how many models were left without limits.

**`enabled_providers` is maintained, not just preserved.** OpenCode treats that list
as a whitelist, so a provider is off either because it is in `disabled_providers` or
because the whitelist leaves it out. The per-provider toggle reflects the effective
state and, when the file uses a whitelist, switching a provider on adds it there as
well as clearing it from `disabled_providers`. Only the entries you actually changed
are touched, so unrelated ones are not rewritten.

Fields left blank are written back unchanged, so an empty API key box never erases an
existing key. Every save writes a `<filename>.bak` copy first. Unsaved edits are
tracked: the status bar shows `Unsaved changes`, and closing or reloading prompts you to
save, discard, or cancel.

Saving rewrites the file as 2-space indented JSON. A file with no comments is written
by the stock serialiser byte-for-byte as before.

## If something goes wrong

Unhandled errors are caught and written to `%TEMP%\OpenCodeProvidersTool-error.log`, and a
dialog shows the message and that path, so a crash never silently closes the window with
unsaved edits.

## Build from source

```
cd OpenCodeProvidersTool
dotnet build -c Release -o ..\release
```

Only the .NET SDK is needed; the .NET Framework 4.8 reference assemblies come from the
`Microsoft.NETFramework.ReferenceAssemblies` package, so no Visual Studio install is
required. `app.ico` is embedded as the exe icon; `Icons.CreateAppIcon()` draws the
window/taskbar icon in code.

## Automation

These need no secrets — a token is provided by GitHub automatically. `ci.yml` and
`release.yml` run on `windows-latest`; `pages.yml` runs on `ubuntu-latest` because it
only uploads a folder.

- **`.github/workflows/ci.yml`** runs on every push to `main` and on pull requests:
  it builds the app, runs `selftest` against `samples/opencode.sample.jsonc`, runs
  `uitest`, and uploads the built exe as a workflow artifact.
- **`.github/workflows/release.yml`** runs when you push a tag such as `v1.0.0`. It
  builds the exe, runs `selftest` as a gate, and publishes a GitHub Release with
  `OpenCodeProvidersTool.exe` attached and generated release notes. So shipping a
  version is:

  ```
  git tag v1.0.0
  git push origin v1.0.0
  ```

- **`.github/workflows/pages.yml`** publishes `site/` to GitHub Pages on every push to
  `main` that touches it. The page is plain HTML and CSS — there is nothing to build, so
  the job only uploads the folder. Its download button points at
  `releases/latest/download/OpenCodeProvidersTool.exe`, so the page is only fully live
  once a release exists.

`samples/opencode.sample.jsonc` exists for those runs: `selftest` needs a config to
round-trip, and a real one is not in the repository. It is also handy for trying the
editor out without touching your own setup.

```
OpenCodeProvidersTool.exe samples\opencode.sample.jsonc
```

## Tests

`selftest` compiles the same `ConfigStore.cs`, `ConfigComments.cs`,
`ModelCatalog.cs`, `ModelDiscovery.cs` and `SmartState.cs` and checks that a
load/save cycle with no edits changes nothing; intended edits apply while untouched
providers and rich model metadata survive; the enable/disable toggle edits
`disabled_providers` correctly while preserving unrelated ids; model limits
round-trip and can be cleared; the models.dev matcher finds providers by id, base URL
and npm, prefers provider-specific limits, matches relay-style model ids, and reports
no match honestly; smart sync fills empty limits while keeping values the user set;
and the Smart add discovery derives `{baseURL}/models`, sends the key and parses the
provider's catalog.

```
cd selftest
dotnet run -c Release -- "%USERPROFILE%\.config\opencode\opencode.jsonc"
```

It prints `RESULT: ALL CHECKS PASSED` and exits 0 when the config round-trips safely.
Run it against a copy of your config before trusting a new build. With no argument it
falls back to the config the app would open; in CI it is pointed at
`samples/opencode.sample.jsonc` instead.

`uitest` builds the real Smart configuration dialogs off-screen and checks the
layout, the lookup, per-field manual overrides, Reset form, and the Smart add
selection. It also writes `smart-model-dialog-opencode.png` and
`smart-add-dialog-opencode.png` for visual review.

```
dotnet run --project uitest -c Release
```

Passing a config path runs a **live preview** instead: it fetches models.dev,
prints which catalog provider each provider matches, which models each model
matches, and how many smart models a save would update — without writing
anything.

```
dotnet run --project uitest -c Release -- "%USERPROFILE%\.config\opencode\opencode.jsonc"
```

`verify-exe.ps1` checks the built artifact: that `Newtonsoft.Json.dll` really is embedded,
and that the exe launches with exactly one visible window and no error dialog.
`dragtest.ps1` grabs the custom title bar and drags it, asserting the window moves.

```
powershell -ExecutionPolicy Bypass -File verify-exe.ps1
powershell -ExecutionPolicy Bypass -File dragtest.ps1
```

## Files

| Path | What it is |
| --- | --- |
| `release\OpenCodeProvidersTool.exe` | The built app — the only file in that folder |
| `OpenCodeProvidersTool\` | C# WinForms source |
| `selftest\` | Config round-trip, limits, comments, whitelist, models.dev matcher and smart-sync tests |
| `uitest\` | Off-screen dialog checks + screenshots + live models.dev preview |
| `samples\` | `opencode.sample.jsonc` — what CI runs the tests against, and a safe config to try |
| `site\` | The product page — plain HTML and CSS, deployed to GitHub Pages |
| `.github\workflows\` | CI on every push, a Release built from a `v*` tag, and the Pages deploy |
| `verify-exe.ps1`, `dragtest.ps1`, `screenshot.ps1` | Artifact, window-drag and UI checks |
| `verify-about-popup.ps1` | Opens a named dialog, captures it with its shadow, checks nothing is left behind |
| `refresh-published.ps1` | Rebuilds `Published\` from the working tree |
| `opencode_modifier.py` | Earlier Python/tkinter version of the same tool |
| `OpenCodeConfigApp.txt` | Earlier C# draft, kept for reference |

### Source layout

| File | Contents |
| --- | --- |
| `Program.cs` | Entry point, command-line arguments, embedded-assembly resolver, crash reporting |
| `Theme.cs` | Palette, fonts, drawing helpers, DPI scaling |
| `Icons.cs` | Vector icons + the app icon, drawn on a 16x16 grid — no image assets |
| `Controls.cs` | Cards, stat tiles, pills, buttons, inputs, toggle switch, checkbox, tooltip, title bar |
| `NavControls.cs` | Sidebar nav items, group labels, the page frame |
| `ListControls.cs` | Scrollable lists with a themed scrollbar, incl. the Smart add check list |
| `Dropdown.cs` | Themed drop-down (the stock ComboBox arrow cannot be recoloured) |
| `Dialogs.cs` | Themed message, prompt, model (Smart configuration) and Smart add dialogs |
| `ModelCatalog.cs` | models.dev client: cache, provider/model matching, token formatting |
| `ModelDiscovery.cs` | The provider's own `{baseURL}/models` reader used by Smart add |
| `SmartState.cs` | Smart/manual flags per model, and the save-time smart sync |
| `OpenCodeLocator.cs` | Installation and config discovery, incl. the Desktop app |
| `ConfigComments.cs` | Captures and re-emits `.jsonc` comments, which the JSON tree cannot hold |
| `ConfigStore.cs` | Config read/write — the part that must never lose data |
| `MainForm.cs` | The Providers page and wiring |

### Two layout traps worth knowing

Both bit this codebase, and both are easy to reintroduce:

- **Anchors are resolved against the parent's size when the child is added.** A freshly
  created `Panel` is 200x100, so a `Dock=Fill` container must be given its real size
  *before* anything inside it is anchored, or right/bottom anchored children end up with
  negative margins and overrun the card.
- **A docked child swallows `WM_NCHITTEST`.** The custom title bar reports
  `HTTRANSPARENT` so hit testing falls through to the form; without it the form never
  answers `HTCAPTION` and the window cannot be dragged or resized by its top edge.

## Note on the Python version

`opencode_modifier.py` was fixed so it compiles and launches (it had an unterminated
f-string and an invalid `padding=` option on `tk.Button`, both fatal). Its save logic
still targets a different schema than the config in use: it writes a `whitelist` array
and replaces the whole provider block, which would delete `name`, `npm` and `models` for
that provider. (`whitelist` and `blacklist` are model-level filters in OpenCode, not
provider-level.) Use the exe above, or port `save_config()` to the `models` schema first.

To build the Python version into an exe anyway:

```
python -m pip install pyinstaller
pyinstaller --onefile --noconsole --name OpenCodeConfigEditor opencode_modifier.py
```

## License

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 zer0ne.

This tool is not affiliated with OpenCode. It only reads and writes the config file
you point it at; every save writes a `.bak` copy first.
