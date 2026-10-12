using UnityEngine;

namespace EchoWorld
{
    // IMGUI primitives plus the supplied chest icon. No generated textures or fonts.
    public static class HudPaint
    {
        public static readonly Color Edge = new Color32(26, 24, 23, 255);
        public static readonly Color Steel = new Color32(105, 112, 112, 255);
        public static readonly Color Wood = new Color32(61, 45, 34, 248);
        public static readonly Color Well = new Color32(33, 29, 26, 255);
        public static readonly Color Ink = new Color32(237, 223, 193, 255);
        public static readonly Color Muted = new Color32(177, 166, 146, 255);
        public static readonly Color Gold = new Color32(199, 160, 88, 255);
        public static readonly Color Stamina = new Color32(137, 157, 100, 255);
        static readonly Color HeartRed = new Color32(183, 57, 49, 255);
        static readonly Color HeartEmpty = new Color32(64, 36, 32, 255);
        static Texture2D chestIcon;
        static bool chestIconLoaded;
        static readonly string[] HeartMask =
        {
            "00011000011000", "00111100111100", "01111111111110",
            "11111111111111", "11111111111111", "11111111111111",
            "01111111111110", "00111111111100", "00011111111000",
            "00001111110000", "00000111100000", "00000011000000"
        };

        public static void Fill(Rect rect, Color color)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        public static Rect Inset(Rect rect, float amount)
        {
            return new Rect(rect.x + amount, rect.y + amount,
                rect.width - amount * 2, rect.height - amount * 2);
        }

        public static void Panel(Rect rect, bool selected = false)
        {
            Fill(rect, Edge);
            Fill(Inset(rect, 2), selected ? Gold : Steel);
            Fill(Inset(rect, 4), Wood);
            Fill(new Rect(rect.x + 4, rect.y + 4, rect.width - 8, 2),
                new Color32(95, 71, 48, 255));
            Fill(new Rect(rect.x + 4, rect.yMax - 6, rect.width - 8, 2), Well);
            // Square iron pins keep the bevel quiet and legible at small sizes.
            Fill(new Rect(rect.x + 5, rect.y + 5, 2, 2), Muted);
            Fill(new Rect(rect.xMax - 7, rect.y + 5, 2, 2), Muted);
        }

        public static void Bar(Rect rect, float fraction, Color color)
        {
            Fill(rect, Edge);
            Rect inside = Inset(rect, 2);
            Fill(inside, Well);
            inside.width = Mathf.Floor(inside.width * Mathf.Clamp01(fraction));
            Fill(inside, color);
            Fill(new Rect(inside.x, inside.y, inside.width, 2), Color.Lerp(color, Ink, .25f));
        }

        static bool HeartPixel(int x, int y)
        {
            return y >= 0 && y < HeartMask.Length && x >= 0 && x < 14 && HeartMask[y][x] == '1';
        }

        static bool HeartInterior(int x, int y)
        {
            return HeartPixel(x, y) && HeartPixel(x - 1, y) && HeartPixel(x + 1, y)
                && HeartPixel(x, y - 1) && HeartPixel(x, y + 1);
        }

        public static void Heart(Rect rect, float fraction)
        {
            float pixel = Mathf.Floor(Mathf.Min(rect.width / 14, rect.height / 12));
            int area = 0;
            for (int y = 0; y < 12; y++)
            for (int x = 0; x < 14; x++)
            {
                if (!HeartPixel(x, y)) continue;
                Rect cell = new Rect(rect.x + x * pixel, rect.y + y * pixel, pixel, pixel);
                bool inside = HeartInterior(x, y);
                Fill(cell, inside ? HeartEmpty : Edge);
                if (inside) area++;
            }
            // Fill by interior area, so 1/20 is still visible in the narrow tip and
            // 19/20 already loses red in the lobes, rather than only in the outline.
            float remaining = Mathf.Clamp01(fraction) * area;
            for (int y = 11; y >= 0; y--)
            {
                int rowArea = 0;
                for (int x = 0; x < 14; x++) if (HeartInterior(x, y)) rowArea++;
                if (rowArea == 0) continue;
                float filled = Mathf.Clamp01(remaining / rowArea);
                for (int x = 0; x < 14; x++)
                {
                    if (!HeartInterior(x, y)) continue;
                    Color color = x < 5 && y < 5 ? Color.Lerp(HeartRed, Ink, .24f) : HeartRed;
                    Fill(new Rect(rect.x + x * pixel, rect.y + (y + 1 - filled) * pixel,
                        pixel, filled * pixel), color);
                }
                remaining -= rowArea;
            }
        }

