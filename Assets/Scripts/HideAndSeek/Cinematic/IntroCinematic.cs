using UnityEngine;
using UnityEngine.Playables;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Reproduce la cinematica de la historia en la pantalla de cine frente al punto de espera.
    ///
    /// Componente local, como <see cref="GameMusic"/> y <see cref="LobbyTutorial"/>: cada cliente
    /// lee la fase del <see cref="HideAndSeekManager"/> y lleva su propio Timeline. La sincronia sale
    /// del reloj del servidor: el Timeline se coloca en <see cref="HideAndSeekManager.IntroElapsed"/>,
    /// asi todos ven el mismo plano a la vez y quien llega tarde entra en el punto correcto.
    ///
    /// La camara de cine solo esta encendida mientras dura la intro: renderiza la escena una segunda
    /// vez (a una RenderTexture), que en Quest no es gratis.
    /// </summary>
    public class IntroCinematic : MonoBehaviour
    {
        [SerializeField] private PlayableDirector director;
        [Tooltip("Actores, atrezo y luces de la cinematica. Solo se activa mientras se reproduce.")]
        [SerializeField] private GameObject setRoot;
        [SerializeField] private Camera cinemaCamera;
        [Tooltip("Pantalla, marco y subtitulos. Se enciende y apaga con una animacion corta.")]
        [SerializeField] private Transform screenRoot;
        [SerializeField] private SubtitleBar subtitles;
        [SerializeField] private SpawnPointSet spawnPoints;
        [Tooltip("Objetos de la escena que estorban la vista de la pantalla (p. ej. las pizarras de la " +
                 "oficina P1). Se apagan mientras dura la cinematica.")]
        [SerializeField] private GameObject[] hideDuringIntro;

        [Header("Ritmo")]
        [Tooltip("Desfase maximo, en segundos, entre el Timeline y el reloj del servidor antes de corregirlo.")]
        [SerializeField] private float maxDrift = 0.15f;
        [SerializeField] private float screenAnimTime = 0.5f;
        [Tooltip("Si el jugador esta mas lejos que esto de la pantalla al empezar (p. ej. entro a mitad), " +
                 "se le lleva al punto de espera.")]
        [SerializeField] private float maxViewerDistance = 4f;

        private bool _playing;
        private bool _placed;
        private float _screenOpen;
        private Vector3 _screenScale = Vector3.one;

        public bool IsPlaying => _playing;

        private void Awake()
        {
            if (spawnPoints == null) spawnPoints = FindAnyObjectByType<SpawnPointSet>();
            if (screenRoot != null) _screenScale = screenRoot.localScale;

            if (director != null)
            {
                director.playOnAwake = false;
                director.timeUpdateMode = DirectorUpdateMode.GameTime;
                director.extrapolationMode = DirectorWrapMode.Hold;
            }

            SetSetActive(false);
            if (screenRoot != null) screenRoot.gameObject.SetActive(false);
        }

        private void Update()
        {
            var manager = HideAndSeekManager.Instance;
            bool intro = manager != null && manager.IsSpawned && manager.State.Value == GameState.Intro;

            if (intro && !_playing) Begin(manager);
            else if (!intro && _playing) End();

            if (_playing) Sync(manager);
            AnimateScreen();
        }

        private void Begin(HideAndSeekManager manager)
        {
            _playing = true;
            _placed = false;

            SetSetActive(true);
            if (screenRoot != null) screenRoot.gameObject.SetActive(true);

            if (director != null)
            {
                director.time = manager.IntroElapsed;
                director.Play();
            }
        }

        private void End()
        {
            _playing = false;
            if (director != null) director.Stop();
            if (subtitles != null) subtitles.Hide();
            SetSetActive(false);
        }

        private void Sync(HideAndSeekManager manager)
        {
            PlaceLateViewer(manager);
            if (director == null) return;

            double target = manager.IntroElapsed;
            if (target >= director.duration) return;

            if (director.state != PlayState.Playing) director.Play();
            if (System.Math.Abs(director.time - target) > maxDrift)
            {
                director.time = target;
            }
        }

        /// <summary>
        /// Al empezar, el servidor ya lleva a todos a su hueco. Esto es para quien entra con la intro
        /// empezada (o aun no tenia rig): si esta lejos de la pantalla, se le acerca.
        /// </summary>
        private void PlaceLateViewer(HideAndSeekManager manager)
        {
            if (_placed) return;
            var rig = PlayerRig.Local;
            if (rig == null || rig.Head == null) return;
            _placed = true;

            if (screenRoot == null || spawnPoints == null || manager.NetworkManager == null) return;
            if (Vector3.Distance(rig.Head.position, screenRoot.position) <= maxViewerDistance) return;

            Transform point = spawnPoints.GetSeekerWaitPoint();
            if (point == null) return;
            Vector3 offset = manager.WaitPointOffset(point, manager.NetworkManager.LocalClientId);
            rig.TeleportTo(point.position + offset, point.eulerAngles.y);
        }

        /// Encendido de "tele vieja": la pantalla se abre en vertical desde una linea.
        private void AnimateScreen()
        {
            if (screenRoot == null) return;

            float target = _playing ? 1f : 0f;
            if (Mathf.Approximately(_screenOpen, target) && screenRoot.gameObject.activeSelf == _playing) return;

            float step = screenAnimTime > 0f ? Time.deltaTime / screenAnimTime : 1f;
            _screenOpen = Mathf.MoveTowards(_screenOpen, target, step);

            float eased = Mathf.SmoothStep(0f, 1f, _screenOpen);
            // Primera mitad: aparece una linea que se alarga. Segunda: la linea se abre en vertical.
            float x = Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(eased * 2f));
            float y = Mathf.Max(0.01f, Mathf.Clamp01((eased - 0.5f) * 2f));
            screenRoot.localScale = new Vector3(_screenScale.x * x, _screenScale.y * y, _screenScale.z);

            if (!_playing && _screenOpen <= 0f) screenRoot.gameObject.SetActive(false);
        }

        private void SetSetActive(bool value)
        {
            if (setRoot != null) setRoot.SetActive(value);
            if (cinemaCamera != null) cinemaCamera.enabled = value;
            if (hideDuringIntro == null) return;
            foreach (var go in hideDuringIntro)
            {
                if (go != null) go.SetActive(!value);
            }
        }
    }
}
