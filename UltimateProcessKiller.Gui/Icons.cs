using System.Drawing;

namespace UltimateProcessKiller.Gui;

/// <summary>
/// Tiny procedural icon rasteriser so the app ships no image files. Each method returns a straight
/// ARGB pixel array (0 = transparent) ready for <c>IPlatformBackend.CreateImage</c>. Shapes are drawn
/// at 3x and box-filtered down, so the edges come out smooth rather than stair-stepped.
/// </summary>
internal static class Icons {
  private const int Scale = 3;

  /// <summary>A filled disc — used as the per-process bullet in the list.</summary>
  public static int[] Dot(int size, Color color) => Render(size, hi => FillCircle(hi, size, size / 2.0, size / 2.0, size * 0.34, color));

  /// <summary>A right-pointing play triangle — "apply this one strategy".</summary>
  public static int[] Play(int size, Color color) => Render(size, hi => {
    var s = size;
    FillTriangle(hi, size, s * 0.28, s * 0.20, s * 0.28, s * 0.80, s * 0.80, s * 0.50, color);
  });

  /// <summary>An upward arrow — "escalate the ladder".</summary>
  public static int[] ArrowUp(int size, Color color) => Render(size, hi => {
    var s = size;
    FillTriangle(hi, size, s * 0.50, s * 0.16, s * 0.16, s * 0.56, s * 0.84, s * 0.56, color); // head
    FillRect(hi, size, s * 0.40, s * 0.52, s * 0.60, s * 0.86, color);                          // shaft
  });

  /// <summary>A circular refresh arrow — a broken ring with an arrowhead at the gap.</summary>
  public static int[] Refresh(int size, Color color) => Render(size, hi => {
    var s = size;
    var cx = s / 2.0;
    var cy = s / 2.0;
    FillRingGap(hi, size, cx, cy, s * 0.36, s * 0.22, color, gapStartDeg: -55, gapEndDeg: 35);
    // arrowhead near the top of the gap, pointing clockwise
    FillTriangle(hi, size, s * 0.78, s * 0.20, s * 0.92, s * 0.44, s * 0.60, s * 0.40, color);
  });

  /// <summary>A crosshair target — the app / window icon for a process killer.</summary>
  public static int[] Target(int size, Color color) => Render(size, hi => {
    var s = size;
    var cx = s / 2.0;
    var cy = s / 2.0;
    FillRing(hi, size, cx, cy, s * 0.46, s * 0.34, color);
    FillCircle(hi, size, cx, cy, s * 0.12, color);
    FillRect(hi, size, s * 0.47, s * 0.02, s * 0.53, s * 0.20, color); // N tick
    FillRect(hi, size, s * 0.47, s * 0.80, s * 0.53, s * 0.98, color); // S
    FillRect(hi, size, s * 0.02, s * 0.47, s * 0.20, s * 0.53, color); // W
    FillRect(hi, size, s * 0.80, s * 0.47, s * 0.98, s * 0.53, color); // E
  });

  // --- rasteriser --------------------------------------------------------------------------------

  private static int[] Render(int size, Action<int[]> draw) {
    var hi = new int[size * Scale * size * Scale];
    draw(hi);
    return Downscale(hi, size);
  }

  private static void FillCircle(int[] hi, int size, double cx, double cy, double r, Color color) {
    var w = size * Scale;
    var argb = color.ToArgb();
    var rr = (r * Scale) * (r * Scale);
    for (var y = 0; y < w; ++y)
      for (var x = 0; x < w; ++x) {
        var dx = x + 0.5 - cx * Scale;
        var dy = y + 0.5 - cy * Scale;
        if (dx * dx + dy * dy <= rr)
          hi[y * w + x] = argb;
      }
  }

  private static void FillRing(int[] hi, int size, double cx, double cy, double rOut, double rIn, Color color) {
    var w = size * Scale;
    var argb = color.ToArgb();
    var ro = (rOut * Scale) * (rOut * Scale);
    var ri = (rIn * Scale) * (rIn * Scale);
    for (var y = 0; y < w; ++y)
      for (var x = 0; x < w; ++x) {
        var dx = x + 0.5 - cx * Scale;
        var dy = y + 0.5 - cy * Scale;
        var d = dx * dx + dy * dy;
        if (d <= ro && d >= ri)
          hi[y * w + x] = argb;
      }
  }

