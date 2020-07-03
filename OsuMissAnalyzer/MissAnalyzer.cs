﻿using System;
using System.Drawing;
using System.Windows.Forms;
using osuDodgyMomentsFinder;
using ReplayAPI;
using BMAPI.v1;
using BMAPI;
using System.IO;
using BMAPI.v1.HitObjects;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Diagnostics;
using System.Threading;

namespace OsuMissAnalyzer
{
    public class MissAnalyzer : Form
    {
        private const int arrowLength = 4;
        private const int sliderGranularity = 10;
        private const int maxTime = 1000;
        private float scale = 1;
        private readonly Options options;
        private Bitmap img;
        private Graphics graphics;
        // private Graphics graphicsOut;
        private ReplayAnalyzer replayAnalyzer;
        private Replay r;
        private Beatmap beatmap;
        private int number = 0;
        private Rectangle area;
        private bool ring;
        private bool all;
        private TableLayoutPanel tableLayoutPanel1;
        private PictureBox mainCanvas;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem menuKit;
        private ToolStripMenuItem newReplayEntry;
        private OsuDatabase database;

        
        

        public MissAnalyzer(string replayFile, string beatmap)
        {
            options = new Options("options.cfg");
            if(options.Settings.ContainsKey("osudir"))
            {
                database = new OsuDatabase(options, "osu!.db");
            }
            Text = "Miss Analyzer";

            FormBorderStyle = FormBorderStyle.FixedSingle;
            Debug.Print("Loading Replay file...");
            if (replayFile == null)
            {
                LoadReplay();
                if (r == null) Environment.Exit(1);
            }
            else
            {
                r = new Replay(replayFile, true, false);
            }
            Debug.Print("Loaded replay {0}", r.Filename);
            Debug.Print("Amount of 300s: {0}", r.Count300);
            Debug.Print("Amount of 100s: {0}", r.Count100);
            Debug.Print("Amount of 50s: {0}", r.Count50);
            Debug.Print("Amount of misses: {0}", r.CountMiss);
            Debug.Print("Loading Beatmap file...");
            if (beatmap == null)
            {
                LoadBeatmap();
                if (this.beatmap == null) Environment.Exit(1);
            }
            else
            {
                this.beatmap = new Beatmap(beatmap);
            }
            Debug.Print("Loaded beatmap {0}", this.beatmap.Filename);
            Debug.Print("Analyzing... ");
            Debug.Print("Amount of replay frames: " + r.ReplayFrames.Count.ToString());
            replayAnalyzer = new ReplayAnalyzer(this.beatmap, r);
            Debug.Print(replayAnalyzer.MainInfo().ToString());
            if (replayAnalyzer.Misses.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Debug.Print("There is no miss in this replay.");
                Console.ReadLine();
                Environment.Exit(1);
            }
        }

        private void LoadReplay()
        {
            if(options.Settings.ContainsKey("osudir"))
            {
                if (MessageBox.Show("Analyze latest replay?", "Miss Analyzer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    r = new Replay(
                            new DirectoryInfo(
                            Path.Combine(options.Settings["osudir"], "Data", "r"))
                                .GetFiles().Where(f => f.Name.EndsWith("osr"))
                                .OrderByDescending(f => f.LastWriteTime)
                                .First().FullName,
                                true, false);
                }
            }
            if(r == null)
            {
                using (OpenFileDialog fileDialog = new OpenFileDialog())
                {
                    fileDialog.Title = "Choose replay file";
                    fileDialog.Filter = "osu! replay files (*.osr)|*.osr";
                    DialogResult d = fileDialog.ShowDialog();
                    if (d == DialogResult.OK)
                    {
                        r = new Replay(fileDialog.FileName, true, false);
                    }
                }
            }
        }

        private void LoadBeatmap()
        {
            if (database != null)
            {
                beatmap = database.GetBeatmap(r.MapHash);
            }
            else
            {
                beatmap = GetBeatmapFromHash(Directory.GetCurrentDirectory(), false);
                if (beatmap == null)
                {
                    if (options.Settings.ContainsKey("songsdir"))
                    {
                        beatmap = GetBeatmapFromHash(options.Settings["songsdir"]);
                    }
                    else if (options.Settings.ContainsKey("osudir")
                      && File.Exists(Path.Combine(options.Settings["osudir"], "Songs"))
                      )
                    {
                        beatmap = GetBeatmapFromHash(Path.Combine(options.Settings["osudir"], "Songs"));
                    }
                    else
                    {
                        using (OpenFileDialog fd = new OpenFileDialog())
                        {
                            fd.Title = "Choose beatmap";
                            fd.Filter = "osu! beatmaps (*.osu)|*.osu";
                            DialogResult d = fd.ShowDialog();
                            if (d == DialogResult.OK)
                            {
                                beatmap = new Beatmap(fd.FileName);
                            }
                        }
                    }
                }
            }
        }

