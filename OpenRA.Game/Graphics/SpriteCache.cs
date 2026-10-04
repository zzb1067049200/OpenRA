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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;

namespace OpenRA.Graphics
{
	public delegate ISpriteFrame AdjustFrame(ISpriteFrame input, int index, int total);

	public sealed class SpriteCache : IDisposable
	{
		public readonly Dictionary<SheetType, SheetBuilder> SheetBuilders;
		readonly ISpriteLoader[] loaders;
		readonly IReadOnlyFileSystem fileSystem;

		readonly Dictionary<
			int,
			(ImmutableArray<int> Frames, MiniYamlNode.SourceLocation Location, AdjustFrame AdjustFrame, bool Premultiplied)> spriteReservations = [];
		readonly Dictionary<string, List<int>> reservationsByFilename = [];

		readonly Dictionary<int, Sprite[]> resolvedSprites = [];

		readonly Dictionary<int, (string Filename, MiniYamlNode.SourceLocation Location)> missingFiles = [];

		int nextReservationToken = 1;

		public SpriteCache(
			IReadOnlyFileSystem fileSystem, ISpriteLoader[] loaders, int bgraSheetSize, int indexedSheetSize, int bgraSheetMargin = 1, int indexedSheetMargin = 1)
		{
			SheetBuilders = new Dictionary<SheetType, SheetBuilder>
			{
				{ SheetType.Indexed, new SheetBuilder(SheetType.Indexed, indexedSheetSize, indexedSheetMargin) },
				{ SheetType.BGRA, new SheetBuilder(SheetType.BGRA, bgraSheetSize, bgraSheetMargin) }
			};

			this.fileSystem = fileSystem;
			this.loaders = loaders;
		}

		public int ReserveSprites(string filename, ImmutableArray<int> frames, MiniYamlNode.SourceLocation location,
			AdjustFrame adjustFrame = null, bool premultiplied = false)
		{
			var token = nextReservationToken++;
			spriteReservations[token] = (frames, location, adjustFrame, premultiplied);
			reservationsByFilename.GetOrAdd(filename, _ => []).Add(token);
			return token;
		}

		static ISpriteFrame[] GetFrames(IReadOnlyFileSystem fileSystem, string filename, ISpriteLoader[] loaders)
		{
			if (!fileSystem.TryOpen(filename, out var stream))
				return null;

			using (stream)
			{
				foreach (var loader in loaders)
					if (loader.TryParseSprite(stream, filename, out var frames, out _))
						return frames;

				return null;
			}
		}

		public ISpriteFrame[] LoadFramesUncached(string filename)
		{
			return GetFrames(fileSystem, filename, loaders);
		}

		/// <summary>
		/// Checks that a frame carries everything the sheet builder is going to ask for.
		/// A loader that cannot decode a frame may still hand back a non-null
		/// <see cref="ISpriteFrame"/> whose <see cref="ISpriteFrame.Data"/> is null or whose
		/// declared size does not match the buffer, and the builder dereferences both without
		/// checking. Returns false with a human-readable reason instead of throwing, so the
		/// caller can log which sprite is at fault.
		/// </summary>
		static bool IsUsable(ISpriteFrame frame, out string reason)
		{
			if (frame == null)
			{
				reason = "frame is null";
				return false;
			}

			var size = frame.Size;

			// Zero-sized frames are legal: the builder returns an empty sprite for them.
			if (size.Width < 0 || size.Height < 0)
			{
				reason = $"negative size {size.Width}x{size.Height}";
				return false;
			}

			if (size.Width == 0 || size.Height == 0)
			{
				reason = null;
				return true;
			}

			var data = frame.Data;
			if (data == null)
			{
				reason = $"declared size {size.Width}x{size.Height} but Data is null";
				return false;
			}

			// Indexed8 stores one byte per pixel, Bgra32 four. Getting this wrong means the
			// loader and the frame type disagree, which would copy the wrong number of bytes.
			var bpp = frame.Type == SpriteFrameType.Indexed8 ? 1 : 4;
			var required = (long)size.Width * size.Height * bpp;
			if (data.Length < required)
			{
				reason = $"declared size {size.Width}x{size.Height} needs {required} byte(s) " +
					$"but Data has {data.Length}";
				return false;
			}

			reason = null;
			return true;
		}

