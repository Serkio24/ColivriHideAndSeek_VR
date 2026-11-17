using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace Colivri.HideAndSeek.EditorTools
{
    /// <summary>
    /// Monta la cinematica de la historia y genera su Timeline.
    ///
    ///  - "Crear set y Timeline": rehace todo "[HideAndSeek] Intro" (pantalla, camara de cine,
    ///    actores, atrezo, luces) menos la carpeta Anclas, que se respeta si ya existe para no perder
    ///    los encuadres retocados a mano. Despues genera el Timeline.
    ///  - "Regenerar Timeline desde anclas": solo vuelve a generar el Timeline leyendo las anclas
    ///    (los <see cref="IntroShot"/> de cada plano y los Lugares de los actores).
    ///
    /// El guion (que pasa en cada plano y cuando) esta en <see cref="BuildShots"/> y en las
    /// coreografias de mas abajo. Los subtitulos son el texto de la historia partido por frases.
    /// </summary>
    public static class IntroCinematicBuilder
    {
        private const string ScenePath = "Assets/Scenes/MainModel/MainModel_Env.unity";
        private const string Folder = "Assets/Cinematic";
        private const string MaterialsFolder = "Assets/Cinematic/Materials";
        private const string TimelinePath = "Assets/Cinematic/IntroTimeline.playable";
        private const string RenderTexturePath = "Assets/Cinematic/IntroScreen.renderTexture";
        private const string VolumePath = "Assets/Cinematic/IntroVolume.asset";
        private const string AuraPath = "Assets/Models/RobotsColivri/AuraConManosyluces.glb";
        private const string NaoPath = "Assets/Models/RobotsColivri/NaoRobot.glb";
        private const string PistolPath = "Assets/Prefabs/HideAndSeek/Pistol.prefab";
        private const string FontPath = "Assets/Fonts/hemi_head/HemiHead-Bold SDF.asset";
        private const string GunShotPath = "Assets/Audio/GunShotSound.wav";

        private const string RootName = "[HideAndSeek] Intro";
        private const string CinematicLayerName = "Cinematic";
        private const string ScreenBlockerName = "WhiteboardsP1 (1)";

        private const float AuraHeight = 1.7f;
        /// Giro del hombro (hacia arriba, en grados) que deja la pistola horizontal. En reposo Aura
        /// ya lleva el antebrazo al frente con la pistola algo caida.
        private const float AimAngle = -28f;
        private const float NaoHeight = 0.85f;
        private const float RunSpeed = 2f;
        private const int NaoCount = 4;

        // ------------------------------------------------------------ guion

        /// Subtitulos: el texto de la historia, frase a frase, con su entrada y salida.
        private static readonly (float start, float end, string text)[] Subtitles =
        {
            (0.8f, 12.4f, "Una noche, un apagón reinició el servidor central del laboratorio Colivrí."),
            (12.8f, 21.4f, "Los robots de mantenimiento creyeron que la actualización pendiente los iba a borrar,"),
            (21.8f, 26.6f, "se escaparon de su estación de carga"),
            (27.0f, 33.0f, "y se escondieron por todo el edificio."),
            (33.6f, 39.6f, "Aura, la robot de seguridad, tiene que encontrarlos"),
            (40.0f, 45.6f, "y sincronizarlos con su pistola de datos"),
            (46.0f, 51.6f, "antes de que el laboratorio se quede sin batería."),
            (52.2f, 58.6f, "¿Ayudarás a Aura o te esconderás con los demás?"),
        };

        private const float TotalDuration = 59f;
        /// Un poco de negro al final antes de que el servidor reparta los roles.
        private const float IntroDuration = 59.5f;

        // Momentos clave que comparten camara, actores y efectos.
        private const float TBlackout = 6.7f;
        private const float TEmergency = 9.0f;
        private const float TUpdateNotice = 15.5f;
        private const float TNao2Turns = 18.3f;
        private const float TNao3Turns = 20.1f;
        private const float TEscape = 22.3f;
        private const float THideStart = 26.8f;
        private const float THideShot = 1.6f;
        private const float TAuraReveal = 33.2f;
        private const float TAuraLights = 34.2f;
        private const float TAuraShootPlace = 39.8f;
        private const float TPeek = 42.5f;
        private const float TShot = 43.2f;
        private const float TBattery = 45.8f;
        private const float TWalk = 48.8f;
        private const float TFinal = 51.8f;

        // ---------------------------------------------------------- menus

        [MenuItem("Colivri/Hide and Seek/Cinemática/Crear set y Timeline")]
        public static void CreateAll()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                Debug.LogError($"[Intro] La escena activa es '{scene.path}'. Abre {ScenePath} primero.");
                return;
            }

            Build(scene, Object.FindAnyObjectByType<SpawnPointSet>(FindObjectsInactive.Include));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// Borra las anclas retocadas y vuelve a las del guion por defecto.
        [MenuItem("Colivri/Hide and Seek/Cinemática/Restablecer anclas por defecto")]
        public static void ResetAnchors()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var root = Find(scene, RootName);
            Transform anchors = root != null ? root.transform.Find("Set/Anclas") : null;
            if (anchors != null) Object.DestroyImmediate(anchors.gameObject);
            CreateAll();
        }

        [MenuItem("Colivri/Hide and Seek/Cinemática/Regenerar Timeline desde anclas")]
        public static void RegenerateMenu()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var root = Find(scene, RootName);
            if (root == null)
            {
                Debug.LogError("[Intro] No hay set de la cinematica. Usa primero 'Crear set y Timeline'.");
                return;
            }
            var refs = SetRefs.Collect(root.transform);
            if (refs == null) return;
            GenerateTimeline(refs);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>Monta todo. Lo llama tambien HideAndSeekSceneSetup.Setup.</summary>
        public static void Build(Scene scene, SpawnPointSet spawnPoints)
        {
            EnsureFolder(Folder);
            EnsureFolder(MaterialsFolder);
            int layer = EnsureLayer(CinematicLayerName);

            var root = Find(scene, RootName);
            if (root == null) root = new GameObject(RootName);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            // Las anclas se conservan; lo demas se rehace desde cero.
            Transform keptAnchors = root.transform.Find("Set/Anclas");
            if (keptAnchors != null) keptAnchors.SetParent(null, true);
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            }
            foreach (var c in root.GetComponents<Component>())
            {
                if (c is Transform) continue;
                Object.DestroyImmediate(c);
            }

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            float floorY = FindFloorY(scene);
            Transform waitPoint = spawnPoints != null ? spawnPoints.GetSeekerWaitPoint() : null;

            var refs = new SetRefs { Root = root.transform };

            // --- Pantalla (fuera del set: tiene su propia animacion de encendido).
            var rt = CreateRenderTexture();
            refs.Screen = CreateScreen(root.transform, waitPoint, rt, font, out refs.ScreenSubtitles);
            refs.LobbySubtitles = CreateLobbySubtitles(root.transform, font);

            // --- Set.
            var set = new GameObject("Set").transform;
            set.SetParent(root.transform, false);
            refs.Set = set;

            refs.CameraRig = CreateCameraRig(set, rt, layer, out refs.Camera);
            refs.Fx = CreateFx(set, layer);

            // Anclas: las que habia, o las de por defecto.
            if (keptAnchors != null)
            {
                keptAnchors.SetParent(set, true);
                refs.Anchors = keptAnchors;
            }
            else
            {
                refs.Anchors = new GameObject("Anclas").transform;
                refs.Anchors.SetParent(set, false);
                CreateDefaultPlaces(refs.Anchors, scene, floorY);
            }

            var places = Places.Read(refs.Anchors);
            CreateProps(set, scene, places, refs, font);
            refs.Aura = CreateAura(set, places, refs);
            refs.Naos = new Actor[NaoCount];
            for (int i = 0; i < NaoCount; i++)
            {
                refs.Naos[i] = CreateNao(set, i, places);
            }
            WireFxTargets(refs);

            // Los planos necesitan saber donde quedan la pistola y las caras: se crean con el set ya montado.
            if (refs.Anchors.Find("Planos") == null) CreateDefaultShots(refs, places);

            // --- Controlador.
            var director = root.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.Hold;
            var cinematic = root.AddComponent<IntroCinematic>();
            refs.Director = director;

            var blocker = Find(scene, ScreenBlockerName);
            var so = new SerializedObject(cinematic);
            so.FindProperty("director").objectReferenceValue = director;
            so.FindProperty("setRoot").objectReferenceValue = set.gameObject;
            so.FindProperty("cinemaCamera").objectReferenceValue = refs.Camera;
            so.FindProperty("screenRoot").objectReferenceValue = refs.Screen;
            so.FindProperty("subtitles").objectReferenceValue = refs.ScreenSubtitles;
            so.FindProperty("spawnPoints").objectReferenceValue = spawnPoints;
            var hide = so.FindProperty("hideDuringIntro");
            hide.arraySize = blocker != null ? 1 : 0;
            if (blocker != null) hide.GetArrayElementAtIndex(0).objectReferenceValue = blocker;
            so.ApplyModifiedPropertiesWithoutUndo();

            GenerateTimeline(refs);
            WireLobbyTutorial(scene, refs.LobbySubtitles);

            // Fuera de Play el set se queda encendido para poder previsualizar con la ventana Timeline;
            // IntroCinematic lo apaga al arrancar.
            Debug.Log($"[Intro] Set de la cinematica montado. Planos y lugares en '{RootName}/Set/Anclas'.");
        }

        // ------------------------------------------------------------ referencias

        private sealed class Actor
        {
            public Transform Root;
            public Transform Body;
            public Animator Animator;
            /// Articulaciones que se animan, con su rotacion local de reposo y la de su padre en el
            /// espacio del cuerpo (para girarlas alrededor de ejes del cuerpo).
            public Transform Head;
            public Transform ArmRight;
            public Transform Muzzle;
            public Renderer[] Renderers;
        }

        private sealed class SetRefs
        {
            public Transform Root, Set, Anchors, Screen, CameraRig;
            public SubtitleBar ScreenSubtitles, LobbySubtitles;
            public Camera Camera;
            public IntroSetFx Fx;
            public Actor Aura;
            public Actor[] Naos;
            public PlayableDirector Director;

            public static SetRefs Collect(Transform root)
            {
                var refs = new SetRefs { Root = root };
                refs.Set = root.Find("Set");
                refs.Screen = root.Find("Pantalla");
                refs.Anchors = refs.Set != null ? refs.Set.Find("Anclas") : null;
                refs.CameraRig = refs.Set != null ? refs.Set.Find("CamaraCine") : null;
                refs.Camera = refs.CameraRig != null ? refs.CameraRig.GetComponentInChildren<Camera>(true) : null;
                refs.Fx = refs.Set != null ? refs.Set.GetComponentInChildren<IntroSetFx>(true) : null;
                refs.ScreenSubtitles = refs.Screen != null ? refs.Screen.GetComponentInChildren<SubtitleBar>(true) : null;
                refs.Director = root.GetComponent<PlayableDirector>();
                refs.Aura = ActorFrom(refs.Set, "Aura");
                refs.Naos = new Actor[NaoCount];
                for (int i = 0; i < NaoCount; i++) refs.Naos[i] = ActorFrom(refs.Set, $"Nao_{i + 1}");

                if (refs.Set == null || refs.Anchors == null || refs.Camera == null || refs.Fx == null
                    || refs.Director == null || refs.Aura == null || Array.Exists(refs.Naos, n => n == null))
                {
                    Debug.LogError("[Intro] Al set le faltan piezas. Vuelve a usar 'Crear set y Timeline'.");
                    return null;
                }
                return refs;
            }

            private static Actor ActorFrom(Transform set, string name)
            {
                Transform root = set != null ? set.Find(name) : null;
                if (root == null) return null;
                var actor = new Actor
                {
                    Root = root,
                    Body = root.Find("Body"),
                    Animator = root.GetComponent<Animator>(),
                };
                if (actor.Body == null || actor.Animator == null) return null;
                var marks = root.GetComponent<IntroActorMarks>();
                if (marks != null)
                {
                    actor.Head = marks.head;
                    actor.ArmRight = marks.armRight;
                    actor.Muzzle = marks.muzzle;
                    actor.Renderers = marks.renderers;
                }
                return actor;
            }
        }

        // ------------------------------------------------------------ lugares

        /// <summary>
        /// Lugares de la coreografia (Anclas/Lugares): donde estan la estacion de carga, la consola,
        /// los escondites y las marcas de Aura. Posicion = punto en el suelo, giro Y = hacia donde mira.
        /// </summary>
        private sealed class Places
        {
            private readonly Dictionary<string, Transform> _map = new Dictionary<string, Transform>();

            public static Places Read(Transform anchors)
            {
                var places = new Places();
                Transform folder = anchors.Find("Lugares");
                if (folder == null) return places;
                foreach (Transform t in folder) places._map[t.name] = t;
                return places;
            }

            public Vector3 Pos(string name) => Get(name).position;
            public float Yaw(string name) => Get(name).eulerAngles.y;
            public Vector3 Fwd(string name) => Get(name).forward;
            public Vector3 Right(string name) => Get(name).right;

            private Transform Get(string name)
            {
                if (_map.TryGetValue(name, out var t) && t != null) return t;
                throw new InvalidOperationException($"[Intro] Falta el lugar '{name}' en Anclas/Lugares.");
            }

            /// Hueco i de la estacion de carga, en fila a lo ancho de la estacion.
            public Vector3 Slot(int i) => Pos("Estacion") + Right("Estacion") * ((i - (NaoCount - 1) * 0.5f) * 0.62f);
        }

        private static void CreateDefaultPlaces(Transform anchors, Scene scene, float floorY)
        {
            var folder = new GameObject("Lugares").transform;
            folder.SetParent(anchors, false);

            void Place(string name, float x, float z, float yaw)
            {
                var t = new GameObject(name).transform;
                t.SetParent(folder, false);
                t.SetPositionAndRotation(new Vector3(x, floorY, z), Quaternion.Euler(0f, yaw, 0f));
            }

            // Estacion de carga en la zona abierta del fondo, con los robots mirando al centro del lab.
            Place("Estacion", 4.6f, 6.45f, 180f);
            // Consola del servidor central, a la izquierda de la estacion (vista desde el centro) y
            // con la pantalla hacia los robots y la camara.
            Place("Consola", 3.5f, 5.3f, 125f);
            // Escondites: bajo la mesa del fondo, tras la fila de mesas del centro, tras la mesa del
            // brazo robotico y tras la mesa del brazo 3 (donde Aura lo encuentra).
            Place("Escondite_1", 0.34f, 0.8f, 90f);
            Place("Escondite_2", 7.4f, -0.2f, 180f);
            Place("Escondite_3", 8.5f, -7.12f, 0f);
            Place("Escondite_4", 11.62f, -4.3f, 270f);
            // Aura: aparicion, disparo, paseo y plano final.
            Place("Aura_Aparicion", 5.6f, -2.7f, 270f);
            Place("Aura_Disparo", 10.35f, -2.55f, 146f);
            Place("Aura_Paseo", 4.5f, -2.8f, 90f);
            Place("Aura_Final", 7.4f, -2.85f, 270f);
        }

        // ------------------------------------------------------------ planos

        private struct ShotSpec
        {
            public string Name, Description;
            public float Start, Duration, FovA, FovB;
            public Vector3 PosA, LookA, PosB, LookB;
        }

        /// <summary>
        /// El guion visual. Cada plano: donde empieza y acaba la camara, a donde mira y el FOV.
        /// Los cortes caen al cambiar de plano. Despues se pueden retocar moviendo las anclas.
        /// </summary>
        private static List<ShotSpec> BuildShots(SetRefs refs, Places p)
        {
            var shots = new List<ShotSpec>();
            float t = 0f;
            void Shot(string name, string description, float duration, Vector3 posA, Vector3 lookA, Vector3 posB,
                      Vector3 lookB, float fovA = 45f, float fovB = -1f)
            {
                shots.Add(new ShotSpec
                {
                    Name = name, Description = description, Start = t, Duration = duration,
                    PosA = posA, LookA = lookA, PosB = posB, LookB = lookB,
                    FovA = fovA, FovB = fovB < 0f ? fovA : fovB,
                });
                t += duration;
            }

            float floor = p.Pos("Estacion").y;
            Vector3 Up(Vector3 v, float h) => new Vector3(v.x, floor + h, v.z);

            Vector3 station = p.Pos("Estacion");
            Vector3 stationFwd = p.Fwd("Estacion");
            Vector3 console = p.Pos("Consola");
            Vector3 consoleFwd = p.Fwd("Consola");
            Vector3 screen = Up(console, 1.12f);
            Vector3 slot1 = p.Slot(1), slot2 = p.Slot(2);
            float naoHead = NaoHeight * 0.88f;

            // --- 1. "Una noche, un apagon reinicio el servidor central del laboratorio Colivri."
            Shot("01a_General", "Plano general del laboratorio de noche, travelling lateral lento.", 4.5f,
                new Vector3(3.4f, floor + 2.6f, -7.0f), new Vector3(8.6f, floor + 0.3f, 2.4f),
                new Vector3(5.0f, floor + 2.5f, -7.1f), new Vector3(9.4f, floor + 0.3f, 2.6f), 52f);
            Shot("01b_LEDsRack", "Primer plano de los LEDs del rack; se apagan con el apagon.", 3f,
                new Vector3(9.25f, floor + 0.55f, 7.3f), new Vector3(10.4f, floor + 0.38f, 8.05f),
                new Vector3(9.55f, floor + 0.5f, 7.5f), new Vector3(10.4f, floor + 0.38f, 8.05f), 38f, 32f);
            Shot("01c_Oscuridad", "Oscuridad: la estacion de carga apenas se intuye.", 1.5f,
                station - stationFwd * -3.3f + Vector3.up * 1.0f + Vector3.right * 1.2f, Up(station, 0.45f),
                station - stationFwd * -3.1f + Vector3.up * 1.0f + Vector3.right * 1.1f, Up(station, 0.45f), 48f);
            Shot("01d_Reinicio", "Luz de emergencia; el monitor del servidor muestra REINICIANDO.", 3.5f,
                screen + consoleFwd * 1.7f + Vector3.up * 0.15f, screen,
                screen + consoleFwd * 1.25f + Vector3.up * 0.1f, screen, 40f);

            // --- 2. "Los robots de mantenimiento creyeron que la actualizacion pendiente los iba a borrar,"
            Shot("02a_Estacion", "General de la estacion de carga con los cuatro robots enchufados.", 3f,
                station + stationFwd * 2.9f + Vector3.up * 0.75f, Up(station, 0.4f),
                station + stationFwd * 2.5f + Vector3.up * 0.7f, Up(station, 0.4f), 50f, 46f);
            Shot("02b_Monitor", "Inserto del monitor: ACTUALIZACION PENDIENTE, se borraran los robots.", 2.8f,
                screen + consoleFwd * 0.95f, screen, screen + consoleFwd * 0.7f, screen, 40f);
            Vector3 n2Face = Up(slot1, naoHead);
            Vector3 n3Face = Up(slot2, naoHead);
            Vector3 n2Side = (n2Face - n3Face).normalized;
            Shot("02c_Nao2", "Primer plano de un robot que gira la cabeza hacia el monitor.", 1.8f,
                n2Face + stationFwd * 1.05f + n2Side * 0.45f + Vector3.up * 0.06f, n2Face - Vector3.up * 0.05f,
                n2Face + stationFwd * 0.9f + n2Side * 0.38f + Vector3.up * 0.05f, n2Face - Vector3.up * 0.05f, 38f);
            Shot("02d_Nao3", "Contraplano sobre el hombro del primero: el robot de al lado lo mira a el.", 1.7f,
                n2Face + n2Side * 0.22f + stationFwd * 0.45f + Vector3.up * 0.1f, n3Face,
                n2Face + n2Side * 0.2f + stationFwd * 0.4f + Vector3.up * 0.09f, n3Face, 40f);

            // --- 3. "se escaparon de su estacion de carga"
            Shot("03a_Huida", "Contrapicado a ras de suelo: los robots se desenchufan y salen corriendo.", 2.7f,
                station + stationFwd * 2.7f + Vector3.up * 0.18f, Up(station, 0.5f),
                station + stationFwd * 2.6f + Vector3.up * 0.18f, Up(station, 0.55f), 58f);
            Shot("03b_Vacia", "La estacion vacia, el enchufe echando chispas.", 2.3f,
                station + stationFwd * 1.4f + p.Right("Estacion") * 0.9f + Vector3.up * 0.6f, Up(station, 0.35f),
                station + stationFwd * 1.25f + p.Right("Estacion") * 0.8f + Vector3.up * 0.55f, Up(station, 0.35f), 44f);

            // --- 4. "y se escondieron por todo el edificio." (montaje rapido)
            Vector3 h1 = p.Pos("Escondite_1"), h2 = p.Pos("Escondite_2"), h3 = p.Pos("Escondite_3"), h4 = p.Pos("Escondite_4");
            Shot("04a_BajoMesa", "Un robot se mete bajo una mesa.", THideShot,
                h1 + p.Fwd("Escondite_1") * 2.0f + Vector3.up * 0.4f, Up(h1, 0.3f),
                h1 + p.Fwd("Escondite_1") * 1.8f + Vector3.up * 0.38f, Up(h1, 0.3f), 46f);
            Shot("04b_TrasMesas", "Otro se agacha tras la fila de mesas.", THideShot,
                h2 + p.Fwd("Escondite_2") * 2.3f + Vector3.up * 1.35f, Up(h2, 0.4f),
                h2 + p.Fwd("Escondite_2") * 2.15f + Vector3.up * 1.3f, Up(h2, 0.4f), 44f);
            Shot("04c_BrazoRobotico", "Otro se esconde detras de la mesa del brazo robotico.", THideShot,
                h3 + p.Fwd("Escondite_3") * 3.0f + Vector3.up * 0.75f, Up(h3, 0.45f),
                h3 + p.Fwd("Escondite_3") * 2.8f + Vector3.up * 0.72f, Up(h3, 0.45f), 44f);
            Shot("04d_UltimoEscondite", "El ultimo se acurruca tras otra mesa.", THideShot,
                h4 + p.Fwd("Escondite_4") * 1.9f + Vector3.up * 1.2f, Up(h4, 0.4f),
                h4 + p.Fwd("Escondite_4") * 1.75f + Vector3.up * 1.15f, Up(h4, 0.4f), 44f);

            // --- 5. "Aura, la robot de seguridad, tiene que encontrarlos"
            Vector3 aura = p.Pos("Aura_Aparicion");
            Vector3 auraFwd = p.Fwd("Aura_Aparicion");
            Vector3 auraHead = Up(aura, AuraHeight * 0.9f);
            Shot("05a_AuraContrapicado", "Contrapicado heroico: Aura en penumbra, se encienden sus luces.", 3.2f,
                aura + auraFwd * 1.5f + Vector3.up * 0.25f, Up(aura, 1.2f),
                aura + auraFwd * 1.45f + Vector3.up * 0.4f, Up(aura, 1.3f), 55f);
            Shot("05b_AuraCara", "Acercamiento lento a la cara de Aura, que mira a camara.", 3.4f,
                auraHead + auraFwd * 1.3f - Vector3.up * 0.1f, auraHead,
                auraHead + auraFwd * 0.75f - Vector3.up * 0.05f, auraHead, 40f, 36f);

            // --- 6. "y sincronizarlos con su pistola de datos"
            Vector3 shoot = p.Pos("Aura_Disparo");
            Vector3 shootFwd = p.Fwd("Aura_Disparo");
            Vector3 shootRight = p.Right("Aura_Disparo");
            Vector3 muzzle = MuzzleAt(refs.Aura, shoot, p.Yaw("Aura_Disparo"));
            Shot("06a_Pistola", "Inserto de la pistola de datos en la mano de Aura; el canon se ilumina.", 2.4f,
                muzzle + shootRight * 0.42f - shootFwd * 0.05f + Vector3.up * 0.06f, muzzle - shootFwd * 0.08f,
                muzzle + shootRight * 0.3f - shootFwd * 0.02f + Vector3.up * 0.04f, muzzle - shootFwd * 0.08f, 36f);
            Shot("06b_Disparo", "Sobre el hombro de Aura: dispara al robot que asoma y lo sincroniza.", 3.6f,
                shoot - shootFwd * 1.25f + shootRight * 0.75f + Vector3.up * 1.6f, Up(h4, 0.6f),
                shoot - shootFwd * 1.1f + shootRight * 0.7f + Vector3.up * 1.58f, Up(h4, 0.6f), 38f);

            // --- 7. "antes de que el laboratorio se quede sin bateria."
            Shot("07a_Bateria", "El monitor muestra la bateria del laboratorio cayendo en rojo.", 3f,
                screen + consoleFwd * 1.0f, screen, screen + consoleFwd * 0.7f, screen, 40f);
            Vector3 walkA = p.Pos("Aura_Paseo"), walkB = p.Pos("Aura_Final");
            Vector3 walkDir = (walkB - walkA).normalized;
            Vector3 walkSide = Vector3.Cross(Vector3.up, walkDir);
            Shot("07b_Paseo", "Aura avanza por el laboratorio en rojo; la camara retrocede delante de ella.", 3f,
                walkA + walkDir * 2.6f - walkSide * 0.8f + Vector3.up * 1.35f, Up(walkA, 1.2f),
                walkB + walkDir * 2.1f - walkSide * 0.8f + Vector3.up * 1.35f, Up(walkB, 1.2f), 48f);

            // --- 8. "¿Ayudaras a Aura o te esconderas con los demas?"
            // La camara mira a la bisectriz entre la cara de Aura y el robot que asoma: los dos quedan
            // a la misma distancia del centro, uno a cada lado.
            Vector3 finalCam = walkB + p.Fwd("Aura_Final") * 3.0f + Vector3.up * 1.3f;
            Vector3 bisector = ((Up(walkB, 1.5f) - finalCam).normalized + (Up(h2, 0.7f) - finalCam).normalized).normalized;
            Vector3 mid = finalCam + bisector * 3f;
            Shot("08a_Final", "Aura mira a camara; detras, un robot asoma tras las mesas. Fundido a negro.", TotalDuration - t,
                finalCam, mid, finalCam + (mid - finalCam).normalized * 0.45f, mid, 50f, 46f);

            return shots;
        }

        private static void CreateDefaultShots(SetRefs refs, Places places)
        {
            var folder = new GameObject("Planos").transform;
            folder.SetParent(refs.Anchors, false);

            foreach (var spec in BuildShots(refs, places))
            {
                var go = new GameObject(spec.Name);
                go.transform.SetParent(folder, false);
                var shot = go.AddComponent<IntroShot>();
                shot.description = spec.Description;
                shot.start = spec.Start;
                shot.duration = spec.Duration;
                shot.fovStart = spec.FovA;
                shot.fovEnd = spec.FovB;
                shot.from = CameraAnchor(go.transform, "Desde", spec.PosA, spec.LookA);
                shot.to = CameraAnchor(go.transform, "Hasta", spec.PosB, spec.LookB);
            }
        }

        private static Transform CameraAnchor(Transform parent, string name, Vector3 pos, Vector3 look)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            Vector3 dir = look - pos;
            t.SetPositionAndRotation(pos, dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.identity);
            return t;
        }

        // ------------------------------------------------------------ timeline

        private static void GenerateTimeline(SetRefs refs)
        {
            var places = Places.Read(refs.Anchors);

            if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath) != null)
            {
                AssetDatabase.DeleteAsset(TimelinePath);
            }
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = "IntroTimeline";
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            timeline.editorSettings.frameRate = 60;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = TotalDuration;

            var director = refs.Director;
            director.playableAsset = timeline;

            // --- Camara: un clip por plano. Los cortes son los limites entre clips.
            var camTrack = timeline.CreateTrack<AnimationTrack>(null, "Cámara");
            director.SetGenericBinding(camTrack, refs.CameraRig.GetComponent<Animator>());
            Transform shotsFolder = refs.Anchors.Find("Planos");
            var shots = new List<IntroShot>(shotsFolder.GetComponentsInChildren<IntroShot>());
            shots.Sort((a, b) => a.start.CompareTo(b.start));
            string camPath = AnimationUtility.CalculateTransformPath(refs.Camera.transform, refs.CameraRig);
            foreach (var shot in shots)
            {
                if (shot.from == null || shot.to == null || shot.duration <= 0f) continue;
                var clip = NewClip(timeline, "Plano " + shot.name);
                var keys = new PoseKeys();
                keys.Add(0f, shot.from.position, shot.from.rotation);
                keys.Add(shot.duration, shot.to.position, shot.to.rotation);
                keys.Write(clip, camPath);
                SetCurve(clip, camPath, typeof(Camera), "field of view",
                    Curve((0f, shot.fovStart), (shot.duration, shot.fovEnd)));

                var tc = camTrack.CreateClip(clip);
                tc.start = shot.start;
                tc.duration = shot.duration;
                tc.displayName = shot.name;
                SetExtrapolation(tc, post: TimelineClip.ClipExtrapolation.Hold);
            }

            // --- Efectos.
            var fxTrack = timeline.CreateTrack<AnimationTrack>(null, "Efectos");
            director.SetGenericBinding(fxTrack, refs.Fx.GetComponent<Animator>());
            AddWholeClip(fxTrack, BuildFxClip(timeline), "Efectos");

            // --- Actores.
            var auraTrack = timeline.CreateTrack<AnimationTrack>(null, "Aura");
            director.SetGenericBinding(auraTrack, refs.Aura.Animator);
            AddWholeClip(auraTrack, BuildAuraClip(timeline, refs.Aura, places), "Aura");

            for (int i = 0; i < NaoCount; i++)
            {
                var track = timeline.CreateTrack<AnimationTrack>(null, $"Robot {i + 1}");
                director.SetGenericBinding(track, refs.Naos[i].Animator);
                AddWholeClip(track, BuildNaoClip(timeline, refs.Naos[i], i, places, refs.Aura), $"Robot {i + 1}");
            }

            // --- Subtitulos.
            var subTrack = timeline.CreateTrack<SubtitleTrack>(null, "Subtítulos");
            director.SetGenericBinding(subTrack, refs.ScreenSubtitles);
            foreach (var (start, end, text) in Subtitles)
            {
                var tc = subTrack.CreateClip<SubtitleClip>();
                tc.start = start;
                tc.duration = end - start;
                tc.displayName = text;
                ((SubtitleClip)tc.asset).text = text;
            }

            // --- Sonido: el disparo de la pistola de datos. La pista queda lista para mas efectos.
            var audioTrack = timeline.CreateTrack<AudioTrack>(null, "Sonido");
            var shotClip = AssetDatabase.LoadAssetAtPath<AudioClip>(GunShotPath);
            if (shotClip != null)
            {
                var tc = audioTrack.CreateClip(shotClip);
                tc.start = TShot;
                tc.displayName = "Disparo";
            }

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();

            var manager = Object.FindAnyObjectByType<HideAndSeekManager>(FindObjectsInactive.Include);
            if (manager != null)
            {
                var mso = new SerializedObject(manager);
                mso.FindProperty("introDuration").floatValue = IntroDuration;
                mso.ApplyModifiedPropertiesWithoutUndo();
            }

            Debug.Log($"[Intro] Timeline generado: {shots.Count} planos, {TotalDuration:0.#} s.");
        }

        private static AnimationClip NewClip(TimelineAsset timeline, string name)
        {
            var clip = new AnimationClip { name = name, frameRate = 60f };
            AssetDatabase.AddObjectToAsset(clip, timeline);
            return clip;
        }

        private static void AddWholeClip(AnimationTrack track, AnimationClip clip, string name)
        {
            var tc = track.CreateClip(clip);
            tc.start = 0;
            tc.duration = TotalDuration;
            tc.displayName = name;
            SetExtrapolation(tc, TimelineClip.ClipExtrapolation.Hold, TimelineClip.ClipExtrapolation.Hold);
        }

        /// <summary>
        /// La extrapolacion de un clip solo se puede cambiar desde la ventana Timeline: por codigo la
        /// propiedad es de solo lectura, asi que se escribe el campo serializado.
        /// </summary>
        private static void SetExtrapolation(TimelineClip clip,
            TimelineClip.ClipExtrapolation pre = TimelineClip.ClipExtrapolation.None,
            TimelineClip.ClipExtrapolation post = TimelineClip.ClipExtrapolation.None)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(TimelineClip).GetField("m_PreExtrapolationMode", flags)?.SetValue(clip, pre);
            typeof(TimelineClip).GetField("m_PostExtrapolationMode", flags)?.SetValue(clip, post);
        }

        // ------------------------------------------------------------ efectos

        private static AnimationClip BuildFxClip(TimelineAsset timeline)
        {
            var clip = NewClip(timeline, "Efectos");
            void F(string field, params (float t, float v)[] keys) =>
                SetCurve(clip, "", typeof(IntroSetFx), field, Curve(keys));
            void Step(string field, params (float t, float v)[] keys) =>
                SetCurve(clip, "", typeof(IntroSetFx), field, StepCurve(keys));

            F("clock", (0f, 0f), (TotalDuration, TotalDuration));
            SetLinear(clip, "", typeof(IntroSetFx), "clock");

            F("fade", (0f, 1f), (1.0f, 0f), (TotalDuration - 1.3f, 0f), (TotalDuration - 0.1f, 1f));
            // Apagon de golpe; con la luz de emergencia se recupera un poco para que se lea la imagen.
            F("blackout", (0f, 0f), (TBlackout, 0f), (TBlackout + 0.12f, 1f), (TEmergency, 1f), (TEmergency + 0.8f, 0.4f));
            F("emergency", (0f, 0f), (TEmergency, 0f), (TEmergency + 0.4f, 1f));
            Step("rackLeds", (0f, 1f), (TBlackout + 0.05f, 0f));
            Step("monitorPage", (0f, 0f), (TEmergency + 0.3f, 1f), (TUpdateNotice, 2f), (TEscape + 4f, 0f), (TBattery, 3f));
            F("rebootProgress", (0f, 0f), (TEmergency + 0.3f, 0f), (12.4f, 1f));
            Step("stationGlow", (0f, 1f), (TEscape, 0f));
            F("spark", (0f, 0f), (TEscape, 0f), (TEscape + 0.05f, 1f), (TEscape + 0.6f, 0.25f), (TEscape + 2.4f, 1f), (THideStart, 0f));
            F("auraLights", (0f, 0f), (TAuraLights, 0f), (TAuraLights + 0.08f, 1f), (TAuraLights + 0.16f, 0.15f),
                (TAuraLights + 0.3f, 1f));
            F("muzzleGlow", (0f, 0f), (TAuraShootPlace + 0.7f, 0f), (TAuraShootPlace + 1.8f, 1f), (TShot + 0.1f, 1f),
                (TShot + 0.3f, 0.3f), (TWalk, 0.3f), (TWalk + 0.01f, 0f));
            Step("beam", (0f, 0f), (TShot, 0f), (TShot + 0.01f, 0.05f), (TShot + 0.3f, 0.7f), (TShot + 1.0f, 0.8f),
                (TShot + 1.3f, 1f), (TShot + 1.32f, 0f));
            SetLinear(clip, "", typeof(IntroSetFx), "beam");
            F("synced", (0f, 0f), (TShot + 0.3f, 0f), (TShot + 0.6f, 1f));
            F("battery", (0f, 1f), (TBattery + 0.2f, 1f), (TBattery + 2.7f, 0.12f));
            return clip;
        }

        // ------------------------------------------------------------ Aura

        private static AnimationClip BuildAuraClip(TimelineAsset timeline, Actor aura, Places p)
        {
            var clip = NewClip(timeline, "Aura");
            string bodyPath = Path(aura, aura.Body);
            var body = new PoseKeys();

            Vector3 hidden = p.Pos("Aura_Aparicion") + Vector3.down * 30f;
            float appearYaw = p.Yaw("Aura_Aparicion");

            // Fuera de escena hasta su aparicion.
            body.Add(0f, hidden, appearYaw);
            body.Step(TAuraReveal - 0.2f, p.Pos("Aura_Aparicion"), appearYaw);
            // Respira: un leve vaiven mientras esta en su marca.
            Breathe(body, TAuraReveal - 0.2f, TAuraShootPlace - 0.05f, p.Pos("Aura_Aparicion"), appearYaw, 0.006f);

            // Marca de disparo.
            Vector3 shoot = p.Pos("Aura_Disparo");
            float shootYaw = p.Yaw("Aura_Disparo");
            body.Step(TAuraShootPlace, shoot, shootYaw);
            body.Add(TShot, shoot, shootYaw);
            // Retroceso del disparo.
            body.Add(TShot + 0.06f, shoot - p.Fwd("Aura_Disparo") * 0.03f, shootYaw);
            body.Add(TShot + 0.35f, shoot, shootYaw);
            body.Add(TWalk - 0.05f, shoot, shootYaw);

            // Paseo y marca final.
            Vector3 walkA = p.Pos("Aura_Paseo"), walkB = p.Pos("Aura_Final");
            float walkYaw = Quaternion.LookRotation(walkB - walkA).eulerAngles.y;
            body.Step(TWalk, walkA, walkYaw);
            Walk(body, TWalk, TFinal - 0.3f, walkA, walkB, walkYaw, 0.018f, 0.45f);
            body.Add(TFinal + 0.4f, walkB, p.Yaw("Aura_Final"));
            Breathe(body, TFinal + 0.4f, TotalDuration, walkB, p.Yaw("Aura_Final"), 0.006f);
            body.Write(clip, bodyPath);

            // Cabeza: mira a camara en el acercamiento del plano 5b y en el final.
            if (aura.Head != null)
            {
                var head = new JointKeys(aura, aura.Head, Vector3.up);
                head.Add(0f, 0f);
                head.Add(TAuraReveal + 3.4f, 0f);
                head.Add(TAuraReveal + 4.0f, -14f);
                head.Add(TAuraShootPlace - 0.05f, -14f);
                head.Add(TAuraShootPlace, 0f);
                head.Add(TFinal + 0.5f, 0f);
                head.Add(TFinal + 1.6f, 8f);
                head.Write(clip);
            }

            // Brazo derecho: levanta la pistola para apuntar y la baja al pasear.
            if (aura.ArmRight != null)
            {
                var arm = new JointKeys(aura, aura.ArmRight, Vector3.right);
                arm.Add(0f, 0f);
                arm.Add(TAuraShootPlace - 0.01f, 0f);
                arm.Add(TAuraShootPlace, AimAngle);
                arm.Add(TShot, AimAngle);
                arm.Add(TShot + 0.07f, AimAngle - 9f);
                arm.Add(TShot + 0.4f, AimAngle);
                arm.Add(TWalk - 0.01f, AimAngle);
                arm.Add(TWalk, 0f);
                arm.Add(TotalDuration, 0f);
                arm.Write(clip);
            }
            return clip;
        }

        // ------------------------------------------------------------ robots

        private static AnimationClip BuildNaoClip(TimelineAsset timeline, Actor nao, int index, Places p, Actor aura)
        {
            var clip = NewClip(timeline, $"Robot {index + 1}");
            string bodyPath = Path(nao, nao.Body);
            var body = new PoseKeys();

            Vector3 slot = p.Slot(index);
            float stationYaw = p.Yaw("Estacion");
            string spotName = $"Escondite_{index + 1}";
            Vector3 spot = p.Pos(spotName);
            float spotYaw = p.Yaw(spotName);
            Vector3 spotFwd = p.Fwd(spotName);

            // Enchufado en la estacion hasta la huida, con un vaiven minimo para que no parezca congelado.
            body.Add(0f, slot, stationYaw);
            Breathe(body, 0f, TEscape + index * 0.12f, slot, stationYaw, 0.004f);

            // Huida: sale corriendo hacia su escondite y se pierde de cuadro.
            float runStart = TEscape + 0.15f + index * 0.12f;
            Vector3 runDir = spot - slot;
            runDir.y = 0f;
            runDir.Normalize();
            float runYaw = Quaternion.LookRotation(runDir).eulerAngles.y;
            body.Add(runStart, slot, runYaw);
            Vector3 runEnd = slot + runDir * RunSpeed * (THideStart - 0.3f - runStart);
            Walk(body, runStart + 0.1f, THideStart - 0.3f, slot + runDir * 0.15f, runEnd, runYaw, 0.03f, 0.18f, 7f);

            // Montaje: entra corriendo en su plano y se agacha en el escondite.
            float shotStart = THideStart + index * THideShot;
            Vector3 side = Vector3.Cross(Vector3.up, spotFwd);
            Vector3 approach = spot + side * (index % 2 == 0 ? 1.3f : -1.3f) - spotFwd * 0.2f;
            float approachYaw = Quaternion.LookRotation(spot - approach).eulerAngles.y;
            body.Step(THideStart - 0.25f, approach, approachYaw);
            body.Add(shotStart + 0.05f, approach, approachYaw);
            Walk(body, shotStart + 0.1f, shotStart + 0.75f, approach, spot, approachYaw, 0.03f, 0.18f, 7f);
            body.Add(shotStart + 0.9f, spot, spotYaw);
            // Escondite 1 es bajo una mesa: se agacha mas.
            float crouch = index == 0 ? 0.3f : 0.2f;
            Vector3 crouched = spot + Vector3.down * crouch;
            body.Add(shotStart + 1.15f, crouched, spotYaw, 14f);

            if (index == 3)
            {
                // El que encuentra Aura: asoma, recibe el rayo y se queda sincronizado (de pie, azul).
                body.Add(TPeek, crouched, spotYaw, 14f);
                Vector3 peek = spot + Vector3.down * 0.02f;
                float faceAura = Quaternion.LookRotation(Flat(p.Pos("Aura_Disparo") - spot)).eulerAngles.y;
                body.Add(TPeek + 0.4f, peek, faceAura, 4f);
                body.Add(TShot + 0.3f, peek, faceAura, 4f);
                body.Add(TShot + 0.45f, peek + Vector3.up * 0.03f, faceAura, -6f);
                body.Add(TShot + 0.9f, peek, faceAura, 0f);
                body.Add(TotalDuration, peek, faceAura, 0f);
            }
            else if (index == 1)
            {
                // El del plano final: asoma por encima de las mesas a espaldas de Aura.
                body.Add(TFinal + 0.8f, crouched, spotYaw, 14f);
                body.Add(TFinal + 1.4f, spot + Vector3.down * 0.06f, spotYaw, 6f);
                body.Add(TotalDuration, spot + Vector3.down * 0.06f, spotYaw, 6f);
            }
            else
            {
                body.Add(TotalDuration, crouched, spotYaw, 14f);
            }
            body.Write(clip, bodyPath);

            // Cabezas: el 2 mira al monitor, el 3 mira al 2, y el del final mira a Aura.
            if (nao.Head != null)
            {
                var head = new JointKeys(nao, nao.Head, Vector3.up);
                head.Add(0f, 0f);
                if (index == 1)
                {
                    float toConsole = HeadYawTowards(slot, stationYaw, p.Pos("Consola"));
                    head.Add(TNao2Turns, 0f);
                    head.Add(TNao2Turns + 0.5f, toConsole);
                    head.Add(TEscape, toConsole);
                    head.Add(TEscape + 0.2f, 0f);
                    head.Add(TFinal + 1.5f, 0f);
                    head.Add(TFinal + 2.4f, HeadYawTowards(spot, spotYaw, p.Pos("Aura_Final")));
                }
                else if (index == 2)
                {
                    float toNao2 = HeadYawTowards(slot, stationYaw, p.Slot(1));
                    head.Add(TNao3Turns, 0f);
                    head.Add(TNao3Turns + 0.35f, toNao2);
                    head.Add(TEscape, toNao2);
                    head.Add(TEscape + 0.2f, 0f);
                }
                head.Add(TotalDuration, head.Last);
                head.Write(clip);
            }
            return clip;
        }

        private static float HeadYawTowards(Vector3 from, float bodyYaw, Vector3 target)
        {
            Vector3 fwd = Quaternion.Euler(0f, bodyYaw, 0f) * Vector3.forward;
            return Mathf.Clamp(Vector3.SignedAngle(fwd, Flat(target - from), Vector3.up), -70f, 70f);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        /// Vaiven suave en el sitio (respiracion de robot), una clave cada 1,5 s.
        private static void Breathe(PoseKeys keys, float from, float to, Vector3 pos, float yaw, float amplitude)
        {
            int i = 0;
            for (float t = from + 0.75f; t < to - 0.4f; t += 0.75f, i++)
            {
                keys.Add(t, pos + Vector3.up * (i % 2 == 0 ? amplitude : 0f), yaw);
            }
            keys.Add(to, pos, yaw);
        }

        /// Desplazamiento en linea recta con bote y balanceo lateral en cada paso.
        private static void Walk(PoseKeys keys, float from, float to, Vector3 a, Vector3 b, float yaw,
                                 float bob, float step, float roll = 3f)
        {
            int n = Mathf.Max(2, Mathf.RoundToInt((to - from) / step));
            for (int i = 0; i <= n; i++)
            {
                float k = i / (float)n;
                float lift = (i % 2 == 1) ? bob : 0f;
                float r = (i % 2 == 1) ? 0f : (i % 4 == 0 ? roll : -roll);
                keys.Add(Mathf.Lerp(from, to, k), Vector3.Lerp(a, b, k) + Vector3.up * lift, yaw, 0f, r);
            }
        }

        /// <summary>
        /// Donde queda el canon de la pistola con Aura en una marca y el brazo levantado: el plano
        /// inserto de la pistola apunta ahi. Se calcula posando el modelo un momento.
        /// </summary>
        private static Vector3 MuzzleAt(Actor aura, Vector3 pos, float yaw)
        {
            if (aura == null || aura.Muzzle == null) return pos + Vector3.up * 1.2f;

            Vector3 oldPos = aura.Body.localPosition;
            Quaternion oldRot = aura.Body.localRotation;
            Quaternion oldArm = aura.ArmRight != null ? aura.ArmRight.localRotation : Quaternion.identity;

            aura.Body.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            if (aura.ArmRight != null) aura.ArmRight.localRotation = new JointKeys(aura, aura.ArmRight, Vector3.right).Rotation(AimAngle);
            Vector3 result = aura.Muzzle.position;

            aura.Body.localPosition = oldPos;
            aura.Body.localRotation = oldRot;
            if (aura.ArmRight != null) aura.ArmRight.localRotation = oldArm;
            return result;
        }

        // ------------------------------------------------------------ curvas

        /// Claves de posicion y rotacion (yaw + inclinaciones) para un transform.
        private sealed class PoseKeys
        {
            private readonly List<(float t, Vector3 p, Quaternion r)> _keys = new List<(float, Vector3, Quaternion)>();

            public void Add(float t, Vector3 p, Quaternion r) => _keys.Add((t, p, r));

            /// yaw en grados, pitch hacia delante y roll lateral opcionales.
            public void Add(float t, Vector3 p, float yaw, float pitch = 0f, float roll = 0f) =>
                _keys.Add((t, p, Quaternion.Euler(pitch, yaw, roll)));

            /// Salto instantaneo (fuera de camara o en un corte): repite la pose anterior justo antes.
            public void Step(float t, Vector3 p, float yaw)
            {
                if (_keys.Count > 0)
                {
                    var last = _keys[_keys.Count - 1];
                    if (t - 1f / 60f > last.t) _keys.Add((t - 1f / 60f, last.p, last.r));
                }
                Add(t, p, yaw);
            }

            public void Write(AnimationClip clip, string path)
            {
                _keys.Sort((a, b) => a.t.CompareTo(b.t));
                var px = new AnimationCurve(); var py = new AnimationCurve(); var pz = new AnimationCurve();
                var ex = new AnimationCurve(); var ey = new AnimationCurve(); var ez = new AnimationCurve();
                Vector3 prevEuler = Vector3.zero;
                float lastT = -1f;
                for (int i = 0; i < _keys.Count; i++)
                {
                    var (t, p, r) = _keys[i];
                    if (t <= lastT) t = lastT + 0.001f;
                    lastT = t;
                    Vector3 e = r.eulerAngles;
                    if (i > 0)
                    {
                        // Euler continuo: sin saltos de 359 a 0 que harian girar al actor una vuelta.
                        e = new Vector3(prevEuler.x + Mathf.DeltaAngle(prevEuler.x, e.x),
                                        prevEuler.y + Mathf.DeltaAngle(prevEuler.y, e.y),
                                        prevEuler.z + Mathf.DeltaAngle(prevEuler.z, e.z));
                    }
                    prevEuler = e;
                    px.AddKey(t, p.x); py.AddKey(t, p.y); pz.AddKey(t, p.z);
                    ex.AddKey(t, e.x); ey.AddKey(t, e.y); ez.AddKey(t, e.z);
                }
                SetCurve(clip, path, typeof(Transform), "m_LocalPosition.x", Smooth(px));
                SetCurve(clip, path, typeof(Transform), "m_LocalPosition.y", Smooth(py));
                SetCurve(clip, path, typeof(Transform), "m_LocalPosition.z", Smooth(pz));
                SetCurve(clip, path, typeof(Transform), "localEulerAnglesRaw.x", Smooth(ex));
                SetCurve(clip, path, typeof(Transform), "localEulerAnglesRaw.y", Smooth(ey));
                SetCurve(clip, path, typeof(Transform), "localEulerAnglesRaw.z", Smooth(ez));
            }
        }

        /// <summary>
        /// Giro de una articulacion alrededor de un eje del cuerpo del actor (Y = girar la cabeza,
        /// X = levantar el brazo hacia delante), sea cual sea el eje local raro que traiga el GLB.
        /// </summary>
        private sealed class JointKeys
        {
            private readonly Actor _actor;
            private readonly Transform _joint;
            private readonly Vector3 _axis;
            private readonly Quaternion _rest;
            private readonly Quaternion _parentInBody;
            private readonly List<(float t, float angle)> _keys = new List<(float, float)>();

            public float Last => _keys.Count > 0 ? _keys[_keys.Count - 1].angle : 0f;

            public JointKeys(Actor actor, Transform joint, Vector3 bodyAxis)
            {
                _actor = actor;
                _joint = joint;
                _axis = bodyAxis;
                var marks = actor.Root.GetComponent<IntroActorMarks>();
                bool isArm = joint == actor.ArmRight;
                _rest = marks != null ? (isArm ? marks.armRest : marks.headRest) : joint.localRotation;
                _parentInBody = Quaternion.Inverse(actor.Body.rotation) * joint.parent.rotation;
                if (marks != null) _parentInBody = isArm ? marks.armParentInBody : marks.headParentInBody;
            }

            public void Add(float t, float angle) => _keys.Add((t, angle));

            public Quaternion Rotation(float angle) =>
                Quaternion.Inverse(_parentInBody) * Quaternion.AngleAxis(angle, _axis) * _parentInBody * _rest;

            public void Write(AnimationClip clip)
            {
                string path = Path(_actor, _joint);
                var x = new AnimationCurve(); var y = new AnimationCurve();
                var z = new AnimationCurve(); var w = new AnimationCurve();
                Quaternion prev = Quaternion.identity;
                bool first = true;
                _keys.Sort((a, b) => a.t.CompareTo(b.t));
                foreach (var (t, angle) in _keys)
                {
                    Quaternion q = Rotation(angle);
                    // Mismo hemisferio que la clave anterior, para que interpole por el camino corto.
                    if (!first && Quaternion.Dot(prev, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    first = false;
                    prev = q;
                    x.AddKey(t, q.x); y.AddKey(t, q.y); z.AddKey(t, q.z); w.AddKey(t, q.w);
                }
                SetCurve(clip, path, typeof(Transform), "m_LocalRotation.x", Smooth(x));
                SetCurve(clip, path, typeof(Transform), "m_LocalRotation.y", Smooth(y));
                SetCurve(clip, path, typeof(Transform), "m_LocalRotation.z", Smooth(z));
                SetCurve(clip, path, typeof(Transform), "m_LocalRotation.w", Smooth(w));
            }
        }

        private static string Path(Actor actor, Transform t) => AnimationUtility.CalculateTransformPath(t, actor.Root);

        private static void SetCurve(AnimationClip clip, string path, Type type, string property, AnimationCurve curve)
        {
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
        }

        private static void SetLinear(AnimationClip clip, string path, Type type, string property)
        {
            var binding = EditorCurveBinding.FloatCurve(path, type, property);
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null) return;
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static AnimationCurve Curve(params (float t, float v)[] keys)
        {
            var curve = new AnimationCurve();
            foreach (var (t, v) in keys) curve.AddKey(t, v);
            return Smooth(curve);
        }

        private static AnimationCurve StepCurve(params (float t, float v)[] keys)
        {
            var curve = new AnimationCurve();
            foreach (var (t, v) in keys) curve.AddKey(t, v);
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            }
            return curve;
        }

        /// ClampedAuto: suave pero sin pasarse del valor de las claves (nada de rebotes).
        private static AnimationCurve Smooth(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            return curve;
        }

        // ------------------------------------------------------------ pantalla

        private static RenderTexture CreateRenderTexture()
        {
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);
            if (rt != null) return rt;
            rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32)
            {
                name = "IntroScreen",
                antiAliasing = 2,
                useMipMap = false,
                filterMode = FilterMode.Bilinear,
            };
            AssetDatabase.CreateAsset(rt, RenderTexturePath);
            return rt;
        }

        /// <summary>
        /// Pantalla de cine flotando en el hueco de la oficina P1, a 2 m del punto de espera y a la
        /// altura de los ojos, con su marco y la franja de subtitulos debajo.
        /// </summary>
        private static Transform CreateScreen(Transform root, Transform waitPoint, RenderTexture rt, TMP_FontAsset font,
                                              out SubtitleBar subtitles)
        {
            var screen = new GameObject("Pantalla").transform;
            screen.SetParent(root, false);

            Vector3 basePos = waitPoint != null ? waitPoint.position : Vector3.zero;
            Vector3 fwd = waitPoint != null ? Flat(waitPoint.forward) : Vector3.forward;
            screen.SetPositionAndRotation(basePos + fwd * 2.0f + Vector3.up * 1.62f, Quaternion.LookRotation(fwd, Vector3.up));

            const float width = 2.0f, height = width * 9f / 16f;

            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Marco";
            Object.DestroyImmediate(frame.GetComponent<Collider>());
            frame.transform.SetParent(screen, false);
            frame.transform.localScale = new Vector3(width + 0.08f, height + 0.08f, 0.04f);
            frame.GetComponent<Renderer>().sharedMaterial = SaveMaterial("Marco", HideAndSeekMaterials.CreateLit(new Color(0.05f, 0.06f, 0.08f)));

            var display = GameObject.CreatePrimitive(PrimitiveType.Quad);
            display.name = "Imagen";
            Object.DestroyImmediate(display.GetComponent<Collider>());
            display.transform.SetParent(screen, false);
            // El Quad se ve desde -Z: su +Z apunta en la misma direccion que mira el jugador.
            display.transform.localPosition = new Vector3(0f, 0f, -0.025f);
            display.transform.localScale = new Vector3(width, height, 1f);
            var mat = HideAndSeekMaterials.CreateUnlit(Color.white);
            mat.SetTexture("_BaseMap", rt);
            mat.mainTexture = rt;
            var renderer = display.GetComponent<Renderer>();
            renderer.sharedMaterial = SaveMaterial("Pantalla", mat);
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            var subGo = new GameObject("Subtitulos");
            subGo.transform.SetParent(screen, false);
            subGo.transform.localPosition = new Vector3(0f, -height * 0.5f - 0.16f, -0.03f);
            subtitles = subGo.AddComponent<SubtitleBar>();
            var so = new SerializedObject(subtitles);
            so.FindProperty("followHead").boolValue = false;
            so.FindProperty("font").objectReferenceValue = font;
            so.FindProperty("width").floatValue = 1900f;
            so.FindProperty("fontSize").floatValue = 60f;
            so.FindProperty("padding").floatValue = 22f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return screen;
        }

        private static SubtitleBar CreateLobbySubtitles(Transform root, TMP_FontAsset font)
        {
            var go = new GameObject("SubtitulosLobby");
            go.transform.SetParent(root, false);
            var bar = go.AddComponent<SubtitleBar>();
            var so = new SerializedObject(bar);
            so.FindProperty("followHead").boolValue = true;
            so.FindProperty("font").objectReferenceValue = font;
            so.ApplyModifiedPropertiesWithoutUndo();
            return bar;
        }

        private static void WireLobbyTutorial(Scene scene, SubtitleBar bar)
        {
            var tutorial = Object.FindAnyObjectByType<LobbyTutorial>(FindObjectsInactive.Include);
            if (tutorial == null) return;
            var so = new SerializedObject(tutorial);
            so.FindProperty("subtitles").objectReferenceValue = bar;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------ camara y efectos

        private static Transform CreateCameraRig(Transform set, RenderTexture rt, int layer, out Camera camera)
        {
            var rig = new GameObject("CamaraCine", typeof(Animator)).transform;
            rig.SetParent(set, false);

            var camGo = new GameObject("Camara");
            camGo.transform.SetParent(rig, false);
            camera = camGo.AddComponent<Camera>();
            camera.targetTexture = rt;
            camera.fieldOfView = 45f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 60f;
            camera.depth = -10;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.volumeLayerMask = layer >= 0 ? 1 << layer : 1;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = false;
            return rig;
        }

        private static IntroSetFx CreateFx(Transform set, int layer)
        {
            var go = new GameObject("Efectos", typeof(Animator));
            go.transform.SetParent(set, false);
            var fx = go.AddComponent<IntroSetFx>();

            var volumeGo = new GameObject("PostProcesadoCine");
            volumeGo.transform.SetParent(go.transform, false);
            if (layer >= 0) volumeGo.layer = layer;
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;
            volume.sharedProfile = CreateVolumeProfile();
            fx.volume = volume;
            return fx;
        }

        private static VolumeProfile CreateVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile != null) return profile;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumePath);
            var color = profile.Add<ColorAdjustments>(true);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = 0.3f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.5f;
            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.value = TonemappingMode.Neutral;
            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ------------------------------------------------------------ atrezo

        private static void CreateProps(Transform set, Scene scene, Places p, SetRefs refs, TMP_FontAsset font)
        {
            var props = new GameObject("Atrezo").transform;
            props.SetParent(set, false);
            var fx = refs.Fx;
            var setLights = new List<Light>();
            var emergencyLights = new List<Light>();

            var grey = SaveMaterial("Estacion", HideAndSeekMaterials.CreateLit(new Color(0.55f, 0.58f, 0.62f)));
            var dark = SaveMaterial("Oscuro", HideAndSeekMaterials.CreateLit(new Color(0.08f, 0.09f, 0.1f)));
            var glow = SaveMaterial("Piloto", HideAndSeekMaterials.CreateUnlit(Color.white));

            // --- Estacion de carga: plataforma, panel trasero con un piloto y un cable por robot.
            Vector3 station = p.Pos("Estacion");
            var stationRoot = new GameObject("EstacionCarga").transform;
            stationRoot.SetParent(props, false);
            stationRoot.SetPositionAndRotation(station, Quaternion.Euler(0f, p.Yaw("Estacion"), 0f));
            float stationWidth = NaoCount * 0.62f + 0.3f;
            Box(stationRoot, "Plataforma", new Vector3(0f, 0.04f, 0f), new Vector3(stationWidth, 0.08f, 0.75f), grey);
            Box(stationRoot, "Panel", new Vector3(0f, 0.55f, -0.42f), new Vector3(stationWidth, 1.1f, 0.08f), grey);
            Box(stationRoot, "Rotulo", new Vector3(0f, 1.02f, -0.375f), new Vector3(stationWidth * 0.9f, 0.1f, 0.01f), dark);
            var pads = new List<Renderer>();
            for (int i = 0; i < NaoCount; i++)
            {
                float x = (i - (NaoCount - 1) * 0.5f) * 0.62f;
                pads.Add(Box(stationRoot, $"Piloto_{i + 1}", new Vector3(x, 0.85f, -0.375f), new Vector3(0.16f, 0.06f, 0.01f), glow));
                pads.Add(Box(stationRoot, $"Base_{i + 1}", new Vector3(x, 0.085f, 0f), new Vector3(0.42f, 0.01f, 0.42f), glow));
                var cable = Box(stationRoot, $"Cable_{i + 1}", new Vector3(x, 0.42f, -0.24f), new Vector3(0.025f, 0.025f, 0.32f), dark);
                cable.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            }
            fx.stationLights = pads.ToArray();
            var spark = new GameObject("Chispa").AddComponent<Light>();
            spark.transform.SetParent(stationRoot, false);
            spark.transform.localPosition = new Vector3(0.35f, 0.25f, -0.3f);
            spark.type = LightType.Point;
            spark.color = new Color(1f, 0.85f, 0.5f);
            spark.range = 2.5f;
            spark.intensity = 0f;
            spark.shadows = LightShadows.None;
            fx.sparkLight = spark;
            setLights.Add(PointLight(stationRoot, "LuzEstacion", new Vector3(0f, 2.1f, 1.2f), new Color(0.75f, 0.85f, 1f), 1.6f, 6f));

            // --- Consola del servidor central: pie, monitor, textos y bateria.
            Vector3 consolePos = p.Pos("Consola");
            var console = new GameObject("ConsolaServidor").transform;
            console.SetParent(props, false);
            console.SetPositionAndRotation(consolePos, Quaternion.Euler(0f, p.Yaw("Consola"), 0f));
            Box(console, "Pie", new Vector3(0f, 0.42f, 0f), new Vector3(0.5f, 0.84f, 0.35f), dark);
            Box(console, "Mastil", new Vector3(0f, 0.95f, -0.05f), new Vector3(0.06f, 0.3f, 0.06f), dark);
            Box(console, "CarcasaMonitor", new Vector3(0f, 1.12f, -0.03f), new Vector3(0.92f, 0.56f, 0.05f), dark);
            var screenGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screenGo.name = "Monitor";
            Object.DestroyImmediate(screenGo.GetComponent<Collider>());
            screenGo.transform.SetParent(console, false);
            screenGo.transform.localPosition = new Vector3(0f, 1.12f, 0.0f);
            // La consola mira a +Z; el Quad se ve desde -Z, asi que se gira para que su cara mire al frente.
            screenGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            screenGo.transform.localScale = new Vector3(0.84f, 0.48f, 1f);
            var monitorMat = SaveMaterial("Monitor", HideAndSeekMaterials.CreateUnlit(Color.white));
            screenGo.GetComponent<Renderer>().sharedMaterial = monitorMat;
            fx.monitorScreen = screenGo.GetComponent<Renderer>();
            fx.monitorTitle = WorldText(console, "Titulo", new Vector3(0f, 1.3f, 0.005f), 0.075f, font, new Vector2(0.8f, 0.1f));
            fx.monitorBody = WorldText(console, "Cuerpo", new Vector3(0f, 1.1f, 0.005f), 0.06f, font, new Vector2(0.8f, 0.28f));
            fx.monitorTitle.color = new Color(0.5f, 0.8f, 0.95f);

            // Barra de bateria: grupo en unidades de la barra (ancho 1) para que IntroSetFx la llene desde la izquierda.
            var battery = new GameObject("Bateria").transform;
            battery.SetParent(console, false);
            battery.localPosition = new Vector3(0f, 0.95f, 0.009f);
            battery.localScale = new Vector3(0.5f, 0.06f, 0.002f);
            Box(battery, "Marco", new Vector3(0f, 0f, -1f), new Vector3(1.06f, 1.5f, 1f), dark);
            var fill = Box(battery, "Barra", Vector3.zero, Vector3.one, glow);
            fx.batteryGroup = battery.gameObject;
            fx.batteryFill = fill.transform;
            fx.batteryFillRenderer = fill;
            setLights.Add(PointLight(console, "LuzMonitor", new Vector3(0f, 1.15f, 0.6f), new Color(0.6f, 0.75f, 1f), 0.9f, 2.5f));

            // --- LEDs en el frente de los racks del fondo.
            var racks = Find(scene, "RacksOfficeS1");
            var leds = new List<Renderer>();
            if (racks != null && TryBounds(racks.transform, out Bounds rb))
            {
                var ledRoot = new GameObject("LEDsRacks").transform;
                ledRoot.SetParent(props, false);
                float zFront = rb.min.z - 0.006f;
                for (float x = rb.min.x + 0.12f; x < rb.max.x - 0.08f; x += 0.11f)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        float y = rb.max.y - 0.08f - row * 0.07f;
                        var led = Box(ledRoot, "LED", Vector3.zero, new Vector3(0.025f, 0.012f, 0.004f), glow);
                        led.transform.position = new Vector3(x + (row % 2) * 0.03f, y, zFront);
                        leds.Add(led);
                    }
                }
                setLights.Add(PointLight(ledRoot, "LuzRacks", new Vector3(rb.center.x, rb.max.y + 0.6f, rb.min.z - 1.0f),
                    new Color(0.6f, 1f, 0.75f), 1.2f, 3.5f));
            }
            fx.rackLedRenderers = leds.ToArray();

            // --- Luces generales del set y las rojas de emergencia.
            setLights.Add(PointLight(props, "LuzCentro", new Vector3(6.5f, station.y + 3.0f, -1.5f), new Color(1f, 0.95f, 0.85f), 2.2f, 9f));
            setLights.Add(PointLight(props, "LuzFondo", new Vector3(10.5f, station.y + 3.0f, -5.0f), new Color(1f, 0.95f, 0.85f), 2.0f, 8f));
            emergencyLights.Add(PointLight(props, "Emergencia_Estacion", station + Vector3.up * 2.6f + p.Fwd("Estacion") * 1.5f, new Color(1f, 0.12f, 0.08f), 2.2f, 7f));
            emergencyLights.Add(PointLight(props, "Emergencia_Centro", new Vector3(6.0f, station.y + 2.8f, -2.4f), new Color(1f, 0.12f, 0.08f), 2.4f, 8f));
            emergencyLights.Add(PointLight(props, "Emergencia_Fondo", new Vector3(10.6f, station.y + 2.8f, -4.0f), new Color(1f, 0.12f, 0.08f), 2.0f, 7f));
            fx.setLights = setLights.ToArray();
            fx.emergencyLights = emergencyLights.ToArray();
            fx.setLightIntensity = setLights.ConvertAll(l => l.intensity).ToArray();
            fx.emergencyLightIntensity = emergencyLights.ConvertAll(l => l.intensity).ToArray();

            // --- Rayo de datos.
            var beamGo = new GameObject("RayoDatos");
            beamGo.transform.SetParent(props, false);
            var line = beamGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 4;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = SaveMaterial("Rayo", HideAndSeekMaterials.CreateUnlit(new Color(0.45f, 0.85f, 1f)));
            line.enabled = false;
            fx.beamLine = line;
        }

        private static void WireFxTargets(SetRefs refs)
        {
            var fx = refs.Fx;
            fx.beamFrom = refs.Aura.Muzzle;
            var target = new GameObject("ObjetivoRayo").transform;
            target.SetParent(refs.Naos[3].Body, false);
            target.localPosition = new Vector3(0f, NaoHeight * 0.6f, 0f);
            fx.beamTo = target;
            fx.syncedRenderers = refs.Naos[3].Renderers;
            EditorUtility.SetDirty(fx);
        }

        // ------------------------------------------------------------ actores

        private static Actor CreateAura(Transform set, Places p, SetRefs refs)
        {
            var actor = CreateActorShell(set, "Aura", AuraPath, AuraHeight, out GameObject model);
            if (model == null) return actor;

            Transform head = FindDeep(model.transform, "head_joint.fixed.bone");
            Transform shoulder = FindDeep(model.transform, "right_shoulder_pitch_joint.revolute.bone");
            Transform rightElbow = FindDeep(model.transform, "right_elbow_roll_joint.revolute.bone");
            Transform leftElbow = FindDeep(model.transform, "left_elbow_roll_joint.revolute.bone");

            // Las manos y las luces de la cabeza cuelgan de la raiz del GLB: se enganchan al brazo y a
            // la cabeza para que se muevan con ellos.
            Reparent(model.transform, "Sketchfab_model", leftElbow);
            Transform rightHand = Reparent(model.transform, "Sketchfab_model.001", rightElbow);
            var lightRenderers = new List<Renderer>();
            foreach (var name in new[] { "Curva_Bezier", "Curva_Bezier.001" })
            {
                Transform l = Reparent(model.transform, name, head);
                if (l != null) lightRenderers.AddRange(l.GetComponentsInChildren<Renderer>());
            }
            var lightMat = SaveMaterial("LucesAura", HideAndSeekMaterials.CreateUnlit(Color.white));
            foreach (var r in lightRenderers)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = lightMat;
                r.sharedMaterials = mats;
            }
            refs.Fx.auraLightRenderers = lightRenderers.ToArray();

            // Luz de la cabeza.
            if (head != null && TryBounds(FindDeep(model.transform, "head_link") ?? head, out Bounds hb))
            {
                var l = new GameObject("LuzCabeza").AddComponent<Light>();
                l.transform.SetParent(actor.Body, false);
                l.transform.position = hb.center + actor.Body.forward * 0.25f;
                l.transform.SetParent(head, true);
                l.type = LightType.Point;
                l.color = new Color(0.5f, 0.85f, 1f);
                l.range = 2.5f;
                l.intensity = 0f;
                l.shadows = LightShadows.None;
                refs.Fx.auraLight = l;
            }

            // Pistola de datos en la mano derecha, alineada con el brazo: al levantarlo apunta al frente.
            var pistolPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PistolPath);
            Transform hand = rightHand != null ? rightHand : rightElbow;
            if (pistolPrefab != null && hand != null && shoulder != null && TryBounds(hand, out Bounds handBounds))
            {
                var pistol = (GameObject)PrefabUtility.InstantiatePrefab(pistolPrefab);
                PrefabUtility.UnpackPrefabInstance(pistol, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                pistol.name = "PistolaDatos";
                Vector3 armDir = (handBounds.center - shoulder.position).normalized;
                pistol.transform.SetPositionAndRotation(handBounds.center + armDir * 0.02f,
                    Quaternion.LookRotation(armDir, actor.Body.forward));
                pistol.transform.localScale = Vector3.one * 1.1f;
                pistol.transform.SetParent(rightElbow != null ? rightElbow : hand, true);
                foreach (var c in pistol.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                actor.Muzzle = FindDeep(pistol.transform, "Muzzle");

                if (actor.Muzzle != null)
                {
                    var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    tip.name = "BrilloCanon";
                    Object.DestroyImmediate(tip.GetComponent<Collider>());
                    tip.transform.SetParent(actor.Muzzle, false);
                    tip.transform.localScale = Vector3.one * 0.03f;
                    var tipRenderer = tip.GetComponent<Renderer>();
                    tipRenderer.sharedMaterial = SaveMaterial("BrilloCanon", HideAndSeekMaterials.CreateUnlit(Color.white));
                    refs.Fx.muzzleRenderer = tipRenderer;

                    var ml = new GameObject("LuzCanon").AddComponent<Light>();
                    ml.transform.SetParent(actor.Muzzle, false);
                    ml.type = LightType.Point;
                    ml.color = new Color(0.4f, 0.8f, 1f);
                    ml.range = 1.2f;
                    ml.intensity = 0f;
                    ml.shadows = LightShadows.None;
                    refs.Fx.muzzleLight = ml;
                }
            }

            actor.Head = head;
            actor.ArmRight = shoulder;
            SaveMarks(actor);
            actor.Body.SetPositionAndRotation(p.Pos("Aura_Aparicion"), Quaternion.Euler(0f, p.Yaw("Aura_Aparicion"), 0f));
            return actor;
        }

        private static Actor CreateNao(Transform set, int index, Places p)
        {
            var actor = CreateActorShell(set, $"Nao_{index + 1}", NaoPath, NaoHeight, out GameObject model);
            if (model == null) return actor;
            actor.Head = FindDeep(model.transform, "HeadYaw.revolute.bone");
            SaveMarks(actor);
            actor.Body.SetPositionAndRotation(p.Slot(index), Quaternion.Euler(0f, p.Yaw("Estacion"), 0f));
            return actor;
        }

        /// <summary>
        /// Actor = raiz con Animator (para el Timeline) en el origen -> Body (lo que se anima) ->
        /// Modelo (escala y giro para que mida lo que toca, mire a +Z y tenga los pies en Body).
        /// </summary>
        private static Actor CreateActorShell(Transform set, string name, string modelPath, float height, out GameObject model)
        {
            var root = new GameObject(name, typeof(Animator)).transform;
            root.SetParent(set, false);
            root.GetComponent<Animator>().applyRootMotion = false;
            var body = new GameObject("Body").transform;
            body.SetParent(root, false);
            var holder = new GameObject("Modelo").transform;
            holder.SetParent(body, false);

            var actor = new Actor { Root = root, Body = body, Animator = root.GetComponent<Animator>() };

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (asset == null)
            {
                Debug.LogError($"[Intro] No se encontro el modelo {modelPath}.");
                model = null;
                return actor;
            }
            model = (GameObject)PrefabUtility.InstantiatePrefab(asset, holder);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            // Los GLB vienen de URDF y miran a -X: con 90 grados miran a +Z, como el resto.
            holder.localRotation = Quaternion.Euler(0f, 90f, 0f);
            if (TryBounds(model.transform, out Bounds b))
            {
                float scale = height / Mathf.Max(0.001f, b.size.y);
                holder.localScale = Vector3.one * scale;
                TryBounds(model.transform, out b);
                Vector3 offset = body.position - new Vector3(b.center.x, b.min.y, b.center.z);
                holder.position += offset;
            }

            actor.Renderers = model.GetComponentsInChildren<Renderer>();
            foreach (var r in actor.Renderers) r.shadowCastingMode = ShadowCastingMode.Off;
            return actor;
        }

        /// Guarda en el actor las articulaciones y su pose de reposo, para regenerar el Timeline luego.
        private static void SaveMarks(Actor actor)
        {
            var marks = actor.Root.gameObject.AddComponent<IntroActorMarks>();
            marks.head = actor.Head;
            marks.armRight = actor.ArmRight;
            marks.muzzle = actor.Muzzle;
            marks.renderers = actor.Renderers;
            if (actor.Head != null)
            {
                marks.headRest = actor.Head.localRotation;
                marks.headParentInBody = Quaternion.Inverse(actor.Body.rotation) * actor.Head.parent.rotation;
            }
            if (actor.ArmRight != null)
            {
                marks.armRest = actor.ArmRight.localRotation;
                marks.armParentInBody = Quaternion.Inverse(actor.Body.rotation) * actor.ArmRight.parent.rotation;
            }
        }

        private static Transform Reparent(Transform model, string name, Transform newParent)
        {
            Transform t = FindDeep(model, name);
            if (t != null && newParent != null) t.SetParent(newParent, true);
            return t;
        }

        // ------------------------------------------------------------ utilidades

        private static Renderer Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        private static Light PointLight(Transform parent, string name, Vector3 worldPos, Color color, float intensity, float range)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.position = worldPos;
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            return light;
        }

        private static TextMeshPro WorldText(Transform parent, string name, Vector3 localPos, float size, TMP_FontAsset font, Vector2 area)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            // Como el monitor: girado para leerse desde delante de la consola.
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var text = go.AddComponent<TextMeshPro>();
            if (font != null) text.font = font;
            text.fontSize = size * 10f;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.rectTransform.sizeDelta = area;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.text = string.Empty;
            return text;
        }

        private static Material SaveMaterial(string name, Material mat)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = mat.shader;
                existing.CopyPropertiesFromMaterial(mat);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            mat.name = name;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static bool TryBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            bool has = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!has) { bounds = r.bounds; has = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return has;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static float FindFloorY(Scene scene)
        {
            var floor = Find(scene, "FloorColivri1");
            return floor != null ? floor.transform.position.y : -4.09f;
        }

        private static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 6; i < layers.arraySize; i++)
            {
                var element = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(element.stringValue)) continue;
                element.stringValue = name;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }
            Debug.LogWarning("[Intro] No quedan capas libres: el post-procesado de la cinematica usara la capa Default.");
            return -1;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        private static GameObject Find(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                var found = FindDeep(root.transform, name);
                if (found != null) return found.gameObject;
            }
            return null;
        }
    }
}
