using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BMAPI;
using BMAPI.v1;
using BMAPI.v1.Events;
using BMAPI.v1.HitObjects;
using ReplayAPI;

namespace osuDodgyMomentsFinder
{

	/* This class is a list of pair of a clickable object and a replay frame hit
     * Initializing the class is a task of associating every keypress with an object hit
     * After that all the procedural checks on suspicious moment become possible
     */
	public class ReplayAnalyzer
	{
		private readonly Beatmap beatmap;
		private readonly Replay replay;
		private readonly double circleRadius;
		private readonly double hitTimeWindow;

		public List<HitFrame> Hits { get; }
		public List<HitFrame> AttemptedHits { get; }
		public List<CircleObject> Misses { get; }
		private List<CircleObject> EffortlessMisses { get; }
		private List<BreakEvent> Breaks { get; }
		private List<SpinnerObject> Spinners { get; }
		private List<ClickFrame> ExtraHits { get; }

		private void ApplyHardrock()
		{
			replay.Flip();
			beatmap.applyHardRock();
		}

		private void SelectBreaks()
		{
			foreach (var event1 in beatmap.Events)
			{
				if (event1.GetType() == typeof(BreakEvent))
				{
					Breaks.Add((BreakEvent)event1);
				}
			}
		}

		private void SelectSpinners()
		{
			foreach (var obj in beatmap.HitObjects)
			{
				if (obj.Type.HasFlag(HitObjectType.Spinner))
				{
					Spinners.Add((SpinnerObject)obj);
				}
			}
		}

		private void AssociateHits()
		{
			int keyIndex = 0;
			var keyCounter = new KeyCounter();

			if ((replay.Mods & Mods.HardRock) > 0)
			{
				ApplyHardrock();
			}

			int breakIndex = 0;
			int combo = 0;

			foreach (CircleObject note in beatmap.HitObjects)
			{
				bool noteHitFlag = false;
				bool noteAttemptedHitFlag = false;

				if (note.Type.HasFlag(HitObjectType.Spinner))
					continue;

				for (int j = keyIndex; j < replay.ReplayFrames.Count; ++j)
				{
					var frame = replay.ReplayFrames[j];
					var lastKey = j > 0 ? replay.ReplayFrames[j - 1].Keys : Keys.None;

					var pressedKey = GetKey(lastKey, frame.Keys);

					if (breakIndex < Breaks.Count && frame.Time > Breaks[breakIndex].EndTime)
					{
						++breakIndex;
					}

					if (frame.Time >= beatmap.HitObjects[0].StartTime - hitTimeWindow && 
					    (breakIndex >= Breaks.Count || frame.Time < Breaks[breakIndex].StartTime - hitTimeWindow))
					{
						keyCounter.Update(lastKey, frame.Keys);
					}

					frame.KeyCounter = new KeyCounter(keyCounter);

					if (frame.Time - note.StartTime > hitTimeWindow)
						break;

					if (pressedKey > 0 && Math.Abs(frame.Time - note.StartTime) <= 
									(frame.Time > note.StartTime 
										&& note is SliderObject s? 
									Math.Min(hitTimeWindow, s.duration / s.RepeatCount) : hitTimeWindow))
					{
                        Point2 point = new Point2(frame.X, frame.Y);
                        if (note.ContainsPoint(point))
						{
							noteAttemptedHitFlag = true;
							++combo;
							frame.Combo = combo;
							noteHitFlag = true;
							Hits.Add(new HitFrame(note, frame, pressedKey));
							keyIndex = j + 1;
							break;
						}
						if (Utils.dist(note.Location.X, note.Location.Y, frame.X, frame.Y) > 150)
						{
							ExtraHits.Add(new ClickFrame(frame, GetKey(lastKey, frame.Keys)));
						}
						else
						{
							noteAttemptedHitFlag = true;
							AttemptedHits.Add(new HitFrame(note, frame, pressedKey));
						}
					}
					if (pressedKey > 0 && Math.Abs(frame.Time - note.StartTime) <= 3 * hitTimeWindow && note.ContainsPoint(new Point2(frame.X, frame.Y)))
					{
						noteAttemptedHitFlag = true;
						AttemptedHits.Add(new HitFrame(note, frame, pressedKey));
					}

					frame.Combo = combo;

				}

				if (!noteHitFlag)
				{
					Misses.Add(note);
				}
				if (!noteAttemptedHitFlag)
				{
					EffortlessMisses.Add(note);
				}
			}
		}

