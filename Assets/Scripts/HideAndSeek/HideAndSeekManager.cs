using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Cerebro autoritativo de la partida. Vive en un NetworkObject colocado en la escena
    /// (junto al [BuildingBlock] Network Manager), de modo que existe en todos los clientes
    /// con el mismo NetworkObjectId.
    ///
    /// Solo el servidor cambia de fase, reparte roles y valida los disparos.
    /// Los clientes se limitan a leer las NetworkVariables y reaccionar en local.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class HideAndSeekManager : NetworkBehaviour
    {
        public static HideAndSeekManager Instance { get; private set; }

        [Header("Duracion de las fases (segundos)")]
        [Tooltip("Tiempo que tienen los escondidos antes de que el buscador recupere la vista.")]
        [SerializeField] private float hideDuration = 30f;
        [Tooltip("Tiempo maximo de busqueda. Si se agota, ganan los escondidos.")]
        [SerializeField] private float seekDuration = 300f;
        [Tooltip("Tiempo que se muestra el resultado antes de volver al lobby.")]
        [SerializeField] private float roundOverDuration = 10f;

        [Header("Reglas")]
        [Tooltip("Jugadores necesarios para arrancar una ronda.")]
        [SerializeField] private int minPlayers = 2;

        [Header("Disparo")]
        [Tooltip("Alcance maximo del disparo, en metros.")]
        [SerializeField] private float shotRange = 30f;
        [Tooltip("Tiempo minimo entre disparos, en segundos.")]
        [SerializeField] private float shotCooldown = 0.5f;
        [Tooltip("Capas que detiene el disparo. Debe incluir las paredes y la capa Player.")]
        [SerializeField] private LayerMask shotMask = ~0;

        [Header("Escena")]
        [SerializeField] private SpawnPointSet spawnPoints;

        // ----------------------------------------------------------- estado en red

        public readonly NetworkVariable<GameState> State = new NetworkVariable<GameState>(
            GameState.WaitingForPlayers,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// Momento (en tiempo de servidor) en el que acaba la fase actual.
        public readonly NetworkVariable<double> PhaseEndServerTime = new NetworkVariable<double>(
            0d,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> HidersAlive = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> HidersTotal = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// Resultado de la ultima ronda: true si gano el buscador.
        public readonly NetworkVariable<bool> SeekerWonLastRound = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // -------------------------------------------------------------- eventos

        /// Se dispara en todos los clientes cuando se ejecuta un disparo (para el trazo visual).
        /// Parametros: clientId del que dispara, origen, punto final, si acerto.
        public event Action<ulong, Vector3, Vector3, bool> ShotFired;
        /// Se dispara en el cliente local cuando le han acertado.
        public event Action LocalPlayerHit;

        public int MinPlayers => minPlayers;

        /// Segundos que quedan de la fase actual (0 si la fase no tiene cuenta atras).
        public float PhaseTimeRemaining
        {
            get
            {
                if (State.Value == GameState.WaitingForPlayers) return 0f;
                if (NetworkManager == null) return 0f;
                return Mathf.Max(0f, (float)(PhaseEndServerTime.Value - ServerNow));
            }
        }

        public PlayerRole LocalRole =>
            NetworkPlayer.Local != null ? NetworkPlayer.Local.Role.Value : PlayerRole.Unassigned;

        private readonly Dictionary<ulong, double> _lastShotTime = new Dictionary<ulong, double>();

        // Cache para no reaplicar el estado local en cada frame.
        private GameState _appliedState = (GameState)255;
        private PlayerRole _appliedRole = (PlayerRole)255;

        private void Awake()
        {
            Instance = this;
            if (spawnPoints == null)
            {
                spawnPoints = FindAnyObjectByType<SpawnPointSet>();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
            }
        }

        private void Update()
        {
            // IsServer se queda en true un rato despues de que el NetworkManager desaparezca
            // (al parar el juego, al cerrar el host o al perder la conexion), y en ese hueco
            // NetworkManager.Singleton ya es null. Sin esta guardia, TickServer petaba con un
            // NullReferenceException cada vez que se salia de Play mode.
            if (IsServer && IsSpawned && NetworkManager != null)
            {
                TickServer();
            }
            RefreshLocalPlayer();
        }

        /// <summary>
        /// Reloj del servidor. Devuelve 0 si la red ya no esta viva, para que ningun sitio
        /// tenga que repetir la comprobacion de null.
        /// </summary>
        private double ServerNow
        {
            get
            {
                var manager = NetworkManager;
                return manager != null ? manager.ServerTime.Time : 0d;
            }
        }

        // ------------------------------------------------------- maquina de estados

        private void TickServer()
        {
            double now = ServerNow;

            switch (State.Value)
            {
                case GameState.WaitingForPlayers:
                    if (NetworkPlayer.All.Count >= minPlayers)
                    {
                        StartRound();
                    }
                    break;

                case GameState.Hiding:
                    if (now >= PhaseEndServerTime.Value)
                    {
                        BeginSeeking(now);
                    }
                    break;

                case GameState.Seeking:
                    if (HidersAlive.Value <= 0)
                    {
                        EndRound(seekerWon: true, now);
                    }
                    else if (now >= PhaseEndServerTime.Value)
                    {
                        EndRound(seekerWon: false, now);
                    }
                    break;

                case GameState.RoundOver:
                    if (now >= PhaseEndServerTime.Value)
                    {
                        ReturnToLobby();
                    }
                    break;
            }
        }

        private void StartRound()
        {
            var players = new List<NetworkPlayer>(NetworkPlayer.All);
            players.RemoveAll(p => p == null);
            if (players.Count < minPlayers) return;

            int seekerIndex = UnityEngine.Random.Range(0, players.Count);
            var hiders = new List<NetworkPlayer>();

            for (int i = 0; i < players.Count; i++)
            {
                if (i == seekerIndex)
                {
                    players[i].SetRole(PlayerRole.Seeker);
                }
                else
                {
                    players[i].SetRole(PlayerRole.Hider);
                    hiders.Add(players[i]);
                }
            }

            HidersTotal.Value = hiders.Count;
            HidersAlive.Value = hiders.Count;

            PlaceSeeker(players[seekerIndex]);
            PlaceHiders(hiders);

            double now = ServerNow;
            PhaseEndServerTime.Value = now + hideDuration;
            State.Value = GameState.Hiding;
            _lastShotTime.Clear();
        }

        private void BeginSeeking(double now)
        {
            PhaseEndServerTime.Value = now + seekDuration;
            State.Value = GameState.Seeking;
        }

        private void EndRound(bool seekerWon, double now)
        {
            SeekerWonLastRound.Value = seekerWon;
            PhaseEndServerTime.Value = now + roundOverDuration;
            State.Value = GameState.RoundOver;
        }

        private void ReturnToLobby()
        {
            foreach (var player in NetworkPlayer.All)
            {
                if (player != null) player.SetRole(PlayerRole.Unassigned);
            }
            HidersAlive.Value = 0;
            HidersTotal.Value = 0;
            State.Value = GameState.WaitingForPlayers;
        }

        /// <summary>
        /// Si se va alguien a media partida hay que recalcular. Si el que se fue era el buscador,
        /// la ronda se corta; si era un escondido, baja el contador.
        /// NetworkPlayer.All ya se limpia solo en OnNetworkDespawn.
        /// </summary>
        private void HandleClientDisconnect(ulong clientId)
        {
            if (!IsServer) return;
            if (State.Value == GameState.WaitingForPlayers || State.Value == GameState.RoundOver) return;

            bool seekerStillHere = false;
            int hidersLeft = 0;
            foreach (var player in NetworkPlayer.All)
            {
                if (player == null || player.OwnerClientId == clientId) continue;
                if (player.Role.Value == PlayerRole.Seeker) seekerStillHere = true;
                if (player.Role.Value == PlayerRole.Hider) hidersLeft++;
            }

            HidersAlive.Value = hidersLeft;

            double now = ServerNow;
            if (!seekerStillHere || hidersLeft == 0)
            {
                EndRound(seekerWon: !seekerStillHere ? false : true, now);
            }
        }

        // ------------------------------------------------------------- posiciones

        private void PlaceSeeker(NetworkPlayer seeker)
        {
            if (spawnPoints == null || seeker == null) return;
            var point = spawnPoints.GetSeekerPoint();
            if (point != null) TeleportPlayer(seeker.OwnerClientId, point);
        }

        private void PlaceHiders(List<NetworkPlayer> hiders)
        {
            if (spawnPoints == null || hiders.Count == 0) return;
            var points = spawnPoints.TakeRandom(hiders.Count);
            for (int i = 0; i < hiders.Count && i < points.Count; i++)
            {
                TeleportPlayer(hiders[i].OwnerClientId, points[i]);
            }
        }

        private void TeleportPlayer(ulong clientId, Transform point)
        {
            TeleportClientRpc(point.position, point.eulerAngles.y, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });
        }

        [ClientRpc]
        private void TeleportClientRpc(Vector3 position, float yaw, ClientRpcParams clientRpcParams = default)
        {
            if (PlayerRig.Local != null)
            {
                PlayerRig.Local.TeleportTo(position, yaw);
            }
        }

        // ---------------------------------------------------------------- disparo

        /// <summary>
        /// El cliente pide disparar. El servidor revalida el rol, la fase, la cadencia y
        /// vuelve a lanzar el rayo con su propia copia del mundo: el cliente nunca decide
        /// a quien mata.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void ShootServerRpc(Vector3 origin, Vector3 direction, ServerRpcParams serverRpcParams = default)
        {
            ulong senderId = serverRpcParams.Receive.SenderClientId;

            if (State.Value != GameState.Seeking) return;

            NetworkPlayer shooter = FindPlayer(senderId);
            if (shooter == null || shooter.Role.Value != PlayerRole.Seeker) return;

            double now = ServerNow;
            if (_lastShotTime.TryGetValue(senderId, out double last) && now - last < shotCooldown) return;
            _lastShotTime[senderId] = now;

            if (direction.sqrMagnitude < 1e-6f) return;
            direction = direction.normalized;

            Vector3 end = origin + direction * shotRange;
            bool didHit = false;

            var hits = Physics.RaycastAll(origin, direction, shotRange, shotMask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                var victim = hit.collider.GetComponentInParent<NetworkPlayer>();
                // El propio cuerpo del que dispara no bloquea ni cuenta.
                if (victim != null && victim == shooter) continue;

                end = hit.point;
                if (victim != null && victim.Role.Value == PlayerRole.Hider)
                {
                    EliminateHider(victim);
                    didHit = true;
                }
                break;
            }

            ShotFiredClientRpc(senderId, origin, end, didHit);
        }

        private void EliminateHider(NetworkPlayer victim)
        {
            victim.SetRole(PlayerRole.Spectator);
            HidersAlive.Value = Mathf.Max(0, HidersAlive.Value - 1);
            HitClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { victim.OwnerClientId } }
            });
        }

        [ClientRpc]
        private void ShotFiredClientRpc(ulong shooterClientId, Vector3 origin, Vector3 end, bool didHit)
        {
            ShotFired?.Invoke(shooterClientId, origin, end, didHit);
        }

        [ClientRpc]
        private void HitClientRpc(ClientRpcParams clientRpcParams = default)
        {
            LocalPlayerHit?.Invoke();
        }

        private NetworkPlayer FindPlayer(ulong clientId)
        {
            foreach (var player in NetworkPlayer.All)
            {
                if (player != null && player.OwnerClientId == clientId) return player;
            }
            return null;
        }

        // -------------------------------------------------------- estado local

        /// <summary>
        /// Traduce el estado de red a efectos locales: venda en los ojos y bloqueo de la
        /// locomocion del buscador mientras los demas se esconden. Se recalcula cada frame
        /// pero solo se aplica cuando algo cambia.
        /// </summary>
        private void RefreshLocalPlayer()
        {
            var rig = PlayerRig.Local;
            if (rig == null) return;

            GameState state = State.Value;
            PlayerRole role = LocalRole;
            if (state == _appliedState && role == _appliedRole) return;

            _appliedState = state;
            _appliedRole = role;

            bool blindfolded = state == GameState.Hiding && role == PlayerRole.Seeker;
            rig.SetBlindfolded(blindfolded);
        }
    }
}
