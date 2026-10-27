using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hindsight.UI
{
    /// <summary>
    /// Shared colors and sprites for the HUD's controls. The project has no UI art, so rounded
    /// panels and icons are drawn here once from signed distance functions and cached; that keeps
    /// them crisp, antialiased, and WebGL safe without importing textures.
    /// </summary>
    public static class UiStyle
    {
        public static readonly Color Bar = new(0.05f, 0.06f, 0.08f, 0.78f);
        public static readonly Color ModalPanel = new(0.08f, 0.09f, 0.11f, 0.97f);
        public static readonly Color Scrim = new(0f, 0f, 0f, 0.55f);
        public static readonly Color Text = new(1f, 1f, 1f, 0.95f);
        public static readonly Color TextMuted = new(1f, 1f, 1f, 0.6f);
        public static readonly Color TextDisabled = new(1f, 1f, 1f, 0.22f);
        public static readonly Color HoverFill = new(1f, 1f, 1f, 0.08f);
        public static readonly Color ActiveFill = new(1f, 1f, 1f, 0.15f);
        public static readonly Color Divider = new(1f, 1f, 1f, 0.12f);
        public static readonly Color Track = new(1f, 1f, 1f, 0.18f);
        public static readonly Color TrackFill = new(1f, 1f, 1f, 0.75f);
        /// <summary>The model's color: suggested plays, the recommended receiver, the prediction start.</summary>
        public static readonly Color Accent = new Color32(0xFF, 0xC6, 0x2F, 0xFF);

        public enum Glyph { Play, Pause, SkipPrevious, SkipNext, StepBack, StepForward, Restart, ChevronLeft, ChevronRight }

        const int IconPixels = 48;
        static readonly Dictionary<string, Sprite> Cache = new();

        /// <summary>A 9-sliced rounded rectangle; use with Image.Type.Sliced.</summary>
        public static Sprite Rounded(int radius) => Cached($"rounded{radius}", () =>
        {
            int size = radius * 2 + 2;
            var c = new Vector2(size / 2f, size / 2f);
            Texture2D tex = Draw(size, p => RoundedBox(p, c, c, radius));
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        });

        public static Sprite Icon(Glyph glyph) => Cached(glyph.ToString(), () =>
        {
            Texture2D tex = Draw(IconPixels, glyph switch
            {
                Glyph.Play => p => Polygon(p, new(17, 12), new(17, 36), new(36, 24)),
                Glyph.Pause => p => Mathf.Min(RoundedBox(p, new(18, 24), new(3.5f, 11), 1), RoundedBox(p, new(30, 24), new(3.5f, 11), 1)),
                Glyph.SkipNext => p => Mathf.Min(Polygon(p, new(13, 13), new(13, 35), new(29, 24)), RoundedBox(p, new(33, 24), new(2.5f, 11), 1)),
                Glyph.SkipPrevious => p => Mathf.Min(Polygon(p, new(35, 13), new(35, 35), new(19, 24)), RoundedBox(p, new(15, 24), new(2.5f, 11), 1)),
                Glyph.StepForward => p => Polygon(p, new(19, 15), new(19, 33), new(31, 24)),
                Glyph.StepBack => p => Polygon(p, new(29, 15), new(29, 33), new(17, 24)),
                Glyph.ChevronRight => p => Mathf.Min(Segment(p, new(20, 15), new(29, 24)), Segment(p, new(29, 24), new(20, 33))) - 1.8f,
                Glyph.ChevronLeft => p => Mathf.Min(Segment(p, new(28, 15), new(19, 24)), Segment(p, new(19, 24), new(28, 33))) - 1.8f,
                Glyph.Restart => RestartArrow,
                _ => throw new ArgumentOutOfRangeException(nameof(glyph)),
            });
            return Sprite.Create(tex, new Rect(0, 0, IconPixels, IconPixels), new Vector2(0.5f, 0.5f), 100f);
        });

        // A circular arrow, open at the upper right, head at the right end pointing up (replay).
        static float RestartArrow(Vector2 p)
        {
            var c = new Vector2(24, 24);
            const float radius = 11f, thickness = 3.4f, gapFrom = 20f, gapTo = 80f;
            Vector2 d = p - c;
            float angle = Mathf.Repeat(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 360f);
            float ring = angle > gapFrom && angle < gapTo ? 99f : Mathf.Abs(d.magnitude - radius) - thickness / 2f;

            float a = gapFrom * Mathf.Deg2Rad;
            var radial = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var tangent = new Vector2(-radial.y, radial.x); // counterclockwise
            Vector2 at = c + radial * radius;
            float head = Polygon(p, at - radial * 5.5f, at + radial * 5.5f, at + tangent * 7f);
            return Mathf.Min(ring, head);
        }

        // ---------- Rasterizing ----------

        static Sprite Cached(string key, Func<Sprite> make)
        {
            if (!Cache.TryGetValue(key, out Sprite sprite) || sprite == null)
                Cache[key] = sprite = make();
            return sprite;
        }

        /// <summary>White, with alpha from a signed distance in pixels (negative inside).</summary>
        static Texture2D Draw(int size, Func<Vector2, float> distance)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float coverage = Mathf.Clamp01(0.5f - distance(new Vector2(x + 0.5f, y + 0.5f)));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(coverage * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static float RoundedBox(Vector2 p, Vector2 center, Vector2 half, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - half + new Vector2(radius, radius);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Convex polygon, either winding. Exact inside and near edges, which is all antialiasing needs.</summary>
        static float Polygon(Vector2 p, params Vector2[] v)
        {
            float area = 0f;
            for (int i = 0; i < v.Length; i++)
            {
                Vector2 a = v[i], b = v[(i + 1) % v.Length];
                area += a.x * b.y - b.x * a.y;
            }
            float sign = area > 0f ? 1f : -1f;
            float d = float.MinValue;
            for (int i = 0; i < v.Length; i++)
            {
                Vector2 a = v[i], b = v[(i + 1) % v.Length];
                Vector2 outward = new Vector2(b.y - a.y, a.x - b.x).normalized * sign;
                d = Mathf.Max(d, Vector2.Dot(p - a, outward));
            }
            return d;
        }
    }
}
