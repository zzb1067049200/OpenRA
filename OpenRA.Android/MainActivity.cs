#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 *
 * Android port adapted from iillaa/OpenRA-android-d2k (GPLv3). The
 * OpenRA.Platforms.Android EGL backend, this Activity, and the engine
 * IsAndroid() changes are derived from that fork.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using OpenRA.Platforms.Android;

// OpenRA downloads freeware game content from official mirrors (openra.net) on first run,
// so the app needs network access. Declared at assembly level so .NET Android merges it
// into the final AndroidManifest.xml.
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]
[assembly: UsesPermission(Android.Manifest.Permission.AccessNetworkState)]
[assembly: UsesPermission(Android.Manifest.Permission.ReadExternalStorage)]
[assembly: UsesPermission(Android.Manifest.Permission.WriteExternalStorage)]
[assembly: UsesPermission(Android.Manifest.Permission.ManageExternalStorage)]
[assembly: UsesPermission("android.permission.READ_MEDIA_AUDIO")]
[assembly: UsesPermission("android.permission.READ_MEDIA_VIDEO")]

namespace OpenRA.Android
{
	[Activity(
		Label = "OpenRA",
		Icon = "@android:drawable/ic_menu_gallery",
		MainLauncher = true,
		Theme = "@android:style/Theme.DeviceDefault.NoActionBar.Fullscreen",
		ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard | ConfigChanges.Navigation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.UiMode | ConfigChanges.Density | ConfigChanges.FontScale,
		ScreenOrientation = ScreenOrientation.Landscape)]
	public class MainActivity : Activity
	{
		const string Tag = "OpenRA";
		OpenRASurfaceView surfaceView;
		AndroidPlatformWindow window;

		string engineDir;
		string supportDir;
		static volatile bool engineStarted;

		// Mod chosen on the launcher chooser. Engine start is deferred until the user picks one,
		// so all three built-in mods (ra / cnc / d2k) are reachable from the menu.
		string selectedMod;

		protected override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			// Global crash diagnostics: surface ANY unhandled exception (launch-time and runtime
			// ones the game-thread try/catch misses) on screen and to a file, so the user can
			// report it without adb. CI only validates compile/package, never a real device run.
			AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
			{
				ShowFatal(e.Exception);
				e.Handled = true;
			};
			AppDomain.CurrentDomain.UnhandledException += (s, e) =>
			{
				if (e.ExceptionObject is Exception ex)
					ShowFatal(ex);
			};

			try
			{

			// Extract engine assets (glsl/, mods/, VERSION) from the APK to internal storage on first
			// launch, then point the engine at that directory. Subsequent launches skip the extraction.
			engineDir = ExtractAssets();

			// Scoped-storage-friendly support dir for user data (settings, maps, logs, content).
			supportDir = Path.Combine(GetExternalFilesDir(null).AbsolutePath, "Support") + Path.DirectorySeparatorChar;
			Directory.CreateDirectory(supportDir);

			// Request storage permission if needed, then check for Download content
			CheckAndRequestStoragePermissions();
			SyncCustomContentFromDownloads(supportDir);

			// Wire platform logs → in-app DevConsole first so all initialization logs are captured
			AndroidPlatform.PlatformLogger      = (tag, msg) => DevConsole.Info(tag, msg);
			AndroidPlatform.PlatformErrorLogger = (tag, msg) => DevConsole.Error(tag, msg);

			// Bridge OpenRA in-game logs to in-app DevConsole
			OpenRA.Log.OnLogMessage = (channel, msg) =>
			{
				if (channel == "sound" || channel == "graphics" || channel == "perf" || channel == "server" || channel == "debug" || channel == "order")
					DevConsole.Info(channel, msg);
			};

			// Pre-load and register native libraries (FreeType, OpenAL) with verbose logging
			AndroidPlatform.Initialize(this);

			var metrics = Resources.DisplayMetrics;
			window = new AndroidPlatformWindow(metrics.WidthPixels, metrics.HeightPixels);
			AndroidPlatform.SetWindow(window);

			surfaceView = new OpenRASurfaceView(this, window);
			window.HostView = surfaceView;
			window.KeyboardDrainAction = ih => surfaceView.DrainKeyboardInput(ih);
			SetContentView(surfaceView);

			// Attach floating debug bubble overlay AFTER SetContentView so it sits on top of SurfaceView
			var overlay = DebugOverlay.Attach(this);
			CrashHelper.SetOverlay(overlay);
			overlay.BringToFront();

			// Start the engine loop once the user picks a mod. The window's WaitForSurfaceAndInitializeGl
			// handles the Android surface churn (create->destroy->create during layout) by retrying.
			ShowModChooser();
			}
			catch (Exception ex)
			{
				ShowFatal(ex);
			}
		}