		public void LoadReservations(ModData modData)
		{
			// Stage marker. A NullReferenceException here has no line number even in a Debug
			// build, because the .NET Android runtime resolves symbols against the assemblies
			// inside libassembly-store.so and never sees a standalone OpenRA.Game.dll to match
			// assemblies/OpenRA.Game.pdb to (see tools/yr-port/inject_pdb_into_apk.py). Rather
			// than keep guessing which of the dozen dereferences in this method is at fault,
			// name the stage before it runs and report the last one reached on the way out.
			var 			stage = "enter";
			try
			{
			Log.Write("debug", $"[nre] LoadReservations: {spriteReservations.Count} reservation(s) across {reservationsByFilename.Count} file(s).");
			var pendingResolve = new List<(
				string Filename,
				int FrameIndex,
				bool Premultiplied,
				AdjustFrame AdjustFrame,
				ISpriteFrame Frame,
				Sprite[] SpritesForToken)>();
			var fileIndex = 0;
			foreach (var (filename, tokens) in reservationsByFilename)
			{
				modData.LoadScreen?.Display();
				stage = $"GetFrames({filename})";
				var loadedFrames = GetFrames(fileSystem, filename, loaders);
				stage = $"tokens({filename}={loadedFrames?.Length.ToString() ?? "null"})";

				// One line per file: 497 of them, which is fine, and it names the exact file the
				// throw came from. The stage string alone only narrows it to a phase.
				Log.Write("debug", $"[nre] file #{fileIndex++}: {filename} -> {loadedFrames?.Length.ToString() ?? "null"} frame(s), {tokens.Count} token(s)");

				foreach (var token in tokens)
				{
					if (!spriteReservations.TryGetValue(token, out var rs))
						continue;

					if (loadedFrames == null)
					{
						resolvedSprites[token] = null;
						missingFiles[token] = (filename, rs.Location);
						continue;
					}

					// A reservation may ask for frames the sheet does not have: mod data
					// ported from the original games carries frame numbers for sprites
					// that were re-cut, and a mod that is only partially ported will
					// request far more frames than exist. That is a data bug, but aborting
					// the whole map load over one cosmetic sprite is worse than drawing
					// what is there. Clamp instead of throwing, and say so.
					//
					// Frames is an ImmutableArray, so "no explicit list" is the default
					// value: Length == 0 rather than a null reference.
					var requested = rs.Frames;
					if (requested.Length > 0 && requested.Any(i => i < 0 || i >= loadedFrames.Length))
					{
						Log.Write("debug",
							$"[frameclamp] {rs.Location}: {filename} has {loadedFrames.Length} frame(s) but the sequence requests {requested.Length} " +
							$"(out of range: {string.Join(',', requested.Where(i => i < 0 || i >= loadedFrames.Length))}). Clamping to what exists.");

						requested = requested.Where(i => i >= 0 && i < loadedFrames.Length).ToImmutableArray();
						if (requested.Length == 0)
						{
							resolvedSprites[token] = null;
							missingFiles[token] = (filename, rs.Location);
							continue;
						}
					}

					// A loader can also hand back a null frame for a slot it could not decode.
					// The rest of this method assumes a non-null frame (it reads Frame.Size and
					// Frame.Type), so drop those here rather than throwing deep in the sheet
					// builder, where the stack trace no longer points at the bad sprite.
				var usable = new List<(int Index, ISpriteFrame Frame)>();
				var j = 0;
				var total = requested.Length > 0 ? requested.Length : loadedFrames.Length;
				var frames = requested.Length > 0
					? (IEnumerable<int>)requested
					: Enumerable.Range(0, loadedFrames.Length);
				foreach (var i in frames)
					{
						stage = $"readFrame({filename}#{i})";
						var frame = loadedFrames[i];
						stage = $"adjustFrame({filename}#{i})";
						if (rs.AdjustFrame != null)
							frame = rs.AdjustFrame(frame, j++, total);

						stage = $"nullCheck({filename}#{i})";
						if (frame == null)
						{
							Log.Write("debug",
								$"[nullframe] {rs.Location}: {filename} frame {i} decoded to null. Skipping it.");
							continue;
						}

						usable.Add((Index: i, Frame: frame));
					}

					if (usable.Count == 0)
					{
						resolvedSprites[token] = null;
						missingFiles[token] = (filename, rs.Location);
						continue;
					}

					stage = $"alloc({filename}#{token})";
					var resolved = new Sprite[loadedFrames.Length];
					resolvedSprites[token] = resolved;

				foreach (var (i, frame) in usable)
					pendingResolve.Add((filename, i, rs.Premultiplied, rs.AdjustFrame, frame, resolved));

					stage = $"tokenDone({filename}#{token})";
					Log.Write("debug", $"[nre]   token {token} done for {filename} ({usable.Count} usable frame(s))");
				}
			}

			spriteReservations.Clear();
			spriteReservations.TrimExcess();
			reservationsByFilename.Clear();
			reservationsByFilename.TrimExcess();

			// When the sheet builder is adding sprites, it reserves height for the tallest sprite seen along the row.
			// We can achieve better sheet packing by keeping sprites with similar heights together.
			stage = "orderByHeight";
			Log.Write("debug", $"[nre] {pendingResolve.Count} pending sprite(s) collected; ordering by height.");
			var orderedPendingResolve = pendingResolve
				.Where(x => x.Frame != null)
				.OrderBy(x => x.Frame.Size.Height);

			stage = "buildCache";
			var spriteCache = new Dictionary<(
				string Filename,
				int FrameIndex,
				bool Premultiplied,
				AdjustFrame AdjustFrame),
				Sprite>(pendingResolve.Count);
			foreach (var (filename, frameIndex, premultiplied, adjustFrame, frame, spritesForToken) in orderedPendingResolve)
			{
				stage = $"isUsable({filename}#{frameIndex})";
				// The sheet builder assumes a frame it can measure, type and blit. A partially
				// ported mod can still produce frames that satisfy the earlier checks but blow
				// up inside here, and at that point the stack trace no longer says which
				// sprite was at fault. Probe the frame once, log what is wrong with it, and
				// skip it so one bad frame cannot abort the whole map.
				if (!IsUsable(frame, out var reason))
				{
					Log.Write("debug", $"[badframe] {filename} frame {frameIndex}: {reason}. Skipping it.");
					continue;
				}

				// The backing array is sized from the sheet we just read, so a stale
				// reservation can still point past its end. Clamp rather than throw.
				if (frameIndex < 0 || frameIndex >= spritesForToken.Length)
				{
					Log.Write("debug",
						$"[badframe] {filename} frame {frameIndex} is outside the resolved sprite array " +
						$"(0..{spritesForToken.Length - 1}). Skipping it.");
					continue;
				}

				// Premultiplied and non-premultiplied sprites must be cached separately
				// to cover the case where the same image is requested in both versions.
				stage = $"sheetAdd({filename}#{frameIndex})";
				spritesForToken[frameIndex] = spriteCache.GetOrAdd(
					(filename, frameIndex, premultiplied, adjustFrame),
					_ =>
					{
						var sheetBuilder = SheetBuilders[SheetBuilder.FrameTypeToSheetType(frame.Type)];
						return sheetBuilder.Add(frame, premultiplied);
					});

				modData.LoadScreen?.Display();
			}

			stage = "releaseBuffers";
			foreach (var sb in SheetBuilders.Values)
				sb.Current?.ReleaseBuffer();

			stage = "done";
			Log.Write("debug", $"[nre] stage reached: {stage}");
			}
			catch (Exception e)
			{
				// "debug", not "error": the only registered channels are perf, debug, server,
				// sound, graphics, geoip, nat, client, sync, lua (see Game.Initialize and
				// ScriptContext). Writing to an unregistered channel makes Log.WriteValue
				// throw ArgumentException("Tried logging to non-existent channel error") on the
				// logging thread, which killed the process before this message could be
				// flushed -- losing the very information the probe exists to collect.
				Log.Write("debug", $"[nre] LoadReservations threw outside a logged stage: {stage}. {e.GetType().Name}: {e.Message}");
				throw;
			}
		}

		public Sprite[] ResolveSprites(int token)
		{
			if (!resolvedSprites.Remove(token, out var resolved))
				throw new InvalidOperationException($"{nameof(token)} {token} has either already been resolved, or was never reserved via {nameof(ReserveSprites)}");

			resolvedSprites.TrimExcess();

			if (missingFiles.TryGetValue(token, out var r))
				throw new FileNotFoundException($"{r.Location}: {r.Filename} not found", r.Filename);

			return resolved;
		}

		public IEnumerable<(string Filename, MiniYamlNode.SourceLocation Location)> MissingFiles => missingFiles.Values.ToHashSet();

		public void Dispose()
		{
			foreach (var sb in SheetBuilders.Values)
				sb.Dispose();
		}
	}
}