		public Keys GetKey(Keys last, Keys current)
		{
			Keys res = Keys.None;
			if (!last.HasFlag(Keys.M1) && current.HasFlag(Keys.M1) && !current.HasFlag(Keys.K1))
				res |= Keys.M1;
			if (!last.HasFlag(Keys.M2) && current.HasFlag(Keys.M2) && !current.HasFlag(Keys.K2))
				res |= Keys.M2;
			if (!last.HasFlag(Keys.K1) && current.HasFlag(Keys.K1))
				res |= Keys.K1 | Keys.M1;
			if (!last.HasFlag(Keys.K2) && current.HasFlag(Keys.K2))
				res |= Keys.K2 | Keys.M2;
			return res;
		}

		private List<double> CalcPressIntervals()
		{
			List<double> result = new List<double>();

			bool k1 = false, k2 = false;
			double k1_timer = 0, k2_timer = 0;
			foreach (var frame in replay.ReplayFrames)
			{
				var hit = Hits.Find(x => x.Frame.Equals(frame));

				if (!ReferenceEquals(hit, null) && hit.Note.Type == HitObjectType.Circle)
				{
					if (!k1 && frame.Keys.HasFlag(Keys.K1))
						k1 = true;

					if (!k2 && frame.Keys.HasFlag(Keys.K2))
						k2 = true;
				}

				//k1
				if (k1 && frame.Keys.HasFlag(Keys.K1))
				{
					k1_timer += frame.TimeDiff;
				}

				if (k1 && !frame.Keys.HasFlag(Keys.K1))
				{
					k1 = false;
					result.Add(k1_timer);
					k1_timer = 0;
				}

				//k2
				if (k2 && frame.Keys.HasFlag(Keys.K2))
				{
					k2_timer += frame.TimeDiff;
				}

				if (k2 && !frame.Keys.HasFlag(Keys.K2))
				{
					k2 = false;
					result.Add(k2_timer);
					k2_timer = 0;
				}
			}

			if (result.Count == 0)
				result.Add(-1);

			return result;
		}

		private List<KeyValuePair<HitFrame, HitFrame>> CheckTappingConsistency()
		{
			var times = new List<KeyValuePair<HitFrame, HitFrame>>();

			double limit = 90 * (replay.Mods.HasFlag(Mods.DoubleTime) ? 1.5 : 1);

			for (int i = 0; i < Hits.Count - 1; ++i)
			{
				HitFrame hit1 = Hits[i], hit2 = Hits[i + 1];

				if ((hit2.Frame.Time - hit1.Frame.Time <= limit || hit2.Note.StartTime - hit1.Note.StartTime <= limit) && (hit1.Key & hit2.Key) > 0)
					times.Add(new KeyValuePair<HitFrame, HitFrame>(hit1, hit2));
			}

			return times;
		}

		private List<ReplayFrame> FindCursorTeleports()
		{
			var times = new List<ReplayFrame>();

			int spinnerIndex = 0;

			for (int i = 2; i < replay.Times.Count - 1; ++i)
			{
				var frame = replay.Times[i + 1];

				if (spinnerIndex < Spinners.Count && frame.Time > Spinners[spinnerIndex].EndTime)
				{
					++spinnerIndex;
				}

				if (IsTeleport(frame) && (spinnerIndex >= Spinners.Count || frame.Time < Spinners[spinnerIndex].StartTime))
				{
					times.Add(frame);
				}
			}

			return times;
		}

