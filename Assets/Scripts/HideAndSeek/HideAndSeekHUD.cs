using System.Collections;
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
    /// En el lobby, y solo en el HUD del host, aparece un boton INICIAR que abre la ronda. Se pulsa
    /// tocandolo con la yema del indice derecho, o con el boton A del mando derecho como atajo.
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

        [Header("Boton de inicio (host)")]
        [Tooltip("Atajo con mandos para pulsar INICIAR sin tener que acertarle al panel. " +
                 "One es el boton A del mando derecho.")]
        [SerializeField] private OVRInput.Button startShortcut = OVRInput.Button.One;
        [Tooltip("Grosor en metros de la caja de toque del boton, medida desde el plano del panel.")]
        [SerializeField] private float pokeDepth = 0.02f;
        [Tooltip("Tiempo muerto tras pulsar, para que un roce no cuente dos veces.")]
        [SerializeField] private float pokeCooldown = 0.5f;
        [Tooltip("Con mandos, cuanto se adelanta el punto de toque respecto al anchor del mando " +
                 "derecho para aproximar su punta.")]
        [SerializeField] private float controllerPokeReach = 0.05f;
        [SerializeField] private Vector2 startButtonSize = new Vector2(240f, 44f);
        [SerializeField] private Color startEnabledColor = new Color(0.20f, 0.65f, 0.30f);
        [SerializeField] private Color startDisabledColor = new Color(0.30f, 0.30f, 0.30f, 0.7f);

        [Header("Colores de rol")]
        [SerializeField] private Color seekerColor = new Color(0.95f, 0.35f, 0.25f);
        [SerializeField] private Color hiderColor = new Color(0.35f, 0.70f, 1f);
        [SerializeField] private Color spectatorColor = new Color(0.75f, 0.75f, 0.75f);
        [SerializeField] private Color neutralColor = Color.white;

        private Canvas _canvas;
        private OVRHand _leftHand;
        private Transform _anchor;
        private Transform _head;
        private bool _visible = true;
        private bool _fistClosed;
        private float _nextToggleTime;
        private RectTransform _startButton;
        private Image _startImage;
        private Transform _rightIndexTip;
        private bool _pokeInside;
        private float _nextStartTime;

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
        }

        private void Start()
        {
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

            var manager = HideAndSeekManager.Instance;
            if (manager == null)
            {
                SetText(statusText, "Conectando...");
                SetText(roleText, string.Empty);
                SetText(timerText, string.Empty);
                SetText(hidersText, string.Empty);
                ShowStartButton(false);
                return;
            }

            PlayerRole role = manager.LocalRole;
            GameState state = manager.State.Value;

            SetText(roleText, RoleLabel(role));
            if (roleText != null) roleText.color = RoleColor(role);

            // TimerDef.FormatMMSS ya existe en el proyecto y es estatico: se reutiliza tal cual.
            SetText(timerText, state == GameState.WaitingForPlayers
                ? "--:--"
                : TimerDef.FormatMMSS(manager.PhaseTimeRemaining));

            SetText(hidersText, manager.HidersTotal.Value > 0
                ? $"Escondidos: {manager.HidersAlive.Value}/{manager.HidersTotal.Value}"
                : string.Empty);

            SetText(statusText, StatusLabel(manager, state, role));

            PollStartButton(manager, state);
        }

        // ------------------------------------------------------- encender / apagar

        /// <summary>Enciende o apaga el HUD sin desactivar este GameObject.</summary>
        public void SetVisible(bool value)
        {
            _visible = value;

            // Al reencender se da el dedo por dentro del boton, para que reabrir el HUD con la
            // mano justo delante no cuente como una pulsacion.
            if (value) _pokeInside = true;

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

        // ------------------------------------------------------- boton de inicio

        /// <summary>
        /// Muestra el boton INICIAR solo en el HUD del host y solo en el lobby, lo pinta segun
        /// haya gente suficiente o no, y recoge la pulsacion.
        /// </summary>
        private void PollStartButton(HideAndSeekManager manager, GameState state)
        {
            bool show = state == GameState.WaitingForPlayers && manager.IsLocalHost;
            ShowStartButton(show);
            if (!show) return;

            bool ready = manager.CanStartRound;
            if (_startImage != null)
            {
                _startImage.color = ready ? startEnabledColor : startDisabledColor;
            }

            // Sin gente suficiente el boton se ve, pero esta inerte: asi el host sabe que existe
            // y que le falta alguien, en vez de verlo aparecer de la nada.
            if (!ready || Time.time < _nextStartTime)
            {
                // El estado del dedo se sigue actualizando aunque no se acepte la pulsacion, para
                // que no cuente como entrada nueva un dedo que ya estaba encima del boton.
                FingerJustEnteredButton();
                return;
            }

            bool pressed = OVRInput.GetDown(startShortcut, OVRInput.Controller.RTouch)
                           || FingerJustEnteredButton();
            if (!pressed) return;

            manager.RequestStartRound();
            _nextStartTime = Time.time + pokeCooldown;
            StartCoroutine(PulseRoutine(OVRInput.Controller.RTouch));
        }

        private void ShowStartButton(bool value)
        {
            if (value) EnsureStartButton();
            if (_startButton == null) return;

            if (_startButton.gameObject.activeSelf != value)
            {
                _startButton.gameObject.SetActive(value);
                // Al aparecer se da el dedo por dentro: si no, un dedo que ya estaba en esa zona
                // del aire arrancaria la ronda en el primer frame.
                _pokeInside = value;
            }
        }

        /// <summary>
        /// Crea el boton por codigo bajo el canvas del HUD, con el mismo estilo que las etiquetas
        /// que monta HideAndSeekSceneSetup. Se hace en runtime y no en el editor porque el HUD ya
        /// esta serializado en la escena y el setup no lo vuelve a tocar.
        ///
        /// Va en el hueco de HidersText, que durante el lobby esta vacio, para no recolocar nada.
        /// </summary>
        private void EnsureStartButton()
        {
            if (_startButton != null) return;

            var go = new GameObject("StartButton", typeof(Image));
            go.transform.SetParent(transform, false);

            _startButton = go.GetComponent<RectTransform>();
            _startButton.anchorMin = new Vector2(0.5f, 0.5f);
            _startButton.anchorMax = new Vector2(0.5f, 0.5f);
            _startButton.pivot = new Vector2(0.5f, 0.5f);
            _startButton.anchoredPosition = new Vector2(0f, -28f);
            _startButton.sizeDelta = startButtonSize;

            _startImage = go.GetComponent<Image>();
            _startImage.color = startDisabledColor;
            // No hay EventSystem en la escena, y ademas el toque se resuelve por geometria:
            // que no sea raycast target evita robarle eventos a nadie.
            _startImage.raycastTarget = false;

            var labelGo = new GameObject("Label", typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);

            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.text = "INICIAR";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 26f;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.white;
            label.raycastTarget = false;
        }

        /// <summary>
        /// True el frame en el que el dedo (o la punta del mando) entra en la caja del boton.
        /// Mismo esquema de flanco que el gesto del puno: solo cuenta la entrada, no quedarse
        /// dentro.
        /// </summary>
        private bool FingerJustEnteredButton()
        {
            if (_startButton == null) return false;

            if (!TryGetPokePoint(out Vector3 point))
            {
                _pokeInside = false;
                return false;
            }

            // En espacio local del boton las X/Y salen en unidades de canvas y la Z tambien, asi
            // que el grosor en metros hay que dividirlo por la escala de mundo. Con esto la caja
            // mide igual aunque el rig del escondido este a la mitad de tamano.
            Vector3 local = _startButton.InverseTransformPoint(point);
            Rect rect = _startButton.rect;
            float depth = pokeDepth / Mathf.Max(1e-5f, Mathf.Abs(_startButton.lossyScale.z));

            bool inside = Mathf.Abs(local.x) <= rect.width * 0.5f
                          && Mathf.Abs(local.y) <= rect.height * 0.5f
                          && Mathf.Abs(local.z) <= depth;

            if (inside == _pokeInside) return false;

            _pokeInside = inside;
            return inside;
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
                    if (!manager.CanStartRound) return waiting;
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

        private static void SetText(TextMeshProUGUI label, string value)
        {
            if (label != null && label.text != value)
            {
                label.text = value;
            }
        }
    }
}