        private Beatmap GetBeatmapFromHash(string dir, bool songsDir = true)
        {
            Debug.Print("\nChecking API Key...");
            JArray j = JArray.Parse("[]");
            if (options.Settings.ContainsKey("apikey"))
            {
                Debug.Print("Found API key, searching for beatmap...");

                using (WebClient w = new WebClient())
                {
                    j = JArray.Parse(w.DownloadString("https://osu.ppy.sh/api/get_beatmaps" +
                                                            "?k=" + options.Settings["apikey"] +
                                                            "&h=" + r.MapHash));
                }
            }
            else
            {
                Debug.Print("No API key found, searching manually. It could take a while...");
                Thread t = new Thread(() =>
                               MessageBox.Show("No API key found, searching manually. It could take a while..."));
            }
            if (songsDir)
            {
                string[] folders;

                if (j.Count > 0) folders = Directory.GetDirectories(dir, j[0]["beatmapset_id"] + "*");
                else folders = Directory.GetDirectories(dir);

                foreach (string folder in folders)
                {
                    Beatmap map = ReadFolder(folder, j.Count > 0 ? (string)j[0]["beatmap_id"] : null);
                    if (map != null) return map;
                }
            }
            else
            {
                Beatmap map = ReadFolder(dir, j.Count > 0 ? (string)j[0]["beatmap_id"] : null);
                if (map != null) return map;
            }
            return null;
        }

        private Beatmap ReadFolder(string folder, string id)
        {
            foreach (string file in Directory.GetFiles(folder, "*.osu"))
            {
                using (StreamReader f = new StreamReader(file))
                {
                    string line = f.ReadLine();
                    if (line == null)
                        continue;
                    while (!f.EndOfStream
                           && !line.StartsWith("BeatmapID"))
                    {
                        line = f.ReadLine();
                    }
                    if (line.StartsWith("BeatmapID") && id != null)
                    {
                        if (line.Substring(10) == id)
                        {
                            return new Beatmap(file);
                        }
                    }
                    else
                    {
                        if (r.MapHash == Beatmap.MD5FromFile(file))
                        {
                            return new Beatmap(file);
                        }
                    }
                }
            }
            return null;
        }

