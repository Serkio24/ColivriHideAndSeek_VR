using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Disparo del buscador. Es un componente puramente local: vive en el PlayerRoot junto a
    /// <see cref="PlayerRig"/> y solo hace algo cuando el jugador local tiene el rol Seeker.
    ///
    /// El feedback (trazo, vibracion, sonido) se ejecuta al instante sin esperar al servidor,
    /// para que el disparo se sienta inmediato. Quien muere lo decide siempre el servidor en
    /// <see cref="HideAndSeekManager.ShootServerRpc"/>.
    /// </summary>
    public class PlayerGun : MonoBehaviour
    {
        [Header("Entrada")]
        [Tooltip("Gatillo del mando derecho.")]
        [SerializeField] private OVRInput.Button fireButton = OVRInput.Button.SecondaryIndexTrigger;
        [Tooltip("Cadencia local. Debe ser igual o mayor que el shotCooldown del HideAndSeekManager.")]
        [SerializeField] private float fireRate = 0.5f;

        [Header("Trazo")]
        [SerializeField] private float tracerDuration = 0.06f;
        [SerializeField] private float tracerWidth = 0.01f;
        [SerializeField] private Color tracerColor = new Color(1f, 0.85f, 0.3f);
        [Tooltip("Alcance del trazo local cuando el disparo no golpea nada.")]
        [SerializeField] private float tracerRange = 30f;
        [Tooltip("Capas que corta el trazo local. Es solo visual; el impacto real lo calcula el servidor.")]
        [SerializeField] private LayerMask tracerMask = ~0;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip fireClip;
        [SerializeField] private AudioClip hitClip;

        [Header("Vibracion")]
        [SerializeField] private float hapticAmplitude = 0.6f;
        [SerializeField] private float hapticDuration = 0.08f;

        private PlayerRig _rig;
        private float _nextFireTime;
        private LineRenderer _tracer;
        private float _tracerHideTime;

        private void Awake()
        {
            _rig = GetComponent<PlayerRig>();
            if (_rig == null) _rig = PlayerRig.Local;
            CreateTracer();
        }

        private void OnEnable()
        {
            StartCoroutine(SubscribeWhenManagerReady());
        }

        private void OnDisable()
        {
            var manager = HideAndSeekManager.Instance;
            if (manager != null)
            {
                manager.ShotFired -= OnShotFired;
                manager.LocalPlayerHit -= OnLocalPlayerHit;
            }
        }

        /// El manager es un objeto de escena que puede spawnear despues que el rig.
        private IEnumerator SubscribeWhenManagerReady()
        {
            while (HideAndSeekManager.Instance == null)
            {
                yield return null;
            }
            HideAndSeekManager.Instance.ShotFired += OnShotFired;
            HideAndSeekManager.Instance.LocalPlayerHit += OnLocalPlayerHit;
        }

        private void Update()
        {
            if (_tracer != null && _tracer.enabled && Time.time >= _tracerHideTime)
            {
                _tracer.enabled = false;
            }

            var manager = HideAndSeekManager.Instance;
            // IsSpawned: si la red se ha caido, el objeto sigue existiendo pero mandarle un
            // ServerRpc solo produciria errores.
            if (manager == null || !manager.IsSpawned) return;
            if (manager.State.Value != GameState.Seeking) return;
            if (manager.LocalRole != PlayerRole.Seeker) return;
            if (Time.time < _nextFireTime) return;

            if (OVRInput.GetDown(fireButton))
            {
                Fire(manager);
            }
        }

        private void Fire(HideAndSeekManager manager)
        {
            if (_rig == null) _rig = PlayerRig.Local;
            var muzzle = _rig != null ? _rig.GetMuzzle() : null;
            if (muzzle == null) return;

            _nextFireTime = Time.time + fireRate;

            Vector3 origin = muzzle.position;
            Vector3 direction = muzzle.forward;

            // Feedback inmediato en local, sin esperar la respuesta del servidor.
            float range = tracerRange * (_rig != null ? _rig.CurrentScale : 1f);
            Vector3 end = origin + direction * range;
            if (Physics.Raycast(origin, direction, out RaycastHit hit, range, tracerMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
            }
            ShowTracer(origin, end);
            PlayClip(fireClip);
            Pulse();

            manager.ShootServerRpc(origin, direction);
        }

        // -------------------------------------------------------------- eventos

        private void OnShotFired(ulong shooterClientId, Vector3 origin, Vector3 end, bool didHit)
        {
            // El que dispara ya vio su propio trazo al instante; no lo dibujamos dos veces.
            if (NetworkManager.Singleton != null && shooterClientId == NetworkManager.Singleton.LocalClientId)
            {
                if (didHit) PlayClip(hitClip);
                return;
            }

            ShowTracer(origin, end);
            PlayClip(didHit ? hitClip : fireClip);
        }

        private void OnLocalPlayerHit()
        {
            PlayClip(hitClip);
            Pulse(OVRInput.Controller.LTouch);
            Pulse(OVRInput.Controller.RTouch);
        }

        // --------------------------------------------------------------- visual

        private void CreateTracer()
        {
            var go = new GameObject("ShotTracer");
            go.transform.SetParent(transform, false);

            _tracer = go.AddComponent<LineRenderer>();
            _tracer.useWorldSpace = true;
            _tracer.positionCount = 2;
            _tracer.numCapVertices = 0;
            _tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tracer.receiveShadows = false;
            _tracer.material = HideAndSeekMaterials.CreateUnlit(tracerColor);
            _tracer.startColor = tracerColor;
            _tracer.endColor = tracerColor;
            _tracer.enabled = false;
        }

        private void ShowTracer(Vector3 from, Vector3 to)
        {
            if (_tracer == null) return;
            float scale = _rig != null ? _rig.CurrentScale : 1f;
            _tracer.startWidth = tracerWidth * scale;
            _tracer.endWidth = tracerWidth * scale;
            _tracer.SetPosition(0, from);
            _tracer.SetPosition(1, to);
            _tracer.enabled = true;
            _tracerHideTime = Time.time + tracerDuration;
        }

        private void PlayClip(AudioClip clip)
        {
            if (audioSource == null || clip == null) return;
            audioSource.PlayOneShot(clip);
        }

        private void Pulse(OVRInput.Controller controller = OVRInput.Controller.RTouch)
        {
            StartCoroutine(PulseRoutine(controller));
        }

        private IEnumerator PulseRoutine(OVRInput.Controller controller)
        {
            OVRInput.SetControllerVibration(1f, hapticAmplitude, controller);
            yield return new WaitForSeconds(hapticDuration);
            OVRInput.SetControllerVibration(0f, 0f, controller);
        }
    }
}
