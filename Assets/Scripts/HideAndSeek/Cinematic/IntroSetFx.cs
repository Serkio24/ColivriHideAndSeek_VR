using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Todos los efectos del set de la cinematica controlados por unos pocos numeros. El Timeline
    /// anima estos campos (una sola pista de animacion con todas las curvas) y aqui se traducen a
    /// luces, colores, textos del monitor y post-procesado de la camara de cine.
    ///
    /// Corre tambien en el editor para que el scrub de la ventana Timeline enseñe el resultado.
    /// </summary>
    [ExecuteAlways]
    public class IntroSetFx : MonoBehaviour
    {
        [Header("Valores animados por el Timeline")]
        [Tooltip("0 = luces normales, 1 = apagon total.")]
        [Range(0f, 1f)] public float blackout;
        [Tooltip("Fundido a negro de la imagen (entrada y salida de la cinematica).")]
        [Range(0f, 1f)] public float fade;
        [Tooltip("Luz roja de emergencia, con pulso.")]
        [Range(0f, 1f)] public float emergency;
        [Tooltip("LEDs de los racks: 0 apagados, 1 parpadeando.")]
        [Range(0f, 1f)] public float rackLeds = 1f;
        [Tooltip("Pagina del monitor del servidor: 0 apagado, 1 reinicio, 2 actualizacion, 3 bateria.")]
        public float monitorPage;
        [Range(0f, 1f)] public float rebootProgress;
        [Range(0f, 1f)] public float battery = 1f;
        [Tooltip("Pilotos de la estacion de carga (verdes mientras cargan los robots).")]
        [Range(0f, 1f)] public float stationGlow = 1f;
        [Tooltip("Chispazo en el enchufe de la estacion.")]
        [Range(0f, 1f)] public float spark;
        [Tooltip("Luces de la cabeza de Aura.")]
        [Range(0f, 1f)] public float auraLights;
        [Tooltip("Brillo del canon de la pistola de datos.")]
        [Range(0f, 1f)] public float muzzleGlow;
        [Tooltip("Rayo de datos: 0 nada, 0..1 el rayo crece del canon al objetivo.")]
        [Range(0f, 1f)] public float beam;
        [Tooltip("El robot alcanzado se tiñe de azul (sincronizado).")]
        [Range(0f, 1f)] public float synced;

        [Header("Referencias (las rellena el builder)")]
        public Volume volume;
        public Light[] setLights = new Light[0];
        public Light[] emergencyLights = new Light[0];
        [Tooltip("Intensidad de cada luz con todo encendido. La guarda el builder: el apagon baja " +
                 "las luces y no se puede leer de ellas sin perder el valor original.")]
        public float[] setLightIntensity = new float[0];
        public float[] emergencyLightIntensity = new float[0];
        public Renderer[] rackLedRenderers = new Renderer[0];
        public TMP_Text monitorTitle;
        public TMP_Text monitorBody;
        public Renderer monitorScreen;
        public GameObject batteryGroup;
        public Transform batteryFill;
        public Renderer batteryFillRenderer;
        public Renderer[] stationLights = new Renderer[0];
        public Light sparkLight;
        public Renderer[] auraLightRenderers = new Renderer[0];
        public Light auraLight;
        public Renderer muzzleRenderer;
        public Light muzzleLight;
        public LineRenderer beamLine;
        public Transform beamFrom;
        public Transform beamTo;
        public Renderer[] syncedRenderers = new Renderer[0];

        [Header("Colores")]
        public Color ledColor = new Color(0.2f, 1f, 0.45f);
        public Color emergencyTint = new Color(1f, 0.62f, 0.58f);
        public Color dataBlue = new Color(0.165f, 0.604f, 0.839f);
        public Color stationColor = new Color(0.25f, 1f, 0.4f);
        public Color auraLightColor = new Color(0.5f, 0.85f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock _block;
        private int _lastPage = -1;
        private int _lastPercent = -1;

        /// Tiempo para parpadeos y pulsos. Lo anima el Timeline en linea recta, asi que el scrub en el
        /// editor y la reproduccion sincronizada dan siempre el mismo parpadeo en el mismo instante.
        public float clock;

        private void OnEnable()
        {
            Apply();
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void OnDisable()
        {
            // Que el perfil (que es un asset) no se quede guardado oscuro tras previsualizar.
            SetPost(0f, 0f, 0f);
        }

        /// Solo como red de seguridad si el builder no guardo las intensidades.
        private void CacheIntensities()
        {
            if (setLightIntensity == null || setLightIntensity.Length != setLights.Length) setLightIntensity = Capture(setLights);
            if (emergencyLightIntensity == null || emergencyLightIntensity.Length != emergencyLights.Length)
                emergencyLightIntensity = Capture(emergencyLights);
        }

        private static float[] Capture(Light[] lights)
        {
            if (lights == null) return new float[0];
            var values = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++)
            {
                values[i] = lights[i] != null ? Mathf.Max(lights[i].intensity, 0.01f) : 0f;
            }
            return values;
        }

        public void Apply()
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            if (setLights == null) setLights = new Light[0];
            if (emergencyLights == null) emergencyLights = new Light[0];
            CacheIntensities();

            float t = clock;
            float pulse = 0.55f + 0.45f * Mathf.Sin(t * 5.5f);

            // --- Luces del set: se apagan con el apagon, la roja pulsa con la emergencia.
            for (int i = 0; i < setLights.Length; i++)
            {
                if (setLights[i] == null) continue;
                setLights[i].intensity = setLightIntensity[i] * (1f - blackout);
                setLights[i].enabled = blackout < 0.999f;
            }
            for (int i = 0; i < emergencyLights.Length; i++)
            {
                if (emergencyLights[i] == null) continue;
                emergencyLights[i].intensity = emergencyLightIntensity[i] * emergency * pulse;
                emergencyLights[i].enabled = emergency > 0.001f;
            }

            // --- Post-procesado de la camara de cine.
            SetPost(blackout, fade, emergency * (0.7f + 0.3f * pulse));

            // --- LEDs de los racks: parpadeo pseudo-aleatorio por LED.
            if (rackLedRenderers != null)
            {
                for (int i = 0; i < rackLedRenderers.Length; i++)
                {
                    if (rackLedRenderers[i] == null) continue;
                    float blink = Mathf.PerlinNoise(i * 3.7f, t * 4f) > 0.45f ? 1f : 0.15f;
                    Color c = Color.Lerp(Color.black, ledColor, rackLeds * blink * (1f - blackout * 0.85f));
                    if (emergency > 0.01f && rackLeds < 0.5f) c = Color.Lerp(c, new Color(1f, 0.2f, 0.1f), emergency * blink);
                    Paint(rackLedRenderers[i], c);
                }
            }

            // --- Estacion de carga.
            if (stationLights != null)
            {
                foreach (var r in stationLights)
                {
                    Paint(r, Color.Lerp(new Color(0.08f, 0.08f, 0.08f), stationColor, stationGlow * (0.75f + 0.25f * pulse)));
                }
            }
            if (sparkLight != null)
            {
                float flicker = Mathf.PerlinNoise(t * 30f, 0.5f) > 0.5f ? 1f : 0.1f;
                sparkLight.enabled = spark > 0.01f;
                sparkLight.intensity = spark * flicker * 6f;
            }

            // --- Monitor del servidor.
            UpdateMonitor(t);

            // --- Aura.
            if (auraLightRenderers != null)
            {
                foreach (var r in auraLightRenderers) Paint(r, Color.Lerp(new Color(0.1f, 0.1f, 0.12f), auraLightColor, auraLights));
            }
            if (auraLight != null)
            {
                auraLight.enabled = auraLights > 0.01f;
                auraLight.intensity = auraLights * 2.5f;
            }
            if (muzzleRenderer != null) Paint(muzzleRenderer, Color.Lerp(new Color(0.1f, 0.1f, 0.1f), dataBlue * 1.4f, muzzleGlow));
            if (muzzleLight != null)
            {
                muzzleLight.enabled = muzzleGlow > 0.01f;
                muzzleLight.intensity = muzzleGlow * 3f;
            }

            // --- Rayo de datos.
            if (beamLine != null)
            {
                bool on = beam > 0.001f && beamFrom != null && beamTo != null;
                beamLine.enabled = on;
                if (on)
                {
                    Vector3 a = beamFrom.position;
                    Vector3 b = Vector3.Lerp(a, beamTo.position, Mathf.Clamp01(beam * 1.6f));
                    beamLine.positionCount = 2;
                    beamLine.SetPosition(0, a);
                    beamLine.SetPosition(1, b);
                    float width = 0.06f * (beam < 0.8f ? 1f : Mathf.InverseLerp(1f, 0.8f, beam) + 0.15f);
                    beamLine.startWidth = width;
                    beamLine.endWidth = width * 0.6f;
                }
            }

            // --- Robot sincronizado.
            if (syncedRenderers != null)
            {
                foreach (var r in syncedRenderers)
                {
                    if (r == null) continue;
                    if (synced <= 0.001f)
                    {
                        r.SetPropertyBlock(null);
                        continue;
                    }
                    float glow = synced * (0.8f + 0.2f * Mathf.Sin(t * 9f));
                    Paint(r, Color.Lerp(Color.white, dataBlue * 1.5f, glow), dataBlue * glow * 0.8f);
                }
            }
        }

        private void UpdateMonitor(float t)
        {
            int page = Mathf.Clamp(Mathf.RoundToInt(monitorPage), 0, 3);
            bool lowBattery = page == 3 && battery < 0.25f;

            if (monitorScreen != null)
            {
                Color bg;
                switch (page)
                {
                    case 0: bg = new Color(0.01f, 0.01f, 0.015f); break;
                    case 2: bg = new Color(0.25f, 0.03f, 0.03f); break;
                    case 3: bg = lowBattery && Mathf.Repeat(t, 0.8f) < 0.4f ? new Color(0.35f, 0.02f, 0.02f) : new Color(0.03f, 0.05f, 0.08f); break;
                    default: bg = new Color(0.03f, 0.05f, 0.1f); break;
                }
                Paint(monitorScreen, bg);
            }

            int percent = page == 1 ? Mathf.RoundToInt(rebootProgress * 100f)
                        : page == 3 ? Mathf.RoundToInt(battery * 100f) : -1;
            if (page != _lastPage || percent != _lastPercent)
            {
                _lastPage = page;
                _lastPercent = percent;
                SetMonitorText(page, percent);
            }

            if (batteryFill != null)
            {
                if (batteryGroup != null) batteryGroup.SetActive(page == 3);
                Vector3 s = batteryFill.localScale;
                s.x = Mathf.Max(0.001f, battery);
                batteryFill.localScale = s;
                // El relleno crece desde la izquierda.
                Vector3 p = batteryFill.localPosition;
                p.x = -0.5f + battery * 0.5f;
                batteryFill.localPosition = p;
                if (batteryFillRenderer != null)
                {
                    Color c = battery > 0.5f ? new Color(0.25f, 0.9f, 0.4f)
                            : battery > 0.25f ? new Color(1f, 0.75f, 0.2f)
                            : new Color(1f, 0.2f, 0.15f);
                    Paint(batteryFillRenderer, c);
                }
            }
        }

        private void SetMonitorText(int page, int percent)
        {
            string title = string.Empty, body = string.Empty;
            switch (page)
            {
                case 1:
                    title = "SERVIDOR CENTRAL";
                    body = $"Reiniciando...\n{Bar(rebootProgress, 14)} {percent}%";
                    break;
                case 2:
                    title = "ACTUALIZACIÓN PENDIENTE";
                    body = "<color=#FFB3A8>Se borrarán los datos de:</color>\nMANTENIMIENTO-01  02  03  04";
                    break;
                case 3:
                    title = "BATERÍA DEL LABORATORIO";
                    body = $"<size=160%>{percent}%</size>";
                    break;
            }
            if (monitorTitle != null) monitorTitle.text = title;
            if (monitorBody != null) monitorBody.text = body;
        }

        private static string Bar(float value, int cells)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(value * cells), 0, cells);
            return "[" + new string('|', filled) + new string('.', cells - filled) + "]";
        }

        private void SetPost(float black, float fadeAmount, float red)
        {
            if (volume == null) return;
            VolumeProfile profile = Application.isPlaying ? volume.profile : volume.sharedProfile;
            if (profile == null) return;

            if (profile.TryGet(out ColorAdjustments color))
            {
                color.postExposure.overrideState = true;
                color.postExposure.value = -black * 3.2f - fadeAmount * 12f;
                color.colorFilter.overrideState = true;
                color.colorFilter.value = Color.Lerp(Color.white, emergencyTint, red);
                color.saturation.overrideState = true;
                color.saturation.value = -black * 40f;
            }
            if (profile.TryGet(out Vignette vignette))
            {
                vignette.intensity.overrideState = true;
                vignette.intensity.value = 0.3f + 0.2f * Mathf.Max(black, red);
            }
        }

        private void Paint(Renderer r, Color c, Color? emission = null)
        {
            if (r == null) return;
            _block.Clear();
            _block.SetColor(BaseColorId, c);
            _block.SetColor(ColorId, c);
            if (emission.HasValue) _block.SetColor(EmissionId, emission.Value);
            r.SetPropertyBlock(_block);
        }
    }
}
