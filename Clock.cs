using System;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;

namespace TinyMUD
{
	public class Clock
	{
		public const int Once = System.Threading.Timeout.Infinite;

		public static readonly DateTime UTC = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		private static readonly Stopwatch sinceStartup = new Stopwatch();
		private static double timeStartup;
		private static double time = 0;

		public static long Now
		{
			get { return (long)((timeStartup + sinceStartup.Elapsed.TotalMilliseconds) * 1000 + 0.5); }
		}

		public static long Elapsed
		{
			get { return (long)(sinceStartup.Elapsed.TotalMilliseconds * 1000 + 0.5); }
		}

		public static long Uptime
		{
			get { return (long)(time * 1000000 + 0.5); }
		}

		public static void Initialize()
		{
			if (!sinceStartup.IsRunning)
			{
				sinceStartup.Start();
				TimeSpan ts = DateTime.UtcNow - UTC;
				timeStartup = ts.TotalMilliseconds;
			}
		}

		public static void Update()
		{
			time += UnityEngine.Time.deltaTime;
		}

		public static DateTime UTCTime()
		{
			return UTCTime(Now);
		}

		public static DateTime UTCTime(long time)
		{
			TimeSpan ts = TimeSpan.FromMilliseconds(time * 0.001);
			return UTC + ts;
		}

		public static DateTime LocalTime()
		{
			return LocalTime(Now);
		}

		public static DateTime LocalTime(long time)
		{
			TimeSpan ts = TimeSpan.FromMilliseconds(time * 0.001);
			return (UTC + ts).ToLocalTime();
		}

		private static readonly SortedDictionary<Clock, bool> clocks = new SortedDictionary<Clock, bool>(Comparer.Default);

		public static void Update(Action<Exception> error)
		{
			long now = Now;
			while (true)
			{
				Clock clock;
				var iterator = clocks.GetEnumerator();
				using (iterator)
				{
					if (!iterator.MoveNext())
						break;
					clock = iterator.Current.Key;
				}
				if (clock.elapsed > now)
					break;
				clocks.Remove(clock);
				if (clock.Time != Once)
				{
					clock.elapsed += Math.Max(clock.Time * 1000, 1);
					clocks.Add(clock, true);
				}
				else
				{
					clock.elapsed = -1;
				}
				try
				{
					clock.Timeout();
				}
				catch (Exception e)
				{
					error(e);
				}
			}
		}

		private static long total = 0;
		private readonly long index;
		private long elapsed;
		private readonly Action callback1;
		private readonly Action<Clock> callback2;

		public int Time;

		protected Clock()
		{
			index = Interlocked.Increment(ref total);
			elapsed = -1;
			Time = Once;
		}

		public Clock(Action callback)
			: this()
		{
			callback1 = callback;
		}

		public Clock(Action<Clock> callback)
			: this()
		{
			callback2 = callback;
		}

		public bool IsRunning
		{
			get { return elapsed != -1; }
		}

		public void Start()
		{
			Start(Time < 0 ? 0 : Time);
		}

		public void Start(int time)
		{
			Stop();
			elapsed = Now + time * 1000;
			clocks.Add(this, true);
		}

		public void Stop()
		{
			if (elapsed != -1)
			{
				if (clocks.Remove(this))
					elapsed = -1;
			}
		}

		protected virtual void Timeout()
		{
			if (callback1 != null)
				callback1();
			else if (callback2 != null)
				callback2(this);
		}

		private class Comparer : IComparer<Clock>
		{
			public int Compare(Clock x, Clock y)
			{
				int result = x.elapsed.CompareTo(y.elapsed);
				return result != 0 ? result : x.index.CompareTo(y.index);
			}

			public static readonly IComparer<Clock> Default = new Comparer();
		}
	}

	public class Clock<T> : Clock
	{
		public T Value;
		private readonly Action<T> callback1;
		private readonly Action<Clock<T>> callback2;

		public Clock(Action callback) : base(callback) { }

		public Clock(Action<T> callback)
		{
			callback1 = callback;
		}

		public Clock(Action<Clock<T>> callback)
		{
			callback2 = callback;
		}

		protected override void Timeout()
		{
			if (callback1 != null)
				callback1(Value);
			else if (callback2 != null)
				callback2(this);
			else
				base.Timeout();
		}
	}
}