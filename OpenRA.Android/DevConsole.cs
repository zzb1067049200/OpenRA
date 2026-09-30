#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Text;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using OpenRA.Platforms.Android;

namespace OpenRA.Android
{
	// ── Log level ─────────────────────────────────────────────────────────────
	public enum LogLevel { Verbose, Info, Warn, Error }

	// ── Log entry ─────────────────────────────────────────────────────────────
	public readonly struct LogEntry
	{
		public readonly DateTime Time;
		public readonly LogLevel Level;
		public readonly string Tag;
		public readonly string Message;

		public LogEntry(LogLevel level, string tag, string message)
		{
			Time    = DateTime.Now;
			Level   = level;
			Tag     = tag;
			Message = message;
		}

		public string Format() =>
			$"[{Time:HH:mm:ss.fff}] [{Level.ToString()[0]}][{Tag}] {Message}";
	}

	// ── Thread-safe log buffer ─────────────────────────────────────────────────
	public static class DevConsole
	{
		public static event Action<LogEntry> OnNewEntry;

		static readonly object Lock = new();
		static readonly List<LogEntry> Entries = new();
		const int MaxEntries = 3000;
		public static volatile bool IsDisabled = false;

		public static void Disable()
		{
			IsDisabled = true;
			Clear();
		}

		public static void Log(LogLevel level, string tag, string message)
		{
			if (IsDisabled)
				return;

			var entry = new LogEntry(level, tag, message);

			var androidPriority = level switch
			{
				LogLevel.Error   => global::Android.Util.LogPriority.Error,
				LogLevel.Warn    => global::Android.Util.LogPriority.Warn,
				LogLevel.Verbose => global::Android.Util.LogPriority.Verbose,
				_                => global::Android.Util.LogPriority.Info,
			};
			global::Android.Util.Log.WriteLine(androidPriority, "OpenRA.Dev", entry.Format());

			lock (Lock)
			{
				Entries.Add(entry);
				if (Entries.Count > MaxEntries)
					Entries.RemoveAt(0);
			}

			OnNewEntry?.Invoke(entry);
		}

		// Convenience helpers
		public static void Log(string tag, string msg)   => Log(LogLevel.Info,    tag, msg);
		public static void Info(string tag, string msg)  => Log(LogLevel.Info,    tag, msg);
		public static void Warn(string tag, string msg)  => Log(LogLevel.Warn,    tag, msg);
		public static void Error(string tag, string msg) => Log(LogLevel.Error,   tag, msg);
		public static void Verbose(string tag, string msg) => Log(LogLevel.Verbose, tag, msg);

		public static List<LogEntry> GetEntries()
		{
			lock (Lock)
				return new List<LogEntry>(Entries);
		}

		public static string GetAllText()
		{
			lock (Lock)
			{
				var sb = new StringBuilder();
				foreach (var e in Entries)
					sb.AppendLine(e.Format());
				return sb.ToString();
			}
		}

		public static void Clear()
		{
			lock (Lock)
				Entries.Clear();
		}
	}

	// ── Floating debug overlay ──────────────────────────────────────────────────
	/// <summary>
	/// Attaches a draggable bubble to the activity window.
	/// Tap the bubble → full-screen log panel. Tap ✕ → back to bubble.
	/// </summary>
	public class DebugOverlay
	{
		// Colors
		static readonly Color BgColor       = Color.ParseColor("#ee111111");
		static readonly Color BubbleColor   = Color.ParseColor("#cc1a1a2e");
		static readonly Color BubbleBorder  = Color.ParseColor("#cc00ff88");
		static readonly Color HeaderColor   = Color.ParseColor("#1f1f2e");
		static readonly Color ColVerbose    = Color.ParseColor("#888888");
		static readonly Color ColInfo       = Color.ParseColor("#aaffaa");
		static readonly Color ColWarn       = Color.ParseColor("#ffdd55");
		static readonly Color ColError      = Color.ParseColor("#ff4444");

		readonly Activity _activity;
		ViewGroup         _root;          // FrameLayout covering full screen
		View              _bubble;
		View              _panel;
		TextView          _logView;
		ScrollView        _scroll;
		bool              _panelVisible;

		int               _bubbleX, _bubbleY;
		int               _bubbleSize;

		// Called once from MainActivity.OnCreate (on UI thread)
		public static DebugOverlay Attach(Activity activity)
		{
			var overlay = new DebugOverlay(activity);
			overlay.Build();
			return overlay;
		}

		DebugOverlay(Activity activity) => _activity = activity;

