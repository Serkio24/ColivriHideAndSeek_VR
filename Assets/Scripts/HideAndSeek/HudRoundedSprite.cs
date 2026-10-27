using UnityEngine;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Sprite blanco de rectangulo redondeado para el fondo y los botones del HUD.
    ///
    /// Se genera por codigo, como el resto del HUD, en vez de depender de un PNG: es un cuadrado
    /// con las esquinas recortadas y borde de 9-slice, asi que una Image en modo Sliced lo estira a
    /// cualquier tamano sin deformar las esquinas. El color lo pone la Image (el sprite es blanco).
    /// El radio en pantalla se ajusta con Image.pixelsPerUnitMultiplier: 1 = Radius unidades de
    /// canvas, 2 = la mitad, etc.
    /// </summary>
    public static class HudRoundedSprite
    {
        /// Radio de las esquinas en pixeles de la textura (y en unidades de canvas con multiplicador 1).
        public const int Radius = 24;

        private const int Size = Radius * 2 + 16;

        private static Sprite _sprite;

        public static Sprite Get()
        {
            if (_sprite != null) return _sprite;

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "HudRoundedRect",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(Coverage(x, y) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            _sprite = Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(Radius, Radius, Radius, Radius));
            _sprite.name = "HudRoundedRect";
            return _sprite;
        }

        /// <summary>
        /// Cuanto del pixel cae dentro de la forma: distancia al centro de la esquina mas cercana,
        /// con un pixel de transicion para que el borde salga suavizado.
        /// </summary>
        private static float Coverage(int x, int y)
        {
            float px = x + 0.5f;
            float py = y + 0.5f;
            float cx = Mathf.Clamp(px, Radius, Size - Radius);
            float cy = Mathf.Clamp(py, Radius, Size - Radius);
            float distance = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            return Mathf.Clamp01(Radius - distance + 0.5f);
        }
    }
}
