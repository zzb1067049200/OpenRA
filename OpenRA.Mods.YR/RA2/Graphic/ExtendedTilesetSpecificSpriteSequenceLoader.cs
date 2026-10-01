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

		static string ResolveTilesetId(string tileset, MiniYaml data)
		{
			var tsId = tileset;
			var overrides = data.NodeWithKeyOrDefault("TilesetOverrides");
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
			var resolvedTileset = ResolveTilesetId(tileset, data);
			foreach (var r in base.ParseFilenames(modData, tileset, frames, data, defaults))
			{
				var spriteName = r.Filename;
				if (LoadField("UseTilesetCode", false, data))
				{
					if (loader.TilesetCodes.TryGetValue(resolvedTileset, out var code) && spriteName.Length >= 2)
						spriteName = spriteName.Substring(0, 1) + code + spriteName.Substring(2, spriteName.Length - 2);
				}

				if (LoadField("UseTilesetSuffix", false, data))
				{
					if (loader.TilesetSuffixes.TryGetValue(resolvedTileset, out var tilesetSuffix))
						spriteName += tilesetSuffix;
				}

				if (LoadField("AddExtension", true, data))
				{
					if (LoadField("UseTilesetExtension", false, data)
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