		void Build()
		{
			var dm    = _activity.Resources.DisplayMetrics;
			_bubbleSize = (int)(56 * dm.Density);   // 56dp
			_bubbleX    = (int)(dm.WidthPixels  - _bubbleSize - 24 * dm.Density);
			_bubbleY    = (int)(dm.HeightPixels * 0.35f);

			// Root transparent frame that sits on top of everything
			_root = new FrameLayout(_activity)
			{
				LayoutParameters = new ViewGroup.LayoutParams(
					ViewGroup.LayoutParams.MatchParent,
					ViewGroup.LayoutParams.MatchParent)
			};
			_root.Elevation = 1000f;

			BuildBubble();
			BuildPanel();

			_root.AddView(_panel);
			_root.AddView(_bubble);

			_activity.AddContentView(_root, new ViewGroup.LayoutParams(
				ViewGroup.LayoutParams.MatchParent,
				ViewGroup.LayoutParams.MatchParent));

			_root.BringToFront();

			// Live-update log when new entries arrive
			DevConsole.OnNewEntry += entry =>
			{
				if (_panelVisible)
					_activity.RunOnUiThread(() => AppendEntry(entry));
			};

			DevConsole.Info("DebugOverlay", "Overlay attached. Tap bubble to open console.");
		}

		public void BringToFront()
		{
			_root?.BringToFront();
		}

		// ── Bubble ─────────────────────────────────────────────────────────────
		void BuildBubble()
		{
			var tv = new TextView(_activity)
			{
				Text = "🐛",
				Gravity = GravityFlags.Center,
			};
			tv.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 22f);

			_bubble = tv;

			var lp = new FrameLayout.LayoutParams(_bubbleSize, _bubbleSize);
			_bubble.LayoutParameters = lp;

			// Draw circle background
			var shape = new global::Android.Graphics.Drawables.GradientDrawable();
			shape.SetShape(global::Android.Graphics.Drawables.ShapeType.Oval);
			shape.SetColor(BubbleColor);
			shape.SetStroke(3, BubbleBorder);
			_bubble.Background = shape;

			_bubble.Alpha = 0.85f;
			_bubble.Elevation = 20f;

			MoveBubbleTo(_bubbleX, _bubbleY);

			// Touch: drag or tap
			float[] downRaw = { 0, 0 };
			int[]   downPos = { 0, 0 };
			bool    dragged = false;

			_bubble.Touch += (_, e) =>
			{
				switch (e.Event.Action)
				{
					case MotionEventActions.Down:
						downRaw[0] = e.Event.RawX;
						downRaw[1] = e.Event.RawY;
						downPos[0] = _bubbleX;
						downPos[1] = _bubbleY;
						dragged = false;
						break;

					case MotionEventActions.Move:
						var dx = e.Event.RawX - downRaw[0];
						var dy = e.Event.RawY - downRaw[1];
						if (MathF.Abs(dx) > 8 || MathF.Abs(dy) > 8)
						{
							dragged = true;
							_bubbleX = (int)(downPos[0] + dx);
							_bubbleY = (int)(downPos[1] + dy);
							MoveBubbleTo(_bubbleX, _bubbleY);
						}
						break;

					case MotionEventActions.Up:
						if (!dragged)
							ShowPanel();
						break;
				}
				e.Handled = true;
			};
		}

		void MoveBubbleTo(int x, int y)
		{
			_bubble.SetX(x);
			_bubble.SetY(y);
		}

		// ── Panel ──────────────────────────────────────────────────────────────
		void BuildPanel()
		{
			var root = new LinearLayout(_activity)
			{
				Orientation = Orientation.Vertical,
				LayoutParameters = new FrameLayout.LayoutParams(
					ViewGroup.LayoutParams.MatchParent,
					ViewGroup.LayoutParams.MatchParent),
				Visibility = ViewStates.Gone
			};
			root.SetBackgroundColor(BgColor);
			root.SetPadding(0, 0, 0, 0);

			// ── Header ─────────────────────────────────────────────────────────
			var header = new LinearLayout(_activity)
			{
				Orientation = Orientation.Horizontal,
				LayoutParameters = new LinearLayout.LayoutParams(
					ViewGroup.LayoutParams.MatchParent,
					ViewGroup.LayoutParams.WrapContent)
			};
			header.SetBackgroundColor(HeaderColor);
			header.SetPadding(16, 8, 8, 8);

			var titleTv = new TextView(_activity)
			{
				Text = "🐛 Debug Console",
				LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
			};
			titleTv.SetTextColor(BubbleBorder);
			titleTv.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14f);
			titleTv.SetTypeface(null, TypefaceStyle.Bold);

			var copyBtn  = MakeHeaderBtn("📋", () =>
			{
				var clip = (global::Android.Content.ClipboardManager)_activity.GetSystemService(Context.ClipboardService);
				if (clip != null)
				{
					clip.PrimaryClip = global::Android.Content.ClipData.NewPlainText("OpenRA Log", DevConsole.GetAllText());
					Toast.MakeText(_activity, "Copied!", ToastLength.Short)?.Show();
				}
			});

