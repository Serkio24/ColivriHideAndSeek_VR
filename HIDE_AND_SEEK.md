# Colivri Hide and Seek — modo multijugador VR

Juego de escondidas en realidad virtual para **hasta 4 jugadores**, ambientado en el gemelo digital
del laboratorio COLIVRI. Un jugador es el **buscador** (tamaño normal, con pistola) y el resto son
**escondidos** (tamaño reducido, para poder meterse en huecos donde no cabe un adulto).

> Este documento cubre el modo multijugador. El juego original de pistas single-player sigue
> documentado en [README.md](README.md) y su lógica sigue intacta en `Assets/Scenes/MainModel/MainModel.unity`.

---

## Cómo se juega

| Fase | Qué pasa |
|---|---|
| **Esperando jugadores** | El matchmaking automático mete a todos en la misma sala. Con 2 o más jugadores arranca la ronda. |
| **Escondite** (30 s) | Se sortea el buscador. El buscador aparece en `Spawn_Buscador_WhenHiding` (hijo de `[HideAndSeek] SpawnPoints`, colocado a mano) y espera ahí **viendo pero sin poder moverse**; al empezar la búsqueda se le lleva a `Spawn_Buscador`; los escondidos encogen al 50 % y se reparten por puntos de aparición distintos. |
| **Búsqueda** (5 min) | El buscador recupera la vista, aparece su pistola en la mano derecha y sale a buscar. Cada impacto elimina a un escondido. |
| **Fin de ronda** (10 s) | Gana el buscador si los encuentra a todos; ganan los escondidos si aguantan hasta que se acabe el tiempo. Luego vuelve al lobby y se sortea otra vez. |

Los escondidos eliminados pasan a **espectador**: recuperan su tamaño, dejan de ser visibles para los
que siguen vivos, y pueden pasear viendo a los demás eliminados.

**Disparar:** gatillo del mando derecho (`SecondaryIndexTrigger`). Cadencia de 0,5 s.

---

## Arquitectura

**Stack:** Unity 6.3 · URP · OpenXR + Meta XR SDK 205 (OVRCameraRig + Interaction SDK) ·
**Netcode for GameObjects 2.13** · Unity Gaming Services (Relay + Lobby + Authentication anónima).

Topología **host-cliente**: el host es un jugador más. El servidor manda sobre el estado de la partida
y los roles; cada cliente manda sobre la pose de su propio rig.

### Escena

Todo vive en `Assets/Scenes/MainModel/MainModel_Env.unity` (la única escena habilitada en el build).

```
PlayerRoot                        <- PlayerRig + PlayerGun + AudioSource. Es lo que se escala.
 └─ OVRCameraRigInteraction
     ├─ OVRCameraRig  (tag Player)
     │   └─ TrackingSpace/{CenterEyeAnchor, LeftHandAnchor, RightHandAnchor}
     └─ OVRInteractionComprehensive/Locomotion  <- PlayerLocomotor (teleport + giro)

[HideAndSeek] Manager             <- NetworkObject de escena. Estado, roles, disparos.
[HideAndSeek] SpawnPoints         <- SpawnPointSet con 12 puntos repartidos por la planta
                                     baja (y = -4,09), a 3 m o más unos de otros. Son
                                     transforms vacíos: no se ven en el juego (en el editor
                                     los dibujan los gizmos, rojo el del buscador).
[HideAndSeek] HUD                 <- Canvas world-space; en runtime se ancla a la muñeca izquierda.

[BuildingBlock] Network Manager   <- NGO + UnityTransport (Relay lo reconfigura al conectar).
[BuildingBlock] Auto Matchmaking  <- lobby "ColivriHideAndSeek", máximo 4 jugadores.
```

### Scripts

Todos en `Assets/Scripts/HideAndSeek/`, namespace `Colivri.HideAndSeek`.

| Archivo | Qué hace |
|---|---|
| `HideAndSeekTypes.cs` | `enum PlayerRole` (Unassigned/Seeker/Hider/Spectator) y `enum GameState`. |
| `HideAndSeekManager.cs` | `NetworkBehaviour` autoritativo: máquina de estados, sorteo de roles, reparto de puntos de aparición y **validación de los disparos**. |
| `NetworkPlayer.cs` | Avatar de red. El buscador se ve como el **Aura** (con manos y luces), los escondidos como el **NaoRobot** y los espectadores como primitivas (cabeza + cuerpo + 2 manos). La cabeza del robot copia la inclinación del casco. El dueño copia la pose de su rig; el rol y el color los escribe el servidor. |
| `PlayerRig.cs` | Rig local: escala según rol, teletransporte forzado, bloqueo de la locomoción del buscador mientras espera, equipar/quitar pistola. |
| `PlayerGun.cs` | Entrada del gatillo, feedback inmediato (trazo + vibración + sonido) y envío del `ShootServerRpc`. |
| `SpawnPointSet.cs` | Lista de puntos de aparición con reparto aleatorio sin repetición. |
| `HideAndSeekHUD.cs` | HUD de muñeca: rol, cuenta atrás y escondidos restantes. |
| `HideAndSeekMaterials.cs` | Materiales URP creados por código (evita los shaders built-in, que saldrían rosados). |

