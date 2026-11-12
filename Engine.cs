using System;
using System.Collections.Generic;
using System.Drawing;

namespace DesktopMiniGolf
{
    public struct V
    {
        public double X, Y;
        public V(double x, double y) { X = x; Y = y; }
        public double Length { get { return Math.Sqrt(X * X + Y * Y); } }
        public static V operator +(V a, V b) { return new V(a.X + b.X, a.Y + b.Y); }
        public static V operator -(V a, V b) { return new V(a.X - b.X, a.Y - b.Y); }
        public static V operator *(V a, double f) { return new V(a.X * f, a.Y * f); }
        public static double Dot(V a, V b) { return a.X * b.X + a.Y * b.Y; }
        public V Unit { get { double l = Length; return l < 0.0001 ? new V(1, 0) : this * (1 / l); } }
        public PointF Point { get { return new PointF((float)X, (float)Y); } }
    }

    public class Block
    {
        public RectangleF Rect;
        public bool Icon;
        public int Asset;
        public string Label;
        public Block(float x, float y, float w, float h, bool icon, int asset, string label)
        { Rect = new RectangleF(x, y, w, h); Icon = icon; Asset = asset; Label = label; }
    }

    public class Zone
    {
        public RectangleF Rect;
        public bool Water;
        public Zone(float x, float y, float w, float h, bool water)
        { Rect = new RectangleF(x, y, w, h); Water = water; }
    }

    public class Course
    {
        public const int Width = 1120, Height = 600;
        public string Name, Subtitle, Hint;
        public int Par;
        public V Start, Hole;
        public List<Block> Blocks = new List<Block>();
        public List<Zone> Zones = new List<Zone>();
        public Course(string name, string subtitle, string hint, int par, V start, V hole)
        { Name = name; Subtitle = subtitle; Hint = hint; Par = par; Start = start; Hole = hole; }
        void Wall(float x, float y, float w, float h, string label)
        { Blocks.Add(new Block(x, y, w, h, false, 0, label)); }
        void Icon(float x, float y, int asset)
        { Blocks.Add(new Block(x, y, 58, 64, true, asset, null)); }

        public static List<Course> Create()
        {
            List<Course> all = new List<Course>();
            Course c = new Course("提前下班", "FIRST THINGS FIRST", "方向对了也要控力：打轻停在洞前，打重越过球洞。", 2, new V(150, 355), new V(930, 300));
            c.Icon(410, 130, 0); c.Icon(590, 440, 1); c.Icon(760, 110, 2); all.Add(c);
            c = new Course("绕开周报", "A LITTLE DETOUR", "周报挡住了直线。走上边，或者借一次墙壁反弹。", 3, new V(140, 340), new V(960, 310));
            c.Wall(420, 205, 145, 275, "周报_最终版"); c.Icon(600, 125, 3); c.Icon(735, 420, 4); all.Add(c);
            c = new Course("借个反弹", "WORK SMARTER", "屏幕边缘也能当球杆。试试让球沿上边绕过去。", 3, new V(160, 440), new V(950, 420));
            c.Wall(450, 210, 150, 360, "待办事项"); c.Icon(700, 250, 1); c.Icon(280, 135, 2); all.Add(c);
            c = new Course("划过沙坑", "TAKE IT SLOW", "浅色沙坑会让球明显减速。绕过去，或加一点力度。", 3, new V(145, 345), new V(960, 300));
            c.Zones.Add(new Zone(360, 175, 290, 280, false)); c.Icon(755, 150, 0); c.Icon(760, 420, 4); all.Add(c);
            c = new Course("别掉进摸鱼池", "STAY ON DRY LAND", "蓝色区域是水。落水回到上一杆的位置，并加一杆。", 4, new V(135, 380), new V(970, 340));
            c.Zones.Add(new Zone(350, 210, 215, 360, true)); c.Zones.Add(new Zone(680, 30, 160, 220, true));
            c.Icon(520, 105, 1); c.Icon(720, 420, 3); all.Add(c);
            c = new Course("最后一道审批", "THE WAY OUT", "先从上面穿过，再从下面绕行。下班就在前面。", 5, new V(120, 430), new V(1000, 200));
            c.Wall(330, 200, 90, 370, "等待审批"); c.Wall(700, 30, 90, 360, "再改一版"); c.Icon(520, 300, 2); all.Add(c);
            return all;
        }