        private void ScaleChange(int i)
        {
            scale += 0.1f * i;
            if (scale < 0.1) scale = 0.1f;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
            ScaleChange(Math.Sign(e.Delta));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            Invalidate();
            switch (e.KeyCode)
            {
                case System.Windows.Forms.Keys.Up:
                    ScaleChange(1);
                    break;
                case System.Windows.Forms.Keys.Down:
                    ScaleChange(-1);
                    break;
                case System.Windows.Forms.Keys.Right:
                    if (number == replayAnalyzer.Misses.Count - 1) break;
                    number++;
                    break;
                case System.Windows.Forms.Keys.Left:
                    if (number == 0) break;
                    number--;
                    break;
                case System.Windows.Forms.Keys.T:
                    ring = !ring;
                    break;
                case System.Windows.Forms.Keys.P:
                    for (int i = 0; i < replayAnalyzer.Misses.Count; i++)
                    {
                        if (all) DrawMiss(beatmap.HitObjects.IndexOf(replayAnalyzer.Misses[i]));
                        else DrawMiss(i);
                        img.Save(r.Filename.Substring(r.Filename.LastIndexOf("\\") + 1,
                                 r.Filename.Length - 5 - r.Filename.LastIndexOf("\\"))
                                 + "." + i + ".png",
                            System.Drawing.Imaging.ImageFormat.Png);
                    }
                    break;
                case System.Windows.Forms.Keys.R:
                    LoadReplay();
                    LoadBeatmap();
                    replayAnalyzer = new ReplayAnalyzer(beatmap, r);
                    Invalidate();
                    number = 0;
                    if (r == null || beatmap == null)
                    {
                        Environment.Exit(1);
                    }
                    break;
                case System.Windows.Forms.Keys.A:
                    if (all)
                    {
                        all = false;
                        number = replayAnalyzer.Misses.Count(x => x.StartTime < beatmap.HitObjects[number].StartTime);
                    }
                    else
                    {
                        all = true;
                        number = beatmap.HitObjects.IndexOf(replayAnalyzer.Misses[number]);
                    }
                    break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // graphicsOut.DrawImage(DrawMiss(number), area);
            mainCanvas.Image = DrawMiss(number);
        }

        /// <summary>
        /// Draws the miss.
        /// </summary>
        /// <returns>A Bitmap containing the drawing</returns>
        /// <param name="num">Index of the miss as it shows up in r.misses.</param>
        private Bitmap DrawMiss(int num)
        {
            bool hardrock = r.Mods.HasFlag(Mods.HardRock);
            CircleObject miss;
            if (all) miss = beatmap.HitObjects[num];
            else miss = replayAnalyzer.Misses[num];
            float radius = (float)miss.Radius;
            Pen circle = new Pen(Color.Gray, radius * 2 / scale)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round
            };
            Pen p = new Pen(Color.White);
            graphics.FillRectangle(p.Brush, area);
            RectangleF bounds = new RectangleF(PointF.Subtract(miss.Location.ToPointF(), Scale(area.Size, scale / 2)),
                Scale(area.Size, scale));

            int i, j, y, z;
            for (y = beatmap.HitObjects.Count(x => x.StartTime <= miss.StartTime) - 1;
                y >= 0 && bounds.Contains(beatmap.HitObjects[y].Location.ToPointF())
                && miss.StartTime - beatmap.HitObjects[y].StartTime < maxTime;
                y--)
            {
            }
            for (z = beatmap.HitObjects.Count(x => x.StartTime <= miss.StartTime) - 1;
                z < beatmap.HitObjects.Count && bounds.Contains(beatmap.HitObjects[z].Location.ToPointF())
                && beatmap.HitObjects[z].StartTime - miss.StartTime < maxTime;
                z++)
            {
            }
            for (i = r.ReplayFrames.Count(x => x.Time <= beatmap.HitObjects[y + 1].StartTime);
                i > 0 && bounds.Contains(r.ReplayFrames[i].PointF)
                && miss.StartTime - r.ReplayFrames[i].Time < maxTime;
                i--)
            {
            }
            for (j = r.ReplayFrames.Count(x => x.Time <= beatmap.HitObjects[z - 1].StartTime);
                j < r.ReplayFrames.Count - 1 && bounds.Contains(r.ReplayFrames[j].PointF)
                && r.ReplayFrames[j].Time - miss.StartTime < maxTime;
                j++)
            {
            }
            p.Color = Color.Gray;
            for (int q = z - 1; q > y; q--)
            {
                int c = Math.Min(255, 100 + (int)(Math.Abs(beatmap.HitObjects[q].StartTime - miss.StartTime) * 100 / maxTime));
                if (beatmap.HitObjects[q].Type == HitObjectType.Slider)
                {
                    SliderObject slider = (SliderObject)beatmap.HitObjects[q];
                    PointF[] pt = new PointF[sliderGranularity];
                    for (int x = 0; x < sliderGranularity; x++)
                    {
                        pt[x] = ScaleToRect(
                            PSub(slider.PositionAtDistance(x * 1f * slider.PixelLength / sliderGranularity).ToPoint(),
                                bounds, hardrock), bounds);
                    }
                    circle.Color = Color.LemonChiffon;
                    graphics.DrawLines(circle, pt);
                }

                p.Color = Color.FromArgb(c == 100 ? c + 50 : c, c, c);
                if (ring)
                {
                    graphics.DrawEllipse(p, ScaleToRect(new RectangleF(PointF.Subtract(
                        PSub(beatmap.HitObjects[q].Location.ToPointF(), bounds, hardrock),
                        new SizeF(radius, radius).ToSize()), new SizeF(radius * 2, radius * 2)), bounds));
                }
                else
                {
                    graphics.FillEllipse(p.Brush, ScaleToRect(new RectangleF(PointF.Subtract(
                        PSub(beatmap.HitObjects[q].Location.ToPointF(), bounds, hardrock),
                        new SizeF(radius, radius).ToSize()), new SizeF(radius * 2, radius * 2)), bounds));
                }
            }
            float distance = 10.0001f;
            for (int k = i; k < j; k++)
            {
                PointF p1 = PSub(r.ReplayFrames[k].PointF, bounds, hardrock);
                PointF p2 = PSub(r.ReplayFrames[k + 1].PointF, bounds, hardrock);
                p.Color = GetHitColor(beatmap.OverallDifficulty, (int)(miss.StartTime - r.ReplayFrames[k].Time));
                graphics.DrawLine(p, ScaleToRect(p1, bounds), ScaleToRect(p2, bounds));
                if (distance > 10 && Math.Abs(miss.StartTime - r.ReplayFrames[k + 1].Time) > 50)
                {
                    Point2 v1 = new Point2(p1.X - p2.X, p1.Y - p2.Y);
                    if (v1.Length > 0)
                    {
                        v1.Normalize();
                        v1 *= (float)(Math.Sqrt(2) * arrowLength / 2);
                        PointF p3 = PointF.Add(p2, new SizeF(v1.X + v1.Y, v1.Y - v1.X));
                        PointF p4 = PointF.Add(p2, new SizeF(v1.X - v1.Y, v1.X + v1.Y));
                        p2 = ScaleToRect(p2, bounds);
                        p3 = ScaleToRect(p3, bounds);
                        p4 = ScaleToRect(p4, bounds);
                        graphics.DrawLine(p, p2, p3);
                        graphics.DrawLine(p, p2, p4);
                    }
                    distance = 0;
                }
                else
                {
                    distance += new Point2(p1.X - p2.X, p1.Y - p2.Y).Length;
                }
                if (replayAnalyzer.GetKey(k == 0 ? ReplayAPI.Keys.None : r.ReplayFrames[k - 1].Keys, r.ReplayFrames[k].Keys) > 0)
                {
                    graphics.DrawEllipse(p, ScaleToRect(new RectangleF(PointF.Subtract(p1, new Size(3, 3)), new Size(6, 6)),
                        bounds));
                }
            }

            p.Color = Color.Black;
            Font f = new Font(FontFamily.GenericSansSerif, 12);
            graphics.DrawString(beatmap.ToString(), f, p.Brush, 0, 0);
            if (all) graphics.DrawString("Object " + (num + 1) + " of " + beatmap.HitObjects.Count, f, p.Brush, 0, f.Height);
            else graphics.DrawString("Miss " + (num + 1) + " of " + replayAnalyzer.Misses.Count, f, p.Brush, 0, f.Height);
            TimeSpan ts = TimeSpan.FromMilliseconds(miss.StartTime);
            graphics.DrawString("Time: " + ts.ToString(@"mm\:ss\.fff"), f, p.Brush, 0, area.Height - f.Height);
            return img;
        }

