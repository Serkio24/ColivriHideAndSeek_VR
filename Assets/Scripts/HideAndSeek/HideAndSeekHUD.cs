using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// HUD del jugador local: un canvas world-space anclado a la muneca izquierda que muestra
    /// el rol, el tiempo de la fase y cuantos escondidos quedan.
    ///
    /// Se ancla solo al rig local en cuanto este existe, asi que el prefab se puede dejar
    /// suelto en la escena.
    ///
    /// Con hand tracking se muestra en el lado de la palma: se lee girando la palma hacia la cara.
    /// Con mandos se coloca flotando sobre la cara superior del mando izquierdo y encara la cabeza
    /// sola, asi que se lee con solo levantar el mando. Cerrar el puno izquierdo (o apretar el grip
    /// del mando) lo enciende y lo apaga.
    ///
    /// El panel tiene varias paginas que se pasan con el joystick derecho (izquierda/derecha):
    ///  0. Partida: lo de siempre. En el lobby, y solo en el HUD del host, aparece un boton INICIAR.
    ///  1. Equipos: cuantos buscan (el resto se esconde). Solo lo cambia el host, en el lobby.
    ///  2. Prueba: solo con el modo de prueba (ver <see cref="HideAndSeekTestMode"/>). Durante la
    ///     ronda deja elegir BUSCAR o ESCONDERSE.
    /// Los botones se pulsan tocandolos con la yema del indice derecho, o con A/B del mando derecho
    /// como atajo. Solo responden los de la pagina que esta a la vista.
    /// </summary>
    public class HideAndSeekHUD : MonoBehaviour
    {
        [Header("Textos")]
        [SerializeField] private TextMeshProUGUI roleText;
        [SerializeField] private TextMeshProUGUI timerText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI hidersText;

        [Header("Anclaje a la muneca (hand tracking)")]
        [Tooltip("Si esta activo, el HUD se hace hijo del anchor de la mano izquierda al arrancar.")]
        [SerializeField] private bool attachToLeftHand = true;
        [Tooltip("Y negativo = lado de la palma (se lee girando la palma hacia la cara). " +
                 "Y positivo lo devuelve al dorso, como un reloj.")]
        [SerializeField] private Vector3 localPosition = new Vector3(0f, -0.06f, 0.02f);
        [Tooltip("Orientacion del panel. El Z de 180 es lo que lo voltea al lado de la palma " +
                 "sin dejar el texto boca abajo.")]
        [SerializeField] private Vector3 localEuler = new Vector3(-55f, 0f, 180f);
        [SerializeField] private float localScale = 0.0012f;

        [Header("Anclaje con mandos")]
        [Tooltip("LeftHandAnchor sigue la pose de la mano cuando hay hand tracking y la del mando " +
                 "cuando no, y las dos orientaciones no coinciden: con los valores de la palma el " +
                 "panel sale reflejado. Con esto activo el HUD usa su propia colocacion mientras se " +
                 "juega con mandos y vuelve sola a la de la palma al soltarlos.")]
        [SerializeField] private bool separateControllerPlacement = true;
        [Tooltip("Offset respecto al anchor del mando izquierdo. +Y sale por la cara superior, la del " +
                 "stick y los botones; -Z tira hacia la muneca. Por defecto el panel flota justo " +
                 "encima del mando.")]
        [SerializeField] private Vector3 controllerLocalPosition = new Vector3(0f, 0.1f, -0.04f);
        [Tooltip("El panel se orienta solo hacia la cabeza en vez de quedarse clavado al mando: basta " +
                 "con levantar el mando para leerlo, sin girar la muneca.")]
        [SerializeField] private bool controllerFacesHead = true;
        [Tooltip("Orientacion fija del panel sobre el mando. Solo se usa si 'controllerFacesHead' " +
                 "esta desactivado.")]
        [SerializeField] private Vector3 controllerLocalEuler = new Vector3(45f, 0f, 0f);

        [Header("Gesto de puno")]
        [Tooltip("Cerrar el puno izquierdo alterna el HUD entre encendido y apagado.")]
        [SerializeField] private bool fistTogglesHud = true;
        [Tooltip("Si el HUD arranca visible o apagado.")]
        [SerializeField] private bool visibleOnStart = true;
        [Tooltip("Con mandos, el equivalente a cerrar el puno: el gatillo lateral (grip) del mando izquierdo.")]
        [SerializeField] private OVRInput.Button fistButton = OVRInput.Button.PrimaryHandTrigger;
        [Tooltip("Con hand tracking: cierre minimo de los cuatro dedos para dar el puno por cerrado.")]
        [Range(0.5f, 1f)][SerializeField] private float fistCloseThreshold = 0.85f;
        [Tooltip("Por debajo de esto el puno vuelve a contar como abierto. La separacion entre los " +
                 "dos umbrales es lo que evita que parpadee con la mano a medio cerrar.")]
        [Range(0.1f, 0.9f)][SerializeField] private float fistOpenThreshold = 0.5f;
        [Tooltip("Tiempo muerto tras cada cambio, para que un puno mantenido no lo encienda y apague en bucle.")]
        [SerializeField] private float toggleCooldown = 0.4f;
        [SerializeField] private float hapticAmplitude = 0.35f;
        [SerializeField] private float hapticDuration = 0.06f;

        [Header("Paginas (joystick derecho)")]
        [Tooltip("Inclinacion lateral del joystick derecho que cuenta como deslizar una pagina.")]
        [Range(0.3f, 1f)][SerializeField] private float swipeThreshold = 0.7f;
        [Tooltip("Por debajo de esto el joystick se da por vuelto al centro y se puede deslizar otra vez.")]
        [Range(0f, 0.6f)][SerializeField] private float swipeRearm = 0.3f;
        [Tooltip("Tiempo aproximado de la animacion al cambiar de pagina, en segundos.")]
        [SerializeField] private float pageSlideTime = 0.08f;
        [SerializeField] private Color pageDotOnColor = new Color(0.165f, 0.604f, 0.839f);
        [SerializeField] private Color pageDotOffColor = new Color(0f, 0f, 0f, 0.2f);

        [Header("Estilo")]
        [Tooltip("Fuente de todos los textos del HUD. Si se deja vacia se usa la de StatusText.")]
        [SerializeField] private TMP_FontAsset font;
        [Tooltip("Relleno del recuadro del HUD.")]
        [SerializeField] private Color panelColor = new Color(1f, 1f, 1f, 0.95f);
        [Tooltip("Borde del recuadro: es el fondo redondeado que asoma alrededor del relleno.")]
        [SerializeField] private Color panelBorderColor = new Color(0.498f, 0.796f, 0.949f);
        [Tooltip("Grosor del borde del recuadro, en unidades de canvas.")]
        [SerializeField] private float panelBorderWidth = 4f;
        [SerializeField] private Color textColor = new Color(0.067f, 0.067f, 0.067f);
        [SerializeField] private Color buttonLabelColor = new Color(0.067f, 0.067f, 0.067f);
        [Tooltip("Texto de un boton que se ve pero no se puede pulsar.")]
        [SerializeField] private Color disabledLabelColor = new Color(0.067f, 0.067f, 0.067f, 0.4f);

        [Header("Botones")]
        [Tooltip("Atajo con mandos para el primer boton de la pagina (INICIAR, +, BUSCAR) sin tener " +
                 "que acertarle al panel. One es el boton A del mando derecho.")]
        [SerializeField] private OVRInput.Button startShortcut = OVRInput.Button.One;
        [Tooltip("Atajo para el segundo boton de la pagina (-, ESCONDERSE). Two es el boton B del mando derecho.")]
        [SerializeField] private OVRInput.Button secondaryShortcut = OVRInput.Button.Two;
        [Tooltip("Grosor en metros de la caja de toque del boton, medida desde el plano del panel.")]
        [SerializeField] private float pokeDepth = 0.02f;
        [Tooltip("Tiempo muerto tras pulsar, para que un roce no cuente dos veces.")]
        [SerializeField] private float pokeCooldown = 0.5f;
        [Tooltip("Con mandos, cuanto se adelanta el punto de toque respecto al anchor del mando " +
                 "derecho para aproximar su punta.")]
        [SerializeField] private float controllerPokeReach = 0.05f;
        [SerializeField] private Vector2 startButtonSize = new Vector2(240f, 44f);
        [Tooltip("Boton principal habilitado, y en la pagina Prueba el rol que se tiene ahora.")]
        [SerializeField] private Color startEnabledColor = new Color(0.498f, 0.796f, 0.949f);
        [SerializeField] private Color startDisabledColor = new Color(0.886f, 0.886f, 0.886f);
        [Tooltip("Color de un boton que se puede pulsar pero no es el accion principal (+/-, rol no elegido).")]
        [SerializeField] private Color buttonIdleColor = new Color(0.847f, 0.937f, 0.984f);

        [Header("Colores de rol")]
        [SerializeField] private Color seekerColor = new Color(0.067f, 0.067f, 0.067f);
        [SerializeField] private Color hiderColor = new Color(0.165f, 0.604f, 0.839f);
        [SerializeField] private Color spectatorColor = new Color(0.54f, 0.54f, 0.54f);
        [SerializeField] private Color neutralColor = new Color(0.067f, 0.067f, 0.067f);
        [SerializeField] private Color testModeColor = new Color(0.067f, 0.067f, 0.067f);

        private const int GamePage = 0;
        private const int TeamsPage = 1;
        private const int TestPage = 2;

        private Canvas _canvas;
        private OVRHand _leftHand;
        private Transform _anchor;
        private Transform _head;
        private bool _visible = true;
        private bool _fistClosed;
        private float _nextToggleTime;
        private Transform _rightIndexTip;
        private float _nextPressTime;

        // --- Paginas ---
        private RectTransform _content;
        private readonly List<RectTransform> _pages = new List<RectTransform>();
        private readonly List<Image> _pageDots = new List<Image>();
        /// Botones de cada pagina, para armarlos al llegar a ella.
        private readonly List<List<HudPokeButton>> _pageButtons = new List<List<HudPokeButton>>();
        private float _pageWidth;
        private int _page;
        private float _slideVelocity;
        private bool _stickCentered = true;

        // --- Pagina Partida ---
        private HudPokeButton _startButton;

        // --- Pagina Equipos ---
        private TextMeshProUGUI _seekersValueText;
        private TextMeshProUGUI _teamHidersText;
        private TextMeshProUGUI _teamHintText;
        private HudPokeButton _seekersMinus;
        private HudPokeButton _seekersPlus;

        // --- Pagina Prueba ---
        private TextMeshProUGUI _testHintText;
        private HudPokeButton _testSeekButton;
        private HudPokeButton _testHideButton;

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
        }

        private void Start()
        {
            BuildPages();
            SetVisible(visibleOnStart);

            if (attachToLeftHand)
            {
                StartCoroutine(AttachWhenRigReady());
            }
        }

        private IEnumerator AttachWhenRigReady()
        {
            while (PlayerRig.Local == null || PlayerRig.Local.LeftHand == null)
            {
                yield return null;
            }

            _anchor = PlayerRig.Local.LeftHand;
            _head = PlayerRig.Local.Head;

            var t = transform;
            t.SetParent(_anchor, false);
            t.localScale = Vector3.one * localScale;
            ApplyPlacement();
        }

        /// <summary>
        /// Recoloca el panel cada frame. No basta con hacerlo una vez al engancharse porque
        /// LeftHandAnchor cambia de pose en caliente al pasar de manos a mandos, y ademas el
        /// modo mando orienta el panel hacia la cabeza.
        /// </summary>
        private void LateUpdate()
        {
            if (_anchor != null)
            {
                ApplyPlacement();
            }
        }

        private void ApplyPlacement()
        {
            var t = transform;

            if (!separateControllerPlacement || LeftHandTracked())
            {
                t.localPosition = localPosition;
                t.localRotation = Quaternion.Euler(localEuler);
                return;
            }

            t.localPosition = controllerLocalPosition;

            if (!controllerFacesHead || _head == null)
            {
                t.localRotation = Quaternion.Euler(controllerLocalEuler);
                return;
            }

            // Un canvas se lee desde su lado -Z, asi que su forward tiene que apuntar al lado
            // contrario de la cabeza. Como up se usa el de la cabeza y no Vector3.up para que el
            // texto acompane si el jugador inclina la cabeza.
            Vector3 away = t.position - _head.position;
            if (away.sqrMagnitude > 0.0001f)
            {
                t.rotation = Quaternion.LookRotation(away, _head.up);
            }
        }

        /// <summary>
        /// Si LeftHandAnchor esta siguiendo ahora mismo la mano o el mando. Es la misma
        /// comprobacion que hace OVRCameraRig al elegir de donde saca la pose del anchor
        /// (mano si es valida, mando si no), para no quedar desincronizados con el.
        /// </summary>
        private static bool LeftHandTracked()
        {
            return OVRInput.GetControllerPositionValid(OVRInput.Controller.LHand);
        }

        private void Update()
        {
            if (fistTogglesHud)
            {
                PollFistGesture();
            }

            // Apagado: no hay nada que refrescar, pero Update sigue vivo para detectar el gesto
            // que lo vuelve a encender. Por eso se apaga el Canvas y no el GameObject.
            if (!_visible) return;

            PollSwipe();
            AnimatePages();

            var manager = HideAndSeekManager.Instance;
            if (manager == null)
            {
                SetText(statusText, "Conectando...");
                SetText(roleText, string.Empty);
                SetText(timerText, string.Empty);
                SetText(hidersText, string.Empty);
                SetText(_seekersValueText, "-");
                SetText(_teamHidersText, string.Empty);
                SetText(_teamHintText, "Conectando...");
                SetText(_testHintText, "Conectando...");
                SetAllButtonsVisible(false);
                return;
            }

            PlayerRole role = manager.LocalRole;
            GameState state = manager.State.Value;

            SetText(roleText, RoleLabel(role));
            if (roleText != null) roleText.color = RoleColor(role);

            SetText(timerText, state == GameState.WaitingForPlayers
                ? "--:--"
                : FormatMMSS(manager.PhaseTimeRemaining));

            SetText(hidersText, manager.HidersTotal.Value > 0
                ? $"Escondidos: {manager.HidersAlive.Value}/{manager.HidersTotal.Value}"
                : string.Empty);

            SetText(statusText, StatusLabel(manager, state, role));

            // El punto de toque se calcula una vez y lo comparten todos los botones.
            bool hasPoint = TryGetPokePoint(out Vector3 point);

            UpdateStartButton(manager, state, hasPoint, point);
            UpdateTeamsPage(manager, state, hasPoint, point);
            UpdateTestPage(manager, state, role, hasPoint, point);
        }

        // ------------------------------------------------------- encender / apagar

        /// <summary>Enciende o apaga el HUD sin desactivar este GameObject.</summary>
        public void SetVisible(bool value)
        {
            _visible = value;

            // Al reencender se da el dedo por dentro de los botones, para que reabrir el HUD con la
            // mano justo delante no cuente como una pulsacion.
            if (value) ArmAllButtons();

            if (_canvas != null)
            {
                _canvas.enabled = value;
                return;
            }

            // Sin Canvas propio (montaje manual): se apagan los hijos, nunca este objeto,
            // porque si se desactiva a si mismo deja de correr Update y ya no se puede reencender.
            for (int i = 0; i < transform.childCount; i++)
            {
                transform.GetChild(i).gameObject.SetActive(value);
            }
        }

        public void ToggleVisible() => SetVisible(!_visible);

        // --------------------------------------------------------- gesto de puno

        private void PollFistGesture()
        {
            if (Time.time < _nextToggleTime) return;

            bool toggled = OVRInput.GetDown(fistButton, OVRInput.Controller.LTouch) || HandFistJustClosed();
            if (!toggled) return;

            ToggleVisible();
            _nextToggleTime = Time.time + toggleCooldown;
            StartCoroutine(PulseRoutine(OVRInput.Controller.LTouch));
        }

        /// <summary>
        /// Puno cerrado con hand tracking. Se mide con la fuerza de cierre de los cuatro dedos
        /// (sin el pulgar, que en un puno queda cruzado por encima y da lecturas raras) y se toma
        /// el minimo: los cuatro tienen que estar cerrados, no basta con uno.
        ///
        /// Es una aproximacion: GetFingerPinchStrength mide punta de dedo contra pulgar, no
        /// curvatura real. Da de sobra para encender un menu; si algun dia hace falta detectar
        /// un puno de verdad, lo que toca es el ShapeRecognizer del Interaction SDK.
        /// </summary>
        private bool HandFistJustClosed()
        {
            var hand = ResolveLeftHand();
            if (hand == null || !hand.IsTracked || !hand.IsDataHighConfidence || hand.IsSystemGestureInProgress)
            {
                // Sin datos fiables se olvida el estado, para no soltar un falso positivo en
                // cuanto la mano vuelva al campo de vision.
                _fistClosed = false;
                return false;
            }

            float closure = Mathf.Min(
                Mathf.Min(hand.GetFingerPinchStrength(OVRHand.HandFinger.Index),
                          hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle)),
                Mathf.Min(hand.GetFingerPinchStrength(OVRHand.HandFinger.Ring),
                          hand.GetFingerPinchStrength(OVRHand.HandFinger.Pinky)));

            if (!_fistClosed && closure >= fistCloseThreshold)
            {
                _fistClosed = true;
                return true;
            }

            if (_fistClosed && closure <= fistOpenThreshold)
            {
                _fistClosed = false;
            }

            return false;
        }

        private OVRHand ResolveLeftHand()
        {
            if (_leftHand != null) return _leftHand;

            // Se buscan tambien los inactivos: con los mandos en la mano, los OVRHand del rig
            // pueden estar apagados y encenderse solo al soltarlos.
            foreach (var hand in FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (hand.GetHand() == OVRPlugin.Hand.HandLeft)
                {
                    _leftHand = hand;
                    break;
                }
            }

            return _leftHand;
        }

        private IEnumerator PulseRoutine(OVRInput.Controller controller)
        {
            OVRInput.SetControllerVibration(1f, hapticAmplitude, controller);
            yield return new WaitForSeconds(hapticDuration);
            OVRInput.SetControllerVibration(0f, 0f, controller);
        }

        // ----------------------------------------------------------------- paginas

        private int PageCount => _pages.Count;

        /// La pagina esta a la vista y quieta: solo entonces aceptan pulsaciones sus botones.
        private bool PageSettled(int page)
        {
            return _page == page
                   && _content != null
                   && Mathf.Abs(_content.anchoredPosition.x + page * _pageWidth) < 2f;
        }

        /// <summary>
        /// Monta las paginas por codigo, como antes se hacia con el boton INICIAR: el HUD ya esta
        /// serializado en la escena y el setup no lo vuelve a tocar.
        ///
        /// Viewport (recorta) -> Content (se desliza) -> una pagina por pantalla, una al lado de la
        /// otra. Los cuatro textos de siempre pasan a la pagina 0; como tienen el anchor centrado y
        /// la pagina mide lo mismo que el canvas, se quedan exactamente donde estaban.
        /// </summary>
        private void BuildPages()
        {
            if (_content != null) return;

            var root = (RectTransform)transform;
            Vector2 size = root.rect.size;
            if (size.x <= 0f || size.y <= 0f) size = new Vector2(320f, 200f);
            _pageWidth = size.x;

            ApplyPanelStyle(root);

            var viewport = new GameObject("PageViewport", typeof(RectTransform), typeof(RectMask2D))
                .GetComponent<RectTransform>();
            viewport.SetParent(root, false);
            Stretch(viewport);

            _content = new GameObject("PageContent", typeof(RectTransform)).GetComponent<RectTransform>();
            _content.SetParent(viewport, false);
            Stretch(_content);

            var game = AddPage("Page_Partida");
            foreach (var label in new[] { roleText, timerText, hidersText, statusText })
            {
                if (label == null) continue;
                label.transform.SetParent(game, false);
                StyleLabel(label);
            }
            BuildGamePage(game);

            BuildTeamsPage(AddPage("Page_Equipos"));

            if (HideAndSeekTestMode.Enabled)
            {
                BuildTestPage(AddPage("Page_Prueba"));
            }

            BuildPageDots(root, size);
            ShowPage(0, instant: true);
        }

        /// <summary>
        /// Recuadro redondeado con borde: el Background serializado pasa a ser el borde (sprite
        /// redondeado en panelBorderColor) y se le mete dentro un relleno igual pero encogido
        /// panelBorderWidth por cada lado. Si no hay Background (montaje manual) se crea.
        /// </summary>
        private void ApplyPanelStyle(RectTransform root)
        {
            Transform background = root.Find("Background");
            var border = background != null ? background.GetComponent<Image>() : null;
            if (border == null)
            {
                border = new GameObject("Background", typeof(Image)).GetComponent<Image>();
                border.transform.SetParent(root, false);
                border.transform.SetAsFirstSibling();
                Stretch(border.rectTransform);
            }

            Sprite sprite = HudRoundedSprite.Get();
            border.sprite = sprite;
            border.type = Image.Type.Sliced;
            border.pixelsPerUnitMultiplier = 1f;
            border.color = panelBorderColor;
            border.raycastTarget = false;

            var fill = new GameObject("BackgroundFill", typeof(Image)).GetComponent<Image>();
            fill.transform.SetParent(border.transform, false);
            Stretch(fill.rectTransform);
            fill.rectTransform.offsetMin = new Vector2(panelBorderWidth, panelBorderWidth);
            fill.rectTransform.offsetMax = new Vector2(-panelBorderWidth, -panelBorderWidth);
            fill.sprite = sprite;
            fill.type = Image.Type.Sliced;
            // Radio algo menor que el del borde para que el grosor se vea parejo en las esquinas.
            fill.pixelsPerUnitMultiplier = HudRoundedSprite.Radius / Mathf.Max(1f, HudRoundedSprite.Radius - panelBorderWidth);
            fill.color = panelColor;
            fill.raycastTarget = false;
        }

        private TMP_FontAsset HudFont => font != null ? font : statusText != null ? statusText.font : null;

        /// <summary>
        /// Fuente y color del HUD. Se quita Bold porque la fuente ya es bold de por si.
        /// HemiHead es ancha: en vez de partir en dos lineas (y pisar el boton de debajo) el texto
        /// se queda en una y encoge hasta la mitad de su tamano si no cabe.
        /// </summary>
        private void StyleLabel(TextMeshProUGUI label)
        {
            if (HudFont != null) label.font = HudFont;
            label.fontStyle &= ~FontStyles.Bold;
            label.color = textColor;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = label.fontSize * 0.5f;
            label.enableAutoSizing = true;
        }

        private RectTransform AddPage(string name)
        {
            var page = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            page.SetParent(_content, false);
            Stretch(page);
            // Con offsets iguales a los dos lados la pagina conserva el tamano del canvas y solo
            // se corre una pantalla a la derecha por cada pagina anterior.
            float x = _pages.Count * _pageWidth;
            page.offsetMin = new Vector2(x, 0f);
            page.offsetMax = new Vector2(x, 0f);

            _pages.Add(page);
            _pageButtons.Add(new List<HudPokeButton>());
            return page;
        }

        /// Puntos de pagina en el borde inferior. Van fuera del viewport para que no se deslicen.
        private void BuildPageDots(RectTransform root, Vector2 size)
        {
            if (PageCount < 2) return;

            const float dotSize = 7f;
            const float spacing = 14f;
            float first = -(PageCount - 1) * 0.5f * spacing;

            for (int i = 0; i < PageCount; i++)
            {
                var dot = new GameObject($"PageDot_{i}", typeof(Image));
                dot.transform.SetParent(root, false);

                var rect = dot.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(dotSize, dotSize);
                rect.anchoredPosition = new Vector2(first + i * spacing, -size.y * 0.5f + 8f);

                var image = dot.GetComponent<Image>();
                image.raycastTarget = false;
                // Mismo sprite que el panel, con el radio a la mitad del punto: sale redondo.
                image.sprite = HudRoundedSprite.Get();
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = HudRoundedSprite.Radius / (dotSize * 0.5f);
                _pageDots.Add(image);
            }
        }

        /// <summary>
        /// Joystick derecho a la derecha = pagina siguiente, a la izquierda = anterior. Solo cuenta
        /// el flanco: hay que volver al centro para pasar otra. El joystick derecho no gira la
        /// camara (PlayerRig apaga su turner), asi que aqui no hay conflicto.
        /// </summary>
        private void PollSwipe()
        {
            if (PageCount < 2) return;

            float x = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).x;

            if (!_stickCentered)
            {
                if (Mathf.Abs(x) < swipeRearm) _stickCentered = true;
                return;
            }

            if (Mathf.Abs(x) < swipeThreshold) return;
            _stickCentered = false;

            int target = Mathf.Clamp(_page + (x > 0f ? 1 : -1), 0, PageCount - 1);
            if (target == _page) return;

            ShowPage(target, instant: false);
            StartCoroutine(PulseRoutine(OVRInput.Controller.RTouch));
        }

        private void ShowPage(int page, bool instant)
        {
            _page = page;

            // Los botones de la pagina que llega se arman: un dedo que ya estaba en esa zona del
            // aire no cuenta como pulsacion.
            foreach (var button in _pageButtons[page]) button.ArmInside();

            for (int i = 0; i < _pageDots.Count; i++)
            {
                _pageDots[i].color = i == page ? pageDotOnColor : pageDotOffColor;
            }

            if (instant && _content != null)
            {
                _content.anchoredPosition = new Vector2(-page * _pageWidth, 0f);
                _slideVelocity = 0f;
            }
        }

        private void AnimatePages()
        {
            if (_content == null) return;

            float target = -_page * _pageWidth;
            Vector2 position = _content.anchoredPosition;
            if (Mathf.Approximately(position.x, target)) return;

            position.x = Mathf.SmoothDamp(position.x, target, ref _slideVelocity, pageSlideTime);
            if (Mathf.Abs(position.x - target) < 0.5f) position.x = target;
            _content.anchoredPosition = position;
        }

        // ----------------------------------------------------- pagina 0: Partida

        /// INICIAR va en el hueco de HidersText, que durante el lobby esta vacio, para no recolocar nada.
        private void BuildGamePage(RectTransform page)
        {
            _startButton = AddButton(GamePage, page, "StartButton", "INICIAR", startButtonSize,
                new Vector2(0f, -28f), 26f);
            PaintButton(_startButton, false, startEnabledColor);
            _startButton.Visible = false;
        }

        /// <summary>
        /// Muestra el boton INICIAR solo en el HUD del host y solo en el lobby, lo pinta segun
        /// haya gente suficiente o no, y recoge la pulsacion.
        /// </summary>
        private void UpdateStartButton(HideAndSeekManager manager, GameState state, bool hasPoint, Vector3 point)
        {
            bool show = state == GameState.WaitingForPlayers && manager.IsLocalHost;
            _startButton.Visible = show;

            // El estado del dedo se sigue actualizando aunque no se acepte la pulsacion, para
            // que no cuente como entrada nueva un dedo que ya estaba encima del boton.
            bool poked = _startButton.Poll(hasPoint, point, pokeDepth);
            if (!show) return;

            // Sin gente suficiente el boton se ve, pero esta inerte: asi el host sabe que existe
            // y que le falta alguien, en vez de verlo aparecer de la nada.
            bool ready = manager.CanStartRound;
            PaintButton(_startButton, ready, startEnabledColor);

            if (!ready || !CanPress(GamePage)) return;
            if (!poked && !ShortcutDown(startShortcut)) return;

            manager.RequestStartRound();
            ConfirmPress();
        }

        // ----------------------------------------------------- pagina 1: Equipos

        private void BuildTeamsPage(RectTransform page)
        {
            CreateText(page, "TeamsTitle", new Vector2(0f, 72f), 28f, FontStyles.Normal).text = "EQUIPOS";

            _seekersValueText = CreateText(page, "SeekersValue", new Vector2(0f, 24f), 30f, FontStyles.Normal);
            _seekersValueText.rectTransform.sizeDelta = new Vector2(160f, 56f);
            _seekersValueText.color = seekerColor;

            var buttonSize = new Vector2(56f, 44f);
            _seekersMinus = AddButton(TeamsPage, page, "SeekersMinus", "-", buttonSize, new Vector2(-115f, 24f), 30f);
            _seekersPlus = AddButton(TeamsPage, page, "SeekersPlus", "+", buttonSize, new Vector2(115f, 24f), 30f);

            _teamHidersText = CreateText(page, "TeamsHiders", new Vector2(0f, -24f), 22f, FontStyles.Normal);
            _teamHidersText.color = hiderColor;

            _teamHintText = CreateText(page, "TeamsHint", new Vector2(0f, -64f), 18f, FontStyles.Italic);
        }

        /// <summary>
        /// Cuantos buscan; el resto se esconde. Todos ven los numeros, pero solo el host los cambia
        /// y solo en el lobby. El manager vuelve a validar los limites (1 .. capacidad - 1).
        /// </summary>
        private void UpdateTeamsPage(HideAndSeekManager manager, GameState state, bool hasPoint, Vector3 point)
        {
            int seekers = manager.SeekerCount.Value;
            bool lobby = state == GameState.WaitingForPlayers;
            bool editable = lobby && manager.IsLocalHost;

            SetText(_seekersValueText, seekers == 1 ? "1 busca" : $"{seekers} buscan");
            SetText(_teamHidersText, lobby
                ? $"Se esconden: el resto ({Mathf.Max(0, manager.PlayerCount - seekers)})"
                : "Se esconden: el resto");

            string hint;
            if (!lobby) hint = "Se cambia entre rondas";
            else if (!manager.IsLocalHost) hint = "Lo elige el anfitrion";
            else if (manager.PlayerCount < manager.PlayersNeeded) hint = $"Hacen falta {manager.PlayersNeeded} jugadores";
            else hint = "A: mas    B: menos";
            SetText(_teamHintText, hint);

            _seekersMinus.Visible = editable;
            _seekersPlus.Visible = editable;

            bool canLess = editable && seekers > 1;
            bool canMore = editable && seekers < manager.MaxSeekers;
            PaintButton(_seekersMinus, canLess, buttonIdleColor);
            PaintButton(_seekersPlus, canMore, buttonIdleColor);

            bool pokedLess = _seekersMinus.Poll(hasPoint, point, pokeDepth);
            bool pokedMore = _seekersPlus.Poll(hasPoint, point, pokeDepth);
            if (!editable || !CanPress(TeamsPage)) return;

            if (canMore && (pokedMore || ShortcutDown(startShortcut)))
            {
                manager.RequestSetSeekerCount(seekers + 1);
                ConfirmPress();
            }
            else if (canLess && (pokedLess || ShortcutDown(secondaryShortcut)))
            {
                manager.RequestSetSeekerCount(seekers - 1);
                ConfirmPress();
            }
        }

        // ------------------------------------------------------ pagina 2: Prueba

        private void BuildTestPage(RectTransform page)
        {
            var title = CreateText(page, "TestTitle", new Vector2(0f, 72f), 26f, FontStyles.Normal);
            title.text = "MODO PRUEBA";
            title.color = testModeColor;

            var buttonSize = new Vector2(140f, 56f);
            _testSeekButton = AddButton(TestPage, page, "TestSeek", "BUSCAR", buttonSize, new Vector2(-76f, 12f), 22f);
            _testHideButton = AddButton(TestPage, page, "TestHide", "ESCONDERSE", buttonSize, new Vector2(76f, 12f), 22f);

            _testHintText = CreateText(page, "TestHint", new Vector2(0f, -50f), 18f, FontStyles.Italic);
        }

        /// <summary>
        /// Modo prueba: durante la ronda el jugador elige su rol. El rol actual sale en el color del
        /// boton principal y el otro en el neutro. No se usan los colores de rol como relleno: el
        /// del buscador es negro y se comeria la etiqueta. Lo valida el servidor en RequestTestRoleServerRpc.
        /// </summary>
        private void UpdateTestPage(HideAndSeekManager manager, GameState state, PlayerRole role,
                                    bool hasPoint, Vector3 point)
        {
            if (_testSeekButton == null) return;

            bool inRound = state == GameState.Hiding || state == GameState.Seeking;
            bool usable = inRound && manager.IsSpawned;

            _testSeekButton.Visible = true;
            _testHideButton.Visible = true;
            PaintButton(_testSeekButton, usable, role == PlayerRole.Seeker ? startEnabledColor : buttonIdleColor);
            PaintButton(_testHideButton, usable, role == PlayerRole.Hider ? startEnabledColor : buttonIdleColor);

            SetText(_testHintText, usable ? "A: buscar    B: esconderse" : "Disponible durante la ronda");

            bool pokedSeek = _testSeekButton.Poll(hasPoint, point, pokeDepth);
            bool pokedHide = _testHideButton.Poll(hasPoint, point, pokeDepth);
            if (!usable || !CanPress(TestPage)) return;

            PlayerRole wanted = PlayerRole.Unassigned;
            if (pokedSeek || ShortcutDown(startShortcut)) wanted = PlayerRole.Seeker;
            else if (pokedHide || ShortcutDown(secondaryShortcut)) wanted = PlayerRole.Hider;

            if (wanted == PlayerRole.Unassigned || wanted == role) return;

            manager.RequestTestRoleServerRpc(wanted);
            ConfirmPress();
        }

        // ------------------------------------------------------------ botones

        private HudPokeButton AddButton(int page, RectTransform parent, string name, string text,
                                        Vector2 size, Vector2 position, float fontSize)
        {
            var button = HudPokeButton.Create(parent, name, text, size, position, fontSize, HudFont, buttonLabelColor);
            _pageButtons[page].Add(button);
            return button;
        }

        /// Relleno <paramref name="fill"/> si se puede pulsar; si no, gris con la etiqueta atenuada.
        private void PaintButton(HudPokeButton button, bool enabled, Color fill)
        {
            if (enabled) button.SetColors(fill, buttonLabelColor);
            else button.SetColors(startDisabledColor, disabledLabelColor);
        }

        private void ArmAllButtons()
        {
            foreach (var buttons in _pageButtons)
            {
                foreach (var button in buttons) button.ArmInside();
            }
        }

        private void SetAllButtonsVisible(bool value)
        {
            foreach (var buttons in _pageButtons)
            {
                foreach (var button in buttons) button.Visible = value;
            }
        }

        /// Se puede pulsar en esa pagina: esta a la vista, quieta y fuera del tiempo muerto.
        private bool CanPress(int page) => PageSettled(page) && Time.time >= _nextPressTime;

        private static bool ShortcutDown(OVRInput.Button button)
        {
            return OVRInput.GetDown(button, OVRInput.Controller.RTouch);
        }

        private void ConfirmPress()
        {
            _nextPressTime = Time.time + pokeCooldown;
            StartCoroutine(PulseRoutine(OVRInput.Controller.RTouch));
        }

        /// <summary>
        /// De donde sale el toque: la yema del indice derecho con hand tracking, y la punta
        /// aproximada del mando derecho cuando se juega con mandos.
        /// </summary>
        private bool TryGetPokePoint(out Vector3 point)
        {
            if (OVRInput.GetControllerPositionValid(OVRInput.Controller.RHand))
            {
                Transform tip = ResolveRightIndexTip();
                if (tip != null)
                {
                    point = tip.position;
                    return true;
                }
            }

            Transform hand = PlayerRig.Local != null ? PlayerRig.Local.RightHand : null;
            if (hand != null)
            {
                point = hand.position + hand.forward * controllerPokeReach;
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Yema del indice derecho. Se aceptan los dos esqueletos porque el rig puede correr con
        /// el de OVR o con el de OpenXR, y sus BoneId no coinciden. Bones esta vacia hasta que el
        /// esqueleto se inicializa, asi que si no se encuentra se reintenta en frames siguientes.
        /// </summary>
        private Transform ResolveRightIndexTip()
        {
            if (_rightIndexTip != null) return _rightIndexTip;

            foreach (var skeleton in FindObjectsByType<OVRSkeleton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var type = skeleton.GetSkeletonType();
                if (type != OVRSkeleton.SkeletonType.HandRight && type != OVRSkeleton.SkeletonType.XRHandRight)
                {
                    continue;
                }
                if (!skeleton.IsInitialized || skeleton.Bones == null) continue;

                foreach (var bone in skeleton.Bones)
                {
                    if (bone.Id != OVRSkeleton.BoneId.Hand_IndexTip && bone.Id != OVRSkeleton.BoneId.XRHand_IndexTip)
                    {
                        continue;
                    }
                    _rightIndexTip = bone.Transform;
                    return _rightIndexTip;
                }
            }

            return null;
        }

        // ----------------------------------------------------------------- textos

        private static string RoleLabel(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Seeker: return "BUSCADOR";
                case PlayerRole.Hider: return "ESCONDIDO";
                case PlayerRole.Spectator: return "ELIMINADO";
                default: return "EN ESPERA";
            }
        }

        private Color RoleColor(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Seeker: return seekerColor;
                case PlayerRole.Hider: return hiderColor;
                case PlayerRole.Spectator: return spectatorColor;
                default: return neutralColor;
            }
        }

        private static string StatusLabel(HideAndSeekManager manager, GameState state, PlayerRole role)
        {
            switch (state)
            {
                case GameState.WaitingForPlayers:
                {
                    // El denominador es la capacidad de la sala, no el minimo para jugar: lo que
                    // interesa saber en el lobby es cuanta gente cabe todavia.
                    string waiting = $"Esperando jugadores ({manager.PlayerCount}/{manager.MaxPlayers})";
                    if (!manager.CanStartRound)
                    {
                        // Con varios buscadores hace falta mas gente; se dice cuanta.
                        int missing = manager.PlayersNeeded - manager.PlayerCount;
                        return missing > 0 ? $"{waiting} - faltan {missing}" : waiting;
                    }
                    return manager.IsLocalHost
                        ? $"{waiting} - pulsa INICIAR"
                        : $"{waiting} - el anfitrion inicia";
                }

                case GameState.Hiding:
                    if (role == PlayerRole.Seeker) return "Cuenta atras... no puedes ver";
                    return "Escondete!";

                case GameState.Seeking:
                    if (role == PlayerRole.Seeker) return "Encuentralos y dispara";
                    if (role == PlayerRole.Spectator) return "Te encontraron. Observa la partida";
                    return "Te estan buscando";

                case GameState.RoundOver:
                    return manager.SeekerWonLastRound.Value
                        ? "Fin: gano el buscador"
                        : "Fin: ganaron los escondidos";

                default:
                    return string.Empty;
            }
        }

        /// Mismo estilo que las etiquetas que monta HideAndSeekSceneSetup.
        private TextMeshProUGUI CreateText(Transform parent, string name, Vector2 position, float size,
                                           FontStyles style)
        {
            var go = new GameObject(name, typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(300f, 56f);

            var label = go.GetComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = size;
            label.fontStyle = style;
            label.raycastTarget = false;
            StyleLabel(label);
            label.text = string.Empty;
            return label;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// Segundos a "mm:ss". Los negativos cuentan como cero.
        private static string FormatMMSS(float seconds)
        {
            int total = Mathf.FloorToInt(Mathf.Max(0f, seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        private static void SetText(TextMeshProUGUI label, string value)
        {
            if (label != null && label.text != value)
            {
                label.text = value;
            }
        }
    }
}