		private bool IsTeleport(ReplayFrame frame)
		{
			if (frame.TravelledDistanceDiff >= 40 && double.IsInfinity(frame.Speed))
				return true;

			return frame.TravelledDistanceDiff >= 150 && frame.Speed >= 6;
		}

		public string OutputDistances()
		{
			string res = "";
			foreach (var value in FindCursorTeleports())
			{
				res += value.TravelledDistanceDiff + ",";
			}
			return res.Remove(res.Length - 1);
		}

		private double CalculateAverageFrameTimeDiff()
		{
			return replay.Times.ConvertAll(x => x.TimeDiff).Where(x => x > 0 && x < 30).Average();
		}

		private double CalculateAverageFrameTimeDiffv2()
		{
			int count = 0;
			int sum = 0;

			for (int i = 1; i < replay.Times.Count - 1; i++)
			{
				if (!replay.Times[i - 1].Keys.HasFlag(Keys.K1) && !replay.Times[i - 1].Keys.HasFlag(Keys.K2) && !replay.Times[i - 1].Keys.HasFlag(Keys.M1) && !replay.Times[i - 1].Keys.HasFlag(Keys.M2) &&
					!replay.Times[i].Keys.HasFlag(Keys.K1) && !replay.Times[i].Keys.HasFlag(Keys.K2) && !replay.Times[i].Keys.HasFlag(Keys.M1) && !replay.Times[i].Keys.HasFlag(Keys.M2) &&
					!replay.Times[i + 1].Keys.HasFlag(Keys.K1) && !replay.Times[i + 1].Keys.HasFlag(Keys.K2) && !replay.Times[i + 1].Keys.HasFlag(Keys.M1) && !replay.Times[i + 1].Keys.HasFlag(Keys.M2))
				{
					count++;
					sum += replay.Times[i].TimeDiff;
				}
			}

			if (count == 0)
			{
				return -1.0;
			}

			return (double)sum / count;
		}

		private List<double> SpeedList()
		{
			return replay.Times.ConvertAll(x => x.Speed);
		}

		private List<double> AccelerationList()
		{
			return replay.Times.ConvertAll(x => x.Acceleration);
		}

		public string OutputSpeed()
		{
			string res = SpeedList().Aggregate("", (current, value) => current + (value + ","));
			return res.Remove(res.Length - 1);
		}

		public string OutputAcceleration()
		{
			string res = replay.Times.ConvertAll(x => x.Acceleration).Aggregate("", (current, value) => current + (value + ","));
			return res.Remove(res.Length - 1);
		}

		public string OutputTime()
		{
			string res = replay.Times.ConvertAll(x => x.Time).Aggregate("", (current, value) => current + (value + ","));
			return res.Remove(res.Length - 1);
		}

		private List<HitFrame> FindOverAimHits()
		{
			var result = new List<HitFrame>();
			int keyIndex = 0;

			foreach (var t in Hits)
			{
				var note = t.Note;

				// Searches for init circle object hover
				for (int j = keyIndex; j < replay.ReplayFrames.Count; ++j)
				{
					ReplayFrame frame = replay.ReplayFrames[j];
					if (!note.ContainsPoint(new Point2(frame.X, frame.Y)) ||
					   !(Math.Abs(frame.Time - note.StartTime) <= hitTimeWindow)) continue;

					while (note.ContainsPoint(new Point2(frame.X, frame.Y)) && frame.Time < t.Frame.Time)
					{
						++j;
						frame = replay.ReplayFrames[j];
					}

					if (!note.ContainsPoint(new Point2(frame.X, frame.Y)))
					{
						result.Add(t);
					}
				}
			}
			return result;
		}


		// Recalculate the highest CS value for which the player would still have the same amount of misses
		private double BestCSValue()
		{
			double pixelPerfect = FindBestPixelHit();

			double y = pixelPerfect * circleRadius;

			double x = (54.42 - y) / 4.48;

			return x;
		}

