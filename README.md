<div align="center">
  <a href="https://uranrog-01.github.io/opencode_providers/">
    <img src="site/assets/logo.png" alt="OpenCode Providers Tool" width="68" height="68" />
  </a>

  <h1>OpenCode Providers Tool</h1>

  <p><b>The visual provider &amp; model manager for <a href="https://opencode.ai">OpenCode</a></b></p>
  <p>Add, edit, or remove models on existing providers in-place — <em>without ever disconnecting them</em>.</p>

  <p>
    <a href="https://github.com/uranrog-01/opencode_providers/releases/latest"><img alt="Latest Release" src="https://img.shields.io/github/v/release/uranrog-01/opencode_providers?style=flat-square&color=10b981&labelColor=18181b" /></a>
    <a href="https://github.com/uranrog-01/opencode_providers/releases"><img alt="Windows 10 / 11" src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078d4?style=flat-square&logo=windows11&logoColor=white&labelColor=18181b" /></a>
    <a href="https://www.virustotal.com/gui/file/965a31b4cc005e6f8f268b78e099f8ef7b6a13d29dc6c305e08daa67028e9bd1?nocache=1"><img alt="VirusTotal Clean" src="https://img.shields.io/badge/VirusTotal-0%2F72%20Clean-success?style=flat-square&logo=virustotal&logoColor=white&labelColor=18181b" /></a>
    <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/License-MIT-64748b?style=flat-square&labelColor=18181b" /></a>
  </p>

  <p>
    <a href="https://github.com/uranrog-01/opencode_providers/releases/latest/download/OpenCodeProvidersTool.exe"><b>⬇️ Download Portable .exe (~870 KB)</b></a> &nbsp;&bull;&nbsp;
    <a href="https://uranrog-01.github.io/opencode_providers/"><b>🌐 Product Website</b></a> &nbsp;&bull;&nbsp;
    <a href="https://www.virustotal.com/gui/file/965a31b4cc005e6f8f268b78e099f8ef7b6a13d29dc6c305e08daa67028e9bd1?nocache=1"><b>🛡️ VirusTotal Clean Report</b></a>
  </p>

  <br />

  <a href="https://github.com/uranrog-01/opencode_providers/releases/latest/download/OpenCodeProvidersTool.exe">
    <img src="site/assets/apptool_main.jpg" alt="OpenCode Providers Tool Interface" width="880" />
  </a>
</div>

<br />

---

### The Problem & The Solution

In [OpenCode](https://opencode.ai), connected providers and custom models are stored in a `.jsonc` configuration file.

* **The Problem:** In OpenCode's native UI, once a provider is connected, you cannot modify, add, or prune its models without **disconnecting the provider entirely** or manually editing delicate JSONC files. Hand-editing risks syntax mistakes, wiped comments, and broken token limit configurations.
* **The Solution:** **OpenCode Providers Tool** lets you edit any provider **in-place**. Add new models, tweak limits, import live model catalogs, or remove old entries with instant automatic backups — leaving your provider connected and your comments 100% intact.

<div align="center">
  <p><sub><b>OpenCode Providers View — easily configured and managed via OpenCode Providers Tool:</b></sub></p>
  <img src="site/assets/opencodeapp.png" alt="OpenCode Connected Providers" width="820" />
</div>

<br />

---

### Key Features

* **In-Place Provider Editing** — Modify existing providers directly. Add, rename, or delete models without disconnecting or re-authenticating.
* **Smart Add Live Model Catalog** — Query live `{baseURL}/models` endpoints with one click and batch-import models automatically.
* **Intelligent Token Limits** — Auto-populates exact context and output limits powered by [models.dev](https://models.dev) with manual override controls.
* **Comment-Safe JSONC Engine** — Preserves all existing `//` line comments, `/* */` block comments, and custom structure.
* **Automatic Safety Backups** — Generates a timestamped `.bak` copy of your configuration file before every save.
* **100% Local & Private** — Single portable executable with zero network telemetry. API keys never leave your machine.

---

### Visual Workflow

#### 1 · Select & Edit Providers In-Place
Choose any existing provider from the left panel. View its models, API key, base URL, and active state without disconnecting.

#### 2 · Smart Add Models from Live Endpoints
Hit **Smart Add** to fetch all available models directly from your provider's live endpoint:

<div align="center">
  <img src="site/assets/smart-add.png" alt="Smart Add Live Dialog" width="620" />
</div>

<br />

#### 3 · Auto-Sync Token Limits & Options
Models.dev database auto-fills context and output token limits with single-click precision:

<div align="center">
  <img src="site/assets/smart-model.png" alt="Smart Model Token Limits" width="520" />
</div>

<br />

#### 4 · Instantly Active in OpenCode
Save changes and switch to OpenCode — all models and providers are immediately ready to use.

---

### Comparison

| Feature | Hand-Editing `.jsonc` | Native OpenCode UI | OpenCode Providers Tool |
| :--- | :--- | :--- | :--- |
| **Edit Connected Providers** | Risky raw file edits | Requires Disconnecting | **In-Place Editing (Zero Disconnects)** |
| **Add / Prune Models** | Manual copy-pasting | Not supported | **1-Click Visual Management** |
| **Model Discovery** | Check provider docs | None | **Live `{baseURL}/models` Query** |
| **Token Limits** | Guess / search specs | None | **Auto-Filled via models.dev** |
| **Preserves Comments** | Formatters strip them | Overwritten | **100% Comment-Safe Engine** |
| **Safety Backups** | Manual copies | None | **Automatic `.bak` on every save** |

---

### Quick Installation

Open PowerShell and run:

```powershell
irm https://github.com/uranrog-01/opencode_providers/releases/latest/download/OpenCodeProvidersTool.exe -OutFile OpenCodeProvidersTool.exe
.\OpenCodeProvidersTool.exe
```

* **Standalone Portable Executable** (~870 KB)
* **Zero Dependencies** — Runs on .NET Framework 4.8 (pre-installed on Windows 10 & 11)
* **No Administrative Rights Required**

---

### Privacy & Security

* **Local Execution Only:** Operates entirely on your local machine with no external tracking or cloud relays.
* **Direct Connections Only:** API keys are sent strictly to your specified provider endpoints when querying models.
* **VirusTotal Verified:** Scanned and verified **0/72 Clean** on [VirusTotal](https://www.virustotal.com/gui/file/965a31b4cc005e6f8f268b78e099f8ef7b6a13d29dc6c305e08daa67028e9bd1?nocache=1).

---

### Build from Source

```bash
git clone https://github.com/uranrog-01/opencode_providers.git
cd opencode_providers/OpenCodeProvidersTool
dotnet build -c Release -o ..\release
```

*Prerequisite:* [.NET SDK 8.0+](https://dotnet.microsoft.com/download)

---

<div align="center">
  <p>Open source under the <a href="LICENSE">MIT License</a> · Created by <b>zer0ne</b></p>
  <p><sub>Not affiliated with OpenCode. All edits are performed locally on your machine.</sub></p>
</div>
