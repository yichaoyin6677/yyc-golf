using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DesktopMiniGolf
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
            {
                string result = Tests.Run();
                File.WriteAllText(args.Length > 1 ? args[1] : "self-test.txt", result, Encoding.UTF8);
                return;
            }
            if (args.Length > 1 && args[0] == "--make-icon")
            { Art.SaveIcon(args[1]); return; }
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--smoke-test")
            {
                try
                {
                    using (GolfForm form = new GolfForm())
                    using (Timer closeTimer = new Timer())
                    {
                        bool painted = false;
                        form.Paint += delegate { painted = true; };
                        // CI launches helpers hidden. Exercise the actual renderer explicitly,
                        // then run the normal message loop and timed window close.
                        form.CreateControl();
                        using (Bitmap preview = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                            form.DrawToBitmap(preview, new Rectangle(Point.Empty, form.ClientSize));
                        if (!painted) throw new InvalidOperationException("The main window did not render.");
                        closeTimer.Interval = 1200;
                        closeTimer.Tick += delegate { closeTimer.Stop(); form.Close(); };
                        closeTimer.Start(); Application.Run(form);
                    }
                }
                catch (Exception e)
                {
                    File.WriteAllText(args.Length > 1 ? args[1] : "smoke-test.txt", e.ToString());
                    Environment.ExitCode = 1;
                }
                return;
            }
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            { MessageBox.Show("发生了一点问题，请重新打开游戏。\n" + e.Exception.Message, "桌面迷你高尔夫"); };
            Application.Run(new GolfForm());
        }
    }

    class DesktopAsset : IDisposable
    {
        public string Name;
        public Bitmap Image;
        public void Dispose() { if (Image != null) Image.Dispose(); }
    }

    static class DesktopIcons
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr Icon;
            public int Index;
            public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attr, out SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        public static string DisplayName(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".lnk" || ext == ".url" || ext == ".appref-ms" ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path);
        }
        static int Priority(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".lnk" || ext == ".url" || ext == ".exe" || ext == ".appref-ms") return 0;
            return Directory.Exists(path) ? 1 : 2;
        }
        public static List<string> FindEntries(params string[] roots)
        {
            List<string> paths = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots)
            {
                try
                {
                    foreach (string path in Directory.GetFileSystemEntries(root))
                    {
                        try
                        {
                            string file = Path.GetFileName(path);
                            if (file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                            if ((File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                            if (seen.Add(file)) paths.Add(path);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            paths.Sort(delegate(string a, string b)
            {
                int rank = Priority(a).CompareTo(Priority(b));
                return rank == 0 ? StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(a), Path.GetFileName(b)) : rank;
            });
            return paths;
        }
        public static List<DesktopAsset> Read(out int realCount)
        {
            string[] roots = { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) };
            return ReadFrom(out realCount, roots);
        }
        public static List<DesktopAsset> ReadFrom(out int realCount, params string[] roots)
        {
            List<DesktopAsset> all = new List<DesktopAsset>();
            foreach (string path in FindEntries(roots))
            {
                if (all.Count >= 24) break;
                try
                {
                    SHFILEINFO info;
                    SHGetFileInfo(path, 0, out info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 0x100);
                    if (info.Icon != IntPtr.Zero)
                    {
                        try { all.Add(new DesktopAsset { Name = DisplayName(path), Image = Icon.FromHandle(info.Icon).ToBitmap() }); }
                        finally { DestroyIcon(info.Icon); }
                    }
                }
                catch { }
            }
            realCount = all.Count;
            if (all.Count == 0)
            {
                string[] names = { "周报.docx", "灵感便签", "此电脑", "项目资料", "待办清单" };
                for (int i = 0; i < names.Length; i++) all.Add(new DesktopAsset { Name = names[i], Image = Art.FileIcon(i) });
            }
            return all;
        }
    }

    class Hit
    {
        public RectangleF Bounds;
        public string Id;
        public Hit(string id, float x, float y, float w, float h) { Id = id; Bounds = new RectangleF(x, y, w, h); }
    }
    class Particle
    {
        public V Pos, Vel;
        public double Life, MaxLife;
        public Color Color;
    }

    class GolfForm : Form
    {
        readonly Color Ink = Color.FromArgb(28, 52, 46), Muted = Color.FromArgb(112, 129, 118), Green = Color.FromArgb(34, 103, 77);
        readonly List<Course> courses = Course.Create();
        readonly List<DesktopAsset> assets;
        readonly List<Hit> hits = new List<Hit>();
        readonly List<Particle> particles = new List<Particle>();
        readonly List<V> trail = new List<V>();
        readonly Random random = new Random();
        readonly Timer timer = new Timer();
        readonly Stopwatch watch = Stopwatch.StartNew();
        readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        readonly int[] completed = { -1, -1, -1, -1, -1, -1 };
        readonly int[] best = { -1, -1, -1, -1, -1, -1 };
        readonly string recordPath;
        Physics game;
        int level, seed, oldBounces, oldWater, realAssetCount;
        double lastTime, time, pottedTime, toastTime;
        float scale = 1, offsetX, offsetY;
        bool dragging, sound = true, help, fullscreen, hasFocus, editing;
        string editKind = "";
        Block editBlock;
        V editOld, editOffset;
        V mouse, dragOrigin;
        string hover = "", toast = "";
        Rectangle savedBounds;
        FormBorderStyle savedBorder;
        double keyboardAngle = -.1, keyboardPower = .5;
        bool keyboardAim;
        SoundBank audio = new SoundBank();

        public GolfForm()
        {
            Text = "桌面迷你高尔夫 · 下班一杆";
            DoubleBuffered = true;
            BackColor = Color.FromArgb(246, 246, 237);
            Font = new Font("Microsoft YaHei UI", 10);
            Rectangle available = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1280, available.Width - 70), Math.Min(880, available.Height - 85));
            MinimumSize = new Size(800, 560);
            SetStyle(ControlStyles.ResizeRedraw, true);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            Icon = Art.AppIcon();
            assets = DesktopIcons.Read(out realAssetCount);
            recordPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopMiniGolf", "records-v1.txt");
            try
            {
                string[] rows = File.ReadAllLines(recordPath);
                for (int i = 0; i < Math.Min(6, rows.Length); i++) { int n; if (int.TryParse(rows[i], out n) && n > 0) best[i] = n; }
            }
            catch { }
            seed = random.Next();
            SetLevel(6);
            Toast(realAssetCount > 0 ? "已读取本机 " + realAssetCount + " 个桌面图标，球场准备好了。" : "桌面没有可用图标，先用示例图标开一局。");
            timer.Interval = 16;
            timer.Tick += TickGame;
            timer.Start();
            MouseDown += Down;
            MouseMove += MovePointer;
            MouseUp += Up;
            MouseCaptureChanged += delegate { if (!Capture) { dragging = false; CancelEdit(); } };
            KeyDown += KeysDown;
            Activated += delegate { hasFocus = true; lastTime = watch.Elapsed.TotalSeconds; Invalidate(); };
            Deactivate += delegate { hasFocus = false; dragging = false; CancelEdit(); Capture = false; Invalidate(); };
            FormClosed += delegate
            {
                timer.Stop(); timer.Dispose(); audio.Dispose();
                foreach (DesktopAsset a in assets) a.Dispose();
                foreach (Font f in fonts.Values) f.Dispose();
            };
            AccessibleName = "桌面迷你高尔夫";
            AccessibleDescription = "拖动高尔夫球向后蓄力，松手击球。方向键调整，空格击球，R 重开，F11 全屏，Esc 退出。";
        }

        Font F(float size, bool bold)
        {
            string k = size.ToString() + bold;
            if (!fonts.ContainsKey(k)) fonts[k] = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            return fonts[k];
        }
        void TextAt(Graphics g, string text, float x, float y, float size, Color color, bool bold)
        { using (Brush b = new SolidBrush(color)) g.DrawString(text, F(size, bold), b, x, y); }
        void CenterText(Graphics g, string text, RectangleF r, float size, Color color, bool bold)
        {
            using (StringFormat fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (Brush b = new SolidBrush(color)) g.DrawString(text, F(size, bold), b, r, fmt);
        }
        void Label(Graphics g, string text, RectangleF r, float size, Color color, bool bold)
        {
            using (StringFormat fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (Brush b = new SolidBrush(color)) g.DrawString(text, F(size, bold), b, r, fmt);
        }
        void Round(Graphics g, RectangleF r, float radius, Color fill)
        { using (GraphicsPath p = Art.Rounded(r, radius)) using (Brush b = new SolidBrush(fill)) g.FillPath(b, p); }
        void StrokeRound(Graphics g, RectangleF r, float radius, Color color, float width)
        { using (GraphicsPath p = Art.Rounded(r, radius)) using (Pen pen = new Pen(color, width)) g.DrawPath(pen, p); }
        void Button(Graphics g, string id, string label, float x, float y, float w, bool primary)
        {
            RectangleF r = new RectangleF(x, y, w, 38);
            Color color = primary ? Green : Color.FromArgb(232, 235, 222);
            if (hover == id) color = primary ? Color.FromArgb(22, 83, 59) : Color.FromArgb(216, 225, 208);
            Round(g, r, 11, color);
            CenterText(g, label, r, 13, primary ? Color.White : Ink, true);
            hits.Add(new Hit(id, x, y, w, 38));
        }
        void Transform()
        {
            scale = Math.Min(ClientSize.Width / 1280f, ClientSize.Height / 880f);
            offsetX = (ClientSize.Width - 1280 * scale) / 2;
            offsetY = (ClientSize.Height - 880 * scale) / 2;
        }
        V UI(Point p) { Transform(); return new V((p.X - offsetX) / scale, (p.Y - offsetY) / scale); }
        V World(V p) { return p - new V(80, 190); }
        void SetLevel(int index)
        {
            CancelEdit();
            level = index;
            Course c = level == 6 ? Course.Desktop(seed, assets.Count) : courses[level];
            game = new Physics(c);
            dragging = false; Capture = false; keyboardAim = false; editing = false;
            help = false; oldWater = oldBounces = 0; particles.Clear(); trail.Clear(); pottedTime = 0;
            Invalidate();
        }
        int Total()
        {
            int n = game.Strokes;
            for (int i = 0; i < completed.Length; i++) if (i != level && completed[i] >= 0) n += completed[i];
            return n;
        }
        void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(recordPath));
                string[] rows = new string[6];
                for (int i = 0; i < 6; i++) rows[i] = best[i].ToString();
                File.WriteAllLines(recordPath, rows);
            }
            catch { }
        }
        void Toast(string text) { toast = text; toastTime = time + 2.6; }
        void Play(string name) { if (sound) audio.Play(name); }
        void TickGame(object sender, EventArgs e)
        {
            double now = watch.Elapsed.TotalSeconds;
            double dt = Math.Min(.045, Math.Max(0, now - lastTime)); lastTime = now;
            if (!hasFocus || WindowState == FormWindowState.Minimized) return;
            time += dt;
            if (!help)
            {
                bool before = game.Potted;
                if (game.Moving) { trail.Add(game.Ball); if (trail.Count > 22) trail.RemoveAt(0); }
                else if (trail.Count > 0) trail.RemoveAt(0);
                game.Step(dt);
                if (game.Bounces > oldBounces) { Play("bounce"); oldBounces = game.Bounces; }
                if (game.WaterCount > oldWater)
                { oldWater = game.WaterCount; Play("water"); trail.Clear(); Toast("扑通！回到上一杆，加罚 1 杆。"); }
                if (!before && game.Potted)
                {
                    pottedTime = time; Play("hole");
                    if (level < 6)
                    {
                        completed[level] = game.Strokes;
                        if (best[level] < 0 || game.Strokes < best[level]) best[level] = game.Strokes;
                        Save();
                    }
                    for (int i = 0; i < 70; i++) particles.Add(new Particle
                    { Pos = game.Course.Hole, Vel = new V(random.NextDouble() * 440 - 220, -100 - random.NextDouble() * 330),
                      Life = 1.4 + random.NextDouble(), MaxLife = 2.4,
                      Color = i % 3 == 0 ? Color.FromArgb(242, 191, 76) : i % 3 == 1 ? Green : Color.FromArgb(238, 129, 102) });
                }
                for (int i = particles.Count - 1; i >= 0; i--)
                {
                    Particle p = particles[i]; p.Life -= dt;
                    p.Pos += p.Vel * dt; p.Vel.Y += 310 * dt;
                    if (p.Life <= 0) particles.RemoveAt(i);
                }
            }
            Invalidate();
        }
        string HitAt(V p)
        {
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].Bounds.Contains(p.Point)) return hits[i].Id;
            return "";
        }
        void Down(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { dragging = false; CancelEdit(); Capture = false; Invalidate(); return; }
            if (e.Button != MouseButtons.Left) return;
            V ui = UI(e.Location); string id = HitAt(ui);
            if (id != "") { Act(id); return; }
            if (help || game.Potted) return;
            V p = World(ui);
            if (editing)
            {
                foreach (Block b in game.Course.Blocks)
                {
                    RectangleF r = b.Rect; r.Inflate(8, 8);
                    if (!r.Contains(p.Point)) continue;
                    editKind = "block"; editBlock = b; editOld = new V(b.Rect.X, b.Rect.Y); editOffset = p - editOld; Capture = true; return;
                }
                if ((p - game.Course.Hole).Length < 48) { editKind = "hole"; editOld = game.Course.Hole; Capture = true; }
                else if ((p - game.Ball).Length < 45) { editKind = "ball"; editOld = game.Ball; Capture = true; }
                return;
            }
            if ((p - game.Ball).Length <= 50 && !game.Moving)
            { dragging = true; keyboardAim = false; dragOrigin = game.Ball; mouse = p; Capture = true; }
        }
        void MovePointer(object sender, MouseEventArgs e)
        {
            V ui = UI(e.Location); mouse = World(ui); hover = HitAt(ui);
            if (editKind != "")
            {
                double x = Math.Max(70, Math.Min(1050, mouse.X)), y = Math.Max(80, Math.Min(520, mouse.Y));
                if (editKind == "block")
                { RectangleF r = editBlock.Rect; r.X = (float)Math.Max(40, Math.Min(1020, mouse.X - editOffset.X)); r.Y = (float)Math.Max(80, Math.Min(495, mouse.Y - editOffset.Y)); editBlock.Rect = r; }
                else if (editKind == "hole") game.Course.Hole = new V(x, y);
                else game.Ball = game.Course.Start = new V(x, y);
            }
            Cursor = hover != "" || (!game.Potted && !game.Moving && (mouse - game.Ball).Length < 50) ? Cursors.Hand : dragging ? Cursors.Cross : Cursors.Default;
            if (editing && hover == "") Cursor = Cursors.SizeAll;
            Invalidate();
        }
        void Up(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (editKind != "")
            {
                if (!ValidPlacement()) { CancelEdit(); Toast("留一点空隙，球才过得去。"); }
                else editKind = "";
                Capture = false; Invalidate(); return;
            }
            if (!dragging) return;
            V pull = dragOrigin - World(UI(e.Location));
            dragging = false; Capture = false;
            if (game.Shoot(pull)) { Play("shot"); keyboardAim = false; trail.Clear(); }
            Invalidate();
        }
        bool ValidPlacement()
        {
            if ((game.Ball - game.Course.Hole).Length < 70) return false;
            for (int i = 0; i < game.Course.Blocks.Count; i++)
            {
                RectangleF r = game.Course.Blocks[i].Rect; r.Inflate(25, 25);
                if (r.Contains(game.Ball.Point) || r.Contains(game.Course.Hole.Point)) return false;
                r = game.Course.Blocks[i].Rect; r.Inflate(6, 6);
                for (int j = i + 1; j < game.Course.Blocks.Count; j++)
                { RectangleF other = game.Course.Blocks[j].Rect; other.Inflate(6, 6); if (r.IntersectsWith(other)) return false; }
            }
            return true;
        }
        void CancelEdit()
        {
            if (editKind == "block") { RectangleF r = editBlock.Rect; r.X = (float)editOld.X; r.Y = (float)editOld.Y; editBlock.Rect = r; }
            else if (editKind == "hole") game.Course.Hole = editOld;
            else if (editKind == "ball") game.Ball = game.Course.Start = editOld;
            editKind = "";
        }
        void Act(string id)
        {
            if (id == "sound") { sound = !sound; return; }
            if (id == "quit") { Close(); return; }
            if (id == "full") { Fullscreen(); return; }
            if (id == "help") { help = !help; dragging = false; return; }
            if (id == "close-help") { help = false; return; }
            if (id == "desktop")
            {
                int count;
                List<DesktopAsset> current = DesktopIcons.Read(out count);
                foreach (DesktopAsset a in assets) a.Dispose();
                assets.Clear(); assets.AddRange(current); realAssetCount = count;
                seed = random.Next(); SetLevel(6);
                Toast(count > 0 ? "已重新读取本机 " + count + " 个桌面图标。" : "桌面没有可用图标，使用示例图标。");
                return;
            }
            if (id == "shuffle") { seed = random.Next(); SetLevel(6); return; }
            if (id == "edit")
            {
                if (game.Moving) { Toast("等球停下来，再摆球场。"); return; }
                if (game.Potted) SetLevel(6);
                CancelEdit(); editing = !editing; keyboardAim = false;
                if (!editing) { game.Strokes = 0; game.LastShot = game.Ball; }
                Toast(editing ? "拖动图标、白球和球洞，摆出你喜欢的路线。" : "球场摆好了。向后拉球，开打！");
                return;
            }
            if (id == "restart") { SetLevel(level); return; }
            if (id == "next")
            {
                if (level == 6) { seed = random.Next(); SetLevel(6); }
                else if (level == 5) { for (int i = 0; i < 6; i++) completed[i] = -1; SetLevel(0); }
                else SetLevel(level + 1);
                return;
            }
            if (id.StartsWith("level")) SetLevel(int.Parse(id.Substring(5)));
        }
        void Fullscreen()
        {
            if (!fullscreen)
            {
                savedBounds = Bounds; savedBorder = FormBorderStyle;
                FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds; fullscreen = true;
            }
            else { FormBorderStyle = savedBorder; Bounds = savedBounds; fullscreen = false; }
        }
        void KeysDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { if (help) help = false; else Close(); }
            if (e.KeyCode == Keys.F11) Fullscreen();
            if (e.KeyCode == Keys.R && !help) SetLevel(level);
            if (e.KeyCode == Keys.H) { help = !help; dragging = false; Capture = false; }
            if (e.KeyCode == Keys.M) sound = !sound;
            if (help) { Invalidate(); return; }
            if (e.KeyCode == Keys.Space)
            {
                if (game.Potted && time - pottedTime > .4) Act("next");
                else if (!game.Moving && !dragging && !editing)
                { if (game.Shoot(new V(Math.Cos(keyboardAngle), Math.Sin(keyboardAngle)) * (Physics.MaxPull * keyboardPower))) Play("shot"); }
                e.Handled = true; e.SuppressKeyPress = true;
            }
            if (!game.Moving && !game.Potted && !dragging && !editing)
            {
                double step = e.Shift ? .2 : .05;
                if (e.KeyCode == Keys.Left) { keyboardAngle -= step; keyboardAim = true; }
                if (e.KeyCode == Keys.Right) { keyboardAngle += step; keyboardAim = true; }
                if (e.KeyCode == Keys.Up) { keyboardPower = Math.Min(1, keyboardPower + .05); keyboardAim = true; }
                if (e.KeyCode == Keys.Down) { keyboardPower = Math.Max(.05, keyboardPower - .05); keyboardAim = true; }
                if (e.KeyCode >= Keys.Left && e.KeyCode <= Keys.Down) { e.Handled = true; e.SuppressKeyPress = true; }
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Transform(); g.TranslateTransform(offsetX, offsetY); g.ScaleTransform(scale, scale);
            hits.Clear();
            // The application is its own scene. No desktop files or icon positions are changed.
            Round(g, new RectangleF(62, 35, 38, 38), 13, Green);
            using (Pen p = new Pen(Color.White, 2)) { g.DrawLine(p, 75, 46, 75, 65); g.DrawLine(p, 75, 46, 88, 50); }
            using (Brush b = new SolidBrush(Color.FromArgb(247, 210, 108))) g.FillEllipse(b, 82, 62, 5, 5);
            TextAt(g, "下班一杆", 112, 30, 28, Ink, true);
            TextAt(g, "DESKTOP MINI GOLF  ·  " + (realAssetCount > 0 ? "本机 " + realAssetCount + " 个图标" : "示例图标"), 114, 65, 10, Muted, true);
            Button(g, "sound", sound ? "声音  开" : "声音  关", 815, 39, 88, false);
            Button(g, "desktop", "刷新桌面图标", 913, 39, 142, false);
            Button(g, "full", fullscreen ? "窗口" : "全屏", 1065, 39, 68, false);
            Button(g, "quit", "退出", 1143, 39, 74, false);
            TextAt(g, level == 6 ? "自由练习" : "HOLE  " + (level + 1).ToString("00") + " / 06", 65, 112, 11, Muted, true);
            TextAt(g, game.Course.Name, 62, 130, 27, Ink, true);
            for (int i = 0; i < 6; i++)
            {
                float x = 424 + i * 43;
                bool selected = level == i;
                Round(g, new RectangleF(x, 124, 33, 33), 12, selected ? Green : Color.FromArgb(231, 234, 220));
                CenterText(g, completed[i] >= 0 ? "✓" : (i + 1).ToString(), new RectangleF(x, 124, 33, 33), 13, selected ? Color.White : Green, true);
                hits.Add(new Hit("level" + i, x, 124, 33, 33));
            }
            TextAt(g, "本洞杆数", 790, 111, 11, Muted, false);
            TextAt(g, game.Strokes.ToString("00"), 795, 130, 28, Ink, true);
            TextAt(g, "标准杆", 902, 111, 11, Muted, false);
            TextAt(g, game.Course.Par.ToString("00"), 906, 130, 28, Ink, true);
            TextAt(g, "累计杆数", 1004, 111, 11, Muted, false);
            TextAt(g, Total().ToString("00"), 1012, 130, 28, Ink, true);
            Button(g, "restart", "重开  R", 1111, 124, 106, false);
            Round(g, new RectangleF(61, 179, 1158, 626), 26, Color.FromArgb(20, 34, 66, 44));
            Round(g, new RectangleF(60, 176, 1160, 626), 26, Color.FromArgb(227, 234, 217));
            GraphicsState scene = g.Save();
            g.TranslateTransform(80, 190);
            using (GraphicsPath clip = Art.Rounded(new RectangleF(0, 0, 1120, 600), 18)) g.SetClip(clip);
            DrawCourse(g);
            g.Restore(scene);
            TextAt(g, "拖住球向后拉，松手击球", 64, 823, 14, Ink, true);
            TextAt(g, "右键取消  ·  R 重开  ·  F11 全屏  ·  Esc 退出", 65, 847, 11, Muted, false);
            if (level == 6)
            {
                Button(g, "shuffle", "换个布局", 898, 823, 100, false);
                Button(g, "edit", editing ? "摆好了，开打" : "摆一摆球场", 1008, 823, 153, editing);
            }
            else TextAt(g, "工作暂停。球场开放。", 926, 829, 13, Muted, false);
            Button(g, "help", "?", 1177, 823, 40, false);
            if (help) { hits.Clear(); DrawHelp(g); }
        }

        void DrawCourse(Graphics g)
        {
            using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, 1120, 600), Color.FromArgb(213, 230, 211), Color.FromArgb(183, 210, 180), 80f)) g.FillRectangle(bg, 0, 0, 1120, 600);
            using (Brush b = new SolidBrush(Color.FromArgb(24, 255, 255, 245)))
            {
                g.FillEllipse(b, -150, 310, 1010, 550); g.FillEllipse(b, 550, -220, 950, 480);
            }
            using (Pen grid = new Pen(Color.FromArgb(20, 56, 102, 72)))
                for (int x = 50; x < 1100; x += 38) for (int y = 50; y < 590; y += 38) g.DrawEllipse(grid, x, y, 1, 1);
            Round(g, new RectangleF(22, 22, 1076, 556), 18, Color.FromArgb(38, 255, 255, 238));
            StrokeRound(g, new RectangleF(29, 29, 1062, 542), 13, Color.FromArgb(140, 255, 255, 242), 3);
            TextAt(g, game.Course.Subtitle, 53, 52, 10, Color.FromArgb(87, 126, 91), true);
            foreach (Zone z in game.Course.Zones) DrawZone(g, z);
            foreach (Block b in game.Course.Blocks) DrawBlock(g, b);
            if (game.Strokes == 0)
            {
                using (Pen p = new Pen(Color.FromArgb(100, Green), 1)) { p.DashStyle = DashStyle.Dot; g.DrawEllipse(p, (float)game.Course.Start.X - 23, (float)game.Course.Start.Y - 23, 46, 46); }
                Label(g, "START", new RectangleF((float)game.Course.Start.X - 40, (float)game.Course.Start.Y + 29, 80, 20), 9, Green, true);
            }
            DrawHole(g);
            for (int i = 0; i < trail.Count; i++)
            {
                using (Brush b = new SolidBrush(Color.FromArgb(10 + i * 3, Color.White)))
                    g.FillEllipse(b, (float)trail[i].X - 4, (float)trail[i].Y - 4, 8, 8);
            }
            if (!game.Potted)
            {
                if (!editing && (dragging || (keyboardAim && !game.Moving))) DrawAim(g);
                DrawBall(g, game.Ball, 1);
                if (!game.Moving && !dragging && !keyboardAim)
                {
                    float r = (float)(22 + Math.Sin(time * 3) * 2);
                    using (Pen p = new Pen(Color.FromArgb(65, Green), 1.3f))
                        g.DrawEllipse(p, (float)game.Ball.X - r, (float)game.Ball.Y - r, r * 2, r * 2);
                    if (game.Strokes == 0 && level == 0)
                    {
                        Round(g, new RectangleF((float)game.Ball.X - 82, (float)game.Ball.Y + 70, 164, 38), 13, Color.FromArgb(235, 249, 249, 238));
                        CenterText(g, "按住球，向后拉  ↙", new RectangleF((float)game.Ball.X - 82, (float)game.Ball.Y + 70, 164, 38), 12, Green, true);
                    }
                }
            }
            else if (time - pottedTime < .3) DrawBall(g, game.Ball, Math.Max(0, 1 - (time - pottedTime) / .3));
            foreach (Particle p in particles)
                using (Brush b = new SolidBrush(Color.FromArgb((int)(Math.Min(1, p.Life) * 230), p.Color)))
                    g.FillRectangle(b, (float)p.Pos.X, (float)p.Pos.Y, 5, 8);
            string hint = toastTime > time ? toast : editing ? "拖动图标、白球和球洞。摆好后点“摆好了，开打”。" : game.Moving ? "球在路上。让它再滚一会儿。" : game.Course.Hint;
            SizeF textSize = g.MeasureString(hint, F(12, false));
            float hintW = Math.Min(700, textSize.Width + 36);
            Round(g, new RectangleF(560 - hintW / 2, 549, hintW, 30), 11, Color.FromArgb(215, 246, 249, 235));
            CenterText(g, hint, new RectangleF(560 - hintW / 2, 549, hintW, 30), 12, Green, false);
            if (game.Potted && time - pottedTime > .55) DrawResult(g);
        }

        void DrawBlock(Graphics g, Block b)
        {
            RectangleF r = b.Rect;
            Round(g, new RectangleF(r.X + 2, r.Y + 5, r.Width, r.Height), 12, Color.FromArgb(32, 40, 67, 44));
            Round(g, r, 12, Color.FromArgb(230, 246, 247, 236));
            StrokeRound(g, r, 12, Color.FromArgb(90, 255, 255, 250), 1.4f);
            if (editing) StrokeRound(g, new RectangleF(r.X - 4, r.Y - 4, r.Width + 8, r.Height + 8), 14, Color.FromArgb(100, Green), 1.3f);
            if (b.Icon)
            {
                DesktopAsset a = assets[b.Asset % assets.Count];
                g.DrawImage(a.Image, r.X + 9, r.Y + 8, 40, 40);
                Label(g, a.Name, new RectangleF(r.X - 25, r.Bottom + 5, r.Width + 50, 23), 11, Ink, false);
                using (Brush dot = new SolidBrush(Color.FromArgb(56, Green))) g.FillEllipse(dot, r.X + 26, r.Y + 53, 5, 3);
            }
            else
            {
                Round(g, new RectangleF(r.X + 8, r.Y + 9, r.Width - 16, 19), 5, Color.FromArgb(218, 226, 209));
                using (Brush dot = new SolidBrush(Color.FromArgb(138, 167, 140)))
                    for (int i = 0; i < 3; i++) g.FillEllipse(dot, r.X + 15 + i * 9, r.Y + 16, 4, 4);
                string[] lines = b.Label.ToCharArray().Length > 0 ? new string[] { b.Label } : new string[] { "" };
                using (StringFormat fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (Brush br = new SolidBrush(Green))
                    g.DrawString(lines[0], F(14, true), br, new RectangleF(r.X + 12, r.Y + 46, r.Width - 24, Math.Min(90, r.Height - 60)), fmt);
                using (Pen p = new Pen(Color.FromArgb(130, 177, 193, 161), 3))
                    for (int i = 0; i < 4; i++)
                    { float y = r.Y + r.Height - 85 + i * 15; g.DrawLine(p, r.X + 17, y, r.Right - 17 - (i % 2) * 15, y); }
            }
        }
        void DrawZone(Graphics g, Zone z)
        {
            RectangleF r = z.Rect;
            Round(g, r, 24, z.Water ? Color.FromArgb(141, 191, 199) : Color.FromArgb(230, 217, 172));
            StrokeRound(g, new RectangleF(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8), 21, z.Water ? Color.FromArgb(174, 220, 221) : Color.FromArgb(241, 230, 192), 4);
            if (z.Water)
            {
                using (Pen p = new Pen(Color.FromArgb(100, 224, 250, 239), 2))
                    for (float y = r.Y + 32; y < r.Bottom - 15; y += 30)
                        for (float x = r.X + 20; x < r.Right - 30; x += 43)
                        { float off = (float)Math.Sin(time * 1.1 + y) * 4; g.DrawArc(p, x + off, y, 21, 7, 0, 180); }
            }
            else
                using (Brush b = new SolidBrush(Color.FromArgb(130, 192, 173, 129)))
                    for (int i = 0; i < 85; i++)
                    { float x = r.X + 15 + (i * 47 % (int)(r.Width - 30)), y = r.Y + 15 + (i * 73 % (int)(r.Height - 30)); g.FillEllipse(b, x, y, 2, 2); }
            Label(g, z.Water ? "摸 鱼 池" : "沙 坑", new RectangleF(r.X, r.Y + r.Height / 2 - 15, r.Width, 30), 13, z.Water ? Color.FromArgb(71, 126, 140) : Color.FromArgb(158, 136, 87), true);
        }
        void DrawHole(Graphics g)
        {
            float x = (float)game.Course.Hole.X, y = (float)game.Course.Hole.Y;
            using (Brush b = new SolidBrush(Color.FromArgb(29, Green))) g.FillEllipse(b, x - 32, y - 26, 64, 52);
            using (Brush b = new SolidBrush(Color.FromArgb(145, 176, 137))) g.FillEllipse(b, x - 23, y - 19, 46, 38);
            using (Brush b = new SolidBrush(Color.FromArgb(29, 60, 44))) g.FillEllipse(b, x - 17, y - 15, 34, 30);
            using (Pen p = new Pen(Color.FromArgb(247, 248, 229), 2.5f)) g.DrawLine(p, x + 6, y - 7, x + 6, y - 83);
            using (Brush b = new SolidBrush(Color.FromArgb(230, 128, 94)))
                g.FillPolygon(b, new PointF[] { new PointF(x + 7, y - 83), new PointF(x + 40, y - 73), new PointF(x + 7, y - 61) });
            Art.Folder(g, x + 40, y - 40, 36, 28);
            TextAt(g, "下班", x + 40, y - 6, 11, Green, true);
        }
        void DrawBall(Graphics g, V pos, double size)
        {
            float r = (float)(10 * size), x = (float)pos.X, y = (float)pos.Y;
            if (r <= 0) return;
            using (Brush b = new SolidBrush(Color.FromArgb(45, 39, 58, 43))) g.FillEllipse(b, x - r + 2, y - r + 4, r * 2, r * 2);
            using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(x - r, y - r, r * 2, r * 2), Color.White, Color.FromArgb(220, 229, 214), 60f)) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
            using (Pen p = new Pen(Color.FromArgb(190, 209, 190), .8f)) g.DrawEllipse(p, x - r, y - r, r * 2, r * 2);
            using (Brush b = new SolidBrush(Color.FromArgb(120, 195, 207, 189)))
            { g.FillEllipse(b, x + r * .2f, y + r * .2f, 2, 2); g.FillEllipse(b, x - r * .5f, y + r * .3f, 1.8f, 1.8f); g.FillEllipse(b, x - r * .1f, y - r * .3f, 1.8f, 1.8f); }
        }
        void DrawAim(Graphics g)
        {
            V pull = dragging ? dragOrigin - mouse : new V(Math.Cos(keyboardAngle), Math.Sin(keyboardAngle)) * (Physics.MaxPull * keyboardPower);
            double fraction = Math.Min(1, pull.Length / Physics.MaxPull);
            if (pull.Length < 2) return;
            V behind = game.Ball - pull.Unit * Math.Min(pull.Length, Physics.MaxPull);
            using (Pen p = new Pen(Color.FromArgb(115, Green), 2)) { p.DashStyle = DashStyle.Dot; g.DrawLine(p, game.Ball.Point, behind.Point); }
            using (Pen p = new Pen(Color.FromArgb(170, Green), 2)) g.DrawEllipse(p, (float)behind.X - 5, (float)behind.Y - 5, 10, 10);
            Physics preview = new Physics(game.Course); preview.Ball = game.Ball; preview.Shoot(pull);
            for (int i = 0; i < 28; i++)
            {
                preview.Step(.025);
                if (preview.WaterCount > 0 || preview.Potted || !preview.Moving) break;
                using (Brush b = new SolidBrush(Color.FromArgb(190 - i * 5, Green)))
                    g.FillEllipse(b, (float)preview.Ball.X - 2.3f, (float)preview.Ball.Y - 2.3f, 4.6f, 4.6f);
            }
            float bx = (float)Math.Max(50, Math.Min(950, game.Ball.X - 58));
            float by = (float)(game.Ball.Y < 120 ? game.Ball.Y + 43 : game.Ball.Y - 73);
            Round(g, new RectangleF(bx, by, 116, 43), 11, Color.FromArgb(240, 250, 249, 235));
            TextAt(g, "力度  " + (int)(fraction * 100) + "%", bx + 11, by + 6, 11, Green, true);
            Round(g, new RectangleF(bx + 11, by + 29, 94, 5), 2, Color.FromArgb(214, 223, 200));
            if (fraction > .01) Round(g, new RectangleF(bx + 11, by + 29, Math.Max(4, (float)(94 * fraction)), 5), 2, fraction > .85 ? Color.FromArgb(221, 142, 85) : Green);
        }
        string ScoreName()
        {
            int d = game.Strokes - game.Course.Par;
            return game.Strokes == 1 ? "一杆进洞！" : d <= -2 ? "老鹰球！" : d == -1 ? "小鸟球！" : d == 0 ? "标准杆，漂亮！" : "进洞，下班！";
        }
        void DrawResult(Graphics g)
        {
            using (Brush b = new SolidBrush(Color.FromArgb(100, 191, 210, 182))) g.FillRectangle(b, 0, 0, 1120, 600);
            Round(g, new RectangleF(354, 130, 420, 326), 27, Color.FromArgb(30, 34, 66, 44));
            Round(g, new RectangleF(350, 122, 420, 326), 27, Color.FromArgb(250, 250, 240));
            CenterText(g, "NICE PUTT", new RectangleF(350, 149, 420, 25), 11, Muted, true);
            CenterText(g, ScoreName(), new RectangleF(350, 183, 420, 49), 29, Green, true);
            CenterText(g, game.Strokes + " 杆完成  /  标准杆 " + game.Course.Par, new RectangleF(350, 240, 420, 34), 16, Ink, false);
            string record = level < 6 ? "本洞最佳 " + best[level] + " 杆  ·  累计 " + Total() + " 杆" : "你的桌面，也可以是一座球场。";
            bool allDone = true;
            foreach (int n in completed) if (n < 0) allDone = false;
            if (level == 5 && allDone) record = "六洞完成！总计 " + Total() + " 杆，标准杆 20。";
            CenterText(g, record, new RectangleF(350, 280, 420, 30), 13, Muted, false);
            // Hit rectangles are in full-window design coordinates.
            Round(g, new RectangleF(397, 339, 170, 43), 12, hover == "next" ? Color.FromArgb(20, 85, 59) : Green);
            CenterText(g, level == 6 ? "再来一个球场  →" : level == 5 ? "再打一轮  →" : "下一洞  →", new RectangleF(397, 339, 170, 43), 14, Color.White, true);
            hits.Add(new Hit("next", 477, 529, 170, 43));
            Round(g, new RectangleF(581, 339, 137, 43), 12, Color.FromArgb(231, 235, 219));
            CenterText(g, "重打这一洞", new RectangleF(581, 339, 137, 43), 13, Green, true);
            hits.Add(new Hit("restart", 661, 529, 137, 43));
            CenterText(g, "空格进入下一洞", new RectangleF(350, 395, 420, 25), 10, Muted, false);
        }
        void DrawHelp(Graphics g)
        {
            using (Brush b = new SolidBrush(Color.FromArgb(150, 211, 223, 205))) g.FillRectangle(b, 0, 0, 1280, 880);
            Round(g, new RectangleF(365, 205, 550, 440), 27, Color.FromArgb(250, 250, 240));
            TextAt(g, "先摸清，再摸鱼。", 410, 240, 27, Ink, true);
            string[] lines = {
                "①  按住球向后拖，拉得越远，力度越大。",
                "②  松开鼠标击球，虚线会显示一段预测路线。",
                "③  绕开图标，把球打进“下班”文件夹前的球洞。",
                "④  沙坑会减速；落水回到上一杆位置，加罚一杆。",
                "键盘也能玩：← → 转向，↑ ↓ 力度，空格击球。",
                "R 重开 · M 声音 · F11 全屏 · Esc 退出。"
            };
            for (int i = 0; i < lines.Length; i++) TextAt(g, lines[i], 411, 304 + i * 37, i < 4 ? 14 : 12, i < 4 ? Ink : Muted, false);
            Button(g, "close-help", "知道了，开打", 411, 567, 458, true);
        }
    }

    static class Art
    {
        public static GraphicsPath Rounded(RectangleF r, float rad)
        {
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            GraphicsPath p = new GraphicsPath();
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        public static void Folder(Graphics g, float x, float y, float w, float h)
        {
            using (Brush b = new SolidBrush(Color.FromArgb(215, 163, 64))) g.FillRectangle(b, x, y, w * .48f, h * .28f);
            using (Brush b = new SolidBrush(Color.FromArgb(240, 195, 98)))
            using (GraphicsPath p = Rounded(new RectangleF(x, y + h * .18f, w, h * .82f), 4)) g.FillPath(b, p);
            using (Pen p = new Pen(Color.FromArgb(250, 221, 153), 2)) g.DrawLine(p, x + 4, y + h * .4f, x + w - 4, y + h * .4f);
        }
        public static Bitmap FileIcon(int i)
        {
            Bitmap b = new Bitmap(48, 48);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color[] colors = { Color.SteelBlue, Color.Coral, Color.Teal, Color.Goldenrod, Color.MediumSeaGreen };
                using (Brush br = new SolidBrush(Color.FromArgb(246, 247, 239))) g.FillRectangle(br, 7, 2, 33, 43);
                using (Brush br = new SolidBrush(colors[i % colors.Length])) g.FillRectangle(br, 3, 14, 32, 25);
                using (Font f = new Font("Arial", 18, FontStyle.Bold)) using (Brush br = new SolidBrush(Color.White)) g.DrawString("WIDFT"[i % 5].ToString(), f, br, 6, 13);
            }
            return b;
        }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        public static Icon AppIcon()
        {
            using (Bitmap b = new Bitmap(64, 64))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath p = Rounded(new RectangleF(0, 0, 64, 64), 17)) using (Brush br = new SolidBrush(Color.FromArgb(34, 103, 77))) g.FillPath(br, p);
                    using (Pen p = new Pen(Color.FromArgb(249, 245, 224), 3)) g.DrawLine(p, 24, 14, 24, 48);
                    using (Brush br = new SolidBrush(Color.FromArgb(240, 195, 98))) g.FillPolygon(br, new Point[] { new Point(25, 13), new Point(48, 22), new Point(25, 30) });
                    using (Brush br = new SolidBrush(Color.White)) g.FillEllipse(br, 36, 44, 10, 10);
                }
                IntPtr h = b.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); } finally { DestroyIcon(h); }
            }
        }
        public static void SaveIcon(string path) { using (Icon i = AppIcon()) using (FileStream f = File.Create(path)) i.Save(f); }
    }

    class SoundBank : IDisposable
    {
        readonly Dictionary<string, SoundPlayer> players = new Dictionary<string, SoundPlayer>();
        readonly List<MemoryStream> streams = new List<MemoryStream>();
        public SoundBank()
        {
            Make("shot", new double[] { 480, 280 }, .08, .1);
            Make("bounce", new double[] { 270 }, .045, .055);
            Make("water", new double[] { 280, 190, 125 }, .1, .08);
            Make("hole", new double[] { 523.25, 659.25, 783.99, 1046.5 }, .105, .1);
        }
        void Make(string name, double[] tones, double seconds, double volume)
        {
            int rate = 22050, segment = (int)(rate * seconds), count = segment * tones.Length;
            MemoryStream m = new MemoryStream(); BinaryWriter w = new BinaryWriter(m);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + count * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(count * 2);
            for (int i = 0; i < count; i++)
            {
                int local = i % segment; double t = local / (double)rate;
                double envelope = Math.Min(1, local / 100.0) * Math.Pow(1 - local / (double)segment, 1.7);
                w.Write((short)(Math.Sin(t * tones[i / segment] * Math.PI * 2) * envelope * volume * 32767));
            }
            w.Flush(); m.Position = 0; streams.Add(m);
            SoundPlayer p = new SoundPlayer(m); players[name] = p;
            try { p.Load(); } catch { }
        }
        public void Play(string name) { try { players[name].Play(); } catch { } }
        public void Dispose() { foreach (SoundPlayer p in players.Values) p.Dispose(); foreach (MemoryStream m in streams) m.Dispose(); }
    }
}