		public double CalcAccelerationVariance() 
		{ 
			return Utils.variance(AccelerationList()); 
		}

		public string OutputMisses()
		{
			string res = "";
			Misses.ForEach(note => res += "Didn't find the hit for " + note.StartTime);
			return res;
		}

		private double CalcTimeWindow(double OD) 
		{ 
			return -12 * OD + 259.5; 
		}

		public ReplayAnalyzer(Beatmap beatmap, Replay replay)
		{
			this.beatmap = beatmap;
			this.replay = replay;

			if (!replay.FullLoaded)
				throw new Exception(replay.Filename + " IS NOT FULL");

			multiplier = replay.Mods.HasFlag(Mods.DoubleTime) ? 1.5 : 1;
			circleRadius = beatmap.HitObjects[0].Radius;
			hitTimeWindow = CalcTimeWindow(beatmap.OverallDifficulty);

			Hits = new List<HitFrame>();
			AttemptedHits = new List<HitFrame>();
			Misses = new List<CircleObject>();
			EffortlessMisses = new List<CircleObject>();
			ExtraHits = new List<ClickFrame>();
			Breaks = new List<BreakEvent>();
			Spinners = new List<SpinnerObject>();

			SelectBreaks();
			SelectSpinners();
			AssociateHits();
		}


		private double FindBestPixelHit() 
		{ 
			return Hits.Max(pair => Utils.pixelPerfectHitFactor(pair.Frame, pair.Note)); 
		}

		public List<double> FindPixelPerfectHits(double threshold)
		{
			List<double> result = new List<double>();

			foreach (var pair in Hits)
			{
				double factor = Utils.pixelPerfectHitFactor(pair.Frame, pair.Note);

				if (factor >= threshold)
				{
					result.Add(pair.Note.StartTime);
				}
			}


			return result;
		}

		private List<HitFrame> FindAllPixelHits()
		{
			var pixelPerfectHits = new List<HitFrame>();

			foreach (var pair in Hits)
			{
				pixelPerfectHits.Add(pair);
			}

			return pixelPerfectHits;
		}


		public List<HitFrame> FindSortedPixelPerfectHits(int maxSize, double threshold)
		{
			var pixelPerfectHits = (from pair in Hits let factor = pair.Perfectness where factor >= threshold select pair).ToList();

			pixelPerfectHits.Sort((a, b) => b.Perfectness.CompareTo(a.Perfectness));

			return pixelPerfectHits.GetRange(0, Math.Min(maxSize, pixelPerfectHits.Count));
		}


		// private double ur = -1;

		private double UnstableRate()
		{
			double ur = -1;
			if (ur >= 0)
				return ur;
			var values = Hits.ConvertAll(pair => (double)pair.Frame.Time - pair.Note.StartTime);
			ur = 10 * Utils.variance(values);
			return ur;
		}

		private readonly double multiplier;

