using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Franja de subtitulos world-space con el estilo del HUD (HemiHead, texto blanco sobre una
    /// pastilla oscura con borde azul). La usan la cinematica de intro, anclada bajo la pantalla, y
    /// el tutorial del lobby, flotando bajo la mirada del jugador.
    ///
    /// Se monta sola por codigo, como el HUD, asi que basta con añadir el componente a un objeto
    /// vacio. Cambiar de texto hace un fundido corto; un texto vacio la esconde.
    /// </summary>
    [ExecuteAlways]
    public class SubtitleBar : MonoBehaviour
    {
        [Header("Colocacion")]
        [Tooltip("Si esta activo la franja flota delante de la cabeza del jugador local y la sigue " +
                 "con retraso. Si no, se queda donde este este objeto (p. ej. bajo la pantalla).")]
        [SerializeField] private bool followHead;
        [SerializeField] private float followDistance = 1.3f;
        [Tooltip("Grados por debajo de la linea de la mirada a los que se coloca.")]
        [SerializeField] private float followDownAngle = 16f;
        [Tooltip("Velocidad del seguimiento. Mas bajo = mas perezoso (y mas comodo en VR).")]
        [SerializeField] private float followSharpness = 3f;

        [Header("Aspecto")]
        [SerializeField] private TMP_FontAsset font;
        [Tooltip("Ancho de la franja en unidades de canvas (0,001 m cada una con escala 0,001).")]
        [SerializeField] private float width = 900f;
        [SerializeField] private float fontSize = 40f;
        [SerializeField] private float padding = 22f;
        [SerializeField] private Color textColor = Color.white;
        [Tooltip("Opaco a proposito: con transparencia el borde azul de debajo se mezcla y aclara el fondo.")]
        [SerializeField] private Color backgroundColor = new Color(0.04f, 0.05f, 0.07f, 1f);
        [SerializeField] private Color borderColor = new Color(0.498f, 0.796f, 0.949f);
        [SerializeField] private float borderWidth = 3f;
        [SerializeField] private float fadeTime = 0.2f;

        private Canvas _canvas;
        private CanvasGroup _group;
        private RectTransform _panel;
        private TextMeshProUGUI _label;

        private string _current = string.Empty;
        private string _pending;
        private float _alpha;
        private bool _snapNext = true;

        public string CurrentText => _current;

        private void OnEnable()
        {
            EnsureBuilt();
            ApplyImmediate(_current);
        }

        /// <summary>Muestra un texto (con fundido). Vacio o null la esconde.</summary>
        public void Show(string text)
        {
            text ??= string.Empty;
            EnsureBuilt();

            // En el editor (preview del Timeline) no corre Update de forma fiable: se aplica ya.
            if (!Application.isPlaying)
            {
                ApplyImmediate(text);
                return;
            }

            if (text == _current && _pending == null) return;
            if (text == _pending) return;
            _pending = text;
        }

        public void Hide() => Show(string.Empty);

        /// <summary>Al empezar a seguir la cabeza la franja aparece directamente en su sitio.</summary>
        public void SnapToHead() => _snapNext = true;

        private void Update()
        {
            if (!Application.isPlaying) return;
            if (_group == null) return;

            float step = fadeTime > 0f ? Time.deltaTime / fadeTime : 1f;

            if (_pending != null)
            {
                // Primero se apaga lo que habia y luego entra el texto nuevo.
                if (string.IsNullOrEmpty(_current) || _alpha <= 0f)
                {
                    SetLabel(_pending);
                    _current = _pending;
                    _pending = null;
                }
                else
                {
                    _alpha = Mathf.MoveTowards(_alpha, 0f, step);
                }
            }
            else
            {
                float target = string.IsNullOrEmpty(_current) ? 0f : 1f;
                _alpha = Mathf.MoveTowards(_alpha, target, step);
            }

            _group.alpha = _alpha;
            _canvas.enabled = _alpha > 0.001f;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !followHead) return;
            var rig = PlayerRig.Local;
            Transform head = rig != null ? rig.Head : null;
            if (head == null) return;

            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = transform.forward;
            forward.Normalize();

            Vector3 dir = Quaternion.AngleAxis(followDownAngle, Vector3.Cross(Vector3.up, forward)) * forward;
            Vector3 targetPos = head.position + dir * followDistance;
            Quaternion targetRot = Quaternion.LookRotation(targetPos - head.position, Vector3.up);

            if (_snapNext)
            {
                transform.SetPositionAndRotation(targetPos, targetRot);
                _snapNext = false;
                return;
            }

            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, targetPos, k),
                Quaternion.Slerp(transform.rotation, targetRot, k));
        }

        private void ApplyImmediate(string text)
        {
            _current = text ?? string.Empty;
            _pending = null;
            SetLabel(_current);
            _alpha = string.IsNullOrEmpty(_current) ? 0f : 1f;
            if (_group != null) _group.alpha = _alpha;
            if (_canvas != null) _canvas.enabled = _alpha > 0f;
        }

        private void SetLabel(string text)
        {
            if (_label == null) return;
            _label.text = text;
            if (string.IsNullOrEmpty(text)) return;

            // La pastilla se ajusta al alto del texto (una o dos lineas).
            Vector2 preferred = _label.GetPreferredValues(text, width - padding * 2f, 0f);
            float height = preferred.y + padding * 2f;
            _panel.sizeDelta = new Vector2(width, height);
        }

        // ---------------------------------------------------------------- montaje

        private void EnsureBuilt()
        {
            if (_label != null) return;

            // Se reutiliza lo que ya exista (por ejemplo tras recompilar en el editor).
            Transform existing = transform.Find("SubtitleCanvas");
            if (existing != null)
            {
                _canvas = existing.GetComponent<Canvas>();
                _group = existing.GetComponent<CanvasGroup>();
                _panel = existing.Find("Panel") as RectTransform;
                _label = existing.GetComponentInChildren<TextMeshProUGUI>(true);
                if (_canvas != null && _group != null && _panel != null && _label != null) return;
                DestroySafe(existing.gameObject);
            }

            var canvasGo = new GameObject("SubtitleCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            canvasGo.hideFlags = HideFlags.DontSave;
            canvasGo.layer = gameObject.layer;
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localScale = Vector3.one * 0.001f;

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 10;
            ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(width, 200f);

            _group = canvasGo.GetComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;

            Sprite sprite = HudRoundedSprite.Get();

            var border = new GameObject("Panel", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            border.transform.SetParent(canvasGo.transform, false);
            border.sprite = sprite;
            border.type = Image.Type.Sliced;
            border.color = borderColor;
            border.raycastTarget = false;
            _panel = border.rectTransform;
            _panel.sizeDelta = new Vector2(width, 120f);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            fill.transform.SetParent(_panel, false);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(borderWidth, borderWidth);
            fillRect.offsetMax = new Vector2(-borderWidth, -borderWidth);
            fill.sprite = sprite;
            fill.type = Image.Type.Sliced;
            fill.pixelsPerUnitMultiplier = HudRoundedSprite.Radius / Mathf.Max(1f, HudRoundedSprite.Radius - borderWidth);
            fill.color = backgroundColor;
            fill.raycastTarget = false;

            _label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            _label.transform.SetParent(_panel, false);
            var labelRect = _label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(padding, padding * 0.5f);
            labelRect.offsetMax = new Vector2(-padding, -padding * 0.5f);
            if (font != null) _label.font = font;
            _label.fontSize = fontSize;
            _label.fontStyle = FontStyles.Normal;
            _label.color = textColor;
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.Normal;
            _label.raycastTarget = false;
            _label.text = string.Empty;

            foreach (Transform t in canvasGo.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = gameObject.layer;
                t.gameObject.hideFlags = HideFlags.DontSave;
            }
        }

        private static void DestroySafe(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