        /// <summary>
        /// Gets the hit window.
        /// </summary>
        /// <returns>The hit window in ms.</returns>
        /// <param name="od">OD of the map.</param>
        /// <param name="hit">Hit value (300, 100, or 50).</param>
        private static float GetHitWindow(float od, int hit)
        {
            switch (hit)
            {
                case 300:
                    return 79.5f - 6 * od;
                case 100:
                    return 139.5f - 8 * od;
                case 50:
                    return 199.5f - 10 * od;
                default:
                    throw new ArgumentOutOfRangeException(nameof(hit), hit, "Hit value is not 300, 100, or 50");
            }
        }

        /// <summary>
        /// Gets the color associated with the hit window.
        /// Blue for 300s, green for 100s, purple for 50s.
        /// </summary>
        /// <returns>The hit color.</returns>
        /// <param name="od">OD of the map.</param>
        /// <param name="ms">Hit timing in ms (can be negative).</param>
        private static Color GetHitColor(float od, int ms)
        {
            ms = Math.Abs(ms);
            if (ms < GetHitWindow(od, 300)) return Color.SkyBlue;
            if (ms < GetHitWindow(od, 100)) return Color.SpringGreen;
            if (ms < GetHitWindow(od, 50)) return Color.Purple;
            return Color.Black;
        }

        /// <summary>
        /// Flips point about the center of the screen if the Hard Rock mod is on, does nothing otherwise.
        /// </summary>
        /// <returns>A possibly-flipped pooint.</returns>
        /// <param name="p">The point to be flipped.</param>
        /// <param name="s">The height of the rectangle it's being flipped in</param>
        /// <param name="hr">Whether or not Hard Rock is on.</param>
        private PointF Flip(PointF p, float s, bool hr)
        {
            if (!hr) return p;
            p.Y = s - p.Y;
            return p;
        }

        /// <summary>
        /// Changes origin to top right of rect and flips p if hr is <c>true</c>.
        /// </summary>
        /// <returns>A point relative to rect</returns>
        /// <param name="p1">The point.</param>
        /// <param name="rect">The bounding rectangle to subtract from</param>
        /// <param name="hr">Whether or not Hard Rock is on.</param>
        private PointF PSub(PointF p1, RectangleF rect, bool hr)
        {
            PointF p = PointF.Subtract(p1, new SizeF(rect.Location));
            return Flip(p, rect.Width, hr);
        }

