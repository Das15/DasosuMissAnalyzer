using System;
using System.Drawing;

namespace ReplayAPI
{
    public class ReplayFrame
    {
        public int TimeDiff;
        public int Time;
        [System.ComponentModel.DisplayName("Time In Seconds")]
        public float TimeInSeconds { get { return Time / 1000f; } }
        public float X { get; set; }
        public float Y { get; set; }
		public PointF PointF
		{
			get
			{
				return new PointF(X, Y);
			}
		}
        public Keys Keys { get; set; }
        public KeyCounter KeyCounter { get; set; }
        public int Combo { get; set; }
        public double TravelledDistance { get; set; }
        public double TravelledDistanceDiff { get; set; }
        public double Speed { get; set; }
        public double Acceleration { get; set; }

        
        public override string ToString()
        {
            return string.Format("{0}({1}): ({2},{3}) {4} {5}", Time, TimeDiff, X, Y, Keys, TravelledDistanceDiff);
        }
    }
}
