<div align="center">

# OpenCode Providers Tool

**A fast, visual provider editor for OpenCode. Add API keys in seconds, pull live models with 1-Click Smart Add, and auto-sync token limits via models.dev.**

[![GitHub Release](https://img.shields.io/github/v/release/uranrog-01/opencode_providers?style=flat-square&color=3b82f6)](https://github.com/uranrog-01/opencode_providers/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-10b981.svg?style=flat-square)](LICENSE)
[![VirusTotal Clean](https://img.shields.io/badge/VirusTotal-Clean%20(0%2F72)-brightgreen?style=flat-square&logo=virustotal)](https://www.virustotal.com/gui/file/965a31b4cc005e6f8f268b78e099f8ef7b6a13d29dc6c305e08daa67028e9bd1?nocache=1)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue?style=flat-square&logo=windows)](https://github.com/uranrog-01/opencode_providers/releases)

<br />

[**Download OpenCodeProvidersTool.exe (v1.0.0)**](https://github.com/uranrog-01/opencode_providers/releases/latest/download/OpenCodeProvidersTool.exe) • [**Product Website**](https://uranrog-01.github.io/opencode_providers/) • [**VirusTotal Report**](https://www.virustotal.com/gui/file/965a31b4cc005e6f8f268b78e099f8ef7b6a13d29dc6c305e08daa67028e9bd1?nocache=1)

<br />

<img src="site/assets/app-window.png" alt="OpenCode Providers Tool" width="860" />

</div>

---

## Why OpenCode Providers Tool?

Configuring AI providers in [OpenCode](https://github.com/opencode-ai) normally means manually editing `.jsonc` configuration files. That introduces constant friction:

- **Missing token limits break OpenCode:** OpenCode requires both `limit.context` and `limit.output`. Guessing or omitting one causes startup errors.
- **Comments get wiped:** Standard JSON tools delete your `// ...` notes on save.
- **Copy-pasting model IDs is tedious:** Entering long IDs for dozens of models leads to frequent typos.

**OpenCode Providers Tool** is a dedicated, single-file Windows desktop utility that replaces raw JSON editing with a fast, safe, and visual dashboard.

---

## How It Works

### 1. Configure Providers & API Keys
Add pre-set or custom providers (DeepSeek, OpenRouter, Groq, Ollama, Anthropic, or custom relays). Click **Get API Key** to open the provider's token dashboard directly. Keys are masked with a toggle to reveal, and blank fields never overwrite existing secrets.

### 2. 1-Click Smart Add for Models
Skip typing model names by hand. Click **Smart add** to query the provider's live `{baseURL}/models` endpoint using your API key. Select the models you want to import in bulk.

<div align="center">
  <img src="site/assets/smart-add.png" alt="Smart Add Dialog" width="680" />
</div>

### 3. Auto-Sync Token Limits via models.dev
Token limits are looked up automatically against [models.dev](https://models.dev). Context window and max output limits are populated with verified values. Per-field `auto` vs `manual` flags ensure custom overrides are respected.

<div align="center">
  <img src="site/assets/smart-model.png" alt="Smart Configuration Dialog" width="520" />
</div>

### 4. Instantly Connected in OpenCode
When you hit **Save changes**, OpenCode's config is updated. All your configured providers immediately appear under OpenCode's native **Connected providers** screen—ready to use without restarting or debugging JSON syntax.

<div align="center">
  <img src="site/assets/opencode-connected-providers.png" alt="Connected Providers in OpenCode" width="760" />
</div>

---

## Safety & Data Integrity

- **Comments Always Survive:** A custom parser extracts `.jsonc` comments and writes them back in their exact positions.
- **Automatic Backups:** Duplicates your config to `<filename>.bak` before every write.
- **100% Private & Local:** Zero telemetry, no cloud backend. Your API keys never leave your machine.
- **Antivirus Clean:** Verified **0/72 Clean** on [VirusTotal](https://www.virustotal.com/gui/file/965a31b4cc005e6f8f268b78e099f8ef7b6a13d29dc6c305e08daa67028e9bd1?nocache=1).
- **Single Portable File:** Targets pre-installed .NET Framework 4.8. Weighs ~870 KB with `Newtonsoft.Json` embedded. No installer required.

---

## Quick Start

### Download & Run
Download the standalone `.exe` directly from [Releases](https://github.com/uranrog-01/opencode_providers/releases/latest/download/OpenCodeProvidersTool.exe), or run via PowerShell:

```powershell
irm https://github.com/uranrog-01/opencode_providers/releases/latest/download/OpenCodeProvidersTool.exe -OutFile OpenCodeProvidersTool.exe
.\OpenCodeProvidersTool.exe
```

*Optional:* Pass a custom config path directly:
```cmd
OpenCodeProvidersTool.exe "C:\path\to\opencode.jsonc"
```

---

## Build from Source

Requires [.NET SDK 8.0+](https://dotnet.microsoft.com/download):

```bash
git clone https://github.com/uranrog-01/opencode_providers.git
cd opencode_providers/OpenCodeProvidersTool
dotnet build -c Release -o ..\release
```

---

## License

Open source under the [MIT License](LICENSE). Created by **zer0ne**.  
*Not affiliated with OpenCode. All configuration edits are performed locally.*
