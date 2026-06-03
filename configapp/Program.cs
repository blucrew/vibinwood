using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace VibinwoodConfig
{
    // ── Theme ──
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

        readonly TextBox _pathBox = new();
        readonly Label   _status  = new();
        readonly Panel   _scroll  = new();
        readonly Button  _save     = new();

        static readonly string[] Order = { "General", "Continuous", "XToys", "Logging" };

        public MainForm()
        {
            Text = "Vibinwood — Config";
            BackColor = T.Ink; ForeColor = T.Fg;
            Size = new Size(760, 820);
            MinimumSize = new Size(560, 480);
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;

            // ── Top bar ──
            var top = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = T.Panel, Padding = new Padding(14,12,14,10) };
            var title = new Label { Text = "Vibinwood", AutoSize = true, ForeColor = T.Fg,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold), Location = new Point(14, 10) };
            var subtitle = new Label { Text = "Edit com.vibinwood.haptics.cfg directly — Save writes it back in place.",
                AutoSize = true, ForeColor = T.Muted, Location = new Point(16, 38) };

            _pathBox.SetBounds(14, 56, 470, 22);
            Style(_pathBox); _pathBox.ReadOnly = true;
            var browse = MkButton("Browse…", 492, 55); browse.Click += (_,_) => Browse();
            var reload = MkButton("Reload",  580, 55); reload.Click += (_,_) => { if(File.Exists(_path)) LoadCfg(_path); };
            _save.Text = "💾 Save"; _save.SetBounds(656, 55, 80, 24); StyleAccent(_save); _save.Click += (_,_) => Save();

            top.Controls.AddRange(new Control[]{ title, subtitle, _pathBox, browse, reload, _save });

            // ── Status strip ──
            _status.Dock = DockStyle.Bottom; _status.Height = 26; _status.BackColor = T.Panel;
            _status.ForeColor = T.Muted; _status.TextAlign = ContentAlignment.MiddleLeft; _status.Padding = new Padding(14,0,0,0);
            _status.Text = "No file loaded.";

            // ── Scroll area ──
            _scroll.Dock = DockStyle.Fill; _scroll.AutoScroll = true; _scroll.BackColor = T.Ink; _scroll.Padding = new Padding(12);

            Controls.Add(_scroll); Controls.Add(top); Controls.Add(_status);

            // Auto-locate the cfg
            var guess = DefaultPath();
            if (guess != null) LoadCfg(guess);
            else _status.Text = "Click Browse… and pick your com.vibinwood.haptics.cfg.";
        }

        static string? DefaultPath()
        {
            var candidates = new[]
            {
                @"D:\Desktop\Naughty Games\RobinMorningwoodAdventure_TWS\BepInEx\config\com.vibinwood.haptics.cfg",
            };
            return candidates.FirstOrDefault(File.Exists);
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
            _dirty = false;
            Render();
            _status.Text = $"Loaded {Path.GetFileName(path)} — {_entries.Count} settings.";
            Title();
        }

        static string Guess(string v){ v=v.Trim(); if(v is "true" or "false") return "Boolean"; if(Regex.IsMatch(v,@"^-?\d+$")) return "Int32"; if(Regex.IsMatch(v,@"^-?\d*\.?\d+$")) return "Single"; return "String"; }
        string CurVal(Entry e){ var ln=_lines[e.LineIdx]; int eq=ln.IndexOf('='); return ln.Substring(eq+1).Trim(); }
        void SetVal(Entry e, string v){ var ln=_lines[e.LineIdx]; int eq=ln.IndexOf('='); _lines[e.LineIdx]=ln.Substring(0,eq+1)+" "+v; _dirty=true; Title(); }
        void Title(){ Text = "Vibinwood — Config" + (_dirty ? " *" : ""); _save.Enabled = _dirty; }

        // ── Render ──
        void Render()
        {
            _scroll.SuspendLayout();
            _scroll.Controls.Clear();
            var secs = _entries.Select(e=>e.Section).Distinct()
                .OrderBy(s => { int i = Array.IndexOf(Order, s); return i<0?99:i; }).ThenBy(s=>s).ToList();

            int y = 0; int width = _scroll.ClientSize.Width - 36;
            foreach (var sec in secs)
            {
                var grp = new Panel { BackColor = T.Panel, Width = width, Left = 4, Top = y,
                    Anchor = AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right };
                var head = new Label { Text = "  " + sec, Dock = DockStyle.Top, Height = 30, ForeColor = T.Accent,
                    Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, BackColor = T.Panel2 };
                grp.Controls.Add(head);

                int ry = 34;
                foreach (var e in _entries.Where(x=>x.Section==sec))
                {
                    var row = MakeRow(e, width - 16);
                    row.Top = ry; row.Left = 8; row.Width = width - 16;
                    row.Anchor = AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
                    grp.Controls.Add(row);
                    ry += row.Height + 4;
                }
                grp.Height = ry + 8;
                _scroll.Controls.Add(grp);
                y += grp.Height + 12;
            }
            _scroll.ResumeLayout();
        }

        Panel MakeRow(Entry e, int w)
        {
            bool hasDesc = e.Desc.Length > 0;
            var row = new Panel { Height = hasDesc ? 60 : 38, BackColor = T.Panel };

            var name = new Label { Text = e.Key, AutoSize = false, Width = 210, Height = 22, Left = 4, Top = 8,
                ForeColor = T.Fg, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            row.Controls.Add(name);
            if (hasDesc)
            {
                var d = new Label { Text = e.Desc, AutoSize = false, Left = 6, Top = 34, Width = w - 12, Height = 22,
                    ForeColor = T.Muted, Font = new Font("Segoe UI", 8f), TextAlign = ContentAlignment.TopLeft, AutoEllipsis = true };
                row.Controls.Add(d);
            }

            int cx = 224, cw = w - 224 - 4;
            var v = CurVal(e);

            if (e.Type == "Boolean")
            {
                var cb = new CheckBox { Checked = v=="true", Left = cx, Top = 6, AutoSize = true, ForeColor = T.Fg, Text = "" };
                cb.CheckedChanged += (_,_) => SetVal(e, cb.Checked ? "true":"false");
                row.Controls.Add(cb);
            }
            else if (e.Accept != null)
            {
                var combo = new ComboBox { Left = cx, Top = 5, Width = Math.Min(180, cw), DropDownStyle = ComboBoxStyle.DropDownList };
                Style(combo); combo.Items.AddRange(e.Accept.Cast<object>().ToArray());
                combo.SelectedItem = e.Accept.FirstOrDefault(a=>a==v) ?? (object?)null;
                combo.SelectedIndexChanged += (_,_) => SetVal(e, combo.SelectedItem?.ToString() ?? v);
                row.Controls.Add(combo);
            }
            else if (e.Type == "Single")
            {
                bool isMul = Regex.IsMatch(e.Key, "multiplier", RegexOptions.IgnoreCase);
                int max = isMul ? 200 : 100;
                double cur = double.TryParse(v, out var dv) ? dv : 0;
                var bar = new TrackBar { Left = cx, Top = 2, Width = cw - 56, Minimum = 0, Maximum = max,
                    TickStyle = TickStyle.None, Value = Math.Clamp((int)Math.Round(cur*100), 0, max), BackColor = T.Panel };
                var lab = new Label { Left = cx + cw - 50, Top = 7, Width = 46, ForeColor = T.Accent,
                    Text = (bar.Value/100.0).ToString("0.00"), TextAlign = ContentAlignment.MiddleRight };
                bar.ValueChanged += (_,_) => { lab.Text = (bar.Value/100.0).ToString("0.00"); SetVal(e, (bar.Value/100.0).ToString("0.##")); };
                row.Controls.Add(bar); row.Controls.Add(lab);
            }
            else if (e.Type == "Int32")
            {
                var num = new NumericUpDown { Left = cx, Top = 5, Width = 110, Minimum = 0, Maximum = 100000, Increment = 10 };
                Style(num); num.Value = int.TryParse(v, out var iv) ? Math.Clamp(iv,0,100000) : 0;
                num.ValueChanged += (_,_) => SetVal(e, ((int)num.Value).ToString());
                row.Controls.Add(num);
            }
            else
            {
                var tb = new TextBox { Left = cx, Top = 5, Width = cw, Text = v };
                Style(tb);
                if (Regex.IsMatch(e.Key, "webhook", RegexOptions.IgnoreCase)) tb.PlaceholderText = "your XToys private webhook ID";
                tb.TextChanged += (_,_) => SetVal(e, tb.Text);
                row.Controls.Add(tb);
            }
            return row;
        }

        void Save()
        {
            if (_path.Length == 0) { Browse(); if(_path.Length==0) return; }
            try
            {
                File.WriteAllText(_path, string.Join("\r\n", _lines));
                _dirty = false; Title();
                _status.Text = $"Saved {Path.GetFileName(_path)} at {DateTime.Now:HH:mm:ss}. Relaunch the game to apply.";
            }
            catch (Exception ex) { _status.Text = "Save failed: " + ex.Message; }
        }

        // ── Styling helpers ──
        static void Style(Control c){ c.BackColor = T.Ink; c.ForeColor = T.Fg; if (c is TextBox tb) tb.BorderStyle = BorderStyle.FixedSingle; }
        static void StyleAccent(Button b){ b.BackColor = T.Accent; b.ForeColor = Color.FromArgb(0x04,0x21,0x0f); b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.Font = new Font("Segoe UI", 9f, FontStyle.Bold); }
        static Button MkButton(string text, int x, int y)
        {
            var b = new Button { Text = text, Left = x, Top = y, Width = 80, Height = 24, FlatStyle = FlatStyle.Flat,
                BackColor = T.Panel2, ForeColor = T.Fg };
            b.FlatAppearance.BorderColor = T.Line;
            return b;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_dirty)
            {
                var r = MessageBox.Show("Save changes before closing?", "Vibinwood — Config",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes) Save();
            }
            base.OnFormClosing(e);
        }
    }
}