Herramienta de editor: `Assets/Editor/HideAndSeekSceneSetup.cs`, con tres menús idempotentes:

- **Colivri ▸ Hide and Seek ▸ Configurar escena** — rehace toda la configuración de la escena desde cero.
  Los puntos de aparición se regeneran, salvo `Spawn_Buscador_WhenHiding`, que se conserva y se vuelve
  a asignar como `seekerWaitPoint` del `SpawnPointSet`.
- **Colivri ▸ Hide and Seek ▸ Configurar avatar del buscador** — monta el Aura, su mano izquierda
  y la pistola dentro de `NetworkPlayer.prefab`.
- **Colivri ▸ Hide and Seek ▸ Configurar avatar del escondido** — monta el NaoRobot y sus dos manos
  dentro de `NetworkPlayer.prefab`.

Los dos de avatar van aparte porque tocan el prefab y no la escena: no necesitan tener abierta
`MainModel_Env` ni rehacer los puntos de aparición. Comparten el mismo montaje (`MountRobotAvatar`),
que solo cambia por un `RobotAvatarSpec` con el modelo, los nodos de cabeza y brazos y la altura.

### Prefabs

- `Assets/Prefabs/Network/NetworkPlayer.prefab` — asignado como `NetworkConfig.PlayerPrefab`,
  así que NGO lo instancia solo para cada cliente. 3 `NetworkTransform` con autoridad de propietario
  (raíz = cabeza, más una por mano en espacio local). Colliders de impacto en la cabeza y el cuerpo,
  en la capa `Player`. De `BodyPivot` cuelgan además dos robots: `SeekerVisual` con el **Aura**
  (19 mallas, sin brazos ni manos, con las luces de la cabeza), que sólo se enciende para el buscador, y `HiderVisual` con el **NaoRobot**
  (15 mallas, sin brazos, a la mitad de altura), que sólo se enciende para los escondidos. Cada robot
  lleva un `HeadPivot` insertado en el centro de su cabeza:

  ```
  NetworkPlayer            <- raíz = pose del centro del ojo
   ├─ HeadVisual           <- esfera + SphereCollider   (renderer sólo si es espectador/sin rol)
   ├─ BodyPivot            <- sólo el giro horizontal de la cabeza
   │   ├─ BodyVisual       <- cápsula + CapsuleCollider (renderer sólo si es espectador/sin rol)
   │   ├─ SeekerVisual     <- Aura                      (renderer encendido sólo si es buscador)
   │   │   └─ …/HeadPivot  <- copia la rotación del casco
   │   └─ HiderVisual      <- NaoRobot                  (renderer encendido sólo si es escondido)
   │       └─ …/HeadPivot  <- copia la rotación del casco
   ├─ HandL
   │   ├─ Visual           <- cubo (renderer sólo si es espectador/sin rol)
   │   ├─ SeekerHandVisual <- mano izquierda del Aura             (sólo buscador)
   │   └─ HiderHandVisual  <- mano izquierda del NaoRobot           (sólo escondido)
   └─ HandR
       ├─ Visual           <- cubo (renderer sólo si es espectador/sin rol)
       ├─ SeekerGunVisual  <- Pistol.prefab enlazado                (sólo buscador)
       └─ HiderHandVisual  <- mano derecha del NaoRobot             (sólo escondido)
  ```

  Los colliders **no** se tocan al cambiar de rol: sólo se apaga el `MeshRenderer`, nunca el
  `GameObject`, así que el volumen de impacto es el mismo lleves robot o cápsula.
- `Assets/Prefabs/HideAndSeek/Pistol.prefab` — objeto **local**, no en red: se instancia bajo el
  anchor de la mano derecha del buscador. Es un contenedor: su origen es el agarre y `+Z` el cañón.
  Dentro lleva `Model`, una instancia enlazada de `Assets/Prefabs/watergun.prefab` (pistola de agua,
  girada −90° en Y, escalada a ~22 cm), y un `Transform` hijo `Muzzle` en la boquilla que marca de
  dónde sale el disparo. Sin colliders, para no estorbar a los interactores de ISDK.

