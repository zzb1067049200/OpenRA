#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 *
 * Android port adapted from iillaa/OpenRA-android-d2k (GPLv3).
 */
#endregion

using System;
using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using OpenRA.Platforms.Android;
using OpenRA.Primitives;

namespace OpenRA.Android
{
	/// <summary>
	/// On-screen touch control bar (Phase 3). Sits as a transparent overlay on top of the
	/// SurfaceView; only the buttons consume touches, the empty area passes through to the
	/// game. Provides reliable alternatives to the gesture-only scheme:
	///   - Zoom out / Zoom in  (synthetic Ctrl+Scroll at the cursor)
	///   - Right-click / command (synthetic right Down+Up at the last cursor position)
	///   - Box-select toggle (single-finger drag draws a selection box instead of panning)
	///   - Menu / Escape (pause menu)
	///   - Collapse / expand the bar so it never obstructs the battlefield.
	/// </summary>
	public class TouchControls
	{
		static readonly Color BarBg       = Color.ParseColor("#bb0d0d14");
		static readonly Color BtnBg       = Color.ParseColor("#cc1f2433");
		static readonly Color BtnActive   = Color.ParseColor("#cc2a6f4f");
		static readonly Color BtnText     = Color.ParseColor("#e8eefc");
		static readonly Color BtnBorder   = Color.ParseColor("#553b4a66");

		readonly Activity _activity;
		readonly AndroidPlatformWindow _window;

		ViewGroup _root;
		LinearLayout _bar;
		Button _boxBtn;

		public TouchControls(Activity activity, AndroidPlatformWindow window)
		{
			_activity = activity;
			_window = window;
		}

		public static TouchControls Attach(Activity activity, AndroidPlatformWindow window)
		{
			var tc = new TouchControls(activity, window);
			tc.Build();
			return tc;
		}

		void Build()
		{
			var dm = _activity.Resources.DisplayMetrics;

			_root = new FrameLayout(_activity)
			{
				LayoutParameters = new ViewGroup.LayoutParams(
					ViewGroup.LayoutParams.MatchParent,
					ViewGroup.LayoutParams.MatchParent)
			};
			// Transparent root: do NOT set a background and do NOT make it clickable, so touches
			// on empty areas fall through to the SurfaceView below (the game keeps receiving input).
			_root.Elevation = 900f;

			_bar = new LinearLayout(_activity)
			{
				Orientation = Orientation.Horizontal,
				LayoutParameters = new FrameLayout.LayoutParams(
					ViewGroup.LayoutParams.WrapContent,
					ViewGroup.LayoutParams.WrapContent,
					GravityFlags.Bottom | GravityFlags.CenterHorizontal)
				{
					BottomMargin = (int)(18 * dm.Density),
				}
			};
			_bar.SetBackgroundColor(BarBg);
			_bar.SetPadding((int)(10 * dm.Density), (int)(8 * dm.Density), (int)(10 * dm.Density), (int)(8 * dm.Density));

			var size = (int)(52 * dm.Density);

			_bar.AddView(MakeBtn("－", "Zoom out", size, () =>
			{
				var p = CursorOrCenter();
				_window.InjectMouse(MouseInputEvent.Scroll, MouseButton.None, p, new int2(0, -34), Modifiers.Ctrl, 0);
			}));

			_bar.AddView(MakeBtn("＋", "Zoom in", size, () =>
			{
				var p = CursorOrCenter();
				_window.InjectMouse(MouseInputEvent.Scroll, MouseButton.None, p, new int2(0, 34), Modifiers.Ctrl, 0);
			}));

			_boxBtn = MakeBtn("▢", "Box select (drag to select units)", size, () =>
			{
				var on = !_window.BoxSelectMode;
				_window.SetBoxSelectMode(on);
				UpdateBoxBtn(on);
				Toast.MakeText(_activity, on ? "框选模式：单指拖拽选单位" : "框选模式关闭", ToastLength.Short)?.Show();
			});
			_bar.AddView(_boxBtn);

			_bar.AddView(MakeBtn("⛯", "Right-click / command", size, () =>
			{
				var p = CursorOrCenter();
				_window.InjectMouse(MouseInputEvent.Down, MouseButton.Right, p, int2.Zero, Modifiers.None, 1);
				_window.InjectMouse(MouseInputEvent.Up, MouseButton.Right, p, int2.Zero, Modifiers.None, 1);
			}));

			_bar.AddView(MakeBtn("≡", "Menu (Esc)", size, () =>
			{
				_window.InjectKey(OpenRA.Keycode.ESCAPE);
			}));

			_bar.AddView(MakeBtn("▽", "Hide controls", size, () =>
			{
				_bar.Visibility = ViewStates.Gone;
				_restore.Visibility = ViewStates.Visible;
			}));

			// Tiny restore tab shown when the bar is collapsed.
			_restore = MakeBtn("⚙", "Show controls", size, () =>
			{
				_bar.Visibility = ViewStates.Visible;
				_restore.Visibility = ViewStates.Gone;
			});
			_restore.Visibility = ViewStates.Gone;
			var rp = new FrameLayout.LayoutParams(size, size, GravityFlags.Bottom | GravityFlags.Right)
			{
				BottomMargin = (int)(18 * dm.Density),
				RightMargin = (int)(18 * dm.Density),
			};
			_restore.LayoutParameters = rp;

			_root.AddView(_bar);
			_root.AddView(_restore);

			_activity.AddContentView(_root, new ViewGroup.LayoutParams(
				ViewGroup.LayoutParams.MatchParent,
				ViewGroup.LayoutParams.MatchParent));

			_root.BringToFront();

			DevConsole.Info("TouchControls", "Control bar attached. 2-finger pinch=zoom, 2-finger tap=right-click.");
		}

		View _restore;

		Button MakeBtn(string label, string hint, int size, Action onClick)
		{
			var btn = new Button(_activity)
			{
				Text = label,
				LayoutParameters = new LinearLayout.LayoutParams(size, size)
				{
					LeftMargin = (int)(5 * _activity.Resources.DisplayMetrics.Density),
					RightMargin = (int)(5 * _activity.Resources.DisplayMetrics.Density),
				}
			};
			btn.SetTextColor(BtnText);
			btn.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 22f);
			btn.SetBackgroundColor(BtnBg);
			btn.SetPadding(0, 0, 0, 0);
			btn.TooltipText = hint;
			btn.Click += (_, _) => onClick();
			return btn;
		}

		void UpdateBoxBtn(bool active)
		{
			if (_boxBtn == null) return;
			_boxBtn.SetBackgroundColor(active ? BtnActive : BtnBg);
		}

		// Where a synthetic click should land: the last cursor position, or screen center if none yet.
		int2 CursorOrCenter()
		{
			var p = _window.LastCursorPos;
			if (p == int2.Zero)
			{
				var s = _window.EffectiveWindowSize;
				p = new int2(s.Width / 2, s.Height / 2);
			}
			return p;
		}

		public void BringToFront() => _root?.BringToFront();
	}
}