		public StringBuilder MainInfo()
		{
			var sb = new StringBuilder();

			sb.AppendLine("GENERIC INFO");

			sb.AppendLine(Misses.Count > replay.CountMiss
				? $"WARNING! The detected number of misses is not consistent with the replay: {Misses.Count} VS. " +
				  $"{replay.CountMiss} (notepad user or missed on spinners or BUG in the code <- MOST LIKELY )"
				: $"Misses: {Misses.Count}");

			sb.AppendLine($"Unstable rate: {UnstableRate()}");

			if (UnstableRate() < 47.5 * multiplier)
			{
				sb.AppendLine("WARNING! Unstable rate is too low (auto)");
			}

			sb.AppendLine($"The best CS value: {BestCSValue()}");
			sb.AppendLine($"Average frame time difference: {CalculateAverageFrameTimeDiff()}ms");

			double averageFrameTimeDiffv2 = CalculateAverageFrameTimeDiffv2();
			sb.AppendLine($"Average frame time difference v2 (aim only!): {averageFrameTimeDiffv2}ms");

			if ((replay.Mods.HasFlag(Mods.DoubleTime) || replay.Mods.HasFlag(Mods.NightCore)) && averageFrameTimeDiffv2 < 17.35
			   || !replay.Mods.HasFlag(Mods.HalfTime) && averageFrameTimeDiffv2 < 12.3)
			{
				sb.AppendLine("WARNING! Average frame time difference is not consistent with the speed-modifying gameplay mods (timewarp)!" + Environment.NewLine);
			}

			var keyPressIntervals = CalcPressIntervals();

			double averageKeyPressTime = Utils.median(keyPressIntervals) / multiplier;
			sb.AppendLine($"Median Key press time interval: {averageKeyPressTime:0.00}ms");
			sb.AppendLine($"Min Key press time interval: {keyPressIntervals.Min() / multiplier:0.00}ms");

			if (averageKeyPressTime < 30)
			{
				sb.AppendLine("WARNING! Average Key press time interval is inhumanly low (timewarp/relax)!");
			}

			sb.AppendLine($"Extra hits: {ExtraHits.Count}");

			if (replay.Mods.HasFlag(Mods.NoFail))
			{
				sb.AppendLine($"Pass: {replay.IsPass()}");
			}

			return sb;
		}

		public StringBuilder CursorInfo()
		{
			var sb = new StringBuilder();
			sb.AppendLine("Cursor movement Info");

			var cursorAcceleration = AccelerationList();
			sb.AppendLine($"Cursor acceleration mean: {cursorAcceleration.Average()}");
			sb.AppendLine($"Cursor acceleration variance: {Utils.variance(cursorAcceleration)}");

			return sb;
		}


		public StringBuilder PixelPerfectRawData()
		{
			var sb = new StringBuilder();
			var pixelPerfectHits = FindAllPixelHits();

			foreach (var hit in pixelPerfectHits)
			{
				sb.Append(hit).Append(',');
			}

			return sb;
		}

		public StringBuilder TimeFramesRawData()
		{
			var sb = new StringBuilder();
			var timeFrames = replay.ReplayFrames.ConvertAll(x => x.TimeDiff).Where(x => x > 0);

			foreach (int frame in timeFrames)
			{
				sb.Append(frame).Append(',');
			}

			return sb;
		}

		public StringBuilder TravelledDistanceDiffRawData()
		{
			var sb = new StringBuilder();
			var timeFrames = replay.ReplayFrames.ConvertAll(x => x.TravelledDistanceDiff);

			foreach (double frame in timeFrames)
			{
				sb.Append(frame).Append(',');
			}

			return sb;
		}

		public StringBuilder SpeedRawData()
		{
			var sb = new StringBuilder();
			var timeFrames = replay.ReplayFrames.ConvertAll(x => x.Speed);

			foreach (double frame in timeFrames)
			{
				sb.Append(frame).Append(',');
			}

			return sb;
		}

		public StringBuilder AccelerationRawData()
		{
			var sb = new StringBuilder();
			var timeFrames = replay.ReplayFrames.ConvertAll(x => x.Acceleration);

			foreach (double frame in timeFrames)
			{
				sb.Append(frame).Append(',');
			}

			return sb;
		}

		public StringBuilder HitErrorRawData()
		{
			var sb = new StringBuilder();
			var timeFrames = Hits.ConvertAll(x => x.Note.StartTime - x.Frame.Time);

			foreach (float frame in timeFrames)
			{
				sb.Append(frame).Append(',');
			}

			return sb;
		}

		public StringBuilder PressKeyIntevalsRawData()
		{
			var sb = new StringBuilder();
			var timeFrames = CalcPressIntervals();

			foreach (double frame in timeFrames)
			{
				sb.Append(frame).Append(',');
			}

			return sb;
		}