        public static void Lock(float x, float y)
        {
            Fill(new Rect(x + 2, y, 6, 7), Steel);
            Fill(new Rect(x + 4, y + 2, 2, 5), Wood);
            Fill(new Rect(x, y + 6, 10, 8), Muted);
            Fill(new Rect(x + 4, y + 8, 2, 4), Well);
        }

        public static void Item(Rect rect, byte id)
        {
            if (id == 10)
            {
                if (!chestIconLoaded)
                {
                    chestIcon = Resources.Load<Texture2D>("Art/SciFi/crate_metal");
                    chestIconLoaded = true;
                }
                if (chestIcon)
                {
                    GUI.DrawTexture(rect, chestIcon, ScaleMode.ScaleToFit, true);
                    return;
                }
            }
            Color color = LocalSession.Color(id);
            float unit = Mathf.Max(1, Mathf.Floor(Mathf.Min(rect.width, rect.height) / 16));
            float x = Mathf.Floor(rect.center.x - 8 * unit), y = Mathf.Floor(rect.center.y - 8 * unit);
            // Each icon uses a tiny 16x16 grid; the tint comes from the session's item table.
            if (id == 5)
            {
                Fill(new Rect(x + 7 * unit, y + 6 * unit, 3 * unit, 9 * unit), Edge);
                Fill(new Rect(x + 8 * unit, y + 6 * unit, unit, 8 * unit), Gold);
                Fill(new Rect(x + 5 * unit, y + 2 * unit, 7 * unit, 6 * unit), color);
                Fill(new Rect(x + 7 * unit, y, 3 * unit, 6 * unit), Gold);
                Fill(new Rect(x + 8 * unit, y + 3 * unit, unit, 3 * unit), Ink);
                return;
            }
            Fill(new Rect(x + unit, y + 2 * unit, 14 * unit, 13 * unit), Edge);
            Fill(new Rect(x + 2 * unit, y + 3 * unit, 12 * unit, 11 * unit), color);
            Color light = Color.Lerp(color, Ink, .35f), dark = Color.Lerp(color, Edge, .45f);
            Fill(new Rect(x + 2 * unit, y + 3 * unit, 12 * unit, 2 * unit), light);
            Fill(new Rect(x + 12 * unit, y + 5 * unit, 2 * unit, 9 * unit), dark);
            if (id == 1 || id == 9)
            {
                Fill(new Rect(x + 2 * unit, y + 7 * unit, 10 * unit, 7 * unit), Wood);
                Fill(new Rect(x + 4 * unit, y + 10 * unit, 2 * unit, unit), Muted);
            }
            else if (id == 4)
            {
                Fill(new Rect(x + 4 * unit, y + 5 * unit, 7 * unit, 7 * unit), Well);
                Fill(new Rect(x + 5 * unit, y + 7 * unit, 2 * unit, 3 * unit), light);
                Fill(new Rect(x + 7 * unit, y + 5 * unit, 2 * unit, 3 * unit), light);
            }
            else if (id == 6 || id == 10)
            {
                Fill(new Rect(x + 3 * unit, y + 7 * unit, 9 * unit, unit), dark);
                Fill(new Rect(x + 3 * unit, y + 11 * unit, 9 * unit, unit), dark);
                if (id == 10)
                {
                    Fill(new Rect(x + 3 * unit, y + 3 * unit, 2 * unit, 11 * unit), Steel);
                    Fill(new Rect(x + 10 * unit, y + 3 * unit, 2 * unit, 11 * unit), Steel);
                    Fill(new Rect(x + 7 * unit, y + 7 * unit, 2 * unit, 3 * unit), Gold);
                }
            }
            else if (id == 3)
            {
                Fill(new Rect(x + 4 * unit, y + 5 * unit, 2 * unit, unit), Ink);
                Fill(new Rect(x + 9 * unit, y + 10 * unit, 2 * unit, unit), dark);
            }
            else
            {
                Fill(new Rect(x + 4 * unit, y + 7 * unit, 3 * unit, 2 * unit), dark);
                Fill(new Rect(x + 8 * unit, y + 10 * unit, 3 * unit, 2 * unit), light);
            }
        }
    }
}
