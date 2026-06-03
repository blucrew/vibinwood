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
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    class MainForm : Form
    {
        string[] _lines = Array.Empty<string>();
        readonly List<Entry> _entries = new();
        string _path = "";
        bool _dirty;

        readonly TextBox  _pathBox = new();
        readonly Label    _status  = new();
        readonly Panel    _scroll  = new();
        readonly Button   _save    = new();
        readonly ToolTip  _tip     = new() { AutoPopDelay = 20000, InitialDelay = 350, ReshowDelay = 100 };

        static readonly string[] Order = { "General", "Continuous", "XToys", "Logging" };

        public MainForm()
        {
            Text = "Vibinwood — Config";
            BackColor = T.Ink; ForeColor = T.Fg;
            ClientSize = new Size(720, 820);
            MinimumSize = new Size(560, 480);
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;

            // Order matters: add Fill FIRST (back), then Top/Bottom (front) so they claim edges.
            _scroll.Dock = DockStyle.Fill; _scroll.AutoScroll = true; _scroll.BackColor = T.Ink; _scroll.Padding = new Padding(10,8,10,8);
            Controls.Add(_scroll);

            var top = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = T.Panel };
            var title = new Label { Text = "Vibinwood", AutoSize = true, ForeColor = T.Fg,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold), Location = new Point(14, 12) };
            var sub = new Label { Text = "Edits com.vibinwood.haptics.cfg directly. Save writes it back in place.",
                AutoSize = true, ForeColor = T.Muted, Location = new Point(16, 44) };
            _pathBox.SetBounds(14, 64, ClientSize.Width - 28, 22);
            _pathBox.Anchor = AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
            Style(_pathBox); _pathBox.ReadOnly = true;

            var browse = MkBtn("Browse…", 86); browse.Anchor = AnchorStyles.Top|AnchorStyles.Right;
            var reload = MkBtn("Reload", 70);  reload.Anchor = AnchorStyles.Top|AnchorStyles.Right;
            _save.Text = "💾 Save"; _save.Size = new Size(86, 26); StyleAccent(_save); _save.Anchor = AnchorStyles.Top|AnchorStyles.Right;
            void PlaceTopButtons()
            {
                _save.Location  = new Point(top.ClientSize.Width - 14 - _save.Width, 14);
                reload.Location = new Point(_save.Left - 8 - reload.Width, 15);
                browse.Location = new Point(reload.Left - 8 - browse.Width, 15);
            }
            top.Resize += (_,_) => PlaceTopButtons();
            browse.Click += (_,_) => Browse();
            reload.Click += (_,_) => { if (File.Exists(_path)) LoadCfg(_path); };
            _save.Click  += (_,_) => Save();
            top.Controls.AddRange(new Control[]{ title, sub, _pathBox, browse, reload, _save });
            Controls.Add(top);
            PlaceTopButtons();

            _status.Dock = DockStyle.Bottom; _status.Height = 26; _status.BackColor = T.Panel; _status.ForeColor = T.Muted;
            _status.TextAlign = ContentAlignment.MiddleLeft; _status.Padding = new Padding(14,0,0,0); _status.Text = "No file loaded.";
            Controls.Add(_status);

            var guess = DefaultPath();
            if (guess != null) LoadCfg(guess);
            else _status.Text = "Click Browse… and pick your com.vibinwood.haptics.cfg.";
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
                if ((m = Regex.Match(t, @"^# Acceptable values:\s*(.+)$")).Success) { accept = m.Groups[1].Value.Split(',').Select(s=>s.Trim()).ToArray(); continue; }
                if (t.StartsWith("#")) continue;
                if ((m = Regex.Match(ln, @"^(\s*)([\w.\-]+)\s*=\s*(.*)$")).Success)
                {
                    _entries.Add(new Entry { Section=section, Key=m.Groups[2].Value, Type=type ?? Guess(m.Groups[3].Value),
                                             Accept=accept, Desc=desc ?? "", LineIdx=i });
                    type=null;desc=null;accept=null;
                }
            }
            Render();
            _dirty = false; Title();
            _status.Text = $"Loaded {Path.GetFileName(path)} — {_entries.Count} settings.  Hover a name for its description.";
        }

        static string Guess(string v){ v=v.Trim(); if(v is "true" or "false") return "Boolean"; if(Regex.IsMatch(v,@"^-?\d+$")) return "Int32"; if(Regex.IsMatch(v,@"^-?\d*\.?\d+$")) return "Single"; return "String"; }
        string CurVal(Entry e){ var ln=_lines[e.LineIdx]; int eq=ln.IndexOf('='); return ln.Substring(eq+1).Trim(); }
        void SetVal(Entry e, string v){ var ln=_lines[e.LineIdx]; int eq=ln.IndexOf('='); _lines[e.LineIdx]=ln.Substring(0,eq+1)+" "+v; _dirty=true; Title(); }
        void Title(){ Text = "Vibinwood — Config" + (_dirty ? " *" : ""); _save.Enabled = _dirty; }

        // ── Render: one TableLayoutPanel card per section, fixed-height rows ──
        const int RowH = 34, HeadH = 32;

        void Render()
        {
            _scroll.SuspendLayout();
            _scroll.Controls.Clear();
            var secs = _entries.Select(e=>e.Section).Distinct()
                .OrderBy(s => { int i = Array.IndexOf(Order, s); return i<0?99:i; }).ThenBy(s=>s).ToList();

            // Dock=Top stacks last-added on top → add bottom-up so first section ends on top.
            for (int s = secs.Count - 1; s >= 0; s--)
            {
                _scroll.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10, BackColor = T.Ink });
                var card = BuildCard(secs[s]);
                card.Dock = DockStyle.Top;
                _scroll.Controls.Add(card);
            }
            _scroll.ResumeLayout(true);
        }

        TableLayoutPanel BuildCard(string sec)
        {
            var items = _entries.Where(e => e.Section == sec).ToList();
            var tlp = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, BackColor = T.Panel,
                RowCount = items.Count + 1, Height = HeadH + items.Count * RowH + 6, Padding = new Padding(0,0,0,6) };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, HeadH));
            var head = new Label { Text = "  " + sec, Dock = DockStyle.Fill, BackColor = T.Panel2, ForeColor = T.Accent,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0) };
            tlp.Controls.Add(head, 0, 0); tlp.SetColumnSpan(head, 2);

            int row = 1;
            foreach (var e in items)
            {
                tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, RowH));
                var name = new Label { Text = e.Key, Dock = DockStyle.Fill, ForeColor = T.Fg,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(8,0,4,0) };
                if (e.Desc.Length > 0) _tip.SetToolTip(name, e.Desc);
                tlp.Controls.Add(name, 0, row);
                tlp.Controls.Add(MakeControl(e), 1, row);
                row++;
            }
            return tlp;
        }

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
            if (e.Accept != null)
            {
                var combo = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList,
                    Anchor = AnchorStyles.Left, Margin = new Padding(4,5,0,0), FlatStyle = FlatStyle.Flat };
                Style(combo); combo.Items.AddRange(e.Accept.Cast<object>().ToArray());
                combo.SelectedItem = e.Accept.FirstOrDefault(a => a == v);
                if (e.Desc.Length>0) _tip.SetToolTip(combo, e.Desc);
                combo.SelectedIndexChanged += (_,_) => SetVal(e, combo.SelectedItem?.ToString() ?? v);
                return combo;
            }
            if (e.Type == "Single")
            {
                bool isMul = Regex.IsMatch(e.Key, "multiplier", RegexOptions.IgnoreCase);
                var num = new NumericUpDown { Width = 90, Anchor = AnchorStyles.Left, Margin = new Padding(4,5,0,0),
                    DecimalPlaces = 2, Increment = 0.05M, Minimum = 0, Maximum = isMul ? 5M : 1M };
                Style(num); num.Value = decimal.TryParse(v, out var dv) ? Math.Clamp(dv, num.Minimum, num.Maximum) : 0;
                if (e.Desc.Length>0) _tip.SetToolTip(num, e.Desc);
                num.ValueChanged += (_,_) => SetVal(e, num.Value.ToString("0.##"));
                return num;
            }
            if (e.Type == "Int32")
            {
                var num = new NumericUpDown { Width = 110, Anchor = AnchorStyles.Left, Margin = new Padding(4,5,0,0),
                    Minimum = 0, Maximum = 1000000, Increment = 10 };
                Style(num); num.Value = int.TryParse(v, out var iv) ? Math.Clamp(iv, 0, 1000000) : 0;
                if (e.Desc.Length>0) _tip.SetToolTip(num, e.Desc);
                num.ValueChanged += (_,_) => SetVal(e, ((int)num.Value).ToString());
                return num;
            }
            var tb = new TextBox { Anchor = AnchorStyles.Left|AnchorStyles.Right, Margin = new Padding(4,6,8,0), Text = v };
            Style(tb);
            if (Regex.IsMatch(e.Key, "webhook", RegexOptions.IgnoreCase)) tb.PlaceholderText = "your XToys private webhook ID";
            if (e.Desc.Length>0) _tip.SetToolTip(tb, e.Desc);
            tb.TextChanged += (_,_) => SetVal(e, tb.Text);
            return tb;
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

        // ── Style helpers ──
        static void Style(Control c){ c.BackColor = T.Ink; c.ForeColor = T.Fg; if (c is TextBox tb) tb.BorderStyle = BorderStyle.FixedSingle; }
        static void StyleAccent(Button b){ b.BackColor = T.Accent; b.ForeColor = Color.FromArgb(0x04,0x21,0x0f); b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.Font = new Font("Segoe UI", 9f, FontStyle.Bold); }
        static Button MkBtn(string text, int w)
        {
            var b = new Button { Text = text, Width = w, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = T.Panel2, ForeColor = T.Fg };
            b.FlatAppearance.BorderColor = T.Line;
            return b;
        }

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