			var shutdownBtn = MakeHeaderBtn("🛑 Off", () =>
			{
				DevConsole.Disable();
				OpenRA.Log.OnLogMessage = null;
				AndroidPlatform.PlatformLogger = null;
				AndroidPlatform.PlatformErrorLogger = null;
				HidePanel();
				if (_bubble != null)
					_bubble.Visibility = ViewStates.Gone;
				if (_root != null)
					_root.Visibility = ViewStates.Gone;
				Toast.MakeText(_activity, "DevConsole disabled until next launch.", ToastLength.Long)?.Show();
			});
			shutdownBtn.SetBackgroundColor(Color.ParseColor("#553311"));

			var clearBtn = MakeHeaderBtn("🗑", () =>
			{
				DevConsole.Clear();
				_activity.RunOnUiThread(() => _logView.Text = "");
			});

			var closeBtn = MakeHeaderBtn("✕", HidePanel);
			closeBtn.SetBackgroundColor(Color.ParseColor("#883333"));

			header.AddView(titleTv);
			header.AddView(copyBtn);
			header.AddView(shutdownBtn);
			header.AddView(clearBtn);
			header.AddView(closeBtn);

			// ── Log scroll view ────────────────────────────────────────────────
			_scroll = new ScrollView(_activity)
			{
				LayoutParameters = new LinearLayout.LayoutParams(
					ViewGroup.LayoutParams.MatchParent, 0, 1f)
			};

			_logView = new TextView(_activity)
			{
				LayoutParameters = new ViewGroup.LayoutParams(
					ViewGroup.LayoutParams.MatchParent,
					ViewGroup.LayoutParams.WrapContent)
			};
			_logView.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 10.5f);
			_logView.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
			_logView.SetTextIsSelectable(true);
			_logView.SetPadding(12, 8, 12, 12);

			_scroll.AddView(_logView);
			root.AddView(header);
			root.AddView(_scroll);

			_panel = root;
		}

		Button MakeHeaderBtn(string label, Action onClick)
		{
			var btn = new Button(_activity)
			{
				Text = label,
				LayoutParameters = new LinearLayout.LayoutParams(
					ViewGroup.LayoutParams.WrapContent,
					ViewGroup.LayoutParams.WrapContent)
			};
			btn.SetTextColor(Color.White);
			btn.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 13f);
			btn.SetBackgroundColor(Color.ParseColor("#444455"));
			btn.SetPadding(16, 4, 16, 4);
			btn.Click += (_, _) => onClick();
			return btn;
		}

		// ── Show / Hide ────────────────────────────────────────────────────────
		public void ShowPanel()
		{
			_panelVisible = true;
			_root?.BringToFront();
			_panel.Visibility   = ViewStates.Visible;
			_bubble.Visibility  = ViewStates.Gone;

			// Rebuild the full colored log
			RebuildLog();
			_scroll.Post(() => _scroll.FullScroll(FocusSearchDirection.Down));
		}

		public void HidePanel()
		{
			_panelVisible = false;
			_panel.Visibility  = ViewStates.Gone;
			_bubble.Visibility = ViewStates.Visible;
		}

		// ── Log rendering ──────────────────────────────────────────────────────
		void RebuildLog()
		{
			var entries = DevConsole.GetEntries();
			var span    = new SpannableStringBuilder();
			foreach (var e in entries)
				AppendToSpan(span, e);
			_logView.SetText(span, TextView.BufferType.Spannable);
		}

		void AppendEntry(LogEntry entry)
		{
			RebuildLog();
			_scroll.Post(() => _scroll.FullScroll(FocusSearchDirection.Down));
		}

		static void AppendToSpan(SpannableStringBuilder span, LogEntry entry)
		{
			var color = entry.Level switch
			{
				LogLevel.Error   => ColError,
				LogLevel.Warn    => ColWarn,
				LogLevel.Verbose => ColVerbose,
				_                => ColInfo,
			};

			var line  = entry.Format() + "\n";
			int start = span.Length();
			span.Append(line);
			int end = span.Length();
			span.SetSpan(
				new global::Android.Text.Style.ForegroundColorSpan(color),
				start, end,
				SpanTypes.ExclusiveExclusive);
		}
	}

	// ── Crash helper (replaces CrashLogActivity) ──────────────────────────────
	/// <summary>
	/// On crash: log the exception then open the panel automatically.
	/// The overlay is already on screen so we just show it.
	/// </summary>
	public static class CrashHelper
	{
		static DebugOverlay _overlay;

		public static void SetOverlay(DebugOverlay overlay) => _overlay = overlay;

		public static void Handle(Activity activity, Exception ex)
		{
			var msg = $"{ex.GetType().FullName}: {ex.Message}";
			if (ex.InnerException != null)
				msg += $"\nInner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
			msg += $"\n\n{ex.StackTrace}";

			DevConsole.Error("CRASH", msg);

			// sdcard fallback
			try { System.IO.File.WriteAllText("/sdcard/openra_crash.txt", DevConsole.GetAllText()); } catch { }

			// Show the panel on the UI thread automatically so crash is immediately visible
			activity.RunOnUiThread(() =>
			{
				_overlay?.ShowPanel();
			});
		}
	}
}