        /// <summary>
        /// Scales point p by scale factor s.
        /// </summary>
        /// <returns>The scaled point.</returns>
        /// <param name="p">Point to be scaled.</param>
        /// <param name="s">Scale.</param>
        private PointF Scale(PointF p, float s)
        {
            return new PointF(p.X * s, p.Y * s);
        }
        private PointF Scale(PointF point, SizeF size)
        {
            return new PointF(point.X * size.Width, point.Y * size.Height);
        }
        private SizeF Scale(SizeF point, SizeF size)
        {
            return new SizeF(point.Width * size.Width, point.Height * size.Height);
        }
        private SizeF Div(SizeF one, SizeF two)
        {
            return new SizeF(one.Width / two.Width, one.Height / two.Height);
        }

        private SizeF Scale(SizeF p, float s)
        {
            return new SizeF(p.Width * s, p.Height * s);
        }

        private RectangleF Scale(RectangleF rect, float s)
        {
            return new RectangleF(Scale(rect.Location, s), Scale(rect.Size, s));
        }

        private PointF ScaleToRect(PointF p, RectangleF rect)
        {
            return ScaleToRect(p, rect, area.Size);
        }
        private PointF ScaleToRect(PointF p, RectangleF rect, SizeF sz)
        {
            PointF ret = Scale(p, Div(sz, rect.Size));
            return ret;
        }

        private RectangleF ScaleToRect(RectangleF p, RectangleF rect)
        {
            return ScaleToRect(p, rect, area.Size);
        }

        private RectangleF ScaleToRect(RectangleF p, RectangleF rect, SizeF sz)
        {
            return new RectangleF(PointF.Subtract(ScaleToRect(PointF.Add(p.Location, Scale(p.Size, 0.5f)), rect),
                Scale(p.Size, Scale(Div(sz, rect.Size), 0.5f))),
                Scale(p.Size, Div(sz, rect.Size)));
        }

        public void InitializeComponent()
        {
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.mainCanvas = new System.Windows.Forms.PictureBox();
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.menuKit = new System.Windows.Forms.ToolStripMenuItem();
            this.newReplayEntry = new System.Windows.Forms.ToolStripMenuItem();
            this.tableLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.mainCanvas)).BeginInit();
            this.menuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.Controls.Add(this.menuStrip1, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.mainCanvas, 0, 1);
            this.tableLayoutPanel1.Location = new System.Drawing.Point(1, 2);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 2;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 4.460967F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 95.53903F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 61F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(381, 559);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // mainCanvas
            // 
            this.tableLayoutPanel1.SetColumnSpan(this.mainCanvas, 2);
            this.mainCanvas.Location = new System.Drawing.Point(3, 27);
            this.mainCanvas.Name = "mainCanvas";
            this.mainCanvas.Size = new System.Drawing.Size(375, 529);
            this.mainCanvas.TabIndex = 0;
            this.mainCanvas.TabStop = false;
            this.mainCanvas.Click += new System.EventHandler(this.MainCanvas_Click);
            // 
            // menuStrip1
            // 
            this.tableLayoutPanel1.SetColumnSpan(this.menuStrip1, 2);
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuKit});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(381, 24);
            this.menuStrip1.TabIndex = 1;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // menuKit
            // 
            this.menuKit.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.newReplayEntry});
            this.menuKit.Name = "menuKit";
            this.menuKit.Size = new System.Drawing.Size(61, 20);
            this.menuKit.Text = "Options";
            // 
            // newReplayEntry
            // 
            this.newReplayEntry.Name = "newReplayEntry";
            this.newReplayEntry.Size = new System.Drawing.Size(180, 22);
            this.newReplayEntry.Text = "New replay...";
            // 
            // MissAnalyzer
            // 
            this.ClientSize = new System.Drawing.Size(384, 561);
            this.Controls.Add(this.tableLayoutPanel1);
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "MissAnalyzer";
            this.Load += new System.EventHandler(this.MissAnalyzer_Load);
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.mainCanvas)).EndInit();
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.ResumeLayout(false);

        }

        private void MissAnalyzer_Load(object sender, EventArgs e)
        {
            area = mainCanvas.ClientRectangle;
            img = new Bitmap(area.Width, area.Height);
            graphics = Graphics.FromImage(img);
            mainCanvas.Image = img;
            
        }

        private void MainCanvas_Click(object sender, EventArgs e)
        {

        }
    }
}