  private static void FillRingGap(int[] hi, int size, double cx, double cy, double rOut, double rIn, Color color, double gapStartDeg, double gapEndDeg) {
    var w = size * Scale;
    var argb = color.ToArgb();
    var ro = (rOut * Scale) * (rOut * Scale);
    var ri = (rIn * Scale) * (rIn * Scale);
    for (var y = 0; y < w; ++y)
      for (var x = 0; x < w; ++x) {
        var dx = x + 0.5 - cx * Scale;
        var dy = y + 0.5 - cy * Scale;
        var d = dx * dx + dy * dy;
        if (d > ro || d < ri)
          continue;

        var deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (deg >= gapStartDeg && deg <= gapEndDeg)
          continue; // the gap

        hi[y * w + x] = argb;
      }
  }

  private static void FillRect(int[] hi, int size, double x0, double y0, double x1, double y1, Color color) {
    var w = size * Scale;
    var argb = color.ToArgb();
    var ix0 = (int)(x0 * Scale);
    var iy0 = (int)(y0 * Scale);
    var ix1 = (int)(x1 * Scale);
    var iy1 = (int)(y1 * Scale);
    for (var y = Math.Max(0, iy0); y < Math.Min(w, iy1); ++y)
      for (var x = Math.Max(0, ix0); x < Math.Min(w, ix1); ++x)
        hi[y * w + x] = argb;
  }

  private static void FillTriangle(int[] hi, int size, double ax, double ay, double bx, double by, double cx, double cy, Color color) {
    var w = size * Scale;
    var argb = color.ToArgb();
    ax *= Scale; ay *= Scale; bx *= Scale; by *= Scale; cx *= Scale; cy *= Scale;
    var minX = (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)));
    var maxX = (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx)));
    var minY = (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)));
    var maxY = (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy)));
    for (var y = Math.Max(0, minY); y < Math.Min(w, maxY); ++y)
      for (var x = Math.Max(0, minX); x < Math.Min(w, maxX); ++x) {
        var px = x + 0.5;
        var py = y + 0.5;
        var d1 = Sign(px, py, ax, ay, bx, by);
        var d2 = Sign(px, py, bx, by, cx, cy);
        var d3 = Sign(px, py, cx, cy, ax, ay);
        var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        if (!(hasNeg && hasPos))
          hi[y * w + x] = argb;
      }
  }

  private static double Sign(double px, double py, double ax, double ay, double bx, double by)
    => (px - bx) * (ay - by) - (ax - bx) * (py - by);

  /// <summary>Box-filter the hi-res buffer down to <paramref name="size"/>, keeping edge alpha (coverage).</summary>
  private static int[] Downscale(int[] hi, int size) {
    var w = size * Scale;
    var outPixels = new int[size * size];
    var per = Scale * Scale;
    for (var oy = 0; oy < size; ++oy)
      for (var ox = 0; ox < size; ++ox) {
        long a = 0, r = 0, g = 0, b = 0, covered = 0;
        for (var sy = 0; sy < Scale; ++sy)
          for (var sx = 0; sx < Scale; ++sx) {
            var p = hi[(oy * Scale + sy) * w + (ox * Scale + sx)];
            if (((p >> 24) & 0xFF) == 0)
              continue;

            ++covered;
            r += (p >> 16) & 0xFF;
            g += (p >> 8) & 0xFF;
            b += p & 0xFF;
          }

        if (covered == 0)
          continue; // stays transparent, no colour fringe

        a = 255L * covered / per;                     // coverage -> alpha (smooth edges)
        var rr = (int)(r / covered);                  // average colour of the covered samples only
        var gg = (int)(g / covered);
        var bb = (int)(b / covered);
        outPixels[oy * size + ox] = ((int)a << 24) | (rr << 16) | (gg << 8) | bb;
      }

    return outPixels;
  }
}
