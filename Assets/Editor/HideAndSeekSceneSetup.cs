using System.Collections.Generic;
using Colivri.HideAndSeek;
using Meta.XR.MultiplayerBlocks.NGO;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Colivri.HideAndSeek.EditorTools
{
    /// <summary>
    /// Configura la escena online para el juego de escondidas.
    /// Es idempotente: se puede ejecutar las veces que haga falta.
    ///
    /// Lo que hace:
    ///  1. Quita los Building Blocks que no encajan con un juego VR virtual sin colocacion
    ///     (Custom Matchmaking, Colocation, Passthrough) y desactiva MRUK.
    ///  2. Apaga el passthrough del OVRManager y deja la camara con skybox.
    ///  3. Configura el AutoMatchmaking y asigna el PlayerPrefab al NetworkManager.
    ///  4. Envuelve el rig en un "PlayerRoot" escalable con PlayerRig + PlayerGun.
    ///  5. Crea el manager de partida, los puntos de aparicion y el HUD.
    /// </summary>
    public static class HideAndSeekSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/MainModel/MainModel_Env.unity";
        private const string PlayerPrefabPath = "Assets/Prefabs/Network/NetworkPlayer.prefab";
        private const string PistolPrefabPath = "Assets/Prefabs/HideAndSeek/Pistol.prefab";

        private const string PlayerRootName = "PlayerRoot";
        private const string ManagerName = "[HideAndSeek] Manager";
        private const string SpawnPointsName = "[HideAndSeek] SpawnPoints";
        private const string HudName = "[HideAndSeek] HUD";
        /// Hijo de SpawnPoints, colocado a mano, donde espera el buscador mientras los demas se esconden.
        private const string SeekerWaitPointName = "Spawn_Buscador_WhenHiding";

        // --- Modelos de los avatares ---
        private const string HeadPivotName = "HeadPivot";
        private const string SeekerGunVisualName = "SeekerGunVisual";
        /// Mismo offset que PlayerRig.gunLocalPosition/Euler en la escena: los demas ven la pistola
        /// justo donde la tiene el buscador en su propia mano.
        private static readonly Vector3 GunLocalPosition = new Vector3(0f, -0.02f, 0.03f);
        private static readonly Vector3 GunLocalEuler = Vector3.zero;

        /// <summary>Todo lo que distingue el montaje de un robot del de otro.</summary>
        private sealed class RobotAvatarSpec
        {
            public string Label;
            public string ModelPath;
            /// Hijo de BodyPivot que se crea para alojar el robot.
            public string VisualName;
            /// Nodo cuyo subarbol gira con el casco (la cabeza y lo que lleva montado).
            public string HeadSubtreeNode;
            /// Malla de la cabeza. Su centro se alinea con la raiz del avatar (el ojo) y hace de pivote.
            public string HeadMeshNode;
            /// Prefijos de los links de brazo, que se borran: no hay IK que los mueva y quedarian
            /// tiesos al lado de unas manos que si siguen a los mandos.
            public string[] ArmPrefixes;
            /// Nodos sueltos en la raiz del GLB que van montados en la cabeza (luces, adornos).
            /// Se cuelgan del pivote de la cabeza para que giren con el casco. null = ninguno.
            public string[] HeadExtraNodes;
            /// Distancia del centro de la cabeza a los pies, en metros. La raiz del avatar esta a la
            /// altura real de los ojos, asi que con esto los pies caen aproximadamente en el suelo.
            public float EyeHeight;
            /// Giro sobre Y para que el robot mire hacia donde mira el jugador. Los GLB vienen de
            /// URDF y GLTFUtility invierte X al importar: acaban mirando a -X, asi que hay que
            /// girarlos 90 grados para alinearlos con el "adelante" de Unity (+Z).
            public float YawOffset;
            /// Campos de NetworkPlayer donde se enchufan la raiz del robot y el pivote de la cabeza.
            public string VisualField;
            public string HeadField;

            /// Nodos del GLB que se copian como mano bajo HandL/HandR del avatar. null = no hay mano
            /// de robot en ese lado (el buscador lleva la pistola en la derecha).
            public string HandNodeLeft;
            public string HandNodeRight;
            /// Nodos cuya posicion hace de articulacion al medir el eje de cada mano. null = el
            /// propio nodo de la mano. Hace falta cuando el origen de la mano no esta en la muneca.
            public string HandJointNodeLeft;
            public string HandJointNodeRight;
            /// Si en la mano derecha va la pistola en vez de una mano del robot.
            public bool GunInRightHand;
            /// Hijo de HandL/HandR que aloja la copia de la mano.
            public string HandVisualName;
            /// Punto de la malla, a lo largo de su eje, que cae en el agarre del mando
            /// (0 = en la articulacion, 1 = en la punta).
            public float HandGripFraction;
            /// Giro extra tras alinear la malla con el "adelante" del mando, para corregir el ladeo.
            public Vector3 HandEuler;
            /// Campos de NetworkPlayer donde se enchufan las manos (o la pistola).
            public string HandLeftField;
            public string HandRightField;
        }

        private static readonly RobotAvatarSpec SeekerAvatar = new RobotAvatarSpec
        {
            Label = "buscador",
            ModelPath = "Assets/Models/RobotsColivri/AuraConManosyluces.glb",
            VisualName = "SeekerVisual",
            // head_joint tiene traslacion cero (su pivote es el origen del modelo), por eso se
            // inserta un pivote propio en vez de rotar el nodo directamente.
            HeadSubtreeNode = "head_joint.fixed.bone",
            HeadMeshNode = "head_link",
            // Las manos (Sketchfab_model y Sketchfab_model.001) cuelgan de la raiz del GLB y no del
            // brazo: sin brazos se quedarian flotando junto al cuerpo, asi que tambien se quitan.
            ArmPrefixes = new[] { "left_shoulder", "right_shoulder", "left_elbow", "right_elbow", "Sketchfab_model" },
            // Luces de la cabeza, tambien sueltas en la raiz del GLB.
            HeadExtraNodes = new[] { "Curva_Bezier", "Curva_Bezier.001" },
            EyeHeight = 1.61f,
            YawOffset = 90f,
            VisualField = "seekerVisual",
            HeadField = "seekerHead",
            // Sketchfab_model es la mano izquierda (la .001 es la derecha, que lleva la pistola).
            // Su origen queda por encima de la malla, asi que el eje se mide desde el antebrazo.
            HandNodeLeft = "Sketchfab_model",
            HandNodeRight = null,
            HandJointNodeLeft = "left_elbow_roll_link",
            GunInRightHand = true,
            HandVisualName = "SeekerHandVisual",
            HandGripFraction = 0.35f,
            // En el GLB la mano va plana con el pulgar hacia fuera; asi queda de canto, pulgar
            // arriba, como agarrando el mando.
            HandEuler = new Vector3(0f, 0f, -90f),
            HandLeftField = "seekerHandLeft",
            HandRightField = "seekerHandRight",
        };

        private static readonly RobotAvatarSpec HiderAvatar = new RobotAvatarSpec
        {
            Label = "escondido",
            ModelPath = "Assets/Models/RobotsColivri/NaoRobot.glb",
            VisualName = "HiderVisual",
            // HeadPitch lleva la malla Head y los sensores; Neck se queda quieto en HeadYaw.
            HeadSubtreeNode = "HeadPitch.revolute.bone",
            HeadMeshNode = "Head",
            ArmPrefixes = new[] { "LShoulderPitch", "RShoulderPitch" },
            // La del buscador por hiderScale (0,5) de PlayerRig: el escondido ve el mundo a esa altura.
            EyeHeight = 0.805f,
            YawOffset = 90f,
            VisualField = "hiderVisual",
            HeadField = "hiderHead",
            // La malla l_wrist/r_wrist es la mano entera (muneca y dedos).
            HandNodeLeft = "LWristYaw.revolute.bone",
            HandNodeRight = "RWristYaw.revolute.bone",
            GunInRightHand = false,
            HandVisualName = "HiderHandVisual",
            HandGripFraction = 0.5f,
            HandEuler = Vector3.zero,
            HandLeftField = "hiderHandLeft",
            HandRightField = "hiderHandRight",
        };

        // --- Sondeo de la geometria para colocar los puntos de aparicion ---
        private const float ProbeMinX = -4f, ProbeMaxX = 18f, ProbeMinZ = -12f, ProbeMaxZ = 12f;
        private const float ProbeStep = 0.5f;
        private const float ProbeTopY = 20f, ProbeBottomY = -20f;
        /// Un nivel cuenta como planta jugable solo si tiene al menos estos m2.
        private const float MinFloorArea = 100f;
        /// Medidas del buscador a tamano completo: si aqui no cabe, tampoco cabe un escondido.
        private const float PlayerHeight = 1.8f, PlayerRadius = 0.3f;
        private const float MinSpawnSeparation = 3f;
        private const int MaxSpawnPoints = 12;

        [MenuItem("Colivri/Hide and Seek/Configurar escena")]
        public static void Setup()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                Debug.LogError($"[HideAndSeek] La escena activa es '{scene.path}'. Abre {ScenePath} primero.");
                return;
            }

            CleanBuildingBlocks(scene);
            ConfigureRigAndCamera();
            ConfigureNetworking();
            var playerRoot = WrapPlayerRoot();
            var spawnPoints = CreateSpawnPoints(scene);
            CreateManager(scene, spawnPoints);
            CreateHud(scene);
            ConfigurePlayerRoot(playerRoot);
            PlaceRigOnGroundFloor(playerRoot, spawnPoints);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[HideAndSeek] Escena configurada y guardada.");
        }

        // ------------------------------------------------------------ 1. limpieza

        private static void CleanBuildingBlocks(Scene scene)
        {
            foreach (var name in new[]
                     {
                         "[BuildingBlock] Custom Matchmaking",
                         "[BuildingBlock] Colocation",
                         "[BuildingBlock] Passthrough",
                         // La etiqueta flotante con el nombre delataria a los escondidos:
                         // el buscador solo tendria que buscar carteles. Fuera.
                         // Al quitarla tambien desaparece el error de entitlement de Platform Init,
                         // que solo se disparaba porque PlayerNameTagSpawner.Start() lo invocaba.
                         "[BuildingBlock] Player Name Tag"
                     })
            {
                var go = Find(scene, name);
                if (go == null)
                {
                    Debug.Log($"[HideAndSeek] '{name}' ya no esta en la escena.");
                    continue;
                }
                Object.DestroyImmediate(go);
                Debug.Log($"[HideAndSeek] Borrado '{name}'.");
            }

            // MRUK solo sirve en MR. Se desactiva en vez de borrarlo, por si se recupera el modo MR.
            var mruk = Find(scene, "[BuildingBlock] MR Utility Kit");
            if (mruk != null && mruk.activeSelf)
            {
                mruk.SetActive(false);
                Debug.Log("[HideAndSeek] Desactivado '[BuildingBlock] MR Utility Kit'.");
            }
        }

        // -------------------------------------------------------- 2. rig y camara

        private static void ConfigureRigAndCamera()
        {
            var manager = Object.FindAnyObjectByType<OVRManager>(FindObjectsInactive.Include);
            if (manager != null)
            {
                var so = new SerializedObject(manager);
                var prop = so.FindProperty("isInsightPassthroughEnabled");
                if (prop != null)
                {
                    prop.boolValue = false;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("[HideAndSeek] OVRManager.isInsightPassthroughEnabled = false.");
                }
            }

            var rig = Object.FindAnyObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            if (rig != null && rig.centerEyeAnchor != null)
            {
                var cam = rig.centerEyeAnchor.GetComponent<Camera>();
                if (cam != null)
                {
                    cam.clearFlags = CameraClearFlags.Skybox;
                    EditorUtility.SetDirty(cam);
                    Debug.Log("[HideAndSeek] Camara del ojo central: clearFlags = Skybox.");
                }
            }
        }

        // ------------------------------------------------------------- 3. red

        private static void ConfigureNetworking()
        {
            var auto = Object.FindAnyObjectByType<AutoMatchmakingNGO>(FindObjectsInactive.Include);
            if (auto != null)
            {
                auto.lobbyName = "ColivriHideAndSeek";
                auto.maxPlayersPerRoom = 4;
                EditorUtility.SetDirty(auto);
                Debug.Log($"[HideAndSeek] AutoMatchmaking: lobby '{auto.lobbyName}', max {auto.maxPlayersPerRoom} jugadores.");
            }
            else
            {
                Debug.LogWarning("[HideAndSeek] No hay AutoMatchmakingNGO en la escena: no habra matchmaking.");
            }

            var nm = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (nm == null)
            {
                Debug.LogError("[HideAndSeek] No hay NetworkManager en la escena.");
                return;
            }
            if (prefab == null)
            {
                Debug.LogError($"[HideAndSeek] Falta {PlayerPrefabPath}.");
                return;
            }

            nm.NetworkConfig.PlayerPrefab = prefab;
            EditorUtility.SetDirty(nm);
            Debug.Log($"[HideAndSeek] NetworkManager.PlayerPrefab = {prefab.name} " +
                      $"(AutoSpawnPlayerPrefabClientSide = {nm.NetworkConfig.AutoSpawnPlayerPrefabClientSide}).");
        }

        // --------------------------------------------------------- 4. PlayerRoot

        /// <summary>
        /// Mete el rig dentro de un GameObject vacio que se pueda escalar sin romper
        /// el PlayerLocomotor de ISDK (que reposiciona OVRCameraRig en coordenadas de mundo).
        /// </summary>
        private static GameObject WrapPlayerRoot()
        {
            var existing = Object.FindAnyObjectByType<PlayerRig>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Debug.Log("[HideAndSeek] PlayerRoot ya existia.");
                return existing.gameObject;
            }

            var rig = Object.FindAnyObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            if (rig == null)
            {
                Debug.LogError("[HideAndSeek] No hay OVRCameraRig en la escena.");
                return null;
            }

            // El rig vive dentro de "OVRCameraRigInteraction"; se envuelve ese, no el OVRCameraRig,
            // porque el locomotor escribe directamente sobre el transform de OVRCameraRig.
            Transform rigRoot = rig.transform;
            while (rigRoot.parent != null && rigRoot.parent.name != "MainModel")
            {
                rigRoot = rigRoot.parent;
            }

            // PlayerRoot se deja en la raiz de la escena: el jugador deja de colgar del modelo
            // del laboratorio, asi que escalarlo o moverlo no depende del entorno.
            var root = new GameObject(PlayerRootName);
            root.transform.SetPositionAndRotation(rigRoot.position, rigRoot.rotation);
            rigRoot.SetParent(root.transform, true);

            Debug.Log($"[HideAndSeek] Creado '{PlayerRootName}' envolviendo a '{rigRoot.name}'.");
            return root;
        }

        private static void ConfigurePlayerRoot(GameObject playerRoot)
        {
            if (playerRoot == null) return;

            var rig = GetOrAdd<PlayerRig>(playerRoot);
            var gunPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PistolPrefabPath);
            if (gunPrefab != null)
            {
                var so = new SerializedObject(rig);
                so.FindProperty("gunPrefab").objectReferenceValue = gunPrefab;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var gun = GetOrAdd<PlayerGun>(playerRoot);
            var audio = GetOrAdd<AudioSource>(playerRoot);
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;

            var gunSo = new SerializedObject(gun);
            gunSo.FindProperty("audioSource").objectReferenceValue = audio;
            // El trazo local debe cortarse contra lo mismo que el rayo del servidor; si no,
            // se veria el disparo parando donde el servidor no lo para (o al reves).
            gunSo.FindProperty("tracerMask").intValue = ShotLayerMask();
            gunSo.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(playerRoot);
            Debug.Log("[HideAndSeek] PlayerRoot configurado con PlayerRig + PlayerGun.");
        }

        // ------------------------------------------------------- 5. puntos y manager

        /// <summary>
        /// Deja al jugador de pie en la planta baja, en el punto del buscador.
        ///
        /// La escena traia el rig flotando: OVRCameraRig tenia un desplazamiento local de
        /// y = 0.468 hardcodeado que dejaba los pies a media altura entre la planta baja y el
        /// primer piso. Como el origen de tracking es FloorLevel, el transform de OVRCameraRig
        /// ES el suelo, asi que ese desplazamiento se pone a cero y la altura la marca PlayerRoot.
        /// </summary>
        private static void PlaceRigOnGroundFloor(GameObject playerRoot, SpawnPointSet spawnPoints)
        {
            if (playerRoot == null || spawnPoints == null) return;

            var rig = playerRoot.GetComponentInChildren<OVRCameraRig>(true);
            if (rig != null && rig.transform.localPosition.sqrMagnitude > 0.0001f)
            {
                Debug.Log($"[HideAndSeek] Quitado el desplazamiento hardcodeado de OVRCameraRig " +
                          $"({rig.transform.localPosition}); ahora la altura la fija PlayerRoot.");
                rig.transform.localPosition = Vector3.zero;
            }

            // Sin puntos generados no hay donde colocarlo; mejor dejarlo donde esta que mandarlo al origen.
            if (spawnPoints.SeekerPoint == null)
            {
                Debug.LogWarning("[HideAndSeek] No hay punto de buscador: el rig se queda donde estaba.");
                return;
            }

            var start = spawnPoints.SeekerPoint;

            playerRoot.transform.SetPositionAndRotation(start.position, start.rotation);
            EditorUtility.SetDirty(playerRoot);
            Debug.Log($"[HideAndSeek] Rig colocado en la planta baja, en {start.position}.");

            ExcludeRigCollidersFromShots(playerRoot);
        }

        /// <summary>
        /// Saca de la capa de disparo los colliders solidos que cuelgan del rig local.
        ///
        /// La escena traia un SphereCollider de radio 0.5 SIN trigger en CenterEyeAnchor, en la capa
        /// Default (resto del juego de pistas, donde servia para los triggers de proximidad). Como
        /// shotMask incluye Default, ese collider se come los disparos: el del propio jugador, y en
        /// el host tambien los de los demas cuando el rayo pasa cerca de su cabeza. Pasandolos a
        /// Ignore Raycast siguen existiendo para la fisica pero dejan de aparecer en los raycasts.
        /// </summary>
        private static void ExcludeRigCollidersFromShots(GameObject playerRoot)
        {
            int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
            int moved = 0;

            foreach (var col in playerRoot.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger) continue;                       // los triggers ya se ignoran en las consultas
                if (col.gameObject.layer == ignoreRaycast) continue;

                Debug.Log($"[HideAndSeek] '{col.name}' ({col.GetType().Name}) pasa a Ignore Raycast " +
                          "para que no bloquee los disparos.");
                col.gameObject.layer = ignoreRaycast;
                EditorUtility.SetDirty(col.gameObject);
                moved++;
            }

            if (moved > 0) Debug.Log($"[HideAndSeek] {moved} collider(s) del rig excluidos de los disparos.");
        }

        /// <summary>
        /// Coloca los puntos de aparicion sobre la PLANTA BAJA, sondeando la geometria real.
        ///
        /// No sirve usar el centro de las superficies <c>TeleportInteractable</c>: varias de ellas
        /// (PlaneOfficeS*, Curtain*) son planos degenerados con tamano cero en X o en Z, y su centro
        /// cae en sitios donde no hay suelo. En vez de eso se barre el mapa con raycasts y una celda
        /// solo se acepta si tiene suelo de planta baja debajo y hueco libre para estar de pie.
        /// </summary>
        private static SpawnPointSet CreateSpawnPoints(Scene scene)
        {
            var host = Find(scene, SpawnPointsName);
            if (host == null) host = new GameObject(SpawnPointsName);
            var set = GetOrAdd<SpawnPointSet>(host);

            // El punto de espera del buscador lo coloca el usuario a mano, asi que sobrevive.
            var waitPoint = set.SeekerWaitPoint != null
                ? set.SeekerWaitPoint
                : host.transform.Find(SeekerWaitPointName);

            // Este metodo es la fuente de verdad: se rehacen todos los puntos desde cero.
            for (int i = host.transform.childCount - 1; i >= 0; i--)
            {
                var child = host.transform.GetChild(i);
                if (child == waitPoint) continue;
                Object.DestroyImmediate(child.gameObject);
            }

            var waitSo = new SerializedObject(set);
            waitSo.FindProperty("seekerWaitPoint").objectReferenceValue = waitPoint;
            waitSo.ApplyModifiedPropertiesWithoutUndo();
            if (waitPoint == null)
            {
                Debug.LogWarning($"[HideAndSeek] No hay '{SeekerWaitPointName}': el buscador esperara en su punto normal.");
            }

            float groundY = FindGroundFloorY();
            var candidates = CollectStandableCells(groundY);
            Debug.Log($"[HideAndSeek] Planta baja detectada en y={groundY:F2}. " +
                      $"Celdas donde cabe un jugador de pie: {candidates.Count}.");

            if (candidates.Count == 0)
            {
                Debug.LogError("[HideAndSeek] No se encontro suelo pisable en la planta baja.");
                return set;
            }

            var chosen = SpreadOut(candidates, MinSpawnSeparation, MaxSpawnPoints);

            // Centroide de lo pisable: orienta los puntos hacia dentro y marca donde empieza el buscador.
            Vector3 centre = Vector3.zero;
            foreach (var c in candidates) centre += c;
            centre /= candidates.Count;

            Transform seekerPoint = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < chosen.Count; i++)
            {
                var point = new GameObject($"Spawn_{i:00}");
                point.transform.SetParent(host.transform, false);
                point.transform.position = chosen[i];

                Vector3 toCentre = centre - chosen[i];
                toCentre.y = 0f;
                point.transform.rotation = toCentre.sqrMagnitude > 0.01f
                    ? Quaternion.LookRotation(toCentre.normalized, Vector3.up)
                    : Quaternion.identity;

                float d = Vector3.SqrMagnitude(chosen[i] - centre);
                if (d < bestDistance) { bestDistance = d; seekerPoint = point.transform; }
            }

            if (seekerPoint != null)
            {
                seekerPoint.name = "Spawn_Buscador";
                var so = new SerializedObject(set);
                so.FindProperty("seekerPoint").objectReferenceValue = seekerPoint;
                // La lista se rellena sola desde los hijos en runtime; se deja vacia a proposito.
                so.FindProperty("points").ClearArray();
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Sin marca visible en el suelo: los puntos son transforms vacios. En el editor los
            // siguen dibujando los gizmos de SpawnPointSet.
            EditorUtility.SetDirty(host);
            Debug.Log($"[HideAndSeek] {chosen.Count} puntos colocados en la planta baja (y={groundY:F2}), " +
                      $"separados al menos {MinSpawnSeparation} m.");
            return set;
        }

        /// <summary>
        /// Busca el nivel horizontal mas bajo con superficie suficiente para jugar.
        /// Usa RaycastAll porque el modelo tiene varias plantas superpuestas y un raycast normal
        /// solo devolveria el techo.
        /// </summary>
        private static float FindGroundFloorY()
        {
            var area = new Dictionary<int, int>();   // altura en pasos de 0.25 m -> numero de celdas
            for (float x = ProbeMinX; x <= ProbeMaxX; x += ProbeStep)
            {
                for (float z = ProbeMinZ; z <= ProbeMaxZ; z += ProbeStep)
                {
                    var hits = Physics.RaycastAll(new Vector3(x, ProbeTopY, z), Vector3.down,
                        ProbeTopY - ProbeBottomY, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    foreach (var h in hits)
                    {
                        if (Vector3.Dot(h.normal, Vector3.up) < 0.7f) continue;
                        int bucket = Mathf.RoundToInt(h.point.y * 4f);
                        area.TryGetValue(bucket, out int c);
                        area[bucket] = c + 1;
                    }
                }
            }

            int minCells = Mathf.CeilToInt(MinFloorArea / (ProbeStep * ProbeStep));
            var buckets = new List<int>(area.Keys);
            buckets.Sort();
            foreach (var b in buckets)
            {
                if (area[b] >= minCells) return b / 4f;   // el mas bajo que sea lo bastante grande
            }
            return 0f;
        }

        /// <summary>
        /// Celdas de la planta baja donde de verdad cabe un jugador: hay suelo debajo, hay altura
        /// libre encima, no hay nada pegado alrededor, y las celdas vecinas tambien tienen suelo
        /// (para no acabar al borde de un hueco).
        ///
        /// El hueco se comprueba con raycasts y NO con <c>Physics.OverlapCapsule</c>: las paredes
        /// del modelo son un unico MeshCollider concavo (WallsS1), y PhysX no soporta consultas de
        /// solapamiento contra mallas concavas — devuelven impacto siempre y rechazarian el mapa
        /// entero. Los raycasts si funcionan contra ellas.
        /// </summary>
        private static List<Vector3> CollectStandableCells(float groundY)
        {
            var cells = new List<Vector3>();
            for (float x = ProbeMinX; x <= ProbeMaxX; x += ProbeStep)
            {
                for (float z = ProbeMinZ; z <= ProbeMaxZ; z += ProbeStep)
                {
                    if (!HasGroundAt(x, z, groundY)) continue;

                    if (!HasGroundAt(x + ProbeStep, z, groundY) || !HasGroundAt(x - ProbeStep, z, groundY) ||
                        !HasGroundAt(x, z + ProbeStep, groundY) || !HasGroundAt(x, z - ProbeStep, groundY))
                    {
                        continue;
                    }

                    if (!HasHeadroom(x, z, groundY)) continue;
                    if (!HasElbowRoom(x, z, groundY)) continue;

                    // Se usa la altura exacta del impacto, no la del nivel redondeado: si no, el
                    // jugador aparece flotando unos centimetros sobre el suelo.
                    cells.Add(new Vector3(x, GroundHeightAt(x, z, groundY), z));
                }
            }
            return cells;
        }

        /// <summary>Comprueba que por encima de la celda hay al menos la altura de un jugador de pie.</summary>
        private static bool HasHeadroom(float x, float z, float groundY)
        {
            var origin = new Vector3(x, groundY + 0.1f, z);
            if (Physics.Raycast(origin, Vector3.up, out RaycastHit hit, PlayerHeight, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return hit.distance >= PlayerHeight - 0.1f;
            }
            return true;   // nada encima: espacio de sobra
        }

        /// <summary>
        /// Comprueba que no hay pared ni mueble pegado a la celda, lanzando rayos horizontales
        /// a la altura de las rodillas y del pecho.
        /// </summary>
        private static bool HasElbowRoom(float x, float z, float groundY)
        {
            foreach (float height in new[] { 0.4f, 1.2f })
            {
                var origin = new Vector3(x, groundY + height, z);
                for (int a = 0; a < 8; a++)
                {
                    float angle = a * 45f * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    if (Physics.Raycast(origin, dir, PlayerRadius * 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Altura exacta del suelo en esa celda; devuelve groundY si no encuentra nada.</summary>
        private static float GroundHeightAt(float x, float z, float groundY)
        {
            var hits = Physics.RaycastAll(new Vector3(x, groundY + 2.5f, z), Vector3.down, 5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (Vector3.Dot(h.normal, Vector3.up) < 0.7f) continue;
                if (Mathf.Abs(h.point.y - groundY) <= 0.2f) return h.point.y;
            }
            return groundY;
        }

        private static bool HasGroundAt(float x, float z, float groundY)
        {
            var hits = Physics.RaycastAll(new Vector3(x, groundY + 2.5f, z), Vector3.down, 5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (Vector3.Dot(h.normal, Vector3.up) < 0.7f) continue;
                if (Mathf.Abs(h.point.y - groundY) <= 0.2f) return true;
            }
            return false;
        }

        /// <summary>
        /// Reparte los puntos lo mas lejos posible unos de otros: en cada vuelta escoge el candidato
        /// mas alejado de todo lo ya elegido, y para cuando ya no queda sitio con separacion suficiente.
        /// </summary>
        private static List<Vector3> SpreadOut(List<Vector3> candidates, float minSeparation, int maxCount)
        {
            var chosen = new List<Vector3>();
            if (candidates.Count == 0) return chosen;

            // Semilla fija para que dos ejecuciones den exactamente el mismo reparto.
            var state = Random.state;
            Random.InitState(20260822);
            chosen.Add(candidates[Random.Range(0, candidates.Count)]);
            Random.state = state;

            float minSqr = minSeparation * minSeparation;
            while (chosen.Count < maxCount)
            {
                Vector3 best = Vector3.zero;
                float bestScore = -1f;

                foreach (var c in candidates)
                {
                    float nearest = float.MaxValue;
                    foreach (var ch in chosen)
                    {
                        float d = Vector3.SqrMagnitude(c - ch);
                        if (d < nearest) nearest = d;
                    }
                    if (nearest > bestScore) { bestScore = nearest; best = c; }
                }

                if (bestScore < minSqr) break;
                chosen.Add(best);
            }
            return chosen;
        }

        private static void CreateManager(Scene scene, SpawnPointSet spawnPoints)
        {
            var existing = Object.FindAnyObjectByType<HideAndSeekManager>(FindObjectsInactive.Include);
            GameObject host;
            if (existing != null)
            {
                host = existing.gameObject;
            }
            else
            {
                host = Find(scene, ManagerName);
                if (host == null) host = new GameObject(ManagerName);
                GetOrAdd<NetworkObject>(host);
                GetOrAdd<HideAndSeekManager>(host);
            }

            var manager = host.GetComponent<HideAndSeekManager>();
            var so = new SerializedObject(manager);
            so.FindProperty("spawnPoints").objectReferenceValue = spawnPoints;
            so.FindProperty("shotMask").intValue = ShotLayerMask();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(host);
            Debug.Log($"[HideAndSeek] '{ManagerName}' listo (NetworkObject in-scene).");
        }

        // ----------------------------------------------------------------- 6. HUD

        private static void CreateHud(Scene scene)
        {
            if (Object.FindAnyObjectByType<HideAndSeekHUD>(FindObjectsInactive.Include) != null)
            {
                Debug.Log("[HideAndSeek] El HUD ya existia.");
                return;
            }

            var host = new GameObject(HudName, typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = host.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(320f, 200f);
            // Escala de arranque para que no sea un cartel gigante en la escena; en runtime
            // HideAndSeekHUD la vuelve a fijar al engancharse a la muneca.
            rect.localScale = Vector3.one * 0.0012f;

            var background = new GameObject("Background", typeof(UnityEngine.UI.Image));
            background.transform.SetParent(host.transform, false);
            var bgRect = background.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            background.GetComponent<UnityEngine.UI.Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var role = CreateLabel(host.transform, "RoleText", new Vector2(0f, 68f), 34f, FontStyles.Bold);
            var timer = CreateLabel(host.transform, "TimerText", new Vector2(0f, 20f), 48f, FontStyles.Bold);
            var hiders = CreateLabel(host.transform, "HidersText", new Vector2(0f, -28f), 24f, FontStyles.Normal);
            var status = CreateLabel(host.transform, "StatusText", new Vector2(0f, -68f), 20f, FontStyles.Italic);

            var hud = host.AddComponent<HideAndSeekHUD>();
            var so = new SerializedObject(hud);
            so.FindProperty("roleText").objectReferenceValue = role;
            so.FindProperty("timerText").objectReferenceValue = timer;
            so.FindProperty("hidersText").objectReferenceValue = hiders;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(host);
            Debug.Log($"[HideAndSeek] '{HudName}' creado. Se ancla solo a la muneca izquierda en runtime.");
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, Vector2 position,
                                                   float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(300f, 56f);

            var label = go.GetComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = Color.white;
            label.text = name;
            return label;
        }

        // ------------------------------------------------- 7. avatares robot

        /// <summary>Mete el Aura (con manos y luces) dentro de NetworkPlayer.prefab como aspecto del buscador.</summary>
        [MenuItem("Colivri/Hide and Seek/Configurar avatar del buscador")]
        public static void SetupSeekerAvatar() => MountRobotAvatar(SeekerAvatar);

        /// <summary>Mete el NaoRobot dentro de NetworkPlayer.prefab como aspecto de los escondidos.</summary>
        [MenuItem("Colivri/Hide and Seek/Configurar avatar del escondido")]
        public static void SetupHiderAvatar() => MountRobotAvatar(HiderAvatar);

        /// <summary>
        /// Monta un robot dentro de NetworkPlayer.prefab.
        ///
        /// Va en un menu aparte y no dentro de <see cref="Setup"/> porque toca el prefab y no la
        /// escena: no necesita tener abierta MainModel_Env ni rehacer los puntos de aparicion.
        /// Es idempotente: rehace el hijo del robot desde cero en cada pasada.
        /// </summary>
        private static void MountRobotAvatar(RobotAvatarSpec spec)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            if (model == null)
            {
                Debug.LogError($"[HideAndSeek] Falta {spec.ModelPath}.");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (root == null)
            {
                Debug.LogError($"[HideAndSeek] No se pudo abrir {PlayerPrefabPath}.");
                return;
            }

            try
            {
                var player = root.GetComponent<NetworkPlayer>();
                var pivot = root.transform.Find("BodyPivot");
                if (player == null || pivot == null)
                {
                    Debug.LogError("[HideAndSeek] El prefab del avatar no tiene NetworkPlayer o BodyPivot.");
                    return;
                }

                // Este metodo es la fuente de verdad del modelo: se rehace entero.
                var previous = pivot.Find(spec.VisualName);
                if (previous != null) Object.DestroyImmediate(previous.gameObject);

                // Cuelga de BodyPivot y no de la raiz para que solo herede el giro horizontal:
                // si colgase de la raiz, el robot se inclinaria entero al mirar al suelo. La
                // inclinacion la recibe solo la cabeza, a traves de HeadPivot.
                var host = new GameObject(spec.VisualName);
                host.transform.SetParent(pivot, false);
                host.transform.localPosition = Vector3.zero;
                host.transform.localRotation = Quaternion.Euler(0f, spec.YawOffset, 0f);
                host.transform.localScale = Vector3.one;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, host.transform);
                // Se desempaqueta a proposito: hace falta poder borrar los nodos de los brazos.
                // El precio es que reimportar el GLB ya no propaga; se vuelve a ejecutar este menu.
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                int arms = RemoveArms(instance, spec.ArmPrefixes);
                Debug.Log($"[HideAndSeek] Quitados {arms} link(s) de brazo del robot del {spec.Label}.");

                if (!TryMeasureModel(host.transform, instance.transform, out Bounds whole))
                {
                    Debug.LogError($"[HideAndSeek] El modelo del {spec.Label} no tiene mallas que medir.");
                    return;
                }

                var headMesh = FindDescendant(instance.transform, spec.HeadMeshNode);
                if (headMesh == null || !TryMeasureModel(host.transform, headMesh, out Bounds headBounds))
                {
                    Debug.LogError($"[HideAndSeek] No se encontro el nodo '{spec.HeadMeshNode}' del robot.");
                    return;
                }

                var headSubtree = FindDescendant(instance.transform, spec.HeadSubtreeNode);
                if (headSubtree == null || headSubtree.parent == null)
                {
                    Debug.LogError($"[HideAndSeek] No se encontro el nodo '{spec.HeadSubtreeNode}' del robot.");
                    return;
                }

                // Escala por medida real y no por una constante: si el GLB cambia de unidades,
                // esto sigue dando un robot de la altura pedida. Se mide del centro de la cabeza a
                // los pies y no el alto total: el Nao es cabezon y por alto total flotaria.
                float eyeToFeet = headBounds.center.y - whole.min.y;
                float scale = spec.EyeHeight / eyeToFeet;
                host.transform.localScale = Vector3.one * scale;
                // La cabeza del robot acaba donde esta el collider de impacto de la cabeza (y = 0
                // en local), asi que disparar a lo que se ve es disparar al collider.
                host.transform.localPosition = new Vector3(0f, -headBounds.center.y * scale, 0f);

                // Pivote propio en el centro de la cabeza: los nodos del GLB no sirven de pivote
                // (el head_joint del Aura esta en el origen del modelo y la haria orbitar alrededor
                // de la pelvis). En runtime NetworkPlayer copia aqui la rotacion del casco.
                var headPivot = new GameObject(HeadPivotName).transform;
                headPivot.SetParent(headSubtree.parent, false);
                headPivot.SetPositionAndRotation(host.transform.TransformPoint(headBounds.center),
                    host.transform.rotation);
                headSubtree.SetParent(headPivot, true);

                if (spec.HeadExtraNodes != null)
                {
                    foreach (var extraName in spec.HeadExtraNodes)
                    {
                        var extra = FindDescendant(instance.transform, extraName);
                        if (extra == null)
                        {
                            Debug.LogError($"[HideAndSeek] No se encontro el nodo '{extraName}' de la cabeza del robot.");
                            return;
                        }
                        extra.SetParent(headPivot, true);
                    }
                }

                var so = new SerializedObject(player);
                so.FindProperty(spec.VisualField).objectReferenceValue = host;
                so.FindProperty(spec.HeadField).objectReferenceValue = headPivot;
                so.ApplyModifiedPropertiesWithoutUndo();

                MountRobotHands(spec, model, root, player, scale);

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);

                Debug.Log($"[HideAndSeek] Robot del {spec.Label} montado: {whole.size.y:F2} u de alto en el " +
                          $"GLB, escala {scale:F4} para {spec.EyeHeight:F2} m de ojos a pies " +
                          $"({whole.size.y * scale:F2} m de alto). " +
                          $"Cabeza en y={headBounds.center.y * scale:F2} -> offset {host.transform.localPosition.y:F2}. " +
                          $"Pies en y={(whole.min.y * scale) + host.transform.localPosition.y:F2}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Monta las manos del robot (y la pistola del buscador) bajo HandL/HandR del avatar, que
        /// son los anchors que siguen a los mandos por NetworkTransform. Sustituyen a los cubos,
        /// que se quedan solo para espectadores y jugadores sin rol.
        /// </summary>
        private static void MountRobotHands(RobotAvatarSpec spec, GameObject model, GameObject root,
                                            NetworkPlayer player, float scale)
        {
            var handL = root.transform.Find("HandL");
            var handR = root.transform.Find("HandR");
            if (handL == null || handR == null)
            {
                Debug.LogError("[HideAndSeek] El prefab del avatar no tiene HandL o HandR.");
                return;
            }

            var so = new SerializedObject(player);

            so.FindProperty(spec.HandLeftField).objectReferenceValue =
                MountHand(spec, model, handL, spec.HandNodeLeft, spec.HandJointNodeLeft, scale);

            GameObject right = null;
            if (spec.GunInRightHand)
            {
                right = MountGun(handR);
            }
            else if (spec.HandNodeRight != null)
            {
                right = MountHand(spec, model, handR, spec.HandNodeRight, spec.HandJointNodeRight, scale);
            }
            so.FindProperty(spec.HandRightField).objectReferenceValue = right;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Copia el subarbol <paramref name="nodeName"/> del GLB bajo <paramref name="anchor"/>.
        ///
        /// Sale de una copia nueva del modelo porque la del cuerpo ya tiene los brazos borrados.
        /// Se orienta midiendo, no con angulos a mano: el eje de la malla es hacia donde cuelga
        /// desde su articulacion, y se alinea con el "adelante" del mando (+Z del anchor). La
        /// articulacion es <paramref name="jointNodeName"/> si se da, y si no el propio nodo.
        /// </summary>
        private static GameObject MountHand(RobotAvatarSpec spec, GameObject model, Transform anchor,
                                            string nodeName, string jointNodeName, float scale)
        {
            if (string.IsNullOrEmpty(nodeName)) return null;

            var previous = anchor.Find(spec.HandVisualName);
            if (previous != null) Object.DestroyImmediate(previous.gameObject);

            var host = new GameObject(spec.HandVisualName);
            host.transform.SetParent(anchor, false);
            host.transform.localPosition = Vector3.zero;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;

            var copy = (GameObject)PrefabUtility.InstantiatePrefab(model, host.transform);
            PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;

            var node = FindDescendant(copy.transform, nodeName);
            if (node == null)
            {
                Debug.LogError($"[HideAndSeek] No se encontro el nodo de mano '{nodeName}' del robot del {spec.Label}.");
                Object.DestroyImmediate(host);
                return null;
            }

            // Se saca el nodo del resto del modelo conservando su pose, y se tira lo demas.
            var hand = new GameObject("Hand").transform;
            hand.SetParent(host.transform, false);
            node.SetParent(hand, true);

            // La articulacion se toma antes de tirar la copia: puede ser un nodo de fuera de la mano.
            var jointNode = string.IsNullOrEmpty(jointNodeName) ? node : FindDescendant(copy.transform, jointNodeName);
            if (jointNode == null)
            {
                Debug.LogError($"[HideAndSeek] No se encontro el nodo de articulacion '{jointNodeName}' del robot del {spec.Label}.");
                Object.DestroyImmediate(host);
                return null;
            }
            Vector3 joint = hand.InverseTransformPoint(jointNode.position);
            Object.DestroyImmediate(copy);

            foreach (var collider in hand.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }

            if (!TryMeasureModel(hand, node, out Bounds bounds))
            {
                Debug.LogError($"[HideAndSeek] El nodo de mano '{nodeName}' no tiene mallas que medir.");
                Object.DestroyImmediate(host);
                return null;
            }

            // Eje dominante de articulacion -> centro de la malla, con su signo.
            Vector3 outward = bounds.center - joint;
            int axisIndex = 0;
            for (int i = 1; i < 3; i++)
            {
                if (Mathf.Abs(outward[i]) > Mathf.Abs(outward[axisIndex])) axisIndex = i;
            }
            Vector3 axis = Vector3.zero;
            axis[axisIndex] = outward[axisIndex] >= 0f ? 1f : -1f;

            // Punto de agarre a lo largo del eje, centrado en las otras dos direcciones.
            float near = bounds.center[axisIndex] - bounds.extents[axisIndex];
            float far = bounds.center[axisIndex] + bounds.extents[axisIndex];
            if (axis[axisIndex] < 0f) (near, far) = (far, near);
            Vector3 grip = bounds.center;
            grip[axisIndex] = Mathf.Lerp(near, far, spec.HandGripFraction);

            Quaternion rotation = Quaternion.Euler(spec.HandEuler) * Quaternion.FromToRotation(axis, Vector3.forward);
            hand.localScale = Vector3.one * scale;
            hand.localRotation = rotation;
            hand.localPosition = -(rotation * grip) * scale;

            Debug.Log($"[HideAndSeek] Mano '{nodeName}' del {spec.Label} montada en {anchor.name}: " +
                      $"{bounds.size[axisIndex] * scale:F2} m de largo, eje {axis}.");
            return host;
        }

        /// <summary>
        /// Pistola del buscador vista por los demas. Se deja enlazada al prefab de la pistola, asi
        /// que cambiar Pistol.prefab la cambia tambien aqui.
        /// </summary>
        private static GameObject MountGun(Transform anchor)
        {
            var previous = anchor.Find(SeekerGunVisualName);
            if (previous != null) Object.DestroyImmediate(previous.gameObject);

            var gunPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PistolPrefabPath);
            if (gunPrefab == null)
            {
                Debug.LogError($"[HideAndSeek] Falta {PistolPrefabPath}.");
                return null;
            }

            var gun = (GameObject)PrefabUtility.InstantiatePrefab(gunPrefab, anchor);
            gun.name = SeekerGunVisualName;
            gun.transform.localPosition = GunLocalPosition;
            gun.transform.localRotation = Quaternion.Euler(GunLocalEuler);
            gun.transform.localScale = Vector3.one;
            return gun;
        }

        /// <summary>
        /// Borra los links de brazo. Se recorre la lista entera y no solo los hombros porque la
        /// cadena puede venir aplanada; los hijos que ya cayeron con su padre se detectan por null.
        /// </summary>
        private static int RemoveArms(GameObject model, string[] armPrefixes)
        {
            var doomed = new List<Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                foreach (var prefix in armPrefixes)
                {
                    if (t.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        doomed.Add(t);
                        break;
                    }
                }
            }

            int removed = 0;
            foreach (var t in doomed)
            {
                if (t == null) continue;   // ya cayo al destruir a su padre
                Object.DestroyImmediate(t.gameObject);
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// Bounds de las mallas de <paramref name="subtree"/> en el espacio local de
        /// <paramref name="space"/>.
        ///
        /// Se calcula a mano desde <c>sharedMesh.bounds</c> y no con <c>Renderer.bounds</c>: esto
        /// corre sobre el contenido de un prefab cargado fuera de ninguna escena real, donde los
        /// bounds de los renderers pueden no estar actualizados.
        /// </summary>
        private static bool TryMeasureModel(Transform space, Transform subtree, out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (var filter in subtree.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || filter.GetComponent<Renderer>() == null) continue;

                Bounds local = mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var sign = new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f);
                    Vector3 world = filter.transform.TransformPoint(local.center + Vector3.Scale(local.extents, sign));
                    Vector3 point = space.InverseTransformPoint(world);

                    if (!any)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return any;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            return null;
        }

        // --------------------------------------------------------------- utilidades

        /// <summary>
        /// Capas que detienen un disparo: las paredes y el mobiliario (Default) y los jugadores
        /// (Player). Deja fuera a proposito Ignore Raycast, donde viven los colliders del propio rig.
        /// La usan tanto el rayo autoritativo del servidor como el trazo visual del cliente.
        /// </summary>
        private static int ShotLayerMask()
        {
            return (1 << LayerMask.NameToLayer("Default")) | (1 << LayerMask.NameToLayer("Player"));
        }

        /// <summary>
        /// GetComponent devuelve un "null falso" de Unity que NO satisface el operador ??,
        /// asi que hay que comparar con != null explicitamente.
        /// </summary>
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        private static GameObject Find(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == name) return t.gameObject;
                }
            }
            return null;
        }
    }
}
