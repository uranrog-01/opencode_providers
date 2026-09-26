using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenCodeProvidersTool
{
    public class MainForm : Form
    {
        private const int TitleBarHeight = 46;

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MAXIMIZE = 0xF030;
        private const int SC_SIZE = 0xF000;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;

        // ------------------------------------------------------------------- chrome

        private TitleBarPanel _titleBar;
        private WindowButton _btnMinimize;
        private WindowButton _btnClose;
        private Panel _contentHost;
        private Label _statusBar;

        // --------------------------------------------------------------------- pages

        private PagePanel _pageProviders;

        // providers page
        private FieldBox _searchField;
        private ProviderList _providerList;
        private FieldBox _nameField;
        private ToggleSwitch _enabledToggle;
        private Label _enabledLabel;
        private FieldBox _urlField;
        private DropdownField _formatDrop;
        private FieldBox _keyField;
        private LogoTile _logoTile;
        private FieldBox _idField;
        private ListPanel _modelBox;
        private ModelList _modelList;
        private EmptyState _modelEmpty;
        private FlatButton _btnAddModel;
        private FlatButton _btnSmartAdd;
        private Label _modelCountLabel;
        private FlatButton _btnDeleteProvider;

        // --------------------------------------------------------------------- state

        private ConfigDocument _document;
        private ProviderEntry _current;
        private string _installSummary = "";
        private bool _dirty;
        private bool _loading;
        private bool _dpiApplied;

        /// <summary>Page requested on the command line (only the providers page exists).</summary>
        public static string StartupPage = "";

        /// <summary>Config path requested on the command line.</summary>
        public static string StartupConfig = "";

        private static readonly Option[] FormatOptions =
        {
            new Option("OpenAI-compatible (v1/chat/completions)", "@ai-sdk/openai-compatible"),
            new Option("OpenAI (v1/responses)", "@ai-sdk/openai"),
            new Option("Anthropic messages (v1/messages)", "@ai-sdk/anthropic"),
            new Option("Google Gemini", "@ai-sdk/google"),
            new Option("OpenRouter", "@openrouter/ai-sdk-provider"),
            new Option("Amazon Bedrock", "@ai-sdk/amazon-bedrock")
        };

        public MainForm()
        {
            InitializeComponent();
            Application.AddMessageFilter(new DarkList.WheelRouter());
            ModelCatalog.Updated += CatalogReady;
            Shown += (s, e) => LoadStartupConfig();
        }

        /// <summary>
        /// models.dev finished loading in the background: seed smart state and refresh the
        /// list, so the next save can sync recommendations for models that just appeared.
        /// </summary>
        private void CatalogReady(object sender, EventArgs e)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    if (_document == null) return;
                    SmartSync.SeedDocument(_document);
                    RefreshModelList();
                }));
            }
            catch { }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED, flicker-free resizing
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_dpiApplied) return;
            _dpiApplied = true;
            Theme.ApplyDpiLayout(this, new Size(1160, 760), Size.Empty, Theme.DpiFactor(this));
        }

        private void InitializeComponent()
        {
            Text = "OpenCode Providers Tool";
            FormBorderStyle = FormBorderStyle.None;
            try { Icon = Icons.CreateAppIcon(); }
            catch { }
            BackColor = Theme.Page;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            // Fixed, static window: no resizing, no maximizing; the custom caption keeps
            // its own minimize/close buttons, so the system box stays enabled for Alt+F4.
            ClientSize = new Size(1160, 760);
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            BuildTitleBar();
            BuildStatusBar();
            BuildContent();

            // Docking order: the highest index is laid out first, so the title bar claims the
            // top strip, the status bar the bottom edge, and the content fills the rest.
            Controls.Add(_contentHost);
            Controls.Add(_statusBar);
            Controls.Add(_titleBar);

            ShowProvidersPage();
        }

        private void BuildStatusBar()
        {
            _statusBar = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = Theme.PageTop,
                ForeColor = Theme.TextFaint,
                Font = Theme.Small,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 16, 0),
                AutoEllipsis = true,
                Text = ""
            };
        }

        private void BuildTitleBar()
        {
            // Width matters: the anchored window buttons are positioned from the right edge,
            // so the bar must already be full width when they are added — and the offsets
            // must be computed from that width, not a stale constant, or Close lands
            // off-screen.
            _titleBar = new TitleBarPanel { Dock = DockStyle.Top, Size = new Size(1160, TitleBarHeight), Brand = "OpenCode", Subtitle = "Providers Tool" };

            int right = _titleBar.Width;

            _btnMinimize = new WindowButton
            {
                Icon = Glyph.Minimize,
                Size = new Size(44, TitleBarHeight - 2),
                Location = new Point(right - 90, 1),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnMinimize.Click += (s, e) => WindowState = FormWindowState.Minimized;

            _btnClose = new WindowButton
            {
                Icon = Glyph.Close,
                Size = new Size(44, TitleBarHeight - 2),
                Location = new Point(right - 46, 1),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnClose.Click += (s, e) => Close();

            _titleBar.Controls.AddRange(new Control[] { _btnMinimize, _btnClose });
        }

        private void BuildSidebar()
        {
            // Removed: the app is single-page and saves from the header actions now.
        }

        private void BuildContent()
        {
            // Explicit size, and pages positioned by hand below: a Dock=Fill child is
            // sized to its parent's *current* size when added, and a freshly created panel
            // is still 200x100. Anchored grandchildren added against that transient size
            // end up with negative margins and overrun the card.
            _contentHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Page,
                Padding = new Padding(30, 14, 30, 22),
                Size = new Size(960, 744)
            };

            _pageProviders = new PagePanel
            {
                Title = "Providers",
                Description = "",
                Dock = DockStyle.Fill
            };
            _contentHost.Controls.Add(_pageProviders);

            BuildProvidersPage();
        }

        // ------------------------------------------------------------ providers page

        private void BuildProvidersPage()
        {
            var save = new FlatButton
            {
                Text = "Save changes",
                Icon = Glyph.Save,
                Style = ButtonStyle.Primary,
                Font = Theme.Small,
                Size = new Size(132, 32)
            };
            save.Click += (s, e) => SaveCurrent();
            _pageProviders.Actions.Controls.Add(save);

            var openConfig = new FlatButton
            {
                Text = "Open config\u2026",
                Icon = Glyph.Folder,
                Style = ButtonStyle.Subtle,
                Font = Theme.Small,
                // Wide enough for the whole label at this font: at 120 the ellipsis ate it.
                Size = new Size(142, 32)
            };
            openConfig.Click += (s, e) => BrowseForFile();
            _pageProviders.Footer.Controls.Add(openConfig);

            var reload = new FlatButton
            {
                Text = "Reload",
                Icon = Glyph.Refresh,
                Style = ButtonStyle.Subtle,
                Font = Theme.Small,
                Size = new Size(88, 32)
            };
            reload.Click += (s, e) => ReloadCurrent();
            _pageProviders.Footer.Controls.Add(reload);

            var addProvider = new FlatButton
            {
                Text = "Add provider",
                Icon = Glyph.Plus,
                Style = ButtonStyle.Primary,
                Font = Theme.Small,
                Size = new Size(128, 32)
            };
            addProvider.Click += (s, e) => CreateProvider();
            _pageProviders.Actions.Controls.Add(addProvider);

            var about = new FlatButton
            {
                Text = "About",
                Icon = Glyph.Info,
                Style = ButtonStyle.Subtle,
                Font = Theme.Small,
                Size = new Size(84, 32)
            };
            about.Click += (s, e) => AboutDialog.ShowAbout(this, InstallSummary());
            _pageProviders.FooterRight.Controls.Add(about);

            // Explicit design size: children anchored to the bottom or right are positioned
            // against the parent's size at the moment they are added, and a Dock=Fill panel
            // still has its default 200x100 then.
            var card = new CardPanel { Dock = DockStyle.Fill, Title = "", Padding = new Padding(0), Size = new Size(900, 584) };
            _pageProviders.Body.Controls.Add(card);

            const int listWidth = 320;
            const int detailX = listWidth + 20;
            const int detailRight = 900 - 16;

            // The name row is bounded on the right by the Enabled label, the Delete button
            // and the toggle. Its width is derived rather than fixed, so widening the
            // provider column narrows the field instead of pushing it under them.
            const int nameRowRight = detailRight - 160 - 8 - 80 - 12;

            _searchField = new FieldBox
            {
                Location = new Point(14, 14),
                Size = new Size(listWidth - 28, 34),
                LeadingIcon = Glyph.Search,
                Placeholder = "Search providers"
            };
            _searchField.ValueChanged += (s, e) => RebuildProviderList(true);

            _providerList = new ProviderList
            {
                Location = new Point(8, 58),
                Size = new Size(listWidth - 8, 510),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };
            _providerList.SelectedIndexChanged += ProviderList_SelectedIndexChanged;
            _providerList.ProviderDelete += (s, provider) => DeleteProvider(provider);

            var divider = new Panel
            {
                Location = new Point(listWidth, 0),
                Size = new Size(1, 584),
                BackColor = Theme.Border,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };

            _nameField = new FieldBox
            {
                Location = new Point(detailX + 40, 20),
                Size = new Size(nameRowRight - (detailX + 40), 32),
                Placeholder = "Provider name"
            };
            _nameField.ValueChanged += (s, e) => MarkDirty();

            _logoTile = new LogoTile
            {
                Location = new Point(detailX, 20),
                Size = new Size(32, 32),
                BackColor = Theme.Card
            };

            _idField = new FieldBox
            {
                Location = new Point(detailX + 40, 54),
                Size = new Size(220, 30),
                Placeholder = "provider-id"
            };
            _idField.ValueChanged += (s, e) => MarkDirty();
            _idField.Inner.Leave += (s, e) => ApplyIdRename();

            _enabledLabel = new Label
            {
                Location = new Point(detailRight - 160 - 8 - 80, 24),
                Size = new Size(80, 20),
                ForeColor = Theme.TextDim,
                Font = Theme.Small,
                BackColor = Theme.Card,
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            // Delete lives in the item header, beside the enable toggle, always visible.
            _btnDeleteProvider = new FlatButton
            {
                Text = "Delete",
                Icon = Glyph.Trash,
                Style = ButtonStyle.Danger,
                Location = new Point(detailRight - 44 - 6 - 110, 19),
                Size = new Size(110, 34),
                Enabled = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnDeleteProvider.Click += (s, e) => DeleteCurrentProvider();

            _enabledToggle = new ToggleSwitch
            {
                Location = new Point(detailRight - 44, 22),
                Size = new Size(44, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _enabledToggle.CheckedChanged += EnabledToggle_Changed;

            var rule = new Panel
            {
                Location = new Point(detailX, 88),
                Size = new Size(detailRight - detailX, 1),
                BackColor = Theme.Border,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _urlField = new FieldBox
            {
                Location = new Point(detailX, 112),
                Size = new Size(detailRight - detailX, 32),
                Placeholder = "https://api.example.com/v1",
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _urlField.ValueChanged += (s, e) => MarkDirty();

            _formatDrop = new DropdownField
            {
                Location = new Point(detailX, 168),
                Size = new Size(detailRight - detailX, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _formatDrop.SetOptions(FormatOptions);
            _formatDrop.SelectedIndexChanged += (s, e) => MarkDirty();

            _keyField = new FieldBox
            {
                Location = new Point(detailX, 224),
                Size = new Size(detailRight - detailX, 32),
                Placeholder = "Leave blank to keep the saved key",
                Secret = true,
                TrailingIcon = Glyph.Eye,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _keyField.ValueChanged += (s, e) => MarkDirty();
            _keyField.TrailingIconClicked += (s, e) =>
            {
                _keyField.Secret = !_keyField.Secret;
                _keyField.TrailingIcon = _keyField.Secret ? Glyph.Eye : Glyph.EyeOff;
                _keyField.Invalidate();
            };

            _modelCountLabel = new Label
            {
                Location = new Point(detailX, 272),
                Size = new Size(240, 18),
                ForeColor = Theme.TextFaint,
                Font = Theme.Micro,
                BackColor = Theme.Card
            };

            _btnAddModel = new FlatButton
            {
                Text = "Add model",
                Icon = Glyph.Plus,
                Style = ButtonStyle.Subtle,
                Font = Theme.Small,
                Location = new Point(detailRight - 124, 266),
                Size = new Size(124, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnAddModel.Click += (s, e) => AddOrEditModel(null);

            _btnSmartAdd = new FlatButton
            {
                Text = "Smart add",
                Icon = Glyph.Sparkle,
                Style = ButtonStyle.Subtle,
                Font = Theme.Small,
                Location = new Point(detailRight - 124 - 8 - 132, 266),
                Size = new Size(132, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnSmartAdd.Click += (s, e) => SmartAddModels();

            // The models column gets its own inset surface and runs to the bottom of the
            // card, so it reads as a full-height list rather than a small box.
            _modelBox = new ListPanel
            {
                Location = new Point(detailX, 296),
                Size = new Size(detailRight - detailX, 278),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            _modelList = new ModelList
            {
                Location = new Point(7, 7),
                Size = new Size(_modelBox.Width - 14, _modelBox.Height - 14),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _modelList.ItemActivated += (s, e) => AddOrEditModel(_modelList.SelectedItem as ModelEntry);
            _modelList.ModelEdit += (s, model) => AddOrEditModel(model);
            _modelList.ModelDelete += ModelList_DeleteRequested;

            _modelEmpty = new EmptyState
            {
                Location = new Point(7, 7),
                Size = new Size(_modelBox.Width - 14, _modelBox.Height - 14),
                Icon = Glyph.Info,
                Message = "No models are configured yet. Add one to use it in chat.",
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            _modelBox.Controls.Add(_modelList);
            _modelBox.Controls.Add(_modelEmpty);

            card.Controls.AddRange(new Control[]
            {
                _searchField, _providerList, divider,
                _logoTile, _nameField, _idField, _enabledLabel, _enabledToggle, _btnDeleteProvider, rule,
                _urlField, _formatDrop, _keyField,
                _modelCountLabel, _btnAddModel, _btnSmartAdd, _modelBox,
                MakeCaption("BASE URL", detailX, 94),
                MakeCaption("API FORMAT", detailX, 150),
                MakeCaption("API KEY", detailX, 206)
            });
        }

        private static Label MakeCaption(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(300, 16),
                ForeColor = Theme.TextFaint,
                Font = Theme.Micro,
                BackColor = Theme.Card
            };
        }

        // ------------------------------------------------------------------ painting

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            using (var brush = new SolidBrush(Theme.Page))
            {
                g.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void WndProc(ref Message m)
        {
            // Static window: the caption bar only drags — no resize edges anywhere.
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    int sx = unchecked((short)(long)m.LParam);
                    int sy = unchecked((short)((long)m.LParam >> 16));
                    Point p = PointToClient(new Point(sx, sy));

                    if (p.Y <= TitleBarHeight && WindowState == FormWindowState.Normal)
                    {
                        m.Result = (IntPtr)HTCAPTION;
                    }
                }
                return;
            }

            // Double-clicking the caption must not maximize, and neither may any
            // system maximize command — the window stays at its designed size.
            if (m.Msg == WM_NCLBUTTONDBLCLK) return;
            if (m.Msg == WM_SYSCOMMAND)
            {
                long cmd = m.WParam.ToInt64() & 0xFFF0;
                if (cmd == SC_MAXIMIZE || cmd == SC_SIZE) return;
            }

            base.WndProc(ref m);
        }

        // --------------------------------------------------------------- navigation

        private void ShowProvidersPage()
        {
            _pageProviders.Visible = true;
        }

        // ------------------------------------------------------------ file loading

        private void LoadStartupConfig()
        {
            // Smart configuration data loads in the background; the UI never waits for it.
            ModelCatalog.BeginLoad();

            string path = "";
            if (StartupConfig.Length > 0 && File.Exists(StartupConfig)) path = StartupConfig;
            else path = ConfigStore.FindExistingConfig();

            if (path.Length > 0) LoadFile(path);
            else SetStatus("No config file found. Use \"Open config\u2026\" to pick one.");
        }

        private void LoadFile(string path)
        {
            try
            {
                _document = ConfigStore.Load(path);
                _current = null;
                _dirty = false;

                // Seed smart bookkeeping for models the tool has not seen before: values
                // already in the file count as manual, empty limits as auto.
                SmartSync.SeedDocument(_document);

                RebuildProviderList(false);
                UpdateStats();
                SetSaveState("Saved", Theme.Ok);
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "Load failed",
                    "Could not read that file.\r\n\r\n" + ex.Message, MessageKind.Error);
            }
        }

        private void ReloadCurrent()
        {
            if (_document == null || _document.Path.Length == 0 || !File.Exists(_document.Path))
            {
                SetStatus("Nothing to reload — open a config first.");
                return;
            }
            if (!ConfirmDiscardChanges()) return;
            LoadFile(_document.Path);
        }

        private bool ConfirmDiscardChanges()
        {
            if (!_dirty) return true;

            DialogResult result = MessageDialog.AskSave(this,
                "This config has unsaved changes.\r\n\r\nDiscard them and continue?");
            if (result == DialogResult.Cancel) return false;
            if (result == DialogResult.OK) return SaveCurrent();
            return true;
        }

        private void BrowseForFile()
        {
            if (!ConfirmDiscardChanges()) return;

            // The configs found on disk are offered first; Browse falls through to the
            // system dialog for anything the search did not turn up.
            string current = _document == null ? "" : _document.Path;
            ConfigChoice choice = ConfigPickerDialog.Pick(this, InstallSummary(), current);

            if (choice.Browse)
            {
                BrowseWithFileDialog();
                return;
            }
            if (choice.Path.Length == 0) return;
            LoadFile(choice.Path);
        }

        private void BrowseWithFileDialog()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "OpenCode config|opencode.jsonc;opencode.json;*.jsonc;*.json|All files|*.*",
                Title = "Select an OpenCode configuration file"
            })
            {
                if (_document != null && File.Exists(_document.Path))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(_document.Path);
                }
                if (dialog.ShowDialog(this) == DialogResult.OK) LoadFile(dialog.FileName);
            }
        }

        /// <summary>Where OpenCode lives on this machine, for the picker and the About box.</summary>
        private string InstallSummary()
        {
            if (_installSummary.Length > 0) return _installSummary;

            try
            {
                _installSummary = OpenCodeLocator.DescribeInstallations(OpenCodeLocator.FindInstallations());
            }
            catch
            {
                _installSummary = "not found on PATH or in the usual install locations";
            }
            return _installSummary;
        }

        /// <summary>
        /// A note about models whose limits could not be written, because OpenCode requires
        /// limit.context and limit.output together and only one was set.
        /// </summary>
        private static string LimitWarningText(List<string> warnings)
        {
            if (warnings == null || warnings.Count == 0) return "";
            return "   \u00b7   " + warnings.Count + " model" + (warnings.Count == 1 ? "" : "s")
                + " left without limits (context and output must be set together)";
        }

        // ------------------------------------------------------------- provider list

        private void RebuildProviderList(bool keepSelection)
        {
            if (_document == null) return;

            string filter = _searchField.Value.Trim();
            string keepId = keepSelection && _current != null ? _current.Id : null;

            _loading = true;
            _providerList.Items.Clear();

            var enabled = new List<object>();
            var disabled = new List<object>();
            foreach (ProviderEntry provider in _document.Providers)
            {
                if (filter.Length > 0 && !Matches(provider, filter)) continue;
                (provider.IsDisabled ? disabled : enabled).Add(provider);
            }

            if (enabled.Count > 0)
            {
                _providerList.Items.Add(new ListGroupRow("Providers"));
                foreach (object item in enabled) _providerList.Items.Add(item);
            }
            if (disabled.Count > 0)
            {
                _providerList.Items.Add(new ListGroupRow("Disabled"));
                foreach (object item in disabled) _providerList.Items.Add(item);
            }
            _loading = false;

            if (keepId != null)
            {
                foreach (object item in _providerList.Items.Raw)
                {
                    var provider = item as ProviderEntry;
                    if (provider != null && provider.Id == keepId)
                    {
                        _providerList.SelectedItem = provider;
                        return;
                    }
                }
            }

            if (_current == null || _providerList.SelectedItem == null)
            {
                _providerList.SelectFirst();
                if (_providerList.SelectedItem == null) ShowProvider(null);
            }
        }

        private static bool Matches(ProviderEntry provider, string filter)
        {
            return provider.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || provider.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || provider.BaseUrl.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ProviderList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            var provider = _providerList.SelectedItem as ProviderEntry;
            if (provider == null) return;
            if (ReferenceEquals(provider, _current)) return;

            CaptureEditor();
            ShowProvider(provider);
        }

        private void ShowProvider(ProviderEntry provider)
        {
            _current = provider;
            _loading = true;

            bool has = provider != null;
            _nameField.Enabled = has;
            _idField.Enabled = has;
            _urlField.Enabled = has;
            _keyField.Enabled = has;
            _formatDrop.Enabled = has;
            _enabledToggle.Enabled = has;
            _btnAddModel.Enabled = has;
            _btnSmartAdd.Enabled = has;
            _btnDeleteProvider.Enabled = has;

            if (!has)
            {
                _nameField.Value = "";
                _idField.Value = "";
                _urlField.Value = "";
                _keyField.Value = "";
                _logoTile.Letter = "?";
                _formatDrop.SelectedIndex = -1;
                _modelList.Items.Clear();
                _modelCountLabel.Text = "Model list";
                _modelList.Visible = false;
                _modelEmpty.Visible = false;
                _loading = false;
                _btnDeleteProvider.Invalidate();
                return;
            }

            _nameField.Value = provider.DisplayName;
            _logoTile.Letter = provider.DisplayName.Length > 0 ? provider.DisplayName : provider.Id;
            _idField.Value = provider.Id;
            _urlField.Value = provider.BaseUrl;
            _keyField.Value = provider.ApiKey;
            _enabledToggle.Checked = !provider.IsDisabled;
            _enabledLabel.Text = provider.IsDisabled ? "Disabled" : "Enabled";
            UpdateFormatOptions(provider.Npm);
            RefreshModelList();

            _loading = false;
        }

        private void UpdateFormatOptions(string npm)
        {
            var options = new List<Option> { new Option("Not set (OpenCode default)", "") };
            options.AddRange(FormatOptions);

            if (!string.IsNullOrEmpty(npm))
            {
                bool known = false;
                foreach (Option option in options)
                {
                    if (string.Equals(option.Value, npm, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
                }
                if (!known) options.Add(new Option(npm + "   (custom)", npm));
            }

            _formatDrop.SetOptions(options);
            _formatDrop.SelectedValue = npm ?? "";
        }

        private void RefreshModelList()
        {
            _modelList.Items.Clear();
            if (_current == null) return;

            int smart = 0;
            foreach (ModelEntry model in _current.Models)
            {
                SmartModelState state = SmartStateStore.Get(_current.Id, model.Id);
                bool follows = state != null && state.Smart;
                model.SmartIndicator = follows && ModelCatalog.HaveData
                    && ModelCatalog.FindModel(_current.Id, _current.Npm, _current.BaseUrl, model.Id).Found;
                if (model.SmartIndicator) smart++;
                _modelList.Items.Add(model);
            }

            bool any = _current.Models.Count > 0;
            _modelList.Visible = any;
            _modelEmpty.Visible = !any;
            _modelCountLabel.Text = "Model list"
                + (any ? "   (" + _current.Models.Count + (smart > 0 ? " \u00b7 " + smart + " smart" : "") + ")" : "");
        }

        private void EnabledToggle_Changed(object sender, EventArgs e)
        {
            if (_loading || _current == null) return;

            _current.IsDisabled = !_enabledToggle.Checked;
            _enabledLabel.Text = _current.IsDisabled ? "Disabled" : "Enabled";
            MarkDirty();
            RebuildProviderList(true);
        }

        /// <summary>Pushes the editor fields back into the in-memory provider entry.</summary>
        private void CaptureEditor()
        {
            if (_loading || _current == null) return;
            ApplyIdRename();
            _current.DisplayName = _nameField.Value.Trim();
            _current.BaseUrl = _urlField.Value.Trim();
            _current.ApiKey = _keyField.Value.Trim();
            if (_formatDrop.SelectedIndex >= 0) _current.Npm = _formatDrop.SelectedValue;
            _current.IsDisabled = !_enabledToggle.Checked;
        }

        /// <summary>
        /// Applies an id rename typed into the id field: tombstones the old key so the
        /// save moves the block, and carries the disabled_providers entry along.
        /// </summary>
        private void ApplyIdRename()
        {
            if (_loading || _current == null || _document == null) return;

            string newId = _idField.Value.Trim();
            string oldId = _current.Id;
            if (newId == oldId) return;

            if (newId.Length == 0)
            {
                _idField.Value = oldId;
                SetStatus("Provider id cannot be empty.");
                return;
            }
            if (newId.Contains(" ") || newId.Contains("\""))
            {
                _idField.Value = oldId;
                SetStatus("Provider ids cannot contain spaces or quotes.");
                return;
            }
            foreach (ProviderEntry other in _document.Providers)
            {
                if (!ReferenceEquals(other, _current) && other.Id == newId)
                {
                    _idField.Value = oldId;
                    SetStatus("A provider with that id already exists.");
                    return;
                }
            }

            _current.Id = newId;
            if (!_current.IsNew) _document.DeletedProviderIds.Add(oldId);
            for (int i = 0; i < _document.DisabledProviders.Count; i++)
            {
                if (_document.DisabledProviders[i] == oldId) _document.DisabledProviders[i] = newId;
            }

            MarkDirty();
            RebuildProviderList(true);
            SetStatus("Renamed \"" + oldId + "\" to \"" + newId + "\". Save to write it to the file.");
        }

        // ------------------------------------------------------------------- models

        private void AddOrEditModel(ModelEntry existing)
        {
            if (_current == null)
            {
                SetStatus("Select a provider first.");
                return;
            }

            CaptureEditor();
            SmartModelState state = existing == null ? new SmartModelState() : SmartSync.Seed(_current, existing);

            using (var dialog = new ModelDialog(_current, existing, state))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string id = dialog.ModelId;
                if (id.Length == 0) return;
                string name = dialog.ModelName.Length > 0 ? dialog.ModelName : id;

                ModelEntry target = existing;
                if (target == null)
                {
                    target = _current.Models.Find(m => m.Id == id);
                    if (target == null)
                    {
                        target = new ModelEntry { Id = id, IsNew = true };
                        _current.Models.Add(target);
                    }
                }

                string oldId = target.Id;
                bool hadContext = target.ContextWasSet;
                bool hadOutput = target.OutputWasSet;

                target.Id = id;
                target.DisplayName = name;
                target.ContextWindow = dialog.ContextWindow;
                target.MaxOutputTokens = dialog.MaxOutputTokens;
                target.ContextWasSet = hadContext || dialog.ContextWindow.Length > 0;
                target.OutputWasSet = hadOutput || dialog.MaxOutputTokens.Length > 0;
                target.SmartConfiguration = dialog.SmartConfiguration;

                if (!string.Equals(oldId, id, StringComparison.Ordinal))
                {
                    SmartStateStore.Rename(_current.Id, oldId, id);
                }

                SmartStateStore.Set(_current.Id, id, new SmartModelState
                {
                    Smart = dialog.SmartConfiguration,
                    ManualName = dialog.ManualName,
                    ManualContext = dialog.ManualContext,
                    ManualOutput = dialog.ManualOutput,
                    SyncedName = dialog.ModelName,
                    SyncedContext = ConfigStore.ParseCount(dialog.ContextWindow),
                    SyncedOutput = ConfigStore.ParseCount(dialog.MaxOutputTokens),
                    Updated = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                });
                SmartStateStore.Save();

                MarkDirty();
                RefreshModelList();

                foreach (object item in _modelList.Items.Raw)
                {
                    var model = item as ModelEntry;
                    if (model != null && model.Id == id) { _modelList.SelectedItem = model; break; }
                }

                SetStatus((existing != null ? "Updated" : "Added") + " model \"" + id + "\""
                    + (dialog.SmartConfiguration && dialog.CatalogNote.Length > 0 ? "   \u00b7   " + dialog.CatalogNote : "")
                    + ". Save to write it to the file.");
            }
        }

        /// <summary>
        /// Adds every checked model from the models.dev catalog at once and refreshes the
        /// smart limits of checked existing models: the "no one-by-one" path.
        /// </summary>
        private void SmartAddModels()
        {
            if (_current == null)
            {
                SetStatus("Select a provider first.");
                return;
            }

            CaptureEditor();

            using (var dialog = new CatalogDialog(_current))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                int added = 0;
                foreach (CatalogRow row in dialog.SelectedNew)
                {
                    CatalogModel model = row.Model;
                    var entry = new ModelEntry
                    {
                        Id = row.Id,
                        DisplayName = model.Name.Length > 0 ? model.Name : row.Id,
                        ContextWindow = model.Context > 0 ? model.Context.ToString(CultureInfo.InvariantCulture) : "",
                        MaxOutputTokens = model.Output > 0 ? model.Output.ToString(CultureInfo.InvariantCulture) : "",
                        SmartConfiguration = true,
                        IsNew = true
                    };
                    entry.ContextWasSet = entry.ContextWindow.Length > 0;
                    entry.OutputWasSet = entry.MaxOutputTokens.Length > 0;

                    ModelEntry clash = _current.Models.Find(m => m.Id == row.Id);
                    if (clash != null) continue;

                    _current.Models.Add(entry);
                    SmartStateStore.Set(_current.Id, entry.Id, new SmartModelState
                    {
                        Smart = true,
                        SyncedName = entry.DisplayName,
                        SyncedContext = model.Context,
                        SyncedOutput = model.Output,
                        Updated = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                    });
                    added++;
                }

                int synced = 0;
                foreach (CatalogRow row in dialog.SelectedExisting)
                {
                    ModelEntry entry = row.ExistingEntry;
                    if (entry == null) continue;

                    SmartModelState state = SmartSync.Seed(_current, entry);
                    state.Smart = true;
                    bool changed = false;

                    if (row.Model.Name.Length > 0 && !state.ManualName && entry.DisplayName != row.Model.Name)
                    {
                        entry.DisplayName = row.Model.Name;
                        state.SyncedName = row.Model.Name;
                        changed = true;
                    }

                    changed |= ApplyCatalogLimit(entry, row.Model.Context, state, true, dialog.OverwriteLimits);
                    changed |= ApplyCatalogLimit(entry, row.Model.Output, state, false, dialog.OverwriteLimits);

                    if (changed)
                    {
                        state.Updated = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                        synced++;
                    }
                }

                if (added + synced > 0)
                {
                    MarkDirty();
                    RefreshModelList();
                    SmartStateStore.Save();
                }

                SetStatus(added + synced == 0
                    ? "Nothing to add \u2014 every checked model is already configured."
                    : "Smart add: " + added + " new model(s), " + synced + " refreshed. Save to write them to the file.");
            }
        }

        private static bool ApplyCatalogLimit(ModelEntry entry, long recommended, SmartModelState state,
            bool context, bool overwrite)
        {
            if (recommended <= 0) return false;
            if ((context ? state.ManualContext : state.ManualOutput) && !overwrite) return false;

            string text = recommended.ToString(CultureInfo.InvariantCulture);
            bool touched = false;
            if (context)
            {
                if (entry.ContextWindow != text)
                {
                    entry.ContextWindow = text;
                    entry.ContextWasSet = true;
                    touched = true;
                }
                state.ManualContext = false;
                state.SyncedContext = recommended;
            }
            else
            {
                if (entry.MaxOutputTokens != text)
                {
                    entry.MaxOutputTokens = text;
                    entry.OutputWasSet = true;
                    touched = true;
                }
                state.ManualOutput = false;
                state.SyncedOutput = recommended;
            }
            return touched;
        }

        private void ModelList_DeleteRequested(object sender, ModelEntry model)
        {
            if (_current == null || model == null) return;

            _current.Models.Remove(model);
            SmartStateStore.Remove(_current.Id, model.Id);
            SmartStateStore.Save();
            MarkDirty();
            RefreshModelList();
            SetStatus("Removed \"" + model.Id + "\". Save to write it to the file.");
        }

        private void CreateProvider()
        {
            if (_document == null)
            {
                SetStatus("Open a configuration file first.");
                return;
            }

            string id = TextPromptDialog.Show(this, "New provider", "Provider ID (the key under \"provider\")", "");
            if (id.Length == 0) return;

            if (id.Contains(" ") || id.Contains("\""))
            {
                SetStatus("Provider IDs cannot contain spaces or quotes.");
                return;
            }

            foreach (ProviderEntry existing in _document.Providers)
            {
                if (existing.Id == id)
                {
                    SetStatus("A provider with that ID already exists.");
                    return;
                }
            }

            CaptureEditor();
            var created = new ProviderEntry
            {
                Id = id,
                DisplayName = id,
                Npm = "@ai-sdk/openai-compatible",
                IsNew = true
            };
            _document.Providers.Add(created);

            _searchField.Value = "";
            _current = created;
            RebuildProviderList(false);
            _providerList.SelectedItem = created;
            ShowProvider(created);
            MarkDirty();
            SetStatus("Created provider \"" + id + "\". Fill in the URL and models, then save.");
        }

        private void DeleteCurrentProvider()
        {
            if (_document == null)
            {
                SetStatus("Open a configuration file first.");
                return;
            }
            if (_current == null)
            {
                SetStatus("Select a provider in the list to delete it.");
                return;
            }
            DeleteProvider(_current);
        }

        /// <summary>Deletes a provider from either the row icon or the header button.</summary>
        private void DeleteProvider(ProviderEntry entry)
        {
            if (_document == null || entry == null) return;

            string id = entry.Id;
            bool isNew = entry.IsNew;
            if (!MessageDialog.Confirm(this, "Delete provider",
                "Delete provider \"" + id + "\" from this config?\r\n\r\n" +
                (isNew ? "It has not been saved yet, so it will just be discarded."
                 : "Its block under \"provider\" will be removed on the next save (a .bak copy is written first).")))
            {
                return;
            }

            _document.Providers.Remove(entry);
            if (!isNew) _document.DeletedProviderIds.Add(id);
            _document.DisabledProviders.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            SmartStateStore.RemoveProvider(id);
            SmartStateStore.Save();

            if (ReferenceEquals(entry, _current)) _current = null;
            _searchField.Value = "";
            RebuildProviderList(false);
            ShowProvider(_providerList.SelectedItem as ProviderEntry);
            MarkDirty();
            SetStatus("Deleted provider \"" + id + "\". Save to write it to the file.");
        }

        // ------------------------------------------------------------------- saving

        private bool SaveCurrent()
        {
            if (_document == null)
            {
                SetStatus("Open a configuration file first.");
                return false;
            }
            if (string.IsNullOrEmpty(_document.Path))
            {
                BrowseForFile();
                return false;
            }

            CaptureEditor();

            // Smart configuration: refresh recommendations for smart-managed models just
            // before writing, so a save always carries the latest models.dev values.
            int synced = 0;
            if (_document.Providers.Count > 0 && SmartStateStore.Count > 0)
            {
                if (!ModelCatalog.HaveData) SetStatus("Checking models.dev for smart configuration\u2026");
                ModelCatalog.EnsureLoaded(2500);
                synced = SmartSync.Apply(_document, false);
            }

            try
            {
                string backup = ConfigStore.Save(_document, _document.Path);
                SmartStateStore.Save();

                foreach (ProviderEntry provider in _document.Providers) provider.IsNew = false;
                if (_current != null)
                {
                    foreach (ModelEntry model in _current.Models) model.IsNew = false;
                }

                _dirty = false;
                SetSaveState("Saved", Theme.Ok);
                RefreshModelList();
                RebuildProviderList(true);

                string message = "Saved to " + _document.Path;
                if (backup.Length > 0) message += "   (backup: " + Path.GetFileName(backup) + ")";
                if (synced > 0) message += "   \u00b7   smart configuration synced for " + synced + " model" + (synced == 1 ? "" : "s");
                message += LimitWarningText(_document.LimitWarnings);
                SetStatus(message);
                return true;
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "Save failed",
                    "Could not write the file.\r\n\r\n" + ex.Message, MessageKind.Error);
                SetSaveState("Save failed", Theme.Danger);
                return false;
            }
        }

        // ------------------------------------------------------------------- status

        private void SetStatus(string message)
        {
            _statusBar.Text = message;
        }

        private void SetSaveState(string text, Color color)
        {
            _statusBar.ForeColor = color == Theme.Ok ? Theme.TextFaint : color;
            _statusBar.Text = _dirty ? "Unsaved changes" : text;
        }

        private void MarkDirty()
        {
            if (_loading) return;
            if (_dirty) return;
            _dirty = true;
            _statusBar.ForeColor = Theme.Warn;
            _statusBar.Text = "Unsaved changes";
        }

        private void UpdateStats()
        {
            if (_document == null) return;

            int models = 0;
            int disabled = 0;
            foreach (ProviderEntry provider in _document.Providers)
            {
                models += provider.Models.Count;
                if (provider.IsDisabled) disabled++;
            }

            if (!_dirty)
            {
                _statusBar.ForeColor = Theme.TextFaint;
                _statusBar.Text = Path.GetFileName(_document.Path) + "   \u00b7   "
                    + _document.Providers.Count + " providers  \u00b7  " + models + " models"
                    + (disabled > 0 ? "  \u00b7  " + disabled + " disabled" : "");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_dirty && !ConfirmDiscardChanges())
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }
    }

    /// <summary>Rounded tile with the provider's initial, beside the name field.</summary>
    internal class LogoTile : Control
    {
        private string _letter = "?";

        public LogoTile()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public string Letter
        {
            get { return _letter; }
            set
            {
                string source = value ?? "";
                _letter = source.Length > 0 ? source.Substring(0, 1).ToUpperInvariant() : "?";
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceOf(this));

            var body = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.Rounded(body, Theme.S(8, g)))
            using (var brush = new SolidBrush(Theme.CardAlt))
            {
                g.FillPath(brush, path);
            }
            using (GraphicsPath path = Theme.Rounded(body, Theme.S(8, g)))
            using (var pen = new Pen(Theme.Border))
            {
                g.DrawPath(pen, path);
            }

            using (var brush = new SolidBrush(Theme.Text))
            {
                SizeF size = g.MeasureString(_letter, Theme.BodyBold);
                g.DrawString(_letter, Theme.BodyBold, brush,
                    (Width - size.Width) / 2f, (Height - size.Height) / 2f);
            }
        }
    }

    /// <summary>Centred icon and message shown when a section has no content.</summary>
    internal class EmptyState : Control
    {
        private string _message = "";

        public Glyph Icon = Glyph.Info;

        public EmptyState()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        public string Message
        {
            get { return _message; }
            set { _message = value ?? ""; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceOf(this));

            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.Rounded(box, Theme.S(8, g)))
            using (var pen = new Pen(Theme.Border, 1f))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawPath(pen, path);
            }

            float iconSize = Theme.S(20, g);
            SizeF text = g.MeasureString(_message, Theme.Small, Width - Theme.S(80, g));
            float totalWidth = iconSize + Theme.S(10, g) + text.Width;
            float startX = (Width - totalWidth) / 2f;
            if (startX < Theme.S(16, g)) startX = Theme.S(16, g);

            Icons.Draw(g, Icon,
                new RectangleF(startX, (Height - iconSize) / 2f, iconSize, iconSize), Theme.TextFaint);

            using (var brush = new SolidBrush(Theme.TextFaint))
            {
                g.DrawString(_message, Theme.Small, brush,
                    new RectangleF(startX + iconSize + Theme.S(10, g), (Height - text.Height) / 2f,
                        Width - startX - iconSize - Theme.S(26, g), text.Height), Theme.Wrap);
            }
        }
    }
}
