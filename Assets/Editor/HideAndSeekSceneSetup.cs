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

        // --- Plataforma que marca cada punto de aparicion ---
        private const float PadSize = 1.1f;
        private const float PadThickness = 0.05f;
        private const string SeekerPadMaterialPath = "Assets/Prefabs/HideAndSeek/SpawnPadSeeker.mat";
        private const string HiderPadMaterialPath = "Assets/Prefabs/HideAndSeek/SpawnPadHider.mat";

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

            // Este metodo es la fuente de verdad: se rehacen todos los puntos desde cero.
            for (int i = host.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(host.transform.GetChild(i).gameObject);
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

            // Marca visible en el suelo, una vez ya se sabe cual es el punto del buscador.
            foreach (Transform point in host.transform)
            {
                CreatePad(point, point == seekerPoint);
            }

            EditorUtility.SetDirty(host);
            Debug.Log($"[HideAndSeek] {chosen.Count} puntos colocados en la planta baja (y={groundY:F2}), " +
                      $"separados al menos {MinSpawnSeparation} m.");
            return set;
        }

        /// <summary>
        /// Plataforma plana que marca un punto de aparicion en el suelo.
        ///
        /// Va SIN collider a proposito. Si lo tuviera, el sondeo de <see cref="HasGroundAt"/> la
        /// detectaria como suelo al regenerar los puntos y estos subirian el grosor de la plataforma
        /// en cada pasada; ademas taparia disparos rasantes. Es solo un adorno.
        /// </summary>
        private static void CreatePad(Transform point, bool isSeekerPad)
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Plataforma";
            Object.DestroyImmediate(pad.GetComponent<Collider>());

            pad.transform.SetParent(point, false);
            // Ligeramente despegada del suelo para que la cara inferior no haga z-fighting con el.
            pad.transform.localPosition = new Vector3(0f, PadThickness * 0.5f + 0.005f, 0f);
            pad.transform.localRotation = Quaternion.identity;
            pad.transform.localScale = new Vector3(PadSize, PadThickness, PadSize);

            var renderer = pad.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = isSeekerPad
                ? GetOrCreateMaterial(SeekerPadMaterialPath, new Color(0.85f, 0.25f, 0.18f))
                : GetOrCreateMaterial(HiderPadMaterialPath, new Color(0.20f, 0.60f, 0.85f));
        }

        /// <summary>Material compartido en disco, para no crear uno por plataforma.</summary>
        private static Material GetOrCreateMaterial(string path, Color color)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                HideAndSeekMaterials.Tint(existing, color);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var material = HideAndSeekMaterials.CreateLit(color);
            AssetDatabase.CreateAsset(material, path);
            return material;
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
