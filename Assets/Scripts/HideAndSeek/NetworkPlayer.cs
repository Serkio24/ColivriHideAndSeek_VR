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

        [Header("Modelo del buscador")]
        [Tooltip("Raiz del Aura. Solo se ve cuando el jugador es el buscador. " +
                 "Lo monta el menu Colivri > Hide and Seek > Configurar avatar del buscador.")]
        [SerializeField] private GameObject seekerVisual;
        [Tooltip("Pivote insertado en el centro de la cabeza del Aura. Copia la inclinacion del casco.")]
        [SerializeField] private Transform seekerHead;
        [Tooltip("Mano izquierda del Aura, colgada de HandL. Solo se ve cuando el jugador es el buscador.")]
        [SerializeField] private GameObject seekerHandLeft;
        [Tooltip("Pistola colgada de HandR. Solo se ve cuando el jugador es el buscador.")]
        [SerializeField] private GameObject seekerHandRight;
        [Tooltip("Emisivo que se suma a los materiales originales del robot para que el rol se lea a distancia.")]
        [SerializeField] private Color seekerEmission = new Color(0.35f, 0.03f, 0.02f);

        [Header("Modelo de los escondidos")]
        [Tooltip("Raiz del NaoRobot. Solo se ve cuando el jugador es un escondido. " +
                 "Lo monta el menu Colivri > Hide and Seek > Configurar avatar del escondido.")]
        [SerializeField] private GameObject hiderVisual;
        [Tooltip("Pivote insertado en el centro de la cabeza del NaoRobot. Copia la inclinacion del casco.")]
        [SerializeField] private Transform hiderHead;
        [Tooltip("Manos del NaoRobot, colgadas de HandL y HandR. Solo se ven cuando el jugador es un escondido.")]
        [SerializeField] private GameObject hiderHandLeft;
        [SerializeField] private GameObject hiderHandRight;
        [Tooltip("Emisivo del NaoRobot. Negro = sin tinte: un escondido que brilla se delata.")]
        [SerializeField] private Color hiderEmission = Color.black;

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

        /// Cabeza y cuerpo de primitivas. Se tintan con el color del rol y se apagan si es el buscador.
        private readonly List<Renderer> _bodyRenderers = new List<Renderer>();
        /// Los dos cubos de las manos. Se tintan igual; solo se ven si no hay manos de robot (espectador/sin rol).
        private readonly List<Renderer> _handRenderers = new List<Renderer>();
        /// Mano izquierda del Aura y pistola. Solo se ven si es el buscador.
        private readonly List<Renderer> _seekerHandRenderers = new List<Renderer>();
        /// Manos del NaoRobot. Solo se ven si es un escondido.
        private readonly List<Renderer> _hiderHandRenderers = new List<Renderer>();
        /// Mallas del Aura. Conservan sus propios materiales; solo se ven si es el buscador.
        private readonly List<Renderer> _seekerRenderers = new List<Renderer>();
        /// Mallas del NaoRobot. Conservan sus propios materiales; solo se ven si es un escondido.
        private readonly List<Renderer> _hiderRenderers = new List<Renderer>();
        private readonly List<Collider> _colliders = new List<Collider>();

        /// Rotacion de reposo de cada pivote de cabeza relativa a BodyPivot (mirando al frente y nivelado).
        private Quaternion _seekerHeadRest = Quaternion.identity;
        private Quaternion _hiderHeadRest = Quaternion.identity;

        private Vector3 _baseHeadScale = Vector3.one;
        private Vector3 _baseBodyScale = Vector3.one;
        private Vector3 _baseBodyPosition = Vector3.zero;
        private Vector3 _baseHandLeftScale = Vector3.one;
        private Vector3 _baseHandRightScale = Vector3.one;

        private Material _roleMaterial;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            GetComponentsInChildren(true, _colliders);

            if (headVisual != null) _baseHeadScale = headVisual.localScale;
            if (bodyVisual != null)
            {
                _baseBodyScale = bodyVisual.localScale;
                _baseBodyPosition = bodyVisual.localPosition;
            }
            if (handLeftVisual != null) _baseHandLeftScale = handLeftVisual.localScale;
            if (handRightVisual != null) _baseHandRightScale = handRightVisual.localScale;

            // Se captura antes del primer LateUpdate, con el prefab todavia en su pose de montaje.
            _seekerHeadRest = HeadRest(seekerHead);
            _hiderHeadRest = HeadRest(hiderHead);

            _roleMaterial = HideAndSeekMaterials.CreateLit(Color.white);
            SortRenderers();
        }

        private Quaternion HeadRest(Transform head)
        {
            if (head == null || bodyPivot == null) return Quaternion.identity;
            return Quaternion.Inverse(bodyPivot.rotation) * head.rotation;
        }

        /// <summary>
        /// Reparte los renderers en los grupos que se tratan distinto: primitivas del cuerpo,
        /// primitivas de las manos, mallas del robot del buscador y mallas del robot del escondido.
        ///
        /// Las de los robots NO reciben el material de rol: machacarlas con un unico material lit
        /// borraria los materiales que trae el GLB y dejaria al robot como una silueta plana. En su
        /// lugar se les mete un emisivo con un MaterialPropertyBlock, que no obliga a instanciar
        /// (ni luego a destruir) un material por malla.
        /// </summary>
        private void SortRenderers()
        {
            var all = new List<Renderer>();
            GetComponentsInChildren(true, all);

            var seekerBlock = EmissionBlock(seekerEmission);
            var hiderBlock = EmissionBlock(hiderEmission);

            foreach (var r in all)
            {
                if (r == null) continue;

                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                if (TryAddRobotRenderer(r, seekerVisual, _seekerRenderers, seekerBlock)) continue;
                if (TryAddRobotRenderer(r, hiderVisual, _hiderRenderers, hiderBlock)) continue;
                // Las manos de robot van antes que los cubos porque cuelgan de los mismos anchors.
                if (TryAddRobotRenderer(r, seekerHandLeft, _seekerHandRenderers, seekerBlock)) continue;
                // La pistola conserva su material tal cual: sin emisivo.
                if (TryAddRobotRenderer(r, seekerHandRight, _seekerHandRenderers, null)) continue;
                if (TryAddRobotRenderer(r, hiderHandLeft, _hiderHandRenderers, hiderBlock)) continue;
                if (TryAddRobotRenderer(r, hiderHandRight, _hiderHandRenderers, hiderBlock)) continue;

                r.sharedMaterial = _roleMaterial;

                bool isHand = (handLeftVisual != null && r.transform.IsChildOf(handLeftVisual))
                              || (handRightVisual != null && r.transform.IsChildOf(handRightVisual));
                if (isHand) _handRenderers.Add(r);
                else _bodyRenderers.Add(r);
            }
        }

        /// <summary>Bloque con el emisivo del robot; null si es negro, para no pisar el emisivo propio del GLB.</summary>
        private static MaterialPropertyBlock EmissionBlock(Color emission)
        {
            if (emission.maxColorComponent <= 0f) return null;
            var block = new MaterialPropertyBlock();
            block.SetColor(EmissionColorId, emission);
            return block;
        }

        private static bool TryAddRobotRenderer(Renderer r, GameObject visual, List<Renderer> list,
                                                MaterialPropertyBlock block)
        {
            if (visual == null || !r.transform.IsChildOf(visual.transform)) return false;
            list.Add(r);
            if (block != null) r.SetPropertyBlock(block);
            return true;
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
            UpdateRobotHead();
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
        ///
        /// De aqui cuelga tambien el modelo del buscador, por lo mismo: si colgase de la raiz,
        /// el robot se inclinaria entero cada vez que el jugador mirase al suelo.
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

        /// <summary>
        /// Gira la cabeza del robot visible para que mire hacia donde mira el casco. El cuerpo ya
        /// lleva el giro horizontal, asi que esto le suma el cabeceo y el ladeo.
        ///
        /// Parte de la rotacion de la raiz, que ya sincroniza el NetworkTransform: no hay nada nuevo
        /// que enviar. En el dueno no se hace porque nadie ve su propio avatar.
        /// </summary>
        private void UpdateRobotHead()
        {
            if (IsOwner) return;

            switch (Role.Value)
            {
                case PlayerRole.Seeker:
                    if (seekerHead != null) seekerHead.rotation = transform.rotation * _seekerHeadRest;
                    break;
                case PlayerRole.Hider:
                    if (hiderHead != null) hiderHead.rotation = transform.rotation * _hiderHeadRest;
                    break;
            }
        }

        // ------------------------------------------------------------- aspecto

        private void OnRoleChanged(PlayerRole previous, PlayerRole current)
        {
            ApplyRole(current, applyToRig: true);
            // Cambiar de rol puede cambiar quien ve a quien (espectadores), y ademas es lo que
            // conmuta entre las primitivas y el robot.
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

            // Los robots no entran en la escala por rol: el Aura solo lo lleva el buscador (tamano
            // completo) y el Nao ya se monta a la altura de un escondido.

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
        /// - un espectador solo es visible para otros espectadores;
        /// - el buscador se ve como el Aura, los escondidos como el NaoRobot y el resto de
        ///   roles (espectador, sin asignar) como las primitivas.
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
            bool seeker = Role.Value == PlayerRole.Seeker;
            bool hider = Role.Value == PlayerRole.Hider;

            // A los robots se les han quitado los brazos (estarian quietos); sus manos flotan en los
            // anchors que siguen a los mandos. El buscador lleva la pistola en la derecha. Los cubos
            // quedan para quien no tiene robot.
            SetEnabled(_bodyRenderers, visible && !seeker && !hider);
            SetEnabled(_handRenderers, visible && !seeker && !hider);
            SetEnabled(_seekerRenderers, visible && seeker);
            SetEnabled(_hiderRenderers, visible && hider);
            SetEnabled(_seekerHandRenderers, visible && seeker);
            SetEnabled(_hiderHandRenderers, visible && hider);
        }

        private static void SetEnabled(List<Renderer> renderers, bool value)
        {
            foreach (var r in renderers)
            {
                if (r != null) r.enabled = value;
            }
        }
    }
}
