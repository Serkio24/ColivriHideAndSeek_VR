using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Boton del HUD que se pulsa tocandolo con la yema del indice (o la punta del mando).
    ///
    /// No usa el EventSystem (no hay ninguno en la escena): el toque se resuelve por geometria,
    /// mirando si el punto de toque cae dentro de una caja fina pegada al boton. Solo cuenta el
    /// flanco de entrada, no quedarse dentro, igual que el gesto del puno del HUD.
    ///
    /// Es una clase normal y no un MonoBehaviour: la crea y la sondea HideAndSeekHUD, que es
    /// quien sabe de donde sale el punto de toque y si la pulsacion se acepta.
    /// </summary>
    public sealed class HudPokeButton
    {
        public RectTransform Rect { get; private set; }

        private Image _image;
        private TextMeshProUGUI _label;
        private bool _inside;

        /// <summary>
        /// Crea el boton bajo <paramref name="parent"/>, centrado en <paramref name="position"/>.
        /// <paramref name="font"/> puede ser null (se queda la fuente por defecto de TMP).
        /// El fondo es el rectangulo redondeado de <see cref="HudRoundedSprite"/>;
        /// <paramref name="cornerScale"/> va a pixelsPerUnitMultiplier (2 = esquinas a la mitad de radio).
        /// </summary>
        public static HudPokeButton Create(Transform parent, string name, string text, Vector2 size,
                                           Vector2 position, float fontSize, TMP_FontAsset font,
                                           Color labelColor, float cornerScale = 2f)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);

            var button = new HudPokeButton();
            button.Rect = go.GetComponent<RectTransform>();
            button.Rect.anchorMin = new Vector2(0.5f, 0.5f);
            button.Rect.anchorMax = new Vector2(0.5f, 0.5f);
            button.Rect.pivot = new Vector2(0.5f, 0.5f);
            button.Rect.anchoredPosition = position;
            button.Rect.sizeDelta = size;

            button._image = go.GetComponent<Image>();
            // Que no sea raycast target evita robarle eventos a nadie: el toque va por geometria.
            button._image.raycastTarget = false;
            button._image.sprite = HudRoundedSprite.Get();
            button._image.type = Image.Type.Sliced;
            button._image.pixelsPerUnitMultiplier = cornerScale;

            var labelGo = new GameObject("Label", typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);

            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            button._label = labelGo.GetComponent<TextMeshProUGUI>();
            if (font != null) button._label.font = font;
            button._label.text = text;
            button._label.alignment = TextAlignmentOptions.Center;
            button._label.fontSize = fontSize;
            // Una sola linea; si la etiqueta no cabe, encoge en vez de salirse del boton.
            button._label.textWrappingMode = TextWrappingModes.NoWrap;
            button._label.fontSizeMax = fontSize;
            button._label.fontSizeMin = fontSize * 0.5f;
            button._label.enableAutoSizing = true;
            button._label.margin = new Vector4(6f, 0f, 6f, 0f);
            // Sin Bold: la fuente del HUD (HemiHead) ya es bold y TMP la engordaria de mas.
            button._label.fontStyle = FontStyles.Normal;
            button._label.color = labelColor;
            button._label.raycastTarget = false;

            // Recien creado se da el dedo por dentro: si no, un dedo que ya estaba en esa zona del
            // aire contaria como pulsacion en el primer frame.
            button._inside = true;
            return button;
        }

        public bool Visible
        {
            get => Rect != null && Rect.gameObject.activeSelf;
            set
            {
                if (Rect == null || Rect.gameObject.activeSelf == value) return;
                Rect.gameObject.SetActive(value);
                // Al aparecer, mismo truco que al crearlo.
                if (value) _inside = true;
            }
        }

        public void SetColor(Color color)
        {
            if (_image != null && _image.color != color) _image.color = color;
        }

        /// Fondo y texto a la vez, para atenuar la etiqueta cuando el boton esta inerte.
        public void SetColors(Color fill, Color label)
        {
            SetColor(fill);
            if (_label != null && _label.color != label) _label.color = label;
        }

        public void SetText(string text)
        {
            if (_label != null && _label.text != text) _label.text = text;
        }

        /// Da el dedo por dentro, para que la siguiente entrada real sea la unica que cuente.
        public void ArmInside() => _inside = true;

        /// <summary>
        /// True el frame en el que el punto de toque entra en la caja del boton. Hay que llamarlo
        /// cada frame aunque la pulsacion no se vaya a aceptar, para que no cuente como entrada
        /// nueva un dedo que ya estaba encima.
        /// </summary>
        public bool Poll(bool hasPoint, Vector3 point, float pokeDepth)
        {
            if (!Visible) return false;

            if (!hasPoint)
            {
                _inside = false;
                return false;
            }

            // En espacio local del boton las X/Y salen en unidades de canvas y la Z tambien, asi
            // que el grosor en metros hay que dividirlo por la escala de mundo. Con esto la caja
            // mide igual aunque el rig del escondido este a la mitad de tamano.
            Vector3 local = Rect.InverseTransformPoint(point);
            Rect rect = Rect.rect;
            float depth = pokeDepth / Mathf.Max(1e-5f, Mathf.Abs(Rect.lossyScale.z));

            bool inside = Mathf.Abs(local.x) <= rect.width * 0.5f
                          && Mathf.Abs(local.y) <= rect.height * 0.5f
                          && Mathf.Abs(local.z) <= depth;

            if (inside == _inside) return false;

            _inside = inside;
            return inside;
        }
    }
}
