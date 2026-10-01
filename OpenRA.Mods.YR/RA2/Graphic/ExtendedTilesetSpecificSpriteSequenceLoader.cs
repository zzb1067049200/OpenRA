#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
namespace OpenRA.Mods.RA2.Graphics
{
	/// <summary>
	/// Ported to the 2026 engine. The engine dropped SpriteSequenceFormat metadata
	/// blocks (see UpdateRules 20230225/ExplicitSequenceFilenames), so the YR tileset
	/// tables that used to be read from mods/yr/mod.yaml are declared inline here.
	/// The engine also constructs sequence loaders through a parameterless constructor
	/// (ObjectCreator.CreateBasic) and passes ModData per CreateSequence call, so this
	/// type must not take a ModData constructor argument.
	/// </summary>
	public class ExtendedTilesetSpecificSpriteSequenceLoader : DefaultSpriteSequenceLoader
	{
		public readonly string DefaultSpriteExtension = ".shp";
		public readonly FrozenDictionary<string, string> TilesetExtensions;
		public readonly FrozenDictionary<string, string> TilesetCodes;
		public readonly FrozenDictionary<string, string> TilesetSuffixes;

		public ExtendedTilesetSpecificSpriteSequenceLoader()
		{
			TilesetExtensions = new Dictionary<string, string>
			{
				{ "TEMPERATE", ".tem" },
				{ "SNOW", ".sno" },
				{ "URBAN", ".urb" },
				{ "NEWURBAN", ".ubn" },
				{ "DESERT", ".des" },
				{ "LUNAR", ".lun" },
			}.ToFrozenDictionary();

			TilesetCodes = new Dictionary<string, string>
			{
				{ "GENERIC", "g" },
				{ "SNOW", "a" },
				{ "TEMPERATE", "t" },
				{ "URBAN", "u" },
				{ "NEWURBAN", "n" },
				{ "DESERT", "d" },
				{ "LUNAR", "l" },
			}.ToFrozenDictionary();

			TilesetSuffixes = new Dictionary<string, string>
			{
				{ "SNOW", "a" },
			}.ToFrozenDictionary();
		}

		public override ISpriteSequence CreateSequence(
			ModData modData, string tileset, SpriteCache cache, string image, string sequence, MiniYaml data, MiniYaml defaults)
		{
			return new ExtendedTilesetSpecificSpriteSequence(cache, this, image, sequence, data, defaults);
		}
	}

	public class ExtendedTilesetSpecificSpriteSequence : DefaultSpriteSequence
	{
		public ExtendedTilesetSpecificSpriteSequence(SpriteCache cache, ISpriteSequenceLoader loader, string image, string sequence, MiniYaml data, MiniYaml defaults)
			: base(cache, loader, image, sequence, data, defaults) { }

		static MiniYamlNode NodeFor(string key, MiniYaml data, MiniYaml defaults)
		{
			return data.NodeWithKeyOrDefault(key) ?? defaults?.NodeWithKeyOrDefault(key);
		}

		static string ResolveTilesetId(string tileset, MiniYaml data, MiniYaml defaults)
		{
			var tsId = tileset;
			var overrides = NodeFor("TilesetOverrides", data, defaults);
			if (overrides != null)
			{
				var tsNode = overrides.Value.Nodes.FirstOrDefault(n => n.Key == tsId);
				if (tsNode != null)
					tsId = tsNode.Value.Value;
			}
			return tsId;
		}

		protected override IEnumerable<ReservationInfo> ParseFilenames(ModData modData, string tileset, ImmutableArray<int> frames, MiniYaml data, MiniYaml defaults)
		{
			var loader = (ExtendedTilesetSpecificSpriteSequenceLoader)Loader;
			var resolvedTileset = ResolveTilesetId(tileset, data, defaults);
			foreach (var r in base.ParseFilenames(modData, tileset, frames, data, defaults))
			{
				// These sequences predate the engine's explicit `Filename:` field. That field was
				// introduced by the ExplicitSequenceFilenames update rule, which cannot run for this
				// mod: it reads the raw mod.yaml through a reflection lookup of a Manifest field that
				// the 2026 engine no longer has, so it disables itself and leaves every shorthand
				// sequence unconverted (5888 nodes). Reproduce the old resolution order here instead:
				// the sequence node's own value, then the image-level `Defaults:` node's value
				// (the rule reproduces this as `sequence.Value ??= defaults.Value`), then the image
				// name. Example: image `e1` has `Defaults: gi`, so its idle sequence is `gi.shp`.
				var spriteName = r.Filename;
				if (string.IsNullOrEmpty(spriteName))
					spriteName = data.Value;
				if (string.IsNullOrEmpty(spriteName))
					spriteName = defaults?.Value;
				if (string.IsNullOrEmpty(spriteName))
					spriteName = image;

				// All of these may be defined on the sequence node itself or in the image's Defaults node.
				if (LoadField("UseTilesetCode", false, data, defaults))
				{
					if (loader.TilesetCodes.TryGetValue(resolvedTileset, out var code) && spriteName.Length >= 2)
						spriteName = spriteName[..1] + code + spriteName[2..];
				}

				if (LoadField("UseTilesetSuffix", false, data, defaults))
				{
					if (loader.TilesetSuffixes.TryGetValue(resolvedTileset, out var tilesetSuffix))
						spriteName += tilesetSuffix;
				}

				if (LoadField("AddExtension", true, data, defaults))
				{
					if (LoadField("UseTilesetExtension", false, data, defaults)
						&& loader.TilesetExtensions.TryGetValue(resolvedTileset, out var tilesetExtension))
						spriteName += tilesetExtension;
					else
						spriteName += loader.DefaultSpriteExtension;
				}

				yield return new ReservationInfo(spriteName, r.LoadFrames, r.Frames, r.Location);
			}
		}
	}
}
