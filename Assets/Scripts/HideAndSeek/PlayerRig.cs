using System.Collections.Generic;
using UnityEngine;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Controla el rig VR local: escala segun el rol, teletransporte forzado por el servidor,
    /// bloqueo de la locomocion del buscador durante la fase de escondite y equipar/quitar la pistola.
    ///
    /// Va en un GameObject vacio ("PlayerRoot") que es el padre de OVRCameraRigInteraction.
    /// Se escala ese padre y no el propio OVRCameraRig porque el PlayerLocomotor de ISDK
    /// reposiciona el transform de OVRCameraRig en coordenadas de mundo en cada teleport;
    /// escalando el padre la locomocion sigue funcionando igual, solo que en un espacio escalado.
    /// </summary>
    public class PlayerRig : MonoBehaviour
    {
        /// El rig local. Solo hay uno por instancia del juego.
        public static PlayerRig Local { get; private set; }

        [Header("Referencias del rig (se resuelven solas si se dejan vacias)")]
        [Tooltip("Transform de OVRCameraRig. Es el mismo que PlayerLocomotor usa como _playerOrigin.")]
        [SerializeField] private Transform rigOrigin;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Camera centerEyeCamera;

        [Tooltip("Grupos de interactores de locomocion que se apagan mientras el buscador espera a que los demas se escondan. " +
                 "Si se deja vacio se buscan por nombre.")]
        [SerializeField] private GameObject[] locomotionInteractors;

        [Header("Escala por rol")]
        [Tooltip("Escala del buscador. 1 = tamano real.")]
        [SerializeField] private float seekerScale = 1f;
        [Tooltip("Escala de los escondidos. 0.5 equivale a unos 85 cm de altura.")]
        [SerializeField] private float hiderScale = 0.5f;

        [Header("Pistola")]
        [SerializeField] private GameObject gunPrefab;
        [Tooltip("Desplazamiento de la pistola respecto al anchor de la mano derecha.")]
        [SerializeField] private Vector3 gunLocalPosition = new Vector3(0f, -0.02f, 0.03f);
        [SerializeField] private Vector3 gunLocalEuler = Vector3.zero;

        public Transform Head => head;
        public Transform LeftHand => leftHand;
        public Transform RightHand => rightHand;
        public Transform RigOrigin => rigOrigin;

        /// Escala actual del rig, para que otros sistemas ajusten distancias.
        public float CurrentScale { get; private set; } = 1f;

        private float _baseNearClip = 0.1f;
        private GameObject _gunInstance;

        private void Awake()
        {
            Local = this;
            ResolveReferences();

            if (centerEyeCamera != null)
            {
                _baseNearClip = centerEyeCamera.nearClipPlane;
            }

            SetLocomotionEnabled(true);
        }

        private void OnDestroy()
        {
            if (Local == this)
            {
                Local = null;
            }
        }

        private void ResolveReferences()
        {
            // Mismo patron que usa PlayerNameTagNGO del SDK de Meta para encontrar el rig.
            OVRCameraRig rig = null;
            if (OVRManager.instance != null)
            {
                rig = OVRManager.instance.GetComponentInChildren<OVRCameraRig>();
            }
            if (rig == null)
            {
                rig = GetComponentInChildren<OVRCameraRig>();
            }
            if (rig == null)
            {
                Debug.LogError("[PlayerRig] No se encontro un OVRCameraRig. El rig local no funcionara.", this);
                return;
            }

            if (rigOrigin == null) rigOrigin = rig.transform;
            if (head == null) head = rig.centerEyeAnchor;
            if (leftHand == null) leftHand = rig.leftHandAnchor;
            if (rightHand == null) rightHand = rig.rightHandAnchor;
            if (centerEyeCamera == null && head != null) centerEyeCamera = head.GetComponent<Camera>();

            if (locomotionInteractors == null || locomotionInteractors.Length == 0)
            {
                locomotionInteractors = FindLocomotionInteractors();
            }
        }

        private GameObject[] FindLocomotionInteractors()
        {
            var found = new List<GameObject>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "LocomotionHandInteractorGroup" || t.name == "LocomotionControllerInteractorGroup")
                {
                    found.Add(t.gameObject);
                }
            }
            return found.ToArray();
        }

        // ---------------------------------------------------------------- rol

        /// <summary>Aplica todo lo que depende del rol: escala y pistola.</summary>
        public void ApplyRole(PlayerRole role)
        {
            ApplyScale(role == PlayerRole.Hider ? hiderScale : seekerScale);
            SetGunEquipped(role == PlayerRole.Seeker);
        }

        /// <summary>
        /// Escala el rig entero. Tambien encoge el near clip plane: si no, un jugador pequeno
        /// ve recortada la geometria que tiene delante.
        ///
        /// La escala se aplica alrededor del punto del suelo bajo la cabeza, no del origen de
        /// PlayerRoot. PlayerLocomotor mueve OVRCameraRig en coordenadas de mundo, asi que tras
        /// unos cuantos teleports su offset respecto a PlayerRoot es grande; escalando sobre el
        /// origen ese offset se multiplicaba y el jugador aparecia fuera del mapa al morir
        /// (0,5 -> 1 duplica la distancia).
        /// </summary>
        public void ApplyScale(float scale)
        {
            float previous = transform.localScale.x;
            CurrentScale = Mathf.Max(0.05f, scale);

            if (!Mathf.Approximately(previous, CurrentScale) && previous > 0f)
            {
                Vector3 pivot = transform.position;
                if (head != null && rigOrigin != null)
                {
                    pivot = new Vector3(head.position.x, rigOrigin.position.y, head.position.z);
                }

                transform.localScale = Vector3.one * CurrentScale;
                // Con escala uniforme esto deja el pivote fijo en el mundo: el jugador crece o
                // encoge en el sitio y con los pies en el mismo suelo.
                transform.position = pivot + (transform.position - pivot) * (CurrentScale / previous);
            }

            if (centerEyeCamera != null)
            {
                centerEyeCamera.nearClipPlane = _baseNearClip * CurrentScale;
            }
        }

        // -------------------------------------------------------- teletransporte

        /// <summary>
        /// Coloca al jugador en un punto del mundo mirando hacia yawDegrees.
        /// Usa la misma correccion de offset cabeza-origen que hace PlayerLocomotor
        /// para los teleports normales, de modo que el jugador acaba con los pies en el destino
        /// aunque este fisicamente descentrado dentro de su area de juego.
        /// </summary>
        public void TeleportTo(Vector3 worldPosition, float yawDegrees)
        {
            if (rigOrigin == null || head == null)
            {
                transform.position = worldPosition;
                return;
            }

            // Primero la rotacion, porque cambia donde queda la cabeza respecto al origen.
            Vector3 headBefore = head.position;
            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            Vector3 headDrift = head.position - headBefore;
            headDrift.y = 0f;
            transform.position -= headDrift;

            Vector3 positionOffset = rigOrigin.position - head.position;
            positionOffset.y = 0f;
            Vector3 delta = (worldPosition + positionOffset) - rigOrigin.position;
            transform.position += delta;
        }

        // ----------------------------------------------------------- locomocion

        /// <summary>Enciende o apaga la locomocion (el buscador espera quieto mientras los demas se esconden).</summary>
        public void SetLocomotionEnabled(bool value)
        {
            if (locomotionInteractors == null) return;
            foreach (var go in locomotionInteractors)
            {
                if (go != null) go.SetActive(value);
            }
        }

        // ------------------------------------------------------------- pistola

        public void SetGunEquipped(bool equipped)
        {
            if (equipped)
            {
                if (_gunInstance != null || gunPrefab == null || rightHand == null) return;
                _gunInstance = Instantiate(gunPrefab, rightHand);
                _gunInstance.transform.localPosition = gunLocalPosition;
                _gunInstance.transform.localRotation = Quaternion.Euler(gunLocalEuler);
            }
            else if (_gunInstance != null)
            {
                Destroy(_gunInstance);
                _gunInstance = null;
            }
        }

        /// El transform desde el que sale el disparo, o la mano derecha si no hay pistola.
        public Transform GetMuzzle()
        {
            if (_gunInstance != null)
            {
                var muzzle = _gunInstance.transform.Find("Muzzle");
                return muzzle != null ? muzzle : _gunInstance.transform;
            }
            return rightHand;
        }
    }
}