---

## Decisiones de diseño

**El disparo lo decide el servidor.** El cliente dibuja el trazo al instante para que se sienta
inmediato, pero manda `ShootServerRpc(origen, dirección)` y es el servidor quien rehace el raycast
contra su propia copia del mundo y decide si hay impacto. Un cliente no puede declarar bajas.
Las paredes bloquean el disparo (`shotMask` = `Default` + `Player`).

**Se escala `PlayerRoot`, no `OVRCameraRig`.** El `PlayerLocomotor` de ISDK reposiciona el transform
de `OVRCameraRig` en coordenadas de mundo en cada teleport; escalarlo directamente pelearía con él.
Escalando un padre vacío, la locomoción sigue funcionando y además el arco de teleport y los pasos
se acortan proporcionalmente, que es justo lo que quieres cuando mides 85 cm.
El `nearClipPlane` de la cámara también se escala: si no, un jugador pequeño ve recortada la
geometría que tiene delante.

**Sin colocación ni passthrough.** El juego es VR virtual: todos se ven dentro del modelo 3D del
laboratorio, y el mapa es mucho más grande que cualquier sala real. Se quitaron los bloques
`Colocation` y `Passthrough`, y se desactivó `MR Utility Kit`.

**Sin etiquetas de nombre.** El bloque `Player Name Tag` de Meta ponía un cartel flotante sobre cada
jugador — en un juego de escondidas eso convierte la búsqueda en leer carteles. Se quitó.

**Avatares propios en vez de Meta Avatars.** El Meta Avatars SDK no está instalado y añadirlo traería
dependencia de App ID y entitlement, más coste de rendimiento y complicaciones para escalar y para
poner hitboxes predecibles. El buscador lleva `AuraConManosyluces.glb` y los escondidos `NaoRobot.glb`, que
ya estaban en el proyecto; los espectadores siguen siendo primitivas grises.

**Los robots van sin brazos, pero con manos flotantes.** No hay IK que mueva los brazos, así que
quedarían tiesos. En su lugar, el montaje copia la mano de cada robot bajo los anchors `HandL`/`HandR`,
que siguen a los mandos: el escondido lleva las dos manos del Nao (`l_wrist`/`r_wrist`) y el buscador
la mano izquierda del Aura (`Sketchfab_model`) y en la
derecha una copia enlazada de `Pistol.prefab`, con el mismo offset que la pistola local. La mano se
orienta midiendo: el eje de la malla (de su articulación hacia su centro) se alinea con el adelante
del mando, y el punto `HandGripFraction` de ese eje cae en el agarre; si alguna sale girada sobre sí
misma, se corrige con `HandEuler` en el spec. Los cubos quedan sólo para espectadores y jugadores sin
rol.

En `AuraConManosyluces.glb` las manos (`Sketchfab_model`/`.001`) y las luces de la cabeza
(`Curva_Bezier`/`.001`) cuelgan de la raíz del GLB y no del esqueleto. Por eso las manos se quitan del
cuerpo junto con los brazos (`ArmPrefixes`), las luces se cuelgan del `HeadPivot` (`HeadExtraNodes`)
para que giren con la cabeza, y el eje de la mano se mide desde el antebrazo (`HandJointNodeLeft`),
porque el origen de `Sketchfab_model` queda encima de la malla. `HandEuler` la pone de canto, con el
pulgar arriba. El cuerpo queda en **88 074 triángulos / 19 mallas** y la mano en **23 720 / 83 mallas**.

**La escala del rig se aplica alrededor de los pies del jugador.** `PlayerLocomotor` mueve
`OVRCameraRig` en coordenadas de mundo, así que tras varios teleports su offset respecto a `PlayerRoot`
es grande. Escalando `PlayerRoot` sobre su propio origen ese offset se multiplicaba: un escondido
eliminado (0,5 → 1) aparecía al doble de distancia, fuera del mapa. `PlayerRig.ApplyScale` escala ahora
sobre el punto del suelo bajo la cabeza, así que el jugador crece o encoge en el sitio.

**Los robots se colocan midiendo el modelo, no con números a mano.** El montaje escala el GLB por
la distancia del centro de la cabeza a los pies (1,61 m el Aura; 0,805 m el Nao, la mitad, igual que
`hiderScale`) y desplaza el hijo para que el centro de la malla de la cabeza caiga exactamente en la
raíz del avatar — es decir, dentro del `SphereCollider` de impacto de la cabeza. Se mide de ojos a
pies y no el alto total porque el Nao es cabezón: escalado por alto total flotaría sobre el suelo.
Los GLB vienen de URDF y GLTFUtility invierte X al importar, así que ambos miran a −X y llevan 90° de yaw.

