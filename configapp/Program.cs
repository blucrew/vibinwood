using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace VibinwoodConfig
{
    static class T
    {
        public static readonly Color Ink    = Color.FromArgb(0x02,0x06,0x17);
        public static readonly Color Panel  = Color.FromArgb(0x0f,0x17,0x2a);
        public static readonly Color Panel2 = Color.FromArgb(0x1e,0x29,0x3b);
        public static readonly Color Line   = Color.FromArgb(0x1f,0x2c,0x43);
        public static readonly Color Fg     = Color.FromArgb(0xf8,0xfa,0xfc);
        public static readonly Color Muted  = Color.FromArgb(0x94,0xa3,0xb8);
        public static readonly Color Accent = Color.FromArgb(0x22,0xc5,0x5e);
    }

    class Entry
    {
        public string Section = "", Key = "", Type = "", Desc = "";
        public string[]? Accept;
        public int LineIdx;
    }

    static class Program
    {
        [STAThread]
        static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new MainForm()); }
    }

    class MainForm : Form
    {
        string[] _lines = Array.Empty<string>();
        readonly List<Entry> _entries = new();
        string _path = "";
        bool _dirty;

        readonly TextBox    _pathBox = new();
        readonly Label      _status  = new();
        readonly TabControl _tabs    = new();
        readonly Button     _save    = new();
        readonly ToolTip    _tip     = new() { AutoPopDelay = 30000, InitialDelay = 300, ReshowDelay = 80 };

        // First-tab sections (no "." prefix), in this order.
        static readonly string[] GeneralOrder = { "General", "Dual Arousal", "XToys", "Logging" };
        static readonly string[] PrefixOrder  = { "Combat", "HotScene", "Brawl" };
        // Pretty tab names for event prefixes.
        static string PrettyTab(string prefix) => prefix switch { "HotScene" => "Hot Scenes", _ => prefix };
        // Friendlier control labels for the everyday gaymer.
        static string Label_(string k) => k switch {
            "MasterMultiplier" => "Master level", "Multiplier" => "Strength",
            "MinIntensity" => "Min intensity",    "MaxIntensity" => "Max intensity",
            "XToysSource"  => "XToys follows",     "ToyRouting" => "Toy routing",
            "DurationMs"   => "Duration",          "MinDurationMs" => "Min duration",
            "WriteToFile"  => "Write log file",    _ => k };

        public MainForm()
        {
            Text = "Vibinwood — Config";
            BackColor = T.Ink; ForeColor = T.Fg;
            ClientSize = new Size(740, 760);
            MinimumSize = new Size(600, 480);
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;

            // Tabs fill (added first/back), top + status dock around it.
            _tabs.Dock = DockStyle.Fill; _tabs.BackColor = T.Ink;
            _tabs.SizeMode = TabSizeMode.Fixed; _tabs.ItemSize = new Size(130, 30);
            _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabs.DrawItem += DrawTab;
            Controls.Add(_tabs);

            var top = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = T.Panel };
            var title = new Label { Text = "Vibinwood", AutoSize = true, ForeColor = T.Fg,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold), Location = new Point(14, 11) };
            _pathBox.SetBounds(14, 46, ClientSize.Width - 28, 22);
            _pathBox.Anchor = AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
            Style(_pathBox); _pathBox.ReadOnly = true;

            var browse = MkBtn("Browse…", 86); browse.Anchor = AnchorStyles.Top|AnchorStyles.Right;
            var reload = MkBtn("Reload", 70);  reload.Anchor = AnchorStyles.Top|AnchorStyles.Right;
            _save.Text = "💾 Save"; _save.Size = new Size(86, 26); StyleAccent(_save); _save.Anchor = AnchorStyles.Top|AnchorStyles.Right;
            void Place() { _save.Location = new Point(top.ClientSize.Width-14-_save.Width, 14);
                reload.Location = new Point(_save.Left-8-reload.Width, 15);
                browse.Location = new Point(reload.Left-8-browse.Width, 15); }
            top.Resize += (_,_) => Place();
            browse.Click += (_,_) => Browse();
            reload.Click += (_,_) => { if (File.Exists(_path)) LoadCfg(_path); };
            _save.Click  += (_,_) => Save();
            top.Controls.AddRange(new Control[]{ title, _pathBox, browse, reload, _save });
            Controls.Add(top); Place();

            _status.Dock = DockStyle.Bottom; _status.Height = 26; _status.BackColor = T.Panel; _status.ForeColor = T.Muted;
            _status.TextAlign = ContentAlignment.MiddleLeft; _status.Padding = new Padding(14,0,0,0); _status.Text = "No file loaded.";
            Controls.Add(_status);

            var guess = DefaultPath();
            if (guess != null) LoadCfg(guess);
            else _status.Text = "Click Browse… and pick your com.vibinwood.haptics.cfg.";
        }

        void DrawTab(object? s, DrawItemEventArgs e)
        {
            var tp = _tabs.TabPages[e.Index];
            bool sel = _tabs.SelectedIndex == e.Index;
            using var b = new SolidBrush(sel ? T.Panel2 : T.Panel);
            e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tp.Text, new Font("Segoe UI", 9.5f, sel ? FontStyle.Bold : FontStyle.Regular),
                e.Bounds, sel ? T.Accent : T.Fg, TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }

        static string? DefaultPath()
        {
            var c = new[] { @"D:\Desktop\Naughty Games\RobinMorningwoodAdventure_TWS\BepInEx\config\com.vibinwood.haptics.cfg" };
            return c.FirstOrDefault(File.Exists);
        }
        void Browse()
        {
            using var d = new OpenFileDialog { Filter = "BepInEx config (*.cfg)|*.cfg|All files|*.*", Title = "Pick com.vibinwood.haptics.cfg" };
            if (File.Exists(_path)) d.InitialDirectory = Path.GetDirectoryName(_path);
            if (d.ShowDialog(this) == DialogResult.OK) LoadCfg(d.FileName);
        }

        // ── Parse ──
        void LoadCfg(string path)
        {
            _path = path; _pathBox.Text = path;
            _lines = File.ReadAllText(path).Replace("\r\n","\n").Split('\n');
            _entries.Clear();
            string section = ""; string? type = null, desc = null; string[]? accept = null;
            for (int i = 0; i < _lines.Length; i++)
            {
                var ln = _lines[i]; var t = ln.Trim(); Match m;
                if ((m = Regex.Match(t, @"^\[(.+)\]$")).Success) { section = m.Groups[1].Value; type=null;desc=null;accept=null; continue; }
                if (t.StartsWith("## ")) { desc = (desc==null?"":desc+" ") + t.Substring(3); continue; }
                if ((m = Regex.Match(t, @"^# Setting type:\s*(.+)$")).Success) { type = m.Groups[1].Value.Trim(); continue; }
                if ((m = Regex.Match(t, @"^# Acceptable values:\s*(.+)$")).Success) { accept = m.Groups[1].Value.Split(',').Select(x=>x.Trim()).ToArray(); continue; }
                if (t.StartsWith("#")) continue;
                if ((m = Regex.Match(ln, @"^(\s*)([\w.\-]+)\s*=\s*(.*)$")).Success)
                { _entries.Add(new Entry { Section=section, Key=m.Groups[2].Value, Type=type ?? Guess(m.Groups[3].Value), Accept=accept, Desc=desc ?? "", LineIdx=i }); type=null;desc=null;accept=null; }
            }
            Render();
            _dirty = false; Title();
            _status.Text = $"Loaded {Path.GetFileName(path)} — {_entries.Count} settings.  Hover a name for details.";
        }

        static string Guess(string v){ v=v.Trim(); if(v is "true" or "false") return "Boolean"; if(Regex.IsMatch(v,@"^-?\d+$")) return "Int32"; if(Regex.IsMatch(v,@"^-?\d*\.?\d+$")) return "Single"; return "String"; }
        string CurVal(Entry e){ var ln=_lines[e.LineIdx]; int eq=ln.IndexOf('='); return ln.Substring(eq+1).Trim(); }
        void SetVal(Entry e, string v){ var ln=_lines[e.LineIdx]; int eq=ln.IndexOf('='); _lines[e.LineIdx]=ln.Substring(0,eq+1)+" "+v; _dirty=true; Title(); }
        void Title(){ Text = "Vibinwood — Config" + (_dirty ? " *" : ""); _save.Enabled = _dirty; }

        // ── Build tabs ──
        void Render()
        {
            _tabs.SuspendLayout();
            _tabs.TabPages.Clear();

            var prefixes = _entries.Select(e => e.Section).Where(s => s.Contains('.'))
                .Select(s => s.Split('.')[0]).Distinct()
                .OrderBy(p => { int i = Array.IndexOf(PrefixOrder, p); return i<0?99:i; }).ThenBy(p => p).ToList();

            // Tab 1: General + the other non-prefixed sections.
            _tabs.TabPages.Add(MakeTab("General",
                _entries.Select(e=>e.Section).Where(s=>!s.Contains('.')).Distinct()
                        .OrderBy(s => { int i=Array.IndexOf(GeneralOrder,s); return i<0?99:i; }).ToList()));

            // One tab per event prefix (Combat, Hot Scenes, Brawl…).
            foreach (var pfx in prefixes)
                _tabs.TabPages.Add(MakeTab(PrettyTab(pfx),
                    _entries.Select(e=>e.Section).Where(s=>s.StartsWith(pfx+".")).Distinct().OrderBy(s=>s).ToList()));

            _tabs.ResumeLayout();
        }

        TabPage MakeTab(string name, List<string> sections)
        {
            var page = new TabPage(name) { BackColor = T.Ink, AutoScroll = true, Padding = new Padding(10,8,10,8) };
            // Dock=Top stacks newest on top → add bottom-up so first section ends on top.
            for (int i = sections.Count - 1; i >= 0; i--)
            {
                page.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10, BackColor = T.Ink });
                var card = BuildCard(sections[i]); card.Dock = DockStyle.Top;
                page.Controls.Add(card);
            }
            return page;
        }

        const int RowH = 34, HeadH = 32, PatRowH = 70;   // taller: radios wrap to 2–3 lines

        TableLayoutPanel BuildCard(string sec)
        {
            var items = _entries.Where(e => e.Section == sec).ToList();
            var tlp = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, BackColor = T.Panel,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0,0,0,6) };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, HeadH));
            var head = new Label { Text = "  " + sec, Dock = DockStyle.Fill, BackColor = T.Panel2, ForeColor = T.Accent,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0) };
            tlp.Controls.Add(head, 0, 0); tlp.SetColumnSpan(head, 2);

            int row = 1;
            foreach (var e in items)
            {
                bool pat = IsPattern(e);
                tlp.RowStyles.Add(pat ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Absolute, RowH));
                var name = new Label { Text = Label_(e.Key), Dock = DockStyle.Fill, ForeColor = T.Fg,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8,0,4,0) };
                if (e.Desc.Length > 0) _tip.SetToolTip(name, e.Desc);
                tlp.Controls.Add(name, 0, row);
                tlp.Controls.Add(MakeControl(e), 1, row);
                row++;
            }
            return tlp;
        }

        static bool IsPattern(Entry e) => e.Key == "Pattern" && e.Accept != null;
        static bool IsMul(Entry e)  => Regex.IsMatch(e.Key, "multiplier", RegexOptions.IgnoreCase);
        static bool IsDur(Entry e)  => Regex.IsMatch(e.Key, "duration|ms$", RegexOptions.IgnoreCase);

        Control MakeControl(Entry e)
        {
            var v = CurVal(e);

            if (e.Type == "Boolean")
            {
                var cb = new CheckBox { AutoSize = true, ForeColor = T.Fg, Text = "", Anchor = AnchorStyles.Left,
                    Margin = new Padding(4,7,0,0), Checked = v == "true" };
                if (e.Desc.Length>0) _tip.SetToolTip(cb, e.Desc);
                cb.CheckedChanged += (_,_) => SetVal(e, cb.Checked ? "true" : "false");
                return cb;
            }

            if (IsPattern(e))   // radio buttons, one-or-the-other; row auto-grows as they wrap
            {
                var flow = new FlowLayoutPanel { Dock = DockStyle.Top, WrapContents = true, AutoScroll = false,
                    AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = T.Panel, Margin = new Padding(2,4,2,6) };
                foreach (var opt in e.Accept!)
                {
                    var rb = new RadioButton { Text = opt, AutoSize = true, ForeColor = T.Fg, Checked = opt == v,
                        Margin = new Padding(2,3,10,2) };
                    if (e.Desc.Length>0) _tip.SetToolTip(rb, e.Desc);
                    rb.CheckedChanged += (_,_) => { if (rb.Checked) SetVal(e, opt); };
                    flow.Controls.Add(rb);
                }
                return flow;
            }

            if (e.Accept != null)   // other enums → dropdown (Mode, Verbosity, Source)
            {
                var combo = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left,
                    Margin = new Padding(4,5,0,0), FlatStyle = FlatStyle.Flat };
                Style(combo); combo.Items.AddRange(e.Accept.Cast<object>().ToArray());
                combo.SelectedItem = e.Accept.FirstOrDefault(a => a == v);
                if (e.Desc.Length>0) _tip.SetToolTip(combo, e.Desc);
                combo.SelectedIndexChanged += (_,_) => SetVal(e, combo.SelectedItem?.ToString() ?? v);
                return combo;
            }

            if (e.Type == "Single")   // 0–1 → whole percent
            {
                decimal cur = decimal.TryParse(v, out var dv) ? Math.Round(dv * 100) : 0;
                var num = new NumericUpDown { Width = 72, DecimalPlaces = 0, Increment = 5, Minimum = 0,
                    Maximum = IsMul(e) ? 500 : 100, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
                Style(num); num.Value = Math.Clamp(cur, num.Minimum, num.Maximum);
                num.ValueChanged += (_,_) => SetVal(e, (num.Value/100m).ToString("0.###"));
                return Suffixed(num, "%", e);
            }

            if (e.Type == "Int32")   // duration ms → seconds
            {
                if (IsDur(e))
                {
                    decimal cur = int.TryParse(v, out var iv) ? Math.Round(iv/1000m, 1) : 0;
                    var num = new NumericUpDown { Width = 72, DecimalPlaces = 1, Increment = 0.1M, Minimum = 0,
                        Maximum = 60, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
                    Style(num); num.Value = Math.Clamp(cur, 0, 60);
                    num.ValueChanged += (_,_) => SetVal(e, ((int)Math.Round(num.Value*1000)).ToString());
                    return Suffixed(num, "sec", e);
                }
                var n = new NumericUpDown { Width = 110, Minimum = 0, Maximum = 1000000, Increment = 1, Anchor = AnchorStyles.Left, Margin = new Padding(4,5,0,0) };
                Style(n); n.Value = int.TryParse(v, out var i2) ? Math.Clamp(i2,0,1000000) : 0;
                n.ValueChanged += (_,_) => SetVal(e, ((int)n.Value).ToString());
                return n;
            }

            var tb = new TextBox { Anchor = AnchorStyles.Left|AnchorStyles.Right, Margin = new Padding(4,6,8,0), Text = v };
            Style(tb);
            if (Regex.IsMatch(e.Key, "webhook", RegexOptions.IgnoreCase)) tb.PlaceholderText = "your XToys private webhook ID";
            if (e.Desc.Length>0) _tip.SetToolTip(tb, e.Desc);
            tb.TextChanged += (_,_) => SetVal(e, tb.Text);
            return tb;
        }

        // numeric + unit suffix in one cell
        Control Suffixed(NumericUpDown num, string unit, Entry e)
        {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = false,
                BackColor = T.Panel, Margin = new Padding(4,4,0,0) };
            var lab = new Label { Text = unit, AutoSize = true, ForeColor = T.Muted, Margin = new Padding(4,6,0,0) };
            flow.Controls.Add(num); flow.Controls.Add(lab);
            if (e.Desc.Length>0) { _tip.SetToolTip(num, e.Desc); _tip.SetToolTip(lab, e.Desc); }
            return flow;
        }

        void Save()
        {
            if (_path.Length == 0) { Browse(); if (_path.Length == 0) return; }
            try
            {
                File.WriteAllText(_path, string.Join("\r\n", _lines));
                _dirty = false; Title();
                _status.Text = $"Saved {Path.GetFileName(_path)} at {DateTime.Now:HH:mm:ss}. Relaunch the game to apply.";
            }
            catch (Exception ex) { _status.Text = "Save failed: " + ex.Message; }
        }

        static void Style(Control c){ c.BackColor = T.Ink; c.ForeColor = T.Fg; if (c is TextBox tb) tb.BorderStyle = BorderStyle.FixedSingle; }
        static void StyleAccent(Button b){ b.BackColor = T.Accent; b.ForeColor = Color.FromArgb(0x04,0x21,0x0f); b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.Font = new Font("Segoe UI", 9f, FontStyle.Bold); }
        static Button MkBtn(string text, int w){ var b = new Button { Text = text, Width = w, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = T.Panel2, ForeColor = T.Fg }; b.FlatAppearance.BorderColor = T.Line; return b; }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_dirty)
            {
                var r = MessageBox.Show("Save changes before closing?", "Vibinwood — Config", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes) Save();
            }
            base.OnFormClosing(e);
        }
    }
}
