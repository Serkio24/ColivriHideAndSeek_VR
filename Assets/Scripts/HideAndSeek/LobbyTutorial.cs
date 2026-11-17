using UnityEngine;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Tutorial del lobby. Mientras se espera a los jugadores, lleva al jugador local al punto
    /// de espera del buscador y le enseña lo basico por etapas (mirar, manos, HUD, teleport) con
    /// subtitulos que flotan bajo su mirada (<see cref="SubtitleBar"/>).
    ///
    /// La historia ya no va aqui: la cuenta la cinematica (<see cref="IntroCinematic"/>) que ven
    /// todos al pulsar INICIAR. El tablero de la oficina P1 conserva el texto de la escena.
    ///
    /// Componente puramente local, como <see cref="GameMusic"/>: cada cliente lee la fase de la
    /// partida del <see cref="HideAndSeekManager"/> y lleva su propio tutorial, sin nada de red.
    ///
    /// La primera vez que se entra al lobby se hace entero; al volver de una ronda solo se muestra
    /// el mensaje de cierre.
    /// </summary>
    public class LobbyTutorial : MonoBehaviour
    {
        private enum Stage { Bienvenida, Observacion, Manos, Hud, Teleport, Cierre, Fin }

        /// Etapas interactivas, para el "Paso n de N".
        private const Stage FirstStep = Stage.Observacion;
        private const Stage LastStep = Stage.Teleport;

        [Header("Subtitulos")]
        [Tooltip("Franja que sigue la cabeza del jugador.")]
        [SerializeField] private SubtitleBar subtitles;

        [Header("Escena")]
        [Tooltip("Si se deja vacio se busca solo.")]
        [SerializeField] private SpawnPointSet spawnPoints;
        [Tooltip("Circulo al que hay que teletransportarse. Tiene que estar sobre un suelo teleportable.")]
        [SerializeField] private Transform teleportTarget;

        [Header("Textos")]
        [TextArea(2, 4)]
        [SerializeField] private string welcomeText =
            "Bienvenido al laboratorio Colivrí. Mientras llegan los demás jugadores, te enseñamos lo básico.";
        [TextArea(2, 4)]
        [SerializeField] private string lookText = "Mira a tu alrededor para conocer el laboratorio.";
        [TextArea(2, 4)]
        [SerializeField] private string handsText = "Mueve ambas manos para ver tus controles.";
        [TextArea(2, 4)]
        [SerializeField] private string hudText =
            "Cierra el puño izquierdo (o aprieta el grip del mando izquierdo) para abrir o cerrar tu HUD.";
        [TextArea(2, 4)]
        [SerializeField] private string teleportText =
            "Empuja el joystick hacia adelante, apunta al círculo azul y suéltalo para teletransportarte.";
        [TextArea(2, 4)]
        [SerializeField] private string closingText =
            "¡Listo! Espera a que el anfitrión inicie la partida desde su HUD.";

        [Header("Ritmo")]
        [SerializeField] private float welcomeDuration = 6f;
        [Tooltip("Segundos que se queda el mensaje de cierre antes de esconderse.")]
        [SerializeField] private float closingDuration = 8f;

        [Header("Observacion")]
        [Tooltip("Grados que hay que girar la cabeza, sumando en cualquier sentido.")]
        [SerializeField] private float requiredYaw = 300f;
        [Tooltip("Giros mas lentos que esto (grados/s) no cuentan: filtra el temblor de la cabeza.")]
        [SerializeField] private float minAngularSpeed = 5f;

        [Header("Manos")]
        [Tooltip("Metros que tiene que moverse cada mano respecto al rig.")]
        [SerializeField] private float handMoveDelta = 0.15f;

        [Header("Teleport")]
        [Tooltip("Radio, en metros, alrededor del circulo en el que la cabeza cuenta como dentro.")]
        [SerializeField] private float targetRadius = 0.6f;

        [Header("Audio")]
        [Tooltip("Suena al completar cada etapa. Opcional.")]
        [SerializeField] private AudioClip stageCompleteClip;
        [Range(0f, 1f)]
        [SerializeField] private float volume = 0.8f;

        private Stage _stage;
        private float _stageTime;
        private bool _inLobby;
        /// Ya se hizo el tutorial entero una vez: al volver al lobby se salta las etapas.
        private bool _completedOnce;

        private HideAndSeekHUD _hud;
        private AudioSource _audio;

        private float _obsAccumYaw;
        private Vector3 _lastForwardFlat;
        private bool _leftMoved, _rightMoved;
        private Vector3 _left0, _right0;
        private bool _hudVisible0;

        private void Awake()
        {
            if (spawnPoints == null)
            {
                spawnPoints = FindAnyObjectByType<SpawnPointSet>();
            }

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;

            SetTargetVisible(false);
        }

        private void Start()
        {
            _hud = FindAnyObjectByType<HideAndSeekHUD>(FindObjectsInactive.Include);
            if (subtitles != null) subtitles.SnapToHead();
            // Antes de conectarse ya se da la bienvenida.
            Show(welcomeText);
        }

        private void Update()
        {
            var manager = HideAndSeekManager.Instance;
            bool online = manager != null && manager.IsSpawned;
            bool lobby = online && manager.State.Value == GameState.WaitingForPlayers;

            // Sin rig no se puede llevar al jugador ni medir nada: se espera a que exista.
            if (lobby && PlayerRig.Local == null) return;

            if (lobby != _inLobby)
            {
                _inLobby = lobby;
                if (lobby) EnterLobby(manager);
                else LeaveLobby(online);
            }

            if (_inLobby) TickStage();
        }

        // ------------------------------------------------------------- lobby

        private void EnterLobby(HideAndSeekManager manager)
        {
            MoveToWaitPoint(manager);
            // Al volver de una ronda ya no hace falta la bienvenida: solo el recordatorio final.
            GoTo(_completedOnce ? Stage.Cierre : Stage.Bienvenida);
        }

        private void LeaveLobby(bool online)
        {
            SetTargetVisible(false);
            // Con conexion es que empieza la cinematica o la ronda: los subtitulos se apartan.
            // Si se perdio la conexion se vuelve a la bienvenida hasta reconectar.
            Show(online ? string.Empty : welcomeText);
        }

        /// <summary>
        /// Lleva al jugador al punto de espera del buscador, frente al tablero. Cada jugador se
        /// desplaza a un lado segun su clientId (el mismo hueco que le da el manager en la
        /// cinematica), para que no aparezcan uno dentro de otro.
        /// </summary>
        private void MoveToWaitPoint(HideAndSeekManager manager)
        {
            if (spawnPoints == null || manager.NetworkManager == null) return;
            Transform point = spawnPoints.GetSeekerWaitPoint();
            if (point == null) return;

            Vector3 offset = manager.WaitPointOffset(point, manager.NetworkManager.LocalClientId);
            PlayerRig.Local.TeleportTo(point.position + offset, point.eulerAngles.y);
            if (subtitles != null) subtitles.SnapToHead();
        }

        // ------------------------------------------------------------- etapas

        private void GoTo(Stage stage)
        {
            _stage = stage;
            _stageTime = 0f;
            SetTargetVisible(stage == Stage.Teleport);

            var rig = PlayerRig.Local;
            switch (stage)
            {
                case Stage.Bienvenida:
                    Show(welcomeText);
                    break;

                case Stage.Observacion:
                    _obsAccumYaw = 0f;
                    _lastForwardFlat = FlatForward(rig.Head != null ? rig.Head.forward : Vector3.forward);
                    ShowStep(lookText);
                    break;

                case Stage.Manos:
                    _leftMoved = _rightMoved = false;
                    _left0 = HandInRig(rig, rig.LeftHand);
                    _right0 = HandInRig(rig, rig.RightHand);
                    ShowStep(handsText);
                    break;

                case Stage.Hud:
                    if (_hud == null) { Advance(); return; }
                    _hudVisible0 = _hud.IsVisible;
                    ShowStep(hudText);
                    break;

                case Stage.Teleport:
                    if (teleportTarget == null) { Advance(); return; }
                    ShowStep(teleportText);
                    break;

                case Stage.Cierre:
                    _completedOnce = true;
                    Show(closingText);
                    break;

                case Stage.Fin:
                    Show(string.Empty);
                    break;
            }
        }

        private void Advance()
        {
            switch (_stage)
            {
                case Stage.Bienvenida: GoTo(_completedOnce ? Stage.Cierre : FirstStep); break;
                case Stage.Fin: break;
                default: GoTo(_stage + 1); break;
            }
        }

        private void CompleteStage()
        {
            if (stageCompleteClip != null) _audio.PlayOneShot(stageCompleteClip, volume);
            Advance();
        }

        private void TickStage()
        {
            _stageTime += Time.deltaTime;
            var rig = PlayerRig.Local;

            switch (_stage)
            {
                case Stage.Bienvenida:
                    if (_stageTime >= welcomeDuration) Advance();
                    break;

                case Stage.Observacion:
                    if (CheckObservation(rig)) CompleteStage();
                    break;

                case Stage.Manos:
                    if (CheckHandsMoved(rig)) CompleteStage();
                    break;

                case Stage.Hud:
                    if (_hud != null && _hud.IsVisible != _hudVisible0) CompleteStage();
                    break;

                case Stage.Teleport:
                    if (CheckOnTarget(rig)) CompleteStage();
                    break;

                case Stage.Cierre:
                    if (_stageTime >= closingDuration) Advance();
                    break;
            }
        }

        // ------------------------------------------------------------ chequeos

        private bool CheckObservation(PlayerRig rig)
        {
            if (rig.Head == null) return true;
            Vector3 now = FlatForward(rig.Head.forward);
            float delta = Vector3.SignedAngle(_lastForwardFlat, now, Vector3.up);
            if (Mathf.Abs(delta) > minAngularSpeed * Time.deltaTime)
            {
                _obsAccumYaw += Mathf.Abs(delta);
                _lastForwardFlat = now;
            }
            return _obsAccumYaw >= requiredYaw;
        }

        private static Vector3 FlatForward(Vector3 fwd)
        {
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            return fwd.normalized;
        }

        /// <summary>
        /// Las manos se miden en el espacio del rig: si no, girar o teletransportarse contaria
        /// como moverlas.
        /// </summary>
        private bool CheckHandsMoved(PlayerRig rig)
        {
            if (rig.LeftHand == null || rig.RightHand == null) return true;
            if (Vector3.Distance(_left0, HandInRig(rig, rig.LeftHand)) >= handMoveDelta) _leftMoved = true;
            if (Vector3.Distance(_right0, HandInRig(rig, rig.RightHand)) >= handMoveDelta) _rightMoved = true;
            return _leftMoved && _rightMoved;
        }

        private static Vector3 HandInRig(PlayerRig rig, Transform hand)
        {
            if (hand == null) return Vector3.zero;
            return rig.RigOrigin != null ? rig.RigOrigin.InverseTransformPoint(hand.position) : hand.position;
        }

        /// Cabeza encima del circulo: dentro del radio en planta y en una altura de persona sobre el.
        private bool CheckOnTarget(PlayerRig rig)
        {
            if (rig.Head == null) return true;
            Vector3 d = rig.Head.position - teleportTarget.position;
            float height = d.y;
            d.y = 0f;
            return d.magnitude <= targetRadius && height > -0.5f && height < 2.5f;
        }

        // ---------------------------------------------------------- subtitulos

        private void ShowStep(string instruction)
        {
            int step = _stage - FirstStep + 1;
            int total = LastStep - FirstStep + 1;
            Show($"<color=#7FCBF2>Paso {step} de {total}</color>\n{instruction}");
        }

        private void Show(string text)
        {
            if (subtitles != null) subtitles.Show(text);
        }

        private void SetTargetVisible(bool value)
        {
            if (teleportTarget != null) teleportTarget.gameObject.SetActive(value);
        }
    }
}