**La cabeza del robot mira hacia donde mira el casco.** El robot cuelga de `BodyPivot`, que sólo
lleva el giro horizontal; la inclinación (cabeceo y ladeo) la recibe únicamente un `HeadPivot` que
el montaje inserta en el centro de la cabeza y del que cuelga el subárbol de la cabeza
(`head_joint.fixed.bone` en el Aura, `HeadPitch.revolute.bone` en el Nao). No se rota el nodo del
GLB directamente porque el `head_joint` del Aura tiene su origen en el del modelo: la cabeza orbitaría
alrededor de la pelvis. En runtime, `NetworkPlayer` hace `HeadPivot.rotation = raíz.rotation × reposo`
a partir de la rotación que ya sincroniza el `NetworkTransform`, así que no se envía nada nuevo por red.

**El tinte rojo del buscador va por `MaterialPropertyBlock`, no por material.** Al resto del avatar se
le machaca el `sharedMaterial` con un único material lit tintado; hacer lo mismo con el robot borraría
sus 17 materiales y lo dejaría como una silueta plana. En su lugar se le suma un `_EmissionColor` rojo
suave, que los shadergraphs de GLTFUtility exponen y calculan siempre (no hay keyword `_EMISSION` que
activar, que es lo que rompería este truco con el Lit de URP). Así no hay que instanciar —ni luego
destruir— un material por malla.

**Los puntos de aparición se calculan sondeando la geometría, no de las superficies de teleport.**
Varias `TeleportInteractable` del modelo (`PlaneOfficeS*`, `Curtain*`) son planos degenerados con
tamaño cero en X o en Z, así que su centro cae en sitios donde no hay suelo. El generador barre el
mapa con raycasts y acepta una celda solo si hay suelo de planta baja debajo, 1,8 m libres encima y
0,6 m de holgura alrededor. Es determinista: dos ejecuciones dan exactamente los mismos puntos.

El hueco se comprueba con **raycasts y no con `Physics.OverlapCapsule`**: las paredes del modelo son
un único MeshCollider cóncavo (`WallsS1`) y PhysX no soporta consultas de solapamiento contra mallas
cóncavas — devuelven impacto siempre y rechazaban el mapa entero.

**Los puntos de aparición no tienen marca visible.** Antes llevaban una plataforma de color en el
suelo; se quitó para que no delaten dónde empieza cada jugador. Siguen siendo los mismos transforms.

---

## Probar

### En el editor, dos instancias (host + cliente)

El proyecto ya trae **ParrelSync** (`Assets/ParrelSync/`):

1. `ParrelSync ▸ Clones Manager ▸ Create new clone` y abre el clon.
2. Dale a Play en los dos editores. `AutoMatchmakingNGO` llama a `ClearSessionToken()` bajo
   `#if UNITY_EDITOR`, así que cada instancia obtiene una identidad distinta y no chocan.
3. El primero crea el lobby y hace de host; el segundo se une por Relay.

Qué comprobar:

- [ ] Aparece un avatar por jugador que sigue la cabeza y las manos del otro.
- [ ] Se sortea 1 buscador; los escondidos quedan a la mitad de altura.
- [ ] Durante los 30 s de escondite el buscador está en `Spawn_Buscador_WhenHiding`, ve la escena y no puede teletransportarse; al empezar la búsqueda aparece en `Spawn_Buscador`.
- [ ] Ningún escondido aparece en `Spawn_Buscador_WhenHiding`.
- [ ] Al empezar la búsqueda aparece la pistola en la mano derecha.
- [ ] Disparar a un escondido lo elimina; disparar a una pared no atraviesa.
- [ ] El eliminado deja de verse para los vivos y sigue viendo a otros eliminados.
- [ ] El buscador se ve como el Aura y los escondidos como el NaoRobot para los demás; nadie se ve a sí mismo.
- [ ] Al mirar arriba/abajo o ladear el casco, la cabeza del robot lo sigue en el otro cliente (gira sobre sí misma, no orbita).
- [ ] Un escondido eliminado pasa a primitiva gris; al acabar la ronda los robots desaparecen.
- [ ] Los demás ven al buscador con la mano del Aura en la izquierda y la pistola en la derecha, y a los
      escondidos con las manos del Nao; los espectadores siguen con cubos.
