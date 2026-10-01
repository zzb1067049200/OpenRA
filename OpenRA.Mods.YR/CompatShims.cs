#region Copyright & License Information
/*
 * Compatibility shims for OpenRA.Mods.YR against the 2026 engine fork.
 *
 * The engine removed a number of helpers and types that YR relies on heavily.
 * Rather than rewriting hundreds of call sites, we re-provide them here in the
 * global namespace so every YR namespace (OpenRA.Mods.YR.*, OpenRA.Mods.RA2.*,
 * OpenRA.Mods.AS.*) can see them without extra usings.
 *
 * Removed and re-provided:
 *   - IPips / PipType            (pips are now a free-form string pip type)
 *   - ColorPreviewManagerWidget  (dev-only asset-browser widget)
 *   - Exts.F(string, ...)        (string interpolation helper)
 *   - Stream.WriteArray(...)     (memory stream write helper)
 *   - IPositionable.SetVisualPosition(...)
 *   - BodyOrientation.QuantizeOrientation(WRot, int)
 *   - IRenderable.WithPalette(...)
 */
#endregion
using System.Collections.Generic;
using System.IO;
using OpenRA;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

// Engine removed the IPips interface and PipType enum; pips are now surfaced via
// Passenger.CustomPipType (string). We keep a local shim so the legacy GetPips
// implementations still compile. They are not consumed by the engine, so pip
// visuals from these traits are currently absent.
public enum PipType
{
	Transparent,
	Green,
	Yellow,
	Red,
	White,
	Blue,
	Ammo,
	Loading,
	Cargo,
}

public interface IPips
{
	IEnumerable<PipType> GetPips(Actor self);
}

public static class YRPipTypeCompat
{
	// Maps the engine's free-form CustomPipType string back onto the legacy enum.
	public static PipType Parse(string name)
	{
		if (string.IsNullOrEmpty(name))
			return PipType.Transparent;

		return System.Enum.TryParse<PipType>(name, true, out var pip) ? pip : PipType.Transparent;
	}
}

public static class YRCompatExts
{
	// Old OpenRA string interpolation helper, replaced engine-side by FluentProvider.
	public static string F(this string format, params object[] args)
	{
		return string.Format(format, args);
	}

	// Old OpenRA stream helpers.
	public static void WriteArray(this Stream s, byte[] data)
	{
		s.Write(data, 0, data.Length);
	}

	public static void WriteArray(this Stream s, byte[] data, int offset, int count)
	{
		s.Write(data, offset, count);
	}

	// IPositionable.SetVisualPosition was folded into SetPosition.
	public static void SetVisualPosition(this IPositionable p, Actor self, WPos pos)
	{
		p.SetPosition(self, pos);
	}

	// BodyOrientation.QuantizeOrientation lost its facing-count overload on the
	// instance: use the single-argument form (call sites updated accordingly).

	// IRenderable.WithPalette moved behind IPalettedRenderable.
	public static IPalettedRenderable WithPalette(this IRenderable r, PaletteReference palette)
	{
		return ((IPalettedRenderable)r).WithPalette(palette);
	}
}

namespace OpenRA.Mods.YR.Widgets.Logic
{
	using OpenRA.Primitives;
	using OpenRA.Widgets;

	// Dev/asset-browser widget removed from the engine. Inert shim so the
	// VxlBrowser dev tool compiles; color previews simply do nothing at runtime.
	public class ColorPreviewManagerWidget : Widget
	{
		public Color Color { get; set; }
	}
}
