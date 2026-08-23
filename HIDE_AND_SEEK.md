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
| **Escondite** (30 s) | Se sortea el buscador. El buscador se queda **a oscuras y sin poder moverse**; los escondidos encogen al 50 % y se reparten por puntos de aparición distintos. |
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
                                     baja (y = -4,09), a 3 m o más unos de otros. Cada uno
                                     lleva una plataforma plana de 1,1 x 1,1 m que lo marca
                                     en el suelo: roja la del buscador, azules las demás.
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
| `NetworkPlayer.cs` | Avatar de red (cabeza + cuerpo + 2 manos). El dueño copia la pose de su rig; el rol y el color los escribe el servidor. |
| `PlayerRig.cs` | Rig local: escala según rol, teletransporte forzado, venda en los ojos, equipar/quitar pistola. |
| `PlayerGun.cs` | Entrada del gatillo, feedback inmediato (trazo + vibración + sonido) y envío del `ShootServerRpc`. |
| `SpawnPointSet.cs` | Lista de puntos de aparición con reparto aleatorio sin repetición. |
| `HideAndSeekHUD.cs` | HUD de muñeca: rol, cuenta atrás y escondidos restantes. |
| `HideAndSeekMaterials.cs` | Materiales URP creados por código (evita los shaders built-in, que saldrían rosados). |

Herramienta de editor: `Assets/Editor/HideAndSeekSceneSetup.cs` →
menú **Colivri ▸ Hide and Seek ▸ Configurar escena**. Es idempotente y rehace toda la configuración
de la escena desde cero.

### Prefabs

- `Assets/Prefabs/Network/NetworkPlayer.prefab` — asignado como `NetworkConfig.PlayerPrefab`,
  así que NGO lo instancia solo para cada cliente. 3 `NetworkTransform` con autoridad de propietario
  (raíz = cabeza, más una por mano en espacio local). Colliders de impacto en la cabeza y el cuerpo,
  en la capa `Player`.
- `Assets/Prefabs/HideAndSeek/Pistol.prefab` — objeto **local**, no en red: se instancia bajo el
  anchor de la mano derecha del buscador. Tiene un `Transform` hijo `Muzzle` que marca de dónde sale
  el disparo. Sin colliders, para no estorbar a los interactores de ISDK.

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
poner hitboxes predecibles. El avatar es de primitivas, con color por rol.

**Los puntos de aparición se calculan sondeando la geometría, no de las superficies de teleport.**
Varias `TeleportInteractable` del modelo (`PlaneOfficeS*`, `Curtain*`) son planos degenerados con
tamaño cero en X o en Z, así que su centro cae en sitios donde no hay suelo. El generador barre el
mapa con raycasts y acepta una celda solo si hay suelo de planta baja debajo, 1,8 m libres encima y
0,6 m de holgura alrededor. Es determinista: dos ejecuciones dan exactamente los mismos puntos.

El hueco se comprueba con **raycasts y no con `Physics.OverlapCapsule`**: las paredes del modelo son
un único MeshCollider cóncavo (`WallsS1`) y PhysX no soporta consultas de solapamiento contra mallas
cóncavas — devuelven impacto siempre y rechazaban el mapa entero.

**Las plataformas de los puntos no llevan collider.** Son un cubo aplanado (1,1 x 1,1 x 0,05 m) puesto
5 mm sobre el suelo para que la cara inferior no haga z-fighting. Si tuvieran collider, al volver a
ejecutar *Configurar escena* el sondeo las detectaría como suelo y los puntos subirían 5 cm en cada
pasada, además de comerse los disparos rasantes. Comprobado: tres regeneraciones seguidas dan
exactamente las mismas alturas.

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
- [ ] El buscador ve negro y no puede teletransportarse durante los 30 s de escondite.
- [ ] Al empezar la búsqueda aparece la pistola en la mano derecha.
- [ ] Disparar a un escondido lo elimina; disparar a una pared no atraviesa.
- [ ] El eliminado deja de verse para los vivos y sigue viendo a otros eliminados.
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