- [ ] Un escondido que se ha teletransportado lejos y es eliminado se queda en el mismo sitio, a tamaño
      completo y con los pies en el suelo (no fuera del mapa). Igual al volver al lobby.
- [ ] No se ve ninguna plataforma en los puntos de aparición y los jugadores siguen apareciendo en ellos.
- [ ] El emisivo rojo del robot se distingue a ~10 m sin quemar el modelo (si no, ajusta
      `seekerEmission` en el inspector del prefab).
- [ ] La ronda acaba por eliminación total y también por tiempo agotado.

Para iterar más rápido, baja `hideDuration`, `seekDuration` y `minPlayers` en el inspector de
`[HideAndSeek] Manager`.

### Sin casco

Meta XR Simulator (`Meta ▸ Simulator`) permite probar sin ponerse el visor.

### En dispositivo

Build a Quest 3 con al menos 2 cascos. Es la única forma de validar Relay de verdad (fuera de
localhost), la latencia de las poses y si el 50 % se siente bien de tamaño.

---

## Pendientes conocidos

- **Locomoción solo por teleport.** Es lo que se pidió, pero hace que el buscador no pueda perseguir
  ni el escondido huir de forma fluida. El `ControllerSlideInteractor` ya existe en la escena
  **desactivado**: activarlo da movimiento continuo con joystick sin escribir código.
- **Sin voz.** No hay chat de voz; por ahora los jugadores se hablan en persona.
- **El robot del buscador cuesta 87 238 triángulos y 17 draw calls**, en una escena que ya tiene 4227
  renderers. Lo amortigua que sólo hay un buscador por ronda y que nadie renderiza su propio avatar:
  como mucho hay **un** robot en pantalla por cliente. Falta medirlo en dispositivo (Perfetto) contra
  el avatar de cápsulas. Si hiciera falta recortar, `torso_link` (23 337 tris) y `head_link` (13 036)
  son casi la mitad del modelo.
- **La altura del robot es fija (1,75 m).** La raíz del avatar está a la altura real de los ojos del
  jugador, así que uno muy alto o muy bajo verá al robot ligeramente hundido o flotando. El avatar de
  cápsulas ya tiene el mismo problema (el cuerpo cuelga a −0,55 m de los ojos). El arreglo sería
  sincronizar la altura real por red.
- **Los robots están desempacados dentro del prefab**, así que reimportar `AuraConManosyluces.glb` o
  `NaoRobot.glb` no se propaga solo: hay que volver a ejecutar el menú de avatar correspondiente.
- **La mano del Aura son 83 mallas sueltas** (83 draw calls sólo para ella). Si pesa en Quest, habría
  que combinarlas en una o dos mallas.
- **El NaoRobot no está medido en dispositivo.** Suma 15 draw calls más; puede haber hasta 3
  escondidos en pantalla a la vez, a diferencia del único buscador.
- **Los puntos de aparición reparten por espacio, no por calidad de escondite.** Están validados
  (hay suelo, hay hueco) y bien separados, pero el generador no sabe cuáles tienen buenos sitios
  donde meterse. Muévelos a mano si alguno queda soso; se conservan mientras no vuelvas a ejecutar
  *Configurar escena*, que los rehace desde cero.
- **Las paredes no cierran del todo.** Sondeando desde el centro de la planta baja, 14 de 24
  direcciones chocan con un collider y 10 se escapan al vacío. O sea: hay tramos por los que un
  disparo atraviesa la pared. El modelo tiene 4227 renderers y solo 1106 colliders, así que la
  cobertura es parcial por diseño. Cerrarlo bien significa añadir colliders a las paredes que
  faltan — hacerlo con MeshColliders sobre todo el modelo sería carísimo en Quest.
- **Solo la planta baja está preparada.** El segundo piso (y ≈ 0,50) sigue siendo teleportable, así
  que un escondido puede subir aunque nadie aparezca allí. Si no lo quieres, desactiva el
  `TeleportInteractable` de `FloorColivri2` y `FloorColivri3`.
- **Sin rondas encadenadas con rotación de rol**: cada ronda sortea el buscador de cero.
- `[BuildingBlock] Cube` sigue en la escena: es el cubo agarrable de demo de Meta. Sirve para
  comprobar de un vistazo que la red va; bórralo cuando ya no haga falta.
- El rig es una copia desempacada del prefab **`OVRCameraRigInteraction` legacy**, que el propio SDK
  marca como deprecado (avisa en consola al entrar en Play). Migrarlo es trabajo aparte.