		public StringBuilder PixelPerfectInfo()
		{
			var sb = new StringBuilder();
			sb.AppendLine("- [PIXEL PERFECTS]:");

			var pixelHits = FindAllPixelHits();
			var values = pixelHits.Select(x => x.Perfectness).ToList();
			double bestPxPerfect = pixelHits.Max(a => a.Perfectness);
			sb.AppendLine($"The best pixel perfect hit: {bestPxPerfect}");
			double median = values.Median();
			double variance = Utils.variance(values);
			sb.AppendLine($"Median pixel perfect hit: {median}");
			sb.AppendLine($"Perfectness variance: {variance}");

			if (bestPxPerfect < 0.5 || variance < 0.01 || median < 0.2)
			{
				sb.AppendLine("WARNING! Player is aiming the notes too consistently (autohack)");
				sb.AppendLine();
			}

			var pixelperfectHits = pixelHits.Where(x => x.Perfectness > 0.98);
			var hitFrames = pixelperfectHits as HitFrame[] ?? pixelperfectHits.ToArray();
			sb.AppendLine($"Pixel perfect hits: {hitFrames.Length}");

			foreach (var hit in hitFrames)
			{
				sb.AppendLine($"* {hit}");
			}

			var unrealisticPixelPerfects = hitFrames.Where(x => x.Perfectness > 0.99);

			if (unrealisticPixelPerfects.Count() > 15)
			{
				sb.AppendLine("WARNING! Player is constantly doing pixel perfect hits (relax)" + Environment.NewLine);
			}

			return sb;
		}

		public StringBuilder OveraimsInfo()
		{
			var sb = new StringBuilder();
			sb.AppendLine("- [OVER-AIM]:");

			var overAims = FindOverAimHits();
			sb.AppendLine($"Over-aim count: {overAims.Count}");

			foreach (var hit in overAims)
			{
				sb.AppendLine($"* {hit}");
			}

			return sb;
		}

		public StringBuilder TeleportsInfo()
		{
			var sb = new StringBuilder();
			sb.AppendLine("- [CURSOR TELEPORTS]:");

			var teleports = FindCursorTeleports();
			sb.AppendLine($"Teleport count: {teleports.Count}");

			foreach (var frame in teleports)
			{
				sb.AppendLine($"* {frame.Time}ms {frame.TravelledDistanceDiff}px");
			}

			return sb;
		}

		public StringBuilder ExtraHitsInfo()
		{
			var sb = new StringBuilder();
			sb.AppendLine("- [EXTRA HITS]");
			sb.AppendLine($"Extra hits count: {ExtraHits.Count}");

			foreach (var frame in ExtraHits)
			{
				sb.AppendLine(frame.ToString());
			}

			return sb;
		}

		public StringBuilder EffortlessMissesInfo()
		{
			var sb = new StringBuilder();
			sb.AppendLine("- [EFFORTLESS MISSES]");
			sb.AppendLine($"Effortless misses count: {EffortlessMisses.Count}");

			foreach (var note in EffortlessMisses)
			{
				sb.AppendLine($"{note} missed without a corresponding hit");
			}

			return sb;
		}

		public StringBuilder SingletapsInfo()
		{
			var sb = new StringBuilder();
			sb.AppendLine("Singletaps");

			var singletaps = CheckTappingConsistency();
			sb.AppendLine($"Fast singletaps count: {singletaps.Count}");

			foreach (var frame in singletaps)
			{
				sb.AppendLine($"* Object at {frame.Key.Note.StartTime}ms {frame.Key.Key} singletapped with next at {frame.Value.Note.StartTime} " +
							  $"({(frame.Value.Frame.Time - frame.Key.Frame.Time) / multiplier}ms real frame time diff)" +
							  $" - {frame.Key.Frame.Time - frame.Key.Note.StartTime}ms and {frame.Value.Frame.Time - frame.Value.Note.StartTime}ms error.");
			}

			return sb;
		}

	}
}
