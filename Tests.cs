using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace DesktopMiniGolf
{
    static class Tests
    {
        static StringBuilder output;
        static int passed, failed;
        static void Check(bool condition, string name)
        { output.AppendLine((condition ? "PASS " : "FAIL ") + name); if (condition) passed++; else failed++; }
        static Course Empty() { return new Course("test", "", "", 2, new V(120, 300), new V(980, 300)); }
        public static string Run()
        {
            output = new StringBuilder(); passed = failed = 0;
            try
            {
                Physics p = new Physics(Empty()); p.Step(1);
                Check(p.Ball.X == 120 && p.Strokes == 0, "Idle ball stays still");
                Check(!p.Shoot(new V(1, 1)) && p.Strokes == 0, "Tiny drags do not cost a stroke");
                p.Shoot(new V(100, 0));
                Check(!p.Shoot(new V(100, 0)) && p.Strokes == 1, "Cannot shoot a moving ball");
                p.Step(20);
                Check(!p.Moving && !double.IsNaN(p.Ball.X), "Full trajectory settles to rest");

                p = new Physics(Empty()); p.Ball = new V(45, 300); p.Velocity = new V(-780, 0); p.Step(.05);
                Check(p.Ball.X >= 40 && p.Velocity.X > 0, "Boundary reflects full-power shots");
                Course c = Empty(); c.Blocks.Add(new Block(500, 180, 8, 200, false, 0, "thin"));
                p = new Physics(c); p.Ball = new V(450, 300); p.Velocity = new V(780, 0); p.Step(.15);
                Check(p.Velocity.X < 0 && p.Ball.X < 490 && p.Bounces > 0, "High-speed shot cannot tunnel through thin wall");
                p = new Physics(c); p.Ball = new V(499, 185); p.Velocity = new V(80, 30); p.Step(.02);
                Check(!c.Blocks[0].Rect.Contains(p.Ball.Point) && p.Velocity.Length <= 86, "Embedded/corner collision resolves without adding energy");

                Physics plain = new Physics(Empty()); plain.Velocity = new V(500, 0);
                c = Empty(); c.Zones.Add(new Zone(30, 30, 1060, 540, false));
                Physics sand = new Physics(c); sand.Velocity = new V(500, 0);
                plain.Step(.3); sand.Step(.3);
                Check(sand.Velocity.Length < plain.Velocity.Length - 100 && sand.Ball.X < plain.Ball.X, "Sand materially slows ball and reduces distance");

                c = Empty(); c.Zones.Add(new Zone(180, 250, 100, 100, true));
                p = new Physics(c); p.Shoot(new V(100, 0)); p.Step(.5);
                Check(p.WaterCount == 1 && p.Strokes == 2 && !p.Moving && p.Ball.X == c.Start.X, "Water returns ball to last shot with exactly one penalty");

                c = Empty(); c.Hole = new V(500, 300);
                p = new Physics(c); p.Ball = new V(450, 300); p.Velocity = new V(150, 0); p.Step(.6);
                Check(p.Potted && !p.Moving && (p.Ball - c.Hole).Length == 0, "Slow putt is captured and centered in hole");
                p = new Physics(c); p.Ball = new V(460, 300); p.Velocity = new V(780, 0); p.Step(.1);
                Check(!p.Potted, "Overpowered shot can skip across hole");
                p = new Physics(c); p.Shoot(new V(10000, 0));
                Check(Math.Abs(p.Velocity.Length - Physics.MaxPull * Physics.Power) < .001, "Power clamps to safe maximum");
                p.Reset();
                Check(p.Strokes == 0 && !p.Potted && !p.Moving && p.Ball.X == c.Start.X, "Restart clears shot and game state");

                List<Course> courses = Course.Create();
                int par = 0;
                foreach (Course course in courses) { Check(Reachable(course), "Playable route exists: " + course.Name); par += course.Par; }
                Check(courses.Count == 6 && par == 20, "Six-hole scorecard has par 20");
                bool generated = true;
                for (int i = 0; i < 100; i++) if (!Reachable(Course.Desktop(i, 8))) generated = false;
                Check(generated, "100 generated desktop courses have a continuous dry route");
                // Verify a useful opening shot, rather than only local collision behavior.
                p = new Physics(courses[0]);
                V delta = p.Course.Hole - p.Ball;
                double speed = Math.Sqrt(2 * 165 * delta.Length);
                p.Shoot(delta.Unit * (speed / Physics.Power)); p.Step(7);
                Check(p.Potted && p.Strokes == 1, "Opening hole can be completed with a calibrated single putt");
                DesktopInventory();
            }
            catch (Exception e) { output.AppendLine(e.ToString()); failed++; }
            output.AppendLine("RESULT: " + passed + " passed; " + failed + " failed");
            Environment.ExitCode = failed == 0 ? 0 : 1;
            return output.ToString();
        }
        static bool Free(Course c, float x, float y)
        {
            if (x < 42 || x > 1078 || y < 42 || y > 558) return false;
            foreach (Block b in c.Blocks) { RectangleF r = b.Rect; r.Inflate(13, 13); if (r.Contains(x, y)) return false; }
            foreach (Zone z in c.Zones) if (z.Water) { RectangleF r = z.Rect; r.Inflate(13, 13); if (r.Contains(x, y)) return false; }
            return true;
        }
        static void DesktopInventory()
        {
            string fixture = Path.Combine(Path.GetTempPath(), "yyc-golf-test-" + Guid.NewGuid().ToString("N"));
            string user = Path.Combine(fixture, "user"), common = Path.Combine(fixture, "common");
            Directory.CreateDirectory(user); Directory.CreateDirectory(common);
            try
            {
                File.WriteAllText(Path.Combine(user, "Chrome.lnk"), "");
                File.WriteAllText(Path.Combine(common, "Chrome.lnk"), "");
                File.WriteAllText(Path.Combine(common, "Steam.lnk"), "");
                File.WriteAllText(Path.Combine(user, "weekly.docx"), "");
                File.WriteAllText(Path.Combine(user, "desktop.ini"), "");
                File.WriteAllText(Path.Combine(user, "secret.txt"), "");
                File.SetAttributes(Path.Combine(user, "secret.txt"), FileAttributes.Hidden);
                Directory.CreateDirectory(Path.Combine(user, "my.projects"));
                List<string> entries = DesktopIcons.FindEntries(user, common, Path.Combine(fixture, "missing"));
                Check(entries.Count == 4, "Desktop discovery merges roots, deduplicates shortcuts and excludes hidden metadata");
                Check(entries[0] == Path.Combine(user, "Chrome.lnk") && Path.GetFileName(entries[1]) == "Steam.lnk", "User desktop wins duplicates and app shortcuts are prioritized");
                Check(Path.GetFileName(entries[2]) == "my.projects", "Folders are prioritized before ordinary files");
                Check(DesktopIcons.DisplayName(Path.Combine(user, "Chrome.lnk")) == "Chrome" && DesktopIcons.DisplayName(Path.Combine(user, "weekly.docx")) == "weekly.docx" && DesktopIcons.DisplayName(Path.Combine(user, "my.projects")) == "my.projects", "Shortcut suffix is hidden while file and folder names are preserved");
                int realCount;
                List<DesktopAsset> icons = DesktopIcons.ReadFrom(out realCount, user, common);
                try { Check(realCount > 0 && icons.TrueForAll(delegate(DesktopAsset a) { return a.Image != null && a.Image.Width > 0; }), "Windows Shell loads icons from the selected local desktop roots"); }
                finally { foreach (DesktopAsset a in icons) a.Dispose(); }
                icons = DesktopIcons.ReadFrom(out realCount, Path.Combine(fixture, "missing"));
                try { Check(realCount == 0 && icons.Count > 0, "Empty or inaccessible desktop falls back to playable sample icons"); }
                finally { foreach (DesktopAsset a in icons) a.Dispose(); }
            }
            finally
            {
                string full = Path.GetFullPath(fixture);
                string tempRoot = Path.GetFullPath(Path.GetTempPath());
                if (full.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("yyc-golf-test-", StringComparison.Ordinal)) Directory.Delete(full, true);
            }
        }
        static bool Reachable(Course c)
        {
            const int cols = 57, rows = 31;
            bool[,] seen = new bool[cols, rows];
            Queue<Point> q = new Queue<Point>();
            int sx = (int)Math.Round(c.Start.X / 20), sy = (int)Math.Round(c.Start.Y / 20);
            q.Enqueue(new Point(sx, sy)); seen[sx, sy] = true;
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                Point p = q.Dequeue();
                if ((new V(p.X * 20, p.Y * 20) - c.Hole).Length < 28) return true;
                for (int i = 0; i < 4; i++)
                {
                    int x = p.X + dx[i], y = p.Y + dy[i];
                    if (x < 0 || y < 0 || x >= cols || y >= rows || seen[x, y] || !Free(c, x * 20, y * 20)) continue;
                    seen[x, y] = true; q.Enqueue(new Point(x, y));
                }
            }
            return false;
        }
    }
}
