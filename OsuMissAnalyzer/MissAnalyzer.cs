using System;
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
        public readonly Options options;
        public Replay replay;
        public Beatmap beatmap;

        private const int arrowLength = 4;
        private const int sliderGranularity = 10;
        private const int maxTime = 1000;
        private float scale = 1;
        private Bitmap img;
        private Graphics graphics;
        private ReplayAnalyzer replayAnalyzer;
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
            if(!options.Settings.ContainsKey("osudir"))
            {
                CreateConfig();
                replay = new Replay(options.Settings["osudir"], true);
                if (replay == null) throw new NullReferenceException();
            }
            database = new OsuDatabase(options, "osu!.db");
            Text = "Miss Analyzer";

            FormBorderStyle = FormBorderStyle.FixedSingle;
            Debug.Print("Loading Replay file...");
            if (replayFile == null)
            {
                LoadReplay();
                if (replay == null) Environment.Exit(1);
            }
            else
            {
                replay = new Replay(replayFile, true);
            }
            Debug.Print("Loaded replay {0}", replay.Filename);
            Debug.Print("Amount of 300s: {0}", replay.Count300);
            Debug.Print("Amount of 100s: {0}", replay.Count100);
            Debug.Print("Amount of 50s: {0}", replay.Count50);
            Debug.Print("Amount of misses: {0}", replay.CountMiss);
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
            Debug.Print("Amount of replay frames: " + replay.ReplayFrames.Count.ToString());

            replayAnalyzer = new ReplayAnalyzer(this.beatmap, replay);

            if (replayAnalyzer.Misses.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Debug.Print("There is no miss in this replay.");
                Console.ReadLine();
                Environment.Exit(1);
            }
        }
        private void CreateConfig()
        {
            bool isPathChosen = false;
            while (!isPathChosen)
            {
                using (FolderBrowserDialog fileDialog = new FolderBrowserDialog())
                {
                    fileDialog.Description = "Choose the main osu! directory";
                    DialogResult dialogResult = fileDialog.ShowDialog();
                    if (dialogResult == DialogResult.OK)
                    {
                        string osuDir = fileDialog.SelectedPath;
                        string songsDir = osuDir + "\\Songs";
                        options.AddEntry("osudir", osuDir);
                        options.AddEntry("songsdir", songsDir);
                        isPathChosen = true;
                    }
                }
            }
        }
        private void LoadNewReplay()
        {
            database.Close();
            database = new OsuDatabase(options, "osu!.db");

            LoadReplay();
            LoadBeatmap();

            replayAnalyzer = new ReplayAnalyzer(this.beatmap, replay);
            number = 0;

            Invalidate();
        }
        private void LoadReplay()
        {
            if(options.Settings.ContainsKey("osudir"))
            {
                bool dialogResult = false;
                if (replay == null)
                {
                    dialogResult =
                    MessageBox.Show("Analyze latest replay?", "Miss Analyzer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                }
                if (dialogResult)
                {
                    replay = new Replay(
                            new DirectoryInfo(
                            Path.Combine(options.Settings["osudir"], "Data", "r"))
                                .GetFiles().Where(f => f.Name.EndsWith("osr"))
                                .OrderByDescending(f => f.LastWriteTime)
                                .First().FullName,
                                true);
                }
                else
                {
                    using (OpenFileDialog fileDialog = new OpenFileDialog())
                    {
                        DialogResult d;
                        fileDialog.Title = "Choose replay file";
                        fileDialog.Filter = "osu! replay files (*.osr)|*.osr";
                        do
                        {
                            d = fileDialog.ShowDialog();
                            if (d == DialogResult.OK)
                            {
                                replay = new Replay(fileDialog.FileName, true);
                            }
                        } while (d != DialogResult.OK);
                            if (replay == null) throw new NullReferenceException();
                    }
                }
            }
        }

        private void LoadBeatmap()
        {
            if (database != null)
            {
                beatmap = database.GetBeatmap(replay.MapHash);
            }
            else
            {
                beatmap = Program.GetBeatmapFromHash(Directory.GetCurrentDirectory(), this,false);
                if (beatmap == null)
                {
                    if (options.Settings.ContainsKey("songsdir"))
                    {
                        beatmap = Program.GetBeatmapFromHash(options.Settings["songsdir"], this);
                    }
                    else if (options.Settings.ContainsKey("osudir")
                      && File.Exists(Path.Combine(options.Settings["osudir"], "Songs"))
                      )
                    {
                        beatmap = Program.GetBeatmapFromHash(Path.Combine(options.Settings["osudir"], "Songs"), this);
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

        public void ScaleChange(int i)
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
        protected void SaveMissImages()
        {
            for (int i = 0; i < replayAnalyzer.Misses.Count; i++)
            {
                if (all) DrawMiss(beatmap.HitObjects.IndexOf(replayAnalyzer.Misses[i]));
                else DrawMiss(i);
                string path = replay.Filename.Substring(replay.Filename.LastIndexOf("\\") + 1,
                         replay.Filename.Length - 5 - replay.Filename.LastIndexOf("\\"))
                         + "." + i + ".png";
                img.Save(path,
                    System.Drawing.Imaging.ImageFormat.Png);
                Debug.Print("Saved {0} miss to {1}", i, path);
            }
            MessageBox.Show("The images have been saved to the drive.", "Miss Analyzer", MessageBoxButtons.OK, MessageBoxIcon.Question);
        }

        

        /// <summary>
        /// Draws the miss.
        /// </summary>
        /// <returns>A Bitmap containing the drawing</returns>
        /// <param name="num">Index of the miss as it shows up in r.misses.</param>
        private Bitmap DrawMiss(int num)
        {
            bool hardrock = replay.Mods.HasFlag(Mods.HardRock);
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
            Pen pen = new Pen(Color.White);
            graphics.FillRectangle(pen.Brush, area);
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
            for (i = replay.ReplayFrames.Count(x => x.Time <= beatmap.HitObjects[y + 1].StartTime);
                i > 0 && bounds.Contains(replay.ReplayFrames[i].PointF)
                && miss.StartTime - replay.ReplayFrames[i].Time < maxTime;
                i--)
            {
            }
            for (j = replay.ReplayFrames.Count(x => x.Time <= beatmap.HitObjects[z - 1].StartTime);
                j < replay.ReplayFrames.Count - 1 && bounds.Contains(replay.ReplayFrames[j].PointF)
                && replay.ReplayFrames[j].Time - miss.StartTime < maxTime;
                j++)
            {
            }
            pen.Color = Color.Gray;
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

                pen.Color = Color.FromArgb(c == 100 ? c + 50 : c, c, c);
                if (ring)
                {
                    graphics.DrawEllipse(pen, ScaleToRect(new RectangleF(PointF.Subtract(
                        PSub(beatmap.HitObjects[q].Location.ToPointF(), bounds, hardrock),
                        new SizeF(radius, radius).ToSize()), new SizeF(radius * 2, radius * 2)), bounds));
                }
                else
                {
                    graphics.FillEllipse(pen.Brush, ScaleToRect(new RectangleF(PointF.Subtract(
                        PSub(beatmap.HitObjects[q].Location.ToPointF(), bounds, hardrock),
                        new SizeF(radius, radius).ToSize()), new SizeF(radius * 2, radius * 2)), bounds));
                }
            }
            float distance = 10.0001f;
            for (int k = i; k < j; k++)
            {
                PointF coords1 = PSub(replay.ReplayFrames[k].PointF, bounds, hardrock);
                PointF coords2 = PSub(replay.ReplayFrames[k + 1].PointF, bounds, hardrock);
                pen.Color = GetHitColor(beatmap.OverallDifficulty, (int)(miss.StartTime - replay.ReplayFrames[k].Time));
                graphics.DrawLine(pen, ScaleToRect(coords1, bounds), ScaleToRect(coords2, bounds));
                if (distance > 10 && Math.Abs(miss.StartTime - replay.ReplayFrames[k + 1].Time) > 50)
                {
                    Point2 v1 = new Point2(coords1.X - coords2.X, coords1.Y - coords2.Y);
                    if (v1.Length > 0)
                    {
                        v1.Normalize();
                        v1 *= (float)(Math.Sqrt(2) * arrowLength / 2);
                        PointF coords3 = PointF.Add(coords2, new SizeF(v1.X + v1.Y, v1.Y - v1.X));
                        PointF coords4 = PointF.Add(coords2, new SizeF(v1.X - v1.Y, v1.X + v1.Y));
                        coords2 = ScaleToRect(coords2, bounds);
                        coords3 = ScaleToRect(coords3, bounds);
                        coords4 = ScaleToRect(coords4, bounds);
                        graphics.DrawLine(pen, coords2, coords3);
                        graphics.DrawLine(pen, coords2, coords4);
                    }
                    distance = 0;
                }
                else
                {
                    distance += new Point2(coords1.X - coords2.X, coords1.Y - coords2.Y).Length;
                }
                if (replayAnalyzer.GetKey(k == 0 ? ReplayAPI.Keys.None : replay.ReplayFrames[k - 1].Keys, replay.ReplayFrames[k].Keys) > 0)
                {
                    graphics.DrawEllipse(pen, ScaleToRect(new RectangleF(PointF.Subtract(coords1, new Size(3, 3)), new Size(6, 6)),
                        bounds));
                }
            }

            pen.Color = Color.Black;
            Font font = new Font(FontFamily.GenericSansSerif, 12);
            graphics.DrawString(beatmap.ToString(), font, pen.Brush, 0, 0);
            if (all) graphics.DrawString("Object " + (num + 1) + " of " + beatmap.HitObjects.Count, font, pen.Brush, 0, font.Height);
            else graphics.DrawString("Miss " + (num + 1) + " of " + replayAnalyzer.Misses.Count, font, pen.Brush, 0, font.Height);
            TimeSpan ts = TimeSpan.FromMilliseconds(miss.StartTime);
            graphics.DrawString("Time: " + ts.ToString(@"mm\:ss\.fff"), font, pen.Brush, 0, area.Height - font.Height);
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
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.menuKit = new System.Windows.Forms.ToolStripMenuItem();
            this.newReplayEntry = new System.Windows.Forms.ToolStripMenuItem();
            this.mainCanvas = new System.Windows.Forms.PictureBox();
            this.tableLayoutPanel1.SuspendLayout();
            this.menuStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.mainCanvas)).BeginInit();
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
            this.newReplayEntry.Click += new System.EventHandler(this.NewReplayEntry_Click);
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
            // MissAnalyzer
            // 
            this.ClientSize = new System.Drawing.Size(384, 561);
            this.Controls.Add(this.tableLayoutPanel1);
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "MissAnalyzer";
            this.Load += new System.EventHandler(this.MissAnalyzer_Load);
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.mainCanvas)).EndInit();
            this.ResumeLayout(false);

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
                    SaveMissImages();
                    break;
                case System.Windows.Forms.Keys.R:
                    LoadNewReplay();
                    if (replay == null || beatmap == null)
                        throw new ArgumentNullException("replay or beatmap", "Has been not set in LoadNewReplay method.");
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
        private void MissAnalyzer_Load(object sender, EventArgs e)
        {
            area = mainCanvas.ClientRectangle;
            img = new Bitmap(area.Width, area.Height);
            graphics = Graphics.FromImage(img);
            mainCanvas.Image = img;
            
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            mainCanvas.Image = DrawMiss(number);
        }

        private void MainCanvas_Click(object sender, EventArgs e)
        {

        }

        private void NewReplayEntry_Click(object sender, EventArgs e)
        {
            LoadNewReplay();
        }
    }
}