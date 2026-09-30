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
using System.Reflection;
using System.Runtime.InteropServices;
using OpenRA;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Android
{
	// Android implementation of IPlatform. The Activity creates the window (it owns the
	// SurfaceView lifecycle) and registers it via Window before Game.InitializeAndRun is called.
	public class AndroidPlatform : IPlatform
	{
		public static AndroidPlatformWindow Window { get; private set; }

		// App layer (OpenRA.Android) sets this to receive platform log messages in the
		// in-app DevConsole without creating a circular project reference.
		public static Action<string, string> PlatformLogger { get; set; }
		public static Action<string, string> PlatformErrorLogger { get; set; }

		static void PLog(string tag, string msg)
		{
			global::Android.Util.Log.Info("OpenRA", $"[{tag}] {msg}");
			PlatformLogger?.Invoke(tag, msg);
		}

		static void PLogError(string tag, string msg)
		{
			global::Android.Util.Log.Error("OpenRA", $"[{tag}] {msg}");
			PlatformErrorLogger?.Invoke(tag, msg);
		}

		static bool nativeLibsInitialized;
		static IntPtr freetypeHandle = IntPtr.Zero;

		public static void Initialize(global::Android.Content.Context context)
		{
			if (nativeLibsInitialized)
				return;
			nativeLibsInitialized = true;

			// ── OpenAL (soft_oal) ───────────────────────────────────────────────
			try
			{
				PLog("OpenAL", "Pre-loading soft_oal via JavaSystem.LoadLibrary...");
				Java.Lang.JavaSystem.LoadLibrary("soft_oal");
				PLog("OpenAL", "soft_oal pre-loaded OK.");
			}
			catch (Exception ex)
			{
				PLogError("OpenAL", $"JavaSystem.LoadLibrary(soft_oal): {ex.Message}");
			}

			try
			{
				var openalAssembly = Assembly.Load("OpenAL-CS");
				NativeLibrary.SetDllImportResolver(openalAssembly, (libraryName, asm, searchPath) =>
				{
					if (libraryName == "soft_oal")
					{
						foreach (var name in new[] { "soft_oal", "libsoft_oal.so", "libsoft_oal" })
							if (NativeLibrary.TryLoad(name, asm, DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.UserDirectories, out var handle))
								return handle;
					}

					return IntPtr.Zero;
				});
				PLog("OpenAL", "OpenAL-CS resolver registered.");
			}
			catch (Exception ex)
			{
				PLogError("OpenAL", $"Resolver setup: {ex.Message}");
			}

			// ── FreeType (freetype6) ────────────────────────────────────────────
			// 1. Pre-load via Java loader (critical: searches APK native lib dir with linker namespace)
			try
			{
				PLog("FreeType", "Calling JavaSystem.LoadLibrary(\"freetype6\")...");
				Java.Lang.JavaSystem.LoadLibrary("freetype6");
				PLog("FreeType", "JavaSystem.LoadLibrary(\"freetype6\") SUCCEEDED!");
			}
			catch (Exception ex)
			{
				PLogError("FreeType", $"JavaSystem.LoadLibrary(\"freetype6\") FAILED: {ex.Message}");
			}

			// 2. Resolve absolute path and attempt NativeLibrary.TryLoad
			string freetypePath = null;
			try
			{
				var nativeLibDir = context?.ApplicationInfo?.NativeLibraryDir;
				PLog("FreeType", $"nativeLibDir: {nativeLibDir}");

				if (!string.IsNullOrEmpty(nativeLibDir))
				{
					freetypePath = System.IO.Path.Combine(nativeLibDir, "libfreetype6.so");
					var exists = System.IO.File.Exists(freetypePath);
					PLog("FreeType", $"path: {freetypePath}  exists: {exists}");
				}

				if (freetypePath != null && NativeLibrary.TryLoad(freetypePath, out var h))
				{
					freetypeHandle = h;
					PLog("FreeType", $"NativeLibrary.TryLoad({freetypePath}) OK: handle={h}");
				}
				else if (NativeLibrary.TryLoad("libfreetype6.so", out h))
				{
					freetypeHandle = h;
					PLog("FreeType", $"NativeLibrary.TryLoad(libfreetype6.so) OK: handle={h}");
				}
				else if (NativeLibrary.TryLoad("freetype6", out h))
				{
					freetypeHandle = h;
					PLog("FreeType", $"NativeLibrary.TryLoad(freetype6) OK: handle={h}");
				}
				else
				{
					PLogError("FreeType", "NativeLibrary.TryLoad could not load handle directly; will try in resolver callback.");
				}
			}
			catch (Exception ex)
			{
				PLogError("FreeType", $"NativeLibrary probe: {ex.Message}");
			}

			// 3. Register resolver on the assembly containing FreeType DllImports
			try
			{
				var targetAssembly = typeof(FreeTypeFont).Assembly;
				NativeLibrary.SetDllImportResolver(targetAssembly, (libraryName, asm, searchPath) =>
				{
					PLog("Resolver", $"DllImport requested: '{libraryName}' in {asm?.GetName()?.Name}");
					if (libraryName == "freetype6" || libraryName == "libfreetype6" || libraryName == "libfreetype6.so"
						|| libraryName == "freetype" || libraryName == "libfreetype" || libraryName == "libfreetype.so")
					{
						if (freetypeHandle != IntPtr.Zero)
							return freetypeHandle;

						if (freetypePath != null && NativeLibrary.TryLoad(freetypePath, asm, searchPath, out var h))
						{
							PLog("Resolver", $"Loaded via freetypePath: {h}");
							return freetypeHandle = h;
						}

						if (NativeLibrary.TryLoad("libfreetype6.so", asm, searchPath, out h))
						{
							PLog("Resolver", $"Loaded via libfreetype6.so: {h}");
							return freetypeHandle = h;
						}

						if (NativeLibrary.TryLoad("libfreetype.so", asm, searchPath, out h))
						{
							PLog("Resolver", $"Loaded via libfreetype.so: {h}");
							return freetypeHandle = h;
						}

						if (NativeLibrary.TryLoad("freetype6", asm, searchPath, out h))
						{
							PLog("Resolver", $"Loaded via freetype6: {h}");
							return freetypeHandle = h;
						}

						PLogError("Resolver", $"Failed to resolve '{libraryName}'!");
					}

					return IntPtr.Zero;
				});

				PLog("FreeType", $"DllImport resolver registered on {targetAssembly.GetName().Name}.");
			}
			catch (Exception ex)
			{
				PLogError("FreeType", $"SetDllImportResolver FAILED: {ex}");
			}
		}

		static AndroidPlatform()
		{
		}

		public static void SetWindow(AndroidPlatformWindow window) => Window = window;

		public IPlatformWindow CreateWindow(
			Size size, WindowMode windowMode, float scaleModifier, int vertexBatchSize, int indexBatchSize, int videoDisplay, GLProfile profile)
		{
			// The window has already been created by the Activity (it needs the SurfaceView on the
			// UI thread). Apply the engine's requested scale modifier and hand back the existing instance.
			if (scaleModifier > 0)
				Window.SetScaleModifier(scaleModifier);

			// The EGL surface is created asynchronously by the Activity's SurfaceHolder callback on
			// the UI thread. The Renderer constructor accesses Window.Context immediately after this
			// returns, so we must block here until the surface is ready AND GL has been initialized
			// on this (the game) thread.
			Window.WaitForSurfaceAndInitializeGl();

			return Window;
		}

		public ISoundEngine CreateSound(string device)
		{
			try
			{
				PLog("OpenAL", "Initializing OpenAL...");
				var engine = new OpenAlSoundEngine(device);
				PLog("OpenAL", "Initialized successfully.");
				return engine;
			}
			catch (Exception e)
			{
				PLogError("OpenAL", $"Failed: {e}");
				Log.Write("sound", "Failed to initialize OpenAL device. Error was");
				Log.Write("sound", e);
				return new DummySoundEngine();
			}
		}

		public IFont CreateFont(byte[] data)
		{
			return new FreeTypeFont(data);
		}
	}
}
