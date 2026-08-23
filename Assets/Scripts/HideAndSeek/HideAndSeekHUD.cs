using System.Collections;
using TMPro;
using UnityEngine;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// HUD del jugador local: un canvas world-space anclado a la muneca izquierda que muestra
    /// el rol, el tiempo de la fase y cuantos escondidos quedan.
    ///
    /// Se ancla solo al rig local en cuanto este existe, asi que el prefab se puede dejar
    /// suelto en la escena.
    /// </summary>
    public class HideAndSeekHUD : MonoBehaviour
    {
        [Header("Textos")]
        [SerializeField] private TextMeshProUGUI roleText;
        [SerializeField] private TextMeshProUGUI timerText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI hidersText;

        [Header("Anclaje a la muneca")]
        [Tooltip("Si esta activo, el HUD se hace hijo del anchor de la mano izquierda al arrancar.")]
        [SerializeField] private bool attachToLeftHand = true;
        [SerializeField] private Vector3 localPosition = new Vector3(0f, 0.06f, 0.02f);
        [SerializeField] private Vector3 localEuler = new Vector3(55f, 0f, 0f);
        [SerializeField] private float localScale = 0.0012f;

        [Header("Colores de rol")]
        [SerializeField] private Color seekerColor = new Color(0.95f, 0.35f, 0.25f);
        [SerializeField] private Color hiderColor = new Color(0.35f, 0.70f, 1f);
        [SerializeField] private Color spectatorColor = new Color(0.75f, 0.75f, 0.75f);
        [SerializeField] private Color neutralColor = Color.white;

        private void Start()
        {
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

            var t = transform;
            t.SetParent(PlayerRig.Local.LeftHand, false);
            t.localPosition = localPosition;
            t.localRotation = Quaternion.Euler(localEuler);
            t.localScale = Vector3.one * localScale;
        }

        private void Update()
        {
            var manager = HideAndSeekManager.Instance;
            if (manager == null)
            {
                SetText(statusText, "Conectando...");
                SetText(roleText, string.Empty);
                SetText(timerText, string.Empty);
                SetText(hidersText, string.Empty);
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
        }

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
                    return $"Esperando jugadores ({NetworkPlayerCount()}/{manager.MinPlayers})";

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

        private static int NetworkPlayerCount()
        {
            return NetworkPlayer.All.Count;
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
