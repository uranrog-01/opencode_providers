import tkinter as tk
from tkinter import ttk, messagebox, filedialog
import json
import os

class OpenCodeConfigEditor:
    def __init__(self, root):
        self.root = root
        self.root.title("OpenCode Desktop Provider Editor")
        self.root.geometry("650x550")
        self.root.configure(bg="#1e1e2e")
        
        # Configure Styles
        self.style = ttk.Style()
        self.style.theme_use("clam")
        
        # Dark Theme Palette
        self.bg_color = "#1e1e2e"
        self.card_color = "#252538"
        self.accent_color = "#89b4fa"
        self.text_color = "#cdd6f4"
        self.button_color = "#45475a"
        
        self.style.configure(".", background=self.bg_color, foreground=self.text_color)
        self.style.configure("TLabel", background=self.bg_color, foreground=self.text_color, font=("Segoe UI", 10))
        self.style.configure("Header.TLabel", font=("Segoe UI", 14, "bold"), foreground=self.accent_color)
        self.style.configure("TCombobox", fieldbackground=self.card_color, background=self.button_color, foreground=self.text_color)
        
        # Main Layout Frame
        main_frame = tk.Frame(root, bg=self.bg_color, padx=20, pady=20)
        main_frame.pack(fill=tk.BOTH, expand=True)
        
        # Title
        title_label = ttk.Label(main_frame, text="OpenCode Provider Configuration Manager", style="Header.TLabel")
        title_label.pack(anchor=tk.W, pady=(0, 15))
        
        # File Selector Card
        file_card = tk.LabelFrame(main_frame, text=" 1. Target Configuration File ", bg=self.bg_color, fg=self.accent_color, font=("Segoe UI", 10, "bold"), padx=10, pady=10)
        file_card.pack(fill=tk.X, pady=(0, 15))
        
        self.file_path_var = tk.StringVar(value=os.path.join(os.getcwd(), "opencode.json"))
        file_entry = tk.Entry(file_card, textvariable=self.file_path_var, bg=self.card_color, fg=self.text_color, insertbackground=self.text_color, bd=0, relief=tk.FLAT, font=("Segoe UI", 10))
        file_entry.pack(side=tk.LEFT, fill=tk.X, expand=True, ipady=4, padx=(0, 10))
        
        browse_btn = tk.Button(file_card, text="Browse...", command=self.browse_file, bg=self.button_color, fg=self.text_color, activebackground=self.accent_color, activeforeground=self.bg_color, bd=0, padx=10, relief=tk.FLAT, font=("Segoe UI", 9, "bold"))
        browse_btn.pack(side=tk.RIGHT)
        
        # Settings Card
        settings_card = tk.LabelFrame(main_frame, text=" 2. Provider Settings ", bg=self.bg_color, fg=self.accent_color, font=("Segoe UI", 10, "bold"), padx=10, pady=10)
        settings_card.pack(fill=tk.BOTH, expand=True, pady=(0, 15))
        
        # Provider Type
        ttk.Label(settings_card, text="Provider Type:").grid(row=0, column=0, sticky=tk.W, pady=5)
        self.provider_var = tk.StringVar(value="anthropic")
        provider_menu = ttk.Combobox(settings_card, textvariable=self.provider_var, values=["anthropic", "openai", "ollama", "custom"], state="readonly")
        provider_menu.grid(row=0, column=1, sticky=tk.EW, pady=5, padx=(10, 0))
        provider_menu.bind("<<ComboboxSelected>>", self.on_provider_change)
        
        # Base URL
        ttk.Label(settings_card, text="Base URL:").grid(row=1, column=0, sticky=tk.W, pady=5)
        self.url_var = tk.StringVar(value="https://api.anthropic.com")
        self.url_entry = tk.Entry(settings_card, textvariable=self.url_var, bg=self.card_color, fg=self.text_color, insertbackground=self.text_color, bd=0, relief=tk.FLAT, font=("Segoe UI", 10))
        self.url_entry.grid(row=1, column=1, sticky=tk.EW, pady=5, padx=(10, 0), ipady=4)
        
        # Models Whitelist
        ttk.Label(settings_card, text="Models (Comma Separated):").grid(row=2, column=0, sticky=tk.NW, pady=5)
        self.models_text = tk.Text(settings_card, height=4, bg=self.card_color, fg=self.text_color, insertbackground=self.text_color, bd=0, relief=tk.FLAT, font=("Segoe UI", 10), wrap=tk.WORD)
        self.models_text.grid(row=2, column=1, sticky=tk.NSEW, pady=5, padx=(10, 0))
        self.models_text.insert(tk.END, "claude-3-5-sonnet-20241022")
        
        settings_card.columnconfigure(1, weight=1)
        settings_card.rowconfigure(2, weight=1)
        
        # Action Buttons
        btn_frame = tk.Frame(main_frame, bg=self.bg_color)
        btn_frame.pack(fill=tk.X, side=tk.BOTTOM)
        
        load_btn = tk.Button(btn_frame, text="Load Current Config", command=self.load_config, bg=self.button_color, fg=self.text_color, activebackground=self.accent_color, activeforeground=self.bg_color, bd=0, padx=10, pady=10, relief=tk.FLAT, font=("Segoe UI", 10, "bold"))
        load_btn.pack(side=tk.LEFT, padx=(0, 10))
        
        save_btn = tk.Button(btn_frame, text="Apply Changes to JSON", command=self.save_config, bg="#a6e3a1", fg="#11111b", activebackground=self.accent_color, activeforeground=self.bg_color, bd=0, padx=10, pady=10, relief=tk.FLAT, font=("Segoe UI", 10, "bold"))
        save_btn.pack(side=tk.RIGHT)

    def browse_file(self):
        file_path = filedialog.askopenfilename(filetypes=[("JSON files", "*.json"), ("All files", "*.*")])
        if file_path:
            self.file_path_var.set(file_path)
            self.load_config()

    def on_provider_change(self, event=None):
        prov = self.provider_var.get()
        urls = {
            "anthropic": "https://api.anthropic.com",
            "openai": "https://api.openai.com/v1",
            "ollama": "http://localhost:11434",
            "custom": ""
        }
        self.url_var.set(urls.get(prov, ""))

    def load_config(self):
        path = self.file_path_var.get()
        if not os.path.exists(path):
            messagebox.showinfo("New File", "Target config file doesn't exist yet. Saving will create a new one.")
            return
            
        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
            
            provider_block = data.get("provider", {})
            if not provider_block:
                messagebox.showwarning("Empty", "No active provider block found in this file.")
                return
                
            # Detect first provider key
            prov_key = list(provider_block.keys())[0]
            if prov_key in ["anthropic", "openai", "ollama"]:
                self.provider_var.set(prov_key)
            else:
                self.provider_var.set("custom")
                
            prov_data = provider_block.get(prov_key, {})
            base_url = prov_data.get("options", {}).get("baseURL", "")
            self.url_var.set(base_url)
            
            whitelist = prov_data.get("whitelist", [])
            self.models_text.delete("1.0", tk.END)
            self.models_text.insert(tk.END, ", ".join(whitelist))
            
        except Exception as e:
            messagebox.showerror("Error", f"Failed to parse config file: {str(e)}")

    def save_config(self):
        path = self.file_path_var.get()
        prov = self.provider_var.get()
        url = self.url_var.get().strip()
        models_raw = self.models_text.get("1.0", tk.END).strip()
        
        models = [m.strip() for m in models_raw.split(",") if m.strip()]
        
        # Load existing data if available
        data = {}
        if os.path.exists(path):
            try:
                with open(path, "r", encoding="utf-8") as f:
                    data = json.load(f)
            except:
                pass
                
        if "provider" not in data or not isinstance(data["provider"], dict):
            data["provider"] = {}
            
        # Structure the config update
        data["provider"][prov] = {
            "options": {
                "baseURL": url
            },
            "whitelist": models
        }
        
        try:
            with open(path, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
            messagebox.showinfo("Success", f"Configuration updated successfully!\nSaved to: {path}")
        except Exception as e:
            messagebox.showerror("Error", f"Could not write to file: {str(e)}")

if __name__ == "__main__":
    root = tk.Tk()
    app = OpenCodeConfigEditor(root)
    root.mainloop()