		static bool crashDialogShown;

		// Shows a fatal error on screen + writes it to openra_crash.txt (internal files dir and
		// /sdcard fallback) so the user can copy it and report without adb.
		void ShowFatal(Exception ex)
		{
			var msg = $"{ex.GetType().FullName}: {ex.Message}";
			if (ex.InnerException != null)
				msg += $"\nInner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
			msg += $"\n\n{ex.StackTrace}";
			global::Android.Util.Log.Error(Tag, "FATAL: " + msg);

			try { System.IO.File.WriteAllText(System.IO.Path.Combine(FilesDir.AbsolutePath, "openra_crash.txt"), msg); } catch { }
			try { System.IO.File.WriteAllText("/sdcard/openra_crash.txt", msg); } catch { }

			if (crashDialogShown)
				return;
			crashDialogShown = true;

			RunOnUiThread(() =>
			{
				try
				{
					var b = new AlertDialog.Builder(this);
					b.SetTitle("OpenRA 启动失败 (Fatal)");
					b.SetMessage(msg.Length > 3000 ? msg.Substring(0, 3000) : msg);
					b.SetPositiveButton("复制并退出", (sender, args) =>
					{
						try
						{
							var cm = (ClipboardManager)GetSystemService(Context.ClipboardService);
							cm.PrimaryClip = ClipData.NewPlainText("openra_crash", msg);
						}
						catch { }
						Finish();
					});
					b.SetCancelable(false);
					b.Show();
				}
				catch { }
			});
		}

		void ShowModChooser()
		{
			var labels = new[] { "Red Alert (ra)", "Tiberian Dawn (cnc)", "Dune 2000 (d2k)" };
			var mods = new[] { "ra", "cnc", "d2k" };
			var builder = new AlertDialog.Builder(this);
			builder.SetTitle("选择 Mod");
			builder.SetCancelable(false);
			builder.SetItems(labels, (sender, args) =>
			{
				selectedMod = mods[args.Which];
				StartEngineOnce();
			});
			builder.Show();
		}

		void CheckAndRequestStoragePermissions()
		{
			try
			{
				if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
				{
					if (!global::Android.OS.Environment.IsExternalStorageManager)
					{
						DevConsole.Info("Permission", "Storage access not granted. Launching Manage App All Files Access settings...");
						try
						{
							var uri = global::Android.Net.Uri.FromParts("package", PackageName, null);
							var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionManageAppAllFilesAccessPermission, uri);
							StartActivity(intent);
						}
						catch
						{
							var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionManageAllFilesAccessPermission);
							StartActivity(intent);
						}
					}
				}
				else if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
				{
					if (CheckSelfPermission(global::Android.Manifest.Permission.ReadExternalStorage) != Permission.Granted)
					{
						RequestPermissions(new[]
						{
							global::Android.Manifest.Permission.ReadExternalStorage,
							global::Android.Manifest.Permission.WriteExternalStorage
						}, 1001);
					}
				}
			}
			catch (Exception ex)
			{
				DevConsole.Warn("Permission", $"Could not request storage permission: {ex.Message}");
			}
		}

		void SyncCustomContentFromDownloads(string targetSupportDir)
		{
			try
			{
				var downloadPath = global::Android.OS.Environment.GetExternalStoragePublicDirectory(global::Android.OS.Environment.DirectoryDownloads)?.AbsolutePath
					?? "/storage/emulated/0/Download";

				var candidateDirs = new[]
				{
					Path.Combine(downloadPath, "d2k"),
					Path.Combine(downloadPath, "D2K"),
					"/storage/emulated/0/Download/d2k",
					"/sdcard/Download/d2k"
				};

				string d2kDir = null;
				foreach (var dir in candidateDirs)
				{
					if (Directory.Exists(dir))
					{
						d2kDir = dir;
						break;
					}
				}

				if (d2kDir == null)
					return;

				var destMusic = Path.Combine(targetSupportDir, "Content", "d2k", "v3", "Music");
				var destMovies = Path.Combine(targetSupportDir, "Content", "d2k", "v3", "Movies");
				var destMaps = Path.Combine(targetSupportDir, "maps", "d2k", "{DEV_VERSION}");
				int importedCount = 0;

				void ImportFilesFrom(string sourceDir)
				{
					if (!Directory.Exists(sourceDir))
						return;

					foreach (var file in Directory.GetFiles(sourceDir))
					{
						var name = Path.GetFileName(file);
						if (name.EndsWith(".aud", StringComparison.OrdinalIgnoreCase))
						{
							Directory.CreateDirectory(destMusic);
							var target = Path.Combine(destMusic, name);
							if (!File.Exists(target) || new FileInfo(file).Length != new FileInfo(target).Length)
							{
								File.Copy(file, target, true);
								importedCount++;
								DevConsole.Info("Content", $"Imported music track: {name}");
							}
						}
						else if (name.EndsWith(".vqa", StringComparison.OrdinalIgnoreCase))
						{
							Directory.CreateDirectory(destMovies);
							var target = Path.Combine(destMovies, name);
							if (!File.Exists(target) || new FileInfo(file).Length != new FileInfo(target).Length)
							{
								File.Copy(file, target, true);
								importedCount++;
								DevConsole.Info("Content", $"Imported movie cutscene: {name}");
							}
						}
						else if (name.EndsWith(".oramap", StringComparison.OrdinalIgnoreCase))
						{
							Directory.CreateDirectory(destMaps);
							var target = Path.Combine(destMaps, name);
							if (!File.Exists(target) || new FileInfo(file).Length != new FileInfo(target).Length)
							{
								File.Copy(file, target, true);
								importedCount++;
								DevConsole.Info("Content", $"Imported custom map: {name}");
							}
						}
					}
				}

				// 1. Check root d2k folder directly
				ImportFilesFrom(d2kDir);

				// 2. Check Music subfolder
				ImportFilesFrom(Path.Combine(d2kDir, "Music"));
				ImportFilesFrom(Path.Combine(d2kDir, "music"));

				// 3. Check Movies subfolder
				ImportFilesFrom(Path.Combine(d2kDir, "Movies"));
				ImportFilesFrom(Path.Combine(d2kDir, "movies"));

				// 4. Check Maps subfolder
				ImportFilesFrom(Path.Combine(d2kDir, "Maps"));
				ImportFilesFrom(Path.Combine(d2kDir, "maps"));

				if (importedCount > 0)
				{
					DevConsole.Info("Content", $"Successfully imported {importedCount} files from {d2kDir}");
					RunOnUiThread(() =>
					{
						Toast.MakeText(this, $"Imported {importedCount} Dune 2000 files (music/movies/maps)!", ToastLength.Long)?.Show();
					});
				}
			}
			catch (Exception ex)
			{
				DevConsole.Warn("Content", $"Could not sync Download/d2k: {ex.Message}");
			}
		}

		float ComputeDefaultUIScale()
		{
			return 1f;
		}

		// Pause rendering and signal the engine when the app is backgrounded so it stops
		// touching the EGL surface (prevents the black-screen-on-resume crash).
		protected override void OnPause()
		{
			base.OnPause();
			window?.SuspendRendering();
		}

		// Resume rendering when the app returns to the foreground, and sync any newly downloaded content.
		protected override void OnResume()
		{
			base.OnResume();
			window?.ResumeRendering();
			if (!string.IsNullOrEmpty(supportDir))
				SyncCustomContentFromDownloads(supportDir);
		}

		// Called from the SurfaceCallback once the first stable surface is available, and from the
		// mod chooser after the user picks a mod. No-ops until both the surface is ready AND a mod
		// is selected.
		internal void StartEngineOnce()
		{
			if (engineStarted)
				return;
			if (selectedMod == null)
				return;
			engineStarted = true;

			var settingsPath = Path.Combine(supportDir, "settings.yaml");
			var argsList = new List<string>
			{
				$"Engine.EngineDir={engineDir}",
				$"Engine.SupportDir={supportDir}",
				$"Game.Mod={selectedMod}",
				"Game.MouseControlStyle=Classic",
				"Game.MouseScroll=Standard",
				"Game.ViewportEdgeScrollMargin=25"
			};

			if (!File.Exists(settingsPath))
			{
				var uiScale = ComputeDefaultUIScale();
				if (uiScale > 1f)
					argsList.Add($"Graphics.UIScale={uiScale}");
			}

			var args = argsList.ToArray();

			new Thread(() =>
			{
				try
				{
					DevConsole.Info("MainActivity", "Game.InitializeAndRun starting...");
					Game.InitializeAndRun(args);
					DevConsole.Info("MainActivity", "Game.InitializeAndRun returned normally.");
				}
				catch (Exception e)
				{
					global::Android.Util.Log.Error(Tag, $"OpenRA crashed: {e}");
					CrashHelper.Handle(this, e);
				}
				finally
				{
					engineStarted = false;
				}
			})
			{ Name = "OpenRA Main", IsBackground = false }.Start();
		}

		string ExtractAssets()
		{
			var dest = Path.Combine(FilesDir.AbsolutePath, "engine") + Path.DirectorySeparatorChar;
			var marker = Path.Combine(dest, ".extracted");

			if (File.Exists(marker))
				return dest;

			Directory.CreateDirectory(dest);
			CopyAssetDir("glsl", Path.Combine(dest, "glsl"));
			CopyAssetDir("mods", Path.Combine(dest, "mods"));
			CopyAssetFile("VERSION", Path.Combine(dest, "VERSION"));
			CopyAssetFile("global mix database.dat", Path.Combine(dest, "global mix database.dat"));

			// The map directories are excluded from the APK assets (maps are large and not needed
			// for the menu), but MapCache.LoadMaps expects each mod's maps/ folder to exist. Create
			// it for every bundled mod so the main menu can load.
			var modsRoot = Path.Combine(dest, "mods");
			if (Directory.Exists(modsRoot))
				foreach (var modDir in Directory.GetDirectories(modsRoot))
					Directory.CreateDirectory(Path.Combine(modDir, "maps"));

			File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
			global::Android.Util.Log.Info(Tag, $"Extracted engine assets to {dest}");
			return dest;
		}

		void CopyAssetDir(string assetPath, string destDir)
		{
			Directory.CreateDirectory(destDir);
			string[] children;
			try { children = Assets.List(assetPath); }
			catch { return; }

			if (children == null)
				return;

			foreach (var child in children)
			{
				var childAsset = $"{assetPath}/{child}";
				var childDest = Path.Combine(destDir, child);

				// Recurse into directories.
				string[] grandChildren = null;
				try { grandChildren = Assets.List(childAsset); }
				catch { }

				if (grandChildren != null && grandChildren.Length > 0)
					CopyAssetDir(childAsset, childDest);
				else
					CopyAssetFile(childAsset, childDest);
			}
		}

		void CopyAssetFile(string assetPath, string destFile)
		{
			try
			{
				using var input = Assets.Open(assetPath);
				using var output = File.Create(destFile);
				input.CopyTo(output);
			}
			catch (Java.IO.IOException)
			{
				// Some asset entries (e.g. empty dirs) may fail; skip.
			}
		}

		// SurfaceView that owns the Android window surface and forwards touch + keyboard input.
		sealed class OpenRASurfaceView : SurfaceView
		{
			readonly AndroidPlatformWindow window;

			// Keep a strong reference to the callback: Java's AddCallback holds only a weak/global
			// ref, so a purely temporary instance would be collected by the .NET GC and stop firing.
			readonly SurfaceCallback callback;

			readonly MainActivity activity;

			// Buffer for IME-composed text, drained by PumpInput on the game thread.
			readonly System.Collections.Concurrent.ConcurrentQueue<string> textQueue = new();
			readonly System.Collections.Concurrent.ConcurrentQueue<KeyInput> keyQueue = new();

			public OpenRASurfaceView(MainActivity context, AndroidPlatformWindow window)
				: base(context)
			{
				this.window = window;
				this.activity = context;
				callback = new SurfaceCallback(window, context);
				Holder.AddCallback(callback);
				Focusable = true;
				FocusableInTouchMode = true;
			}

			bool lastEventWasMouse;

			bool IsMouseEvent(MotionEvent e)
			{
				var isMouse = e.IsFromSource(InputSourceType.Mouse)
					|| (int)e.GetToolType(0) == 3 // MotionEventToolType.Mouse
					|| ((int)e.Source & (int)InputSourceType.ClassPointer) != 0 && (int)e.GetToolType(0) == 3
					|| e.ButtonState != 0;

				if (isMouse)
				{
					lastEventWasMouse = true;
					return true;
				}

				if (lastEventWasMouse && (e.ActionMasked == MotionEventActions.Up || e.ActionMasked == MotionEventActions.Cancel))
				{
					lastEventWasMouse = false;
					return true;
				}

				lastEventWasMouse = false;
				return false;
			}

			public override bool OnTouchEvent(MotionEvent e)
			{
				if (IsMouseEvent(e))
				{
					window.EnqueueMouseMotion(e);
					return true;
				}

				window.EnqueueMotion(e);
				return true;
			}

			// Hardware mouse / trackball motion is delivered here (not via OnTouchEvent).
			// Detect mouse source and route to the dedicated mouse input path so left/right/
			// middle clicks and hover movement are handled instantly with no long-press delay.
			public override bool OnGenericMotionEvent(MotionEvent e)
			{
				if (IsMouseEvent(e))
				{
					window.EnqueueMouseMotion(e);
					return true;
				}

				// Non-mouse generic motion (e.g. stylus) — fall back to the touch path.
				window.EnqueueMotion(e);
				return true;
			}

			public override bool OnHoverEvent(MotionEvent e)
			{
				if (IsMouseEvent(e))
				{
					window.EnqueueMouseMotion(e);
					return true;
				}

				return base.OnHoverEvent(e);
			}

			// Capture hardware keyboard events (also some IME key events like backspace).
			public override bool DispatchKeyEvent(KeyEvent e)
			{
				// Let the IME handle composition first.
				if (base.DispatchKeyEvent(e))
					return true;

				if (e.Action == KeyEventActions.Down || e.Action == KeyEventActions.Up)
				{
					var ki = new KeyInput
					{
						Event = e.Action == KeyEventActions.Down ? KeyInputEvent.Down : KeyInputEvent.Up,
						Key = MapKeycode(e.KeyCode),
						UnicodeChar = (char)e.UnicodeChar,
						Modifiers = Modifiers.None,
						IsRepeat = e.RepeatCount > 0
					};
					keyQueue.Enqueue(ki);

					// If this key produced a printable character, also send it as text.
					if (e.Action == KeyEventActions.Down && ki.UnicodeChar != 0 && !char.IsControl(ki.UnicodeChar))
						textQueue.Enqueue(ki.UnicodeChar.ToString());
				}

				return true;
			}

			// Provide an InputConnection so the Android IME (soft keyboard) can send composed text.
			public override global::Android.Views.InputMethods.IInputConnection OnCreateInputConnection(global::Android.Views.InputMethods.EditorInfo outAttrs)
			{
				outAttrs.InputType = global::Android.Text.InputTypes.ClassText;
				outAttrs.ImeOptions = (global::Android.Views.InputMethods.ImeFlags)global::Android.Views.InputMethods.ImeAction.None;
				return new OpenRAInputConnection(this, true);
			}

			// Called by PumpInput on the game thread to drain queued text/key events.
			public void DrainKeyboardInput(IInputHandler inputHandler)
			{
				while (keyQueue.TryDequeue(out var ki))
					inputHandler.OnKeyInput(ki);
				while (textQueue.TryDequeue(out var text))
					inputHandler.OnTextInput(text);
			}

			static OpenRA.Keycode MapKeycode(global::Android.Views.Keycode kc)
			{
				// Map common Android keycodes to OpenRA's Keycode enum (which mirrors SDL2).
				// Uses integer comparison to avoid enum-naming differences across .NET Android versions.
				var v = (int)kc;
				return v switch
				{
					66 => OpenRA.Keycode.RETURN,     // KEYCODE_ENTER
					67 => OpenRA.Keycode.BACKSPACE,  // KEYCODE_DEL
					61 => OpenRA.Keycode.TAB,        // KEYCODE_TAB
					111 => OpenRA.Keycode.ESCAPE,    // KEYCODE_ESCAPE
					62 => OpenRA.Keycode.SPACE,      // KEYCODE_SPACE
					17 => OpenRA.Keycode.LEFT,       // KEYCODE_DPAD_LEFT
					22 => OpenRA.Keycode.RIGHT,      // KEYCODE_DPAD_RIGHT
					19 => OpenRA.Keycode.UP,         // KEYCODE_DPAD_UP
					20 => OpenRA.Keycode.DOWN,       // KEYCODE_DPAD_DOWN
					_ => OpenRA.Keycode.UNKNOWN
				};
			}

			// A minimal InputConnection that captures IME text commit.
			sealed class OpenRAInputConnection : global::Android.Views.InputMethods.BaseInputConnection
			{
				readonly OpenRASurfaceView view;

				public OpenRAInputConnection(OpenRASurfaceView targetView, bool fullEditor)
					: base(targetView, fullEditor)
				{
					view = targetView;
				}

				public override bool CommitText(Java.Lang.ICharSequence text, int newCursorPosition)
				{
					if (text != null && text.Length() > 0)
						view.textQueue.Enqueue(text.ToString());
					return true;
				}

				public override bool DeleteSurroundingText(int beforeLength, int afterLength)
				{
					// Map IME delete to backspace key events.
					for (var i = 0; i < beforeLength; i++)
						view.keyQueue.Enqueue(new KeyInput { Event = KeyInputEvent.Down, Key = OpenRA.Keycode.BACKSPACE });
					return true;
				}
			}
		}

		sealed class SurfaceCallback : Java.Lang.Object, ISurfaceHolderCallback
		{
			readonly AndroidPlatformWindow window;
			readonly MainActivity activity;

			public SurfaceCallback(AndroidPlatformWindow window, MainActivity activity)
			{
				this.window = window;
				this.activity = activity;
			}

			public void SurfaceCreated(ISurfaceHolder holder)
			{
				window.NotifySurfaceReady(holder);
				activity.StartEngineOnce();
			}

			public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height) => window.NotifySurfaceReady(holder);
			public void SurfaceDestroyed(ISurfaceHolder holder) => window.NotifySurfaceDestroyed();
		}
	}
}