        public static Course Desktop(int seed, int assets)
        {
            Course c = new Course("我的桌面球场", "YOUR DESKTOP, YOUR COURSE", "瞄准后也要控力，打重会越过球洞。右上角可刷新图标。", 4, new V(120, 410), new V(1000, 200));
            Random r = new Random(seed);
            for (int i = 0; i < 11; i++)
            {
                double x = 240 + r.Next(620), y = 80 + r.Next(390);
                bool overlap = false;
                foreach (Block b in c.Blocks)
                    if (Math.Abs(b.Rect.X - x) < 95 && Math.Abs(b.Rect.Y - y) < 95) overlap = true;
                if (!overlap) c.Icon((float)x, (float)y, i % Math.Max(1, assets));
            }
            return c;
        }
    }

    public class Physics
    {
        public const double Radius = 10, MaxPull = 150, Power = 5.2;
        // The hole accepts a slow roll across its opening. A fast ball rolls over it.
        // These are world-space units, independent of window size and aim assistance.
        public const double HoleCaptureRadius = 10, HoleMaxCaptureSpeed = 100;
        public Course Course;
        public V Ball, Velocity, LastShot;
        public int Strokes, WaterCount, Bounces;
        public bool Potted;
        public bool Moving { get { return Velocity.Length > 0.1; } }
        public Physics(Course course) { Course = course; Reset(); }
        public void Reset() { Ball = Course.Start; LastShot = Ball; Velocity = new V(); Strokes = 0; Potted = false; WaterCount = 0; Bounces = 0; }
        public bool Shoot(V pull)
        {
            if (Moving || Potted || pull.Length < 4) return false;
            LastShot = Ball;
            Velocity = pull.Unit * (Math.Min(MaxPull, pull.Length) * Power);
            Strokes++;
            return true;
        }
        public void Step(double dt)
        {
            if (!Moving || Potted) return;
            // Substeps keep even full-power shots from crossing a thin obstacle.
            int n = Math.Max(1, (int)Math.Ceiling(dt / (1.0 / 240)));
            double h = dt / n;
            for (int i = 0; i < n && Moving && !Potted; i++) StepSmall(h);
        }
        void StepSmall(double dt)
        {
            Ball += Velocity * dt;
            double min = 30 + Radius, maxX = Course.Width - 30 - Radius, maxY = Course.Height - 30 - Radius;
            if (Ball.X < min) { Ball.X = min; Velocity.X = Math.Abs(Velocity.X) * .8; Bounces++; }
            if (Ball.X > maxX) { Ball.X = maxX; Velocity.X = -Math.Abs(Velocity.X) * .8; Bounces++; }
            if (Ball.Y < min) { Ball.Y = min; Velocity.Y = Math.Abs(Velocity.Y) * .8; Bounces++; }
            if (Ball.Y > maxY) { Ball.Y = maxY; Velocity.Y = -Math.Abs(Velocity.Y) * .8; Bounces++; }
            foreach (Block b in Course.Blocks) Resolve(b.Rect);
            double friction = 165;
            foreach (Zone z in Course.Zones)
            {
                if (!z.Rect.Contains(Ball.Point)) continue;
                if (z.Water)
                {
                    Ball = LastShot; Velocity = new V(); Strokes++; WaterCount++; return;
                }
                friction = 650;
            }
            if ((Course.Hole - Ball).Length <= HoleCaptureRadius && Velocity.Length <= HoleMaxCaptureSpeed)
            { Ball = Course.Hole; Velocity = new V(); Potted = true; return; }
            double speed = Velocity.Length;
            double next = Math.Max(0, speed - friction * dt);
            Velocity = next < 7 ? new V() : Velocity.Unit * next;
        }
        void Resolve(RectangleF b)
        {
            double cx = Math.Max(b.Left, Math.Min(b.Right, Ball.X));
            double cy = Math.Max(b.Top, Math.Min(b.Bottom, Ball.Y));
            V d = Ball - new V(cx, cy);
            double length = d.Length;
            if (length >= Radius) return;
            V normal;
            double penetration;
            if (length < .00001)
            {
                double l = Math.Abs(Ball.X - b.Left), r = Math.Abs(b.Right - Ball.X);
                double t = Math.Abs(Ball.Y - b.Top), bot = Math.Abs(b.Bottom - Ball.Y);
                double m = Math.Min(Math.Min(l, r), Math.Min(t, bot));
                if (m == l) normal = new V(-1, 0);
                else if (m == r) normal = new V(1, 0);
                else if (m == t) normal = new V(0, -1);
                else normal = new V(0, 1);
                penetration = Radius + m;
            }
            else { normal = d * (1 / length); penetration = Radius - length; }
            Ball += normal * (penetration + .05);
            double inward = V.Dot(Velocity, normal);
            if (inward < 0) { Velocity -= normal * (1.78 * inward); Bounces++; }
        }
    }
}
