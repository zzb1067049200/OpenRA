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
using System.Collections.Concurrent;
using System.Diagnostics;
using Android.Views;
using OpenRA;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Android
{
	// Translates Android MotionEvents (multi-touch and hardware/Bluetooth mouse) into OpenRA MouseInputs.
	//
	// Touch model (phone-friendly, redesigned for Phase 3):
	//   - Single tap            = Left click (Select unit / issue primary order / click HUD)
	//   - Single-finger drag    = Map panning (smooth 1:1 camera navigation across the battlefield)
	//   - Two-finger pinch      = Zoom in/out (natural gesture, does not compete with box select)
	//   - Two-finger quick tap  = Right click (open building command menu / issue context order)
	//   - Two/three-finger drag = Unit box-selection (draws green box to select multiple units)
	//   - Box-select toggle     = When enabled (on-screen button), a single-finger drag draws the
	//                             selection box instead of panning, so one hand can box-select.
	//
	// Mouse model (hardware / Bluetooth mouse):
	//   - Left/right/middle buttons map directly and instantly to MouseButton events (no touch delays).
	//   - Hover motion = Move with no button held (updates cursor position and enables edge scrolling).
	//   - Scroll wheel = Scroll event (zooms in/out).
	sealed class AndroidInput
	{
		readonly ConcurrentQueue<PendingInput> pending = new();

		enum TouchGestureState { None, PotentialTap, Panning, BoxSelecting, Pinching, TwoFinger }
		TouchGestureState touchState = TouchGestureState.None;

		// Primary finger state
		int2 primaryDownPos;
		int2 primaryLastPos;
		long primaryDownTimeMs;

		// Box selection state
		int2 boxStartPos;
		int2 boxLastPos;

		// Two-finger gesture disambiguation (pinch vs box-drag vs tap)
		long twoFingerDownTimeMs;
		int2 twoFingerCentroid;
		float twoFingerStartDist;
		bool twoFingerMoved;
		TouchGestureState twoFingerClass = TouchGestureState.None;

		bool ignoreTouchesUntilAllUp;

		const int TouchSlopPx = 25;
		const int PinchSlopPx = 18;
		const int TwoFingerTapMaxMs = 250;

		// Physical-mouse state tracking
		int lastMouseButtonState;
		int2 lastMousePos;

		const int MouseBtnPrimary = 1;   // left
		const int MouseBtnSecondary = 2; // right
		const int MouseBtnTertiary = 4;  // middle

		// ── Injected input (from on-screen control bar) ───────────────────────────
		readonly ConcurrentQueue<MouseInput> injectedMouse = new();
		readonly ConcurrentQueue<KeyInput> injectedKeys = new();

		// When true, a single-finger drag draws a selection box instead of panning.
		public bool BoxSelectMode { get; private set; }

		// Last cursor position in logical (effective-window) coordinates. Used by the
		// on-screen "right click / command" button to issue an order where the finger was.
		public int2 LastCursorPos { get; private set; }

		struct PendingInput
		{
			public MotionEventActions Action;
			public float X;
			public float Y;
			public float X2;
			public float Y2;
			public int PointerCount;
			public int PointerId;
			public long TimestampMs;
			public int ButtonState;
			public int ScrollDelta;
			public bool IsMouse;
		}

		public void Enqueue(MotionEvent e, Size windowSize)
		{
			var action = e.ActionMasked;
			var index = e.ActionIndex;
			var count = e.PointerCount;

			var pi = new PendingInput
			{
				Action = action,
				PointerCount = count,
				PointerId = e.GetPointerId(index),
				TimestampMs = e.EventTime,
				IsMouse = false
			};

			if (count >= 2)
			{
				pi.X = e.GetX(0);
				pi.Y = e.GetY(0);
				pi.X2 = e.GetX(1);
				pi.Y2 = e.GetY(1);
			}
			else
			{
				pi.X = e.GetX(0);
				pi.Y = e.GetY(0);
			}

			pending.Enqueue(pi);
		}

		public void EnqueueMouse(MotionEvent e, Size windowSize)
		{
			var action = e.ActionMasked;
			var pos = new int2((int)e.GetX(0), (int)e.GetY(0));

			if (action == MotionEventActions.Scroll)
			{
				var dy = (int)(e.GetAxisValue(Axis.Vscroll) * -10);
				var dx = (int)(e.GetAxisValue(Axis.Hscroll) * 10);
				pending.Enqueue(new PendingInput
				{
					Action = action,
					X = pos.X,
					Y = pos.Y,
					PointerId = -1,
					TimestampMs = e.EventTime,
					IsMouse = true,
					ButtonState = (int)e.ButtonState,
					ScrollDelta = dy != 0 ? dy : dx
				});
			}
			else
			{
				pending.Enqueue(new PendingInput
				{
					Action = action,
					X = pos.X,
					Y = pos.Y,
					PointerId = -1,
					TimestampMs = e.EventTime,
					IsMouse = true,
					ButtonState = (int)e.ButtonState
				});
			}
		}

		// Inject a synthetic mouse event from the on-screen control bar (runs on the game thread via PumpInput).
		public void InjectMouse(MouseInputEvent ev, MouseButton btn, int2 pos, int2 delta = default, Modifiers mods = Modifiers.None, int multiTap = 0)
		{
			injectedMouse.Enqueue(new MouseInput(ev, btn, pos, delta, mods, multiTap));
		}

		// Inject a synthetic key press (Down then Up) from the on-screen control bar.
		public void InjectKey(OpenRA.Keycode kc)
		{
			injectedKeys.Enqueue(new KeyInput { Event = KeyInputEvent.Down, Key = kc, Modifiers = Modifiers.None });
			injectedKeys.Enqueue(new KeyInput { Event = KeyInputEvent.Up, Key = kc, Modifiers = Modifiers.None });
		}

		public void SetBoxSelectMode(bool on) => BoxSelectMode = on;

		public void PumpInput(IInputHandler inputHandler, Size windowSize, Size surfaceSize, float scale)
		{
			// Drain injected (on-screen button) input first.
			while (injectedMouse.TryDequeue(out var mi))
			{
				LastCursorPos = mi.Location;
				inputHandler.OnMouseInput(mi);
			}

			while (injectedKeys.TryDequeue(out var ki))
				inputHandler.OnKeyInput(ki);

			var scaleX = (surfaceSize.Width > 0 && windowSize.Width > 0) ? (float)windowSize.Width / surfaceSize.Width : 1f;
			var scaleY = (surfaceSize.Height > 0 && windowSize.Height > 0) ? (float)windowSize.Height / surfaceSize.Height : 1f;

			while (pending.TryDequeue(out var p))
			{
				var pos = new int2((int)(p.X * scaleX), (int)(p.Y * scaleY));

				if (p.IsMouse)
				{
					HandleMouse(inputHandler, p, pos);
					continue;
				}

				var pos2 = p.PointerCount >= 2 ? new int2((int)(p.X2 * scaleX), (int)(p.Y2 * scaleY)) : int2.Zero;

				switch (p.Action)
				{
					case MotionEventActions.Down:
						ignoreTouchesUntilAllUp = false;
						primaryDownPos = pos;
						primaryLastPos = pos;
						primaryDownTimeMs = p.TimestampMs;
						LastCursorPos = pos;

						if (BoxSelectMode)
						{
							// Single-finger drag draws a selection box instead of panning.
							boxStartPos = pos;
							boxLastPos = pos;
							touchState = TouchGestureState.BoxSelecting;
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Left, boxStartPos, int2.Zero, Modifiers.None, 1));
						}
						else
						{
							touchState = TouchGestureState.PotentialTap;
						}
						break;

					case MotionEventActions.PointerDown:
						if (ignoreTouchesUntilAllUp)
							break;

						if (p.PointerCount == 2)
						{
							// Enter two-finger mode. Finalize any single-finger gesture first.
							if (touchState == TouchGestureState.Panning)
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, primaryLastPos, int2.Zero, Modifiers.None, 1));
							else if (touchState == TouchGestureState.BoxSelecting)
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, boxLastPos, int2.Zero, Modifiers.None, 1));

							twoFingerDownTimeMs = p.TimestampMs;
							twoFingerCentroid = (pos + pos2) / 2;
							twoFingerStartDist = (pos - pos2).Length;
							twoFingerMoved = false;
							twoFingerClass = TouchGestureState.None;
							touchState = TouchGestureState.TwoFinger;
						}
						else if (p.PointerCount >= 3)
						{
							// Three-finger drag = box selection (left drag).
							if (touchState == TouchGestureState.Panning)
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, primaryLastPos, int2.Zero, Modifiers.None, 1));
							else if (touchState == TouchGestureState.TwoFinger)
								twoFingerClass = TouchGestureState.None; // abandon two-finger; fall through to box

							touchState = TouchGestureState.BoxSelecting;
							boxStartPos = (pos + pos2) / 2;
							boxLastPos = pos2;
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Left, boxStartPos, int2.Zero, Modifiers.None, 1));
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.Left, boxLastPos, int2.Zero, Modifiers.None, 0));
						}

						break;

					case MotionEventActions.Move:
						if (ignoreTouchesUntilAllUp)
							break;

						if (p.PointerCount == 2 && touchState == TouchGestureState.TwoFinger)
						{
							var centroid = (pos + pos2) / 2;
							var dist = (pos - pos2).Length;

							if (twoFingerClass == TouchGestureState.None)
							{
								if (Math.Abs(dist - twoFingerStartDist) > PinchSlopPx)
								{
									twoFingerClass = TouchGestureState.Pinching;
									twoFingerMoved = true;
									LastPinchDist = dist;
									LastCursorPos = centroid;
								}
								else if ((centroid - twoFingerCentroid).Length > TouchSlopPx)
								{
									// Two-finger drag (fingers translate together) = box select.
									twoFingerClass = TouchGestureState.BoxSelecting;
									twoFingerMoved = true;
									boxStartPos = twoFingerCentroid;
									boxLastPos = centroid;
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Left, boxStartPos, int2.Zero, Modifiers.None, 1));
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.Left, boxLastPos, int2.Zero, Modifiers.None, 0));
									LastCursorPos = centroid;
								}
							}
							else if (twoFingerClass == TouchGestureState.Pinching)
							{
								var d = (int)(dist - LastPinchDist);
								if (Math.Abs(d) >= 3)
								{
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Scroll, MouseButton.None, centroid, new int2(0, d), Modifiers.Ctrl, 0));
									LastPinchDist = dist;
								}
								LastCursorPos = centroid;
							}
							else if (twoFingerClass == TouchGestureState.BoxSelecting)
							{
								boxLastPos = centroid;
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.Left, boxLastPos, int2.Zero, Modifiers.None, 0));
								LastCursorPos = centroid;
							}
						}
						else if (p.PointerCount >= 3 && touchState == TouchGestureState.BoxSelecting)
						{
							boxLastPos = pos2;
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.Left, boxLastPos, int2.Zero, Modifiers.None, 0));
						}
						else if (p.PointerCount == 1)
						{
							LastCursorPos = pos;
							if (BoxSelectMode && touchState == TouchGestureState.BoxSelecting)
							{
								boxLastPos = pos;
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.Left, boxLastPos, int2.Zero, Modifiers.None, 0));
							}
							else if (touchState == TouchGestureState.PotentialTap)
							{
								if ((pos - primaryDownPos).Length > TouchSlopPx)
								{
									touchState = TouchGestureState.Panning;
									// Synchronize Viewport.LastMousePos to primaryDownPos FIRST to eliminate map jump!
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.None, primaryDownPos, int2.Zero, Modifiers.None, 0));
									// Initiate standard scroll via Right Down
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Right, primaryDownPos, int2.Zero, Modifiers.None, 1));
									// Begin continuous scroll to current finger pos
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.Right, pos, int2.Zero, Modifiers.None, 0));
									primaryLastPos = pos;
								}
							}
							else if (touchState == TouchGestureState.Panning)
							{
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.Right, pos, int2.Zero, Modifiers.None, 0));
								primaryLastPos = pos;
							}
							else if (BoxSelectMode && touchState == TouchGestureState.PotentialTap)
							{
								// Box mode but finger hasn't moved: keep waiting (tap will select on Up).
							}
						}

						break;

					case MotionEventActions.PointerUp:
						if (touchState == TouchGestureState.TwoFinger)
						{
							// First finger lifted during a two-finger gesture.
							if (twoFingerClass == TouchGestureState.Pinching)
							{
								ignoreTouchesUntilAllUp = true;
							}
							else if (twoFingerClass == TouchGestureState.BoxSelecting)
							{
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, boxLastPos, int2.Zero, Modifiers.None, 1));
								ignoreTouchesUntilAllUp = true;
							}
							else
							{
								// Quick two-finger tap with no movement = right click (context / command).
								var duration = p.TimestampMs - twoFingerDownTimeMs;
								if (duration <= TwoFingerTapMaxMs && !twoFingerMoved)
								{
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Right, twoFingerCentroid, int2.Zero, Modifiers.None, 1));
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, twoFingerCentroid, int2.Zero, Modifiers.None, 1));
									LastCursorPos = twoFingerCentroid;
								}
								ignoreTouchesUntilAllUp = true;
							}
							touchState = TouchGestureState.None;
							twoFingerClass = TouchGestureState.None;
						}
						else if (touchState == TouchGestureState.BoxSelecting)
						{
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, boxLastPos, int2.Zero, Modifiers.None, 1));
							touchState = TouchGestureState.None;
							ignoreTouchesUntilAllUp = true;
						}

						break;

					case MotionEventActions.Up:
						if (!ignoreTouchesUntilAllUp && touchState == TouchGestureState.PotentialTap)
						{
							var tapCount = MultiTapDetection.DetectFromMouse(0, primaryDownPos);
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Left, primaryDownPos, int2.Zero, Modifiers.None, tapCount));
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, primaryDownPos, int2.Zero, Modifiers.None, tapCount));
							LastCursorPos = primaryDownPos;
						}
						else if (!ignoreTouchesUntilAllUp && touchState == TouchGestureState.BoxSelecting && BoxSelectMode)
						{
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, boxLastPos, int2.Zero, Modifiers.None, 1));
							LastCursorPos = boxLastPos;
						}
						else if (touchState == TouchGestureState.Panning)
						{
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
						}
						else if (touchState == TouchGestureState.BoxSelecting)
						{
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, boxLastPos, int2.Zero, Modifiers.None, 1));
						}

						touchState = TouchGestureState.None;
						ignoreTouchesUntilAllUp = false;
						twoFingerClass = TouchGestureState.None;

						// Send a neutral cursor move to screen center so edge scrolling does not linger after lifting finger
						inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.None, new int2(windowSize.Width / 2, windowSize.Height / 2), int2.Zero, Modifiers.None, 0));
						break;

					case MotionEventActions.Cancel:
						if (touchState == TouchGestureState.Panning)
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
						else if (touchState == TouchGestureState.BoxSelecting)
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, boxLastPos, int2.Zero, Modifiers.None, 1));

						touchState = TouchGestureState.None;
						ignoreTouchesUntilAllUp = false;
						twoFingerClass = TouchGestureState.None;
						inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.None, new int2(windowSize.Width / 2, windowSize.Height / 2), int2.Zero, Modifiers.None, 0));
						break;
				}
			}
		}

		// Tracks the previous pinch radius so we emit incremental scroll deltas (mirrors the
		// original four-finger pinch behaviour, which used p.X2 as the average spread).
		float LastPinchDist;

		void HandleMouse(IInputHandler inputHandler, in PendingInput p, int2 pos)
		{
			lastMousePos = pos;

			if (p.Action == MotionEventActions.Scroll)
			{
				inputHandler.OnMouseInput(new MouseInput(
					MouseInputEvent.Scroll, MouseButton.None, pos,
					new int2(0, p.ScrollDelta), Modifiers.Ctrl, 0));
				lastMouseButtonState = p.ButtonState;
				return;
			}

			var prev = lastMouseButtonState;
			var curr = p.ButtonState;

			// If Android reports ACTION_DOWN with ButtonState 0, fallback to primary (left) button
			if (p.Action == MotionEventActions.Down && curr == 0)
				curr = MouseBtnPrimary;

			// Fire Down for buttons newly pressed
			if ((curr & MouseBtnPrimary) != 0 && (prev & MouseBtnPrimary) == 0)
			{
				var downTapCount = MultiTapDetection.DetectFromMouse(0, pos);
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Left, pos, int2.Zero, Modifiers.None, downTapCount));
			}

			if ((curr & MouseBtnSecondary) != 0 && (prev & MouseBtnSecondary) == 0)
			{
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
			}

			if ((curr & MouseBtnTertiary) != 0 && (prev & MouseBtnTertiary) == 0)
			{
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Middle, pos, int2.Zero, Modifiers.None, 1));
			}

			// Fire Up for buttons that were released
			if ((prev & MouseBtnPrimary) != 0 && (curr & MouseBtnPrimary) == 0)
			{
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, pos, int2.Zero, Modifiers.None, MultiTapDetection.InfoFromMouse(0)));
			}

			if ((prev & MouseBtnSecondary) != 0 && (curr & MouseBtnSecondary) == 0)
			{
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
			}

			if ((prev & MouseBtnTertiary) != 0 && (curr & MouseBtnTertiary) == 0)
			{
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Middle, pos, int2.Zero, Modifiers.None, 1));
			}

			// If ACTION_UP arrives, ensure all pressed buttons are released cleanly
			if (p.Action == MotionEventActions.Up)
			{
				if ((curr & MouseBtnPrimary) != 0 || (prev & MouseBtnPrimary) != 0)
					inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, pos, int2.Zero, Modifiers.None, MultiTapDetection.InfoFromMouse(0)));

				if ((curr & MouseBtnSecondary) != 0 || (prev & MouseBtnSecondary) != 0)
					inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));

				if ((curr & MouseBtnTertiary) != 0 || (prev & MouseBtnTertiary) != 0)
					inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Middle, pos, int2.Zero, Modifiers.None, 1));

				curr = 0;
			}

			// Determine held button for dragging
			var heldButton = MouseButton.None;
			var tapCount = 0;
			if ((curr & MouseBtnPrimary) != 0)
				heldButton = MouseButton.Left;
			else if ((curr & MouseBtnSecondary) != 0)
				heldButton = MouseButton.Right;
			else if ((curr & MouseBtnTertiary) != 0)
				heldButton = MouseButton.Middle;

			if (heldButton == MouseButton.Left)
				tapCount = MultiTapDetection.InfoFromMouse(0);

			// Forward hover and drag moves (updates cursor position and drives edge scrolling)
			if (p.Action == MotionEventActions.HoverMove || p.Action == MotionEventActions.Move)
			{
				inputHandler.OnMouseInput(new MouseInput(
					MouseInputEvent.Move, heldButton, pos, int2.Zero, Modifiers.None, tapCount));
			}

			lastMouseButtonState = curr;
		}
	}
}
