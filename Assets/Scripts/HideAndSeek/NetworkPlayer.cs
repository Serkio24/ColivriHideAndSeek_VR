using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Avatar de red de un jugador. Se spawnea automaticamente por NGO como PlayerPrefab
    /// (NetworkConfig.PlayerPrefab, con AutoSpawnPlayerPrefabClientSide activado).
    ///
    /// El dueno copia cada frame la pose de la cabeza y las manos de su rig local; el resto de
    /// clientes reciben esas poses por NetworkTransform con autoridad de propietario.
    /// El rol lo escribe unicamente el servidor.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkPlayer : NetworkBehaviour
    {
        /// Todos los avatares vivos en esta instancia, incluido el propio.
        public static readonly List<NetworkPlayer> All = new List<NetworkPlayer>();

        /// El avatar del jugador local.
        public static NetworkPlayer Local { get; private set; }

        [Header("Partes del avatar")]
        [Tooltip("Malla de la cabeza. Lleva el collider de impacto.")]
        [SerializeField] private Transform headVisual;
        [Tooltip("Pivote que sigue solo el giro horizontal de la cabeza, para que el cuerpo no se incline.")]
        [SerializeField] private Transform bodyPivot;
        [Tooltip("Malla del cuerpo. Lleva el collider de impacto principal.")]
        [SerializeField] private Transform bodyVisual;
        [Tooltip("Anchor de la mano izquierda. Lleva su propio NetworkTransform.")]
        [SerializeField] private Transform handLeft;
        [Tooltip("Anchor de la mano derecha. Lleva su propio NetworkTransform.")]
        [SerializeField] private Transform handRight;
        [Tooltip("Malla de la mano izquierda. Es hija del anchor para que escalarla no pelee con el NetworkTransform.")]
        [SerializeField] private Transform handLeftVisual;
        [SerializeField] private Transform handRightVisual;

        [Header("Aspecto por rol")]
        [SerializeField] private Color seekerColor = new Color(0.85f, 0.20f, 0.15f);
        [SerializeField] private Color hiderColor = new Color(0.20f, 0.55f, 0.90f);
        [SerializeField] private Color spectatorColor = new Color(0.60f, 0.60f, 0.60f);

        [Tooltip("Escala de las mallas cuando el jugador es un escondido. Debe coincidir con hiderScale de PlayerRig.")]
        [SerializeField] private float hiderVisualScale = 0.5f;

        /// Rol del jugador. Solo el servidor lo escribe.
        public readonly NetworkVariable<PlayerRole> Role = new NetworkVariable<PlayerRole>(
            PlayerRole.Unassigned,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// Nombre visible del jugador. Lo escribe el servidor al conectarse.
        public readonly NetworkVariable<FixedString64Bytes> DisplayName = new NetworkVariable<FixedString64Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Collider> _colliders = new List<Collider>();

        private Vector3 _baseHeadScale = Vector3.one;
        private Vector3 _baseBodyScale = Vector3.one;
        private Vector3 _baseBodyPosition = Vector3.zero;
        private Vector3 _baseHandLeftScale = Vector3.one;
        private Vector3 _baseHandRightScale = Vector3.one;

        private Material _roleMaterial;

        private void Awake()
        {
            GetComponentsInChildren(true, _renderers);
            GetComponentsInChildren(true, _colliders);

            if (headVisual != null) _baseHeadScale = headVisual.localScale;
            if (bodyVisual != null)
            {
                _baseBodyScale = bodyVisual.localScale;
                _baseBodyPosition = bodyVisual.localPosition;
            }
            if (handLeftVisual != null) _baseHandLeftScale = handLeftVisual.localScale;
            if (handRightVisual != null) _baseHandRightScale = handRightVisual.localScale;

            _roleMaterial = HideAndSeekMaterials.CreateLit(Color.white);
            foreach (var r in _renderers)
            {
                r.sharedMaterial = _roleMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsOwner)
            {
                Local = this;
            }

            Role.OnValueChanged += OnRoleChanged;
            ApplyRole(Role.Value, applyToRig: true);
            RefreshAllVisibility();
        }

        public override void OnNetworkDespawn()
        {
            Role.OnValueChanged -= OnRoleChanged;
            All.Remove(this);
            if (Local == this)
            {
                Local = null;
            }
            RefreshAllVisibility();
        }

        private void OnDestroy()
        {
            if (_roleMaterial != null)
            {
                Destroy(_roleMaterial);
            }
        }

        // ------------------------------------------------------------ servidor

        /// <summary>Asigna el rol. Solo tiene efecto en el servidor.</summary>
        public void SetRole(PlayerRole role)
        {
            if (!IsServer) return;
            Role.Value = role;
        }

        /// <summary>Asigna el nombre visible. Solo tiene efecto en el servidor.</summary>
        public void SetDisplayName(string value)
        {
            if (!IsServer) return;
            DisplayName.Value = new FixedString64Bytes(value ?? string.Empty);
        }

        // ------------------------------------------------------- seguimiento

        private void LateUpdate()
        {
            if (IsOwner)
            {
                FollowLocalRig();
            }
            UpdateBodyPivot();
        }

        private void FollowLocalRig()
        {
            var rig = PlayerRig.Local;
            if (rig == null || rig.Head == null) return;

            transform.SetPositionAndRotation(rig.Head.position, rig.Head.rotation);

            if (handLeft != null && rig.LeftHand != null)
            {
                handLeft.SetPositionAndRotation(rig.LeftHand.position, rig.LeftHand.rotation);
            }
            if (handRight != null && rig.RightHand != null)
            {
                handRight.SetPositionAndRotation(rig.RightHand.position, rig.RightHand.rotation);
            }
        }

        /// <summary>
        /// Mantiene el cuerpo vertical: sigue solo el giro horizontal de la cabeza.
        /// Se calcula en todos los clientes a partir de la rotacion ya sincronizada,
        /// asi que no hace falta enviarlo por red.
        /// </summary>
        private void UpdateBodyPivot()
        {
            if (bodyPivot == null) return;

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
            {
                // Mirando recto arriba o abajo: usar el "arriba" de la cabeza como referencia.
                forward = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            }
            if (forward.sqrMagnitude > 1e-4f)
            {
                bodyPivot.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
        }

        // ------------------------------------------------------------- aspecto

        private void OnRoleChanged(PlayerRole previous, PlayerRole current)
        {
            ApplyRole(current, applyToRig: true);
            // Cambiar de rol puede cambiar quien ve a quien (espectadores).
            RefreshAllVisibility();
        }

        private void ApplyRole(PlayerRole role, bool applyToRig)
        {
            float scale = role == PlayerRole.Hider ? hiderVisualScale : 1f;

            if (headVisual != null) headVisual.localScale = _baseHeadScale * scale;
            if (bodyVisual != null)
            {
                bodyVisual.localScale = _baseBodyScale * scale;
                bodyVisual.localPosition = _baseBodyPosition * scale;
            }
            if (handLeftVisual != null) handLeftVisual.localScale = _baseHandLeftScale * scale;
            if (handRightVisual != null) handRightVisual.localScale = _baseHandRightScale * scale;

            if (_roleMaterial != null)
            {
                HideAndSeekMaterials.Tint(_roleMaterial, ColorForRole(role));
            }

            // Un espectador ya no puede recibir disparos.
            foreach (var c in _colliders)
            {
                if (c != null) c.enabled = role != PlayerRole.Spectator;
            }

            if (applyToRig && IsOwner && PlayerRig.Local != null)
            {
                PlayerRig.Local.ApplyRole(role);
            }
        }

        private Color ColorForRole(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Seeker: return seekerColor;
                case PlayerRole.Hider: return hiderColor;
                case PlayerRole.Spectator: return spectatorColor;
                default: return Color.white;
            }
        }

        // --------------------------------------------------------- visibilidad

        /// <summary>
        /// Recalcula quien se ve en esta instancia del juego. Reglas:
        /// - nunca te ves a ti mismo (ya ves tus propias manos con el rig de ISDK);
        /// - un espectador solo es visible para otros espectadores.
        /// </summary>
        public static void RefreshAllVisibility()
        {
            PlayerRole localRole = Local != null ? Local.Role.Value : PlayerRole.Unassigned;
            foreach (var player in All)
            {
                if (player != null) player.RefreshVisibility(localRole);
            }
        }

        private void RefreshVisibility(PlayerRole localRole)
        {
            bool visible = !IsOwner
                           && (Role.Value != PlayerRole.Spectator || localRole == PlayerRole.Spectator);

            foreach (var r in _renderers)
            {
                if (r != null) r.enabled = visible;
            }
        }
    }
}
