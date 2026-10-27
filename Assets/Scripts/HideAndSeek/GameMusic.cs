using UnityEngine;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Musica de fondo. Componente puramente local: cada cliente lee la fase de la partida
    /// del <see cref="HideAndSeekManager"/> y elige la cancion por su cuenta, sin nada de red.
    ///
    /// En partida (Hiding o Seeking) suena la cancion de juego; en cualquier otro momento
    /// (sin conectar, esperando jugadores o viendo el resultado) suena la del lobby.
    /// </summary>
    public class GameMusic : MonoBehaviour
    {
        [Header("Canciones")]
        [SerializeField] private AudioClip lobbySong;
        [SerializeField] private AudioClip inGameSong;

        [Header("Mezcla")]
        [Range(0f, 1f)]
        [SerializeField] private float volume = 0.5f;
        [Tooltip("Segundos que tarda el fundido entre una cancion y la otra.")]
        [SerializeField] private float fadeDuration = 1f;

        private AudioSource _lobbySource;
        private AudioSource _inGameSource;
        private bool _inMatch;

        private void Awake()
        {
            _lobbySource = CreateSource("LobbyMusic", lobbySong);
            _inGameSource = CreateSource("InGameMusic", inGameSong);
        }

        private void Start()
        {
            // Se arranca siempre fuera de partida; si ya hubiera una en curso, Update hace el cambio.
            _inMatch = false;
            _lobbySource.volume = volume;
            _lobbySource.Play();
        }

        private void Update()
        {
            bool inMatch = IsInMatch();
            if (inMatch != _inMatch)
            {
                _inMatch = inMatch;
                var target = inMatch ? _inGameSource : _lobbySource;
                if (!target.isPlaying)
                {
                    target.volume = 0f;
                    target.Play();
                }
            }

            float step = fadeDuration > 0f ? volume * Time.unscaledDeltaTime / fadeDuration : volume;
            Fade(_inGameSource, _inMatch ? volume : 0f, step);
            Fade(_lobbySource, _inMatch ? 0f : volume, step);
        }

        private static bool IsInMatch()
        {
            var manager = HideAndSeekManager.Instance;
            if (manager == null || !manager.IsSpawned) return false;
            var state = manager.State.Value;
            return state == GameState.Hiding || state == GameState.Seeking;
        }

        /// Acerca el volumen al objetivo y para la fuente cuando se ha apagado del todo.
        private static void Fade(AudioSource source, float targetVolume, float step)
        {
            if (!source.isPlaying) return;
            source.volume = Mathf.MoveTowards(source.volume, targetVolume, step);
            if (targetVolume <= 0f && source.volume <= 0f)
            {
                source.Stop();
            }
        }

        private AudioSource CreateSource(string name, AudioClip clip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            return source;
        }
    }
}
