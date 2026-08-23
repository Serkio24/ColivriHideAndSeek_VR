
# ColivriDigitalTwin_VR

[![Unity](https://img.shields.io/badge/unity-2021.3%2B-blue.svg)](https://unity.com/)
[![Quest](https://img.shields.io/badge/Platform-Meta%20Quest-green.svg)](https://www.meta.com/quest/)
[![Status: Demo](https://img.shields.io/badge/status-Demo-important.svg)]()

---


<div align="center">
  <a href="https://youtu.be/Q7htB_qRoV8">
    <img src="https://github.com/user-attachments/assets/9678d792-ef1d-4209-bf7b-1c8e723df222" alt="ColivriDigitalTwin_VR Demo GIF" />
  </a>
  <br/>
</div>



> **ColivriDigitalTwin_VR** es una demo de pistas en realidad virtual (VR) localizado en el laboratorio COLIVRI. El/la jugador(a) registra su nombre, elige dificultad (Fácil, Normal o Competitivo), y resuelve una clave de 3 dígitos a través de pistas escondidas en todo el laboratorio. El sistema registra el tiempo, actualiza el leaderboard (Top 10), y muestra estadísticas al finalizar.

---

> [!IMPORTANT]
> ## Este repositorio tiene ahora DOS juegos
>
> | | Modo pistas (original) | Modo escondidas (nuevo) |
> |---|---|---|
> | Jugadores | 1 | Hasta 4, online |
> | Escena | `Assets/Scenes/MainModel/MainModel.unity` | `Assets/Scenes/MainModel/MainModel_Env.unity` |
> | En el build | No (deshabilitada) | **Sí, es la única escena habilitada** |
> | Documentación | Este README | **[HIDE_AND_SEEK.md](HIDE_AND_SEEK.md)** |
>
> El resto de este README describe el **modo pistas single-player**. Su código sigue intacto en
> `Assets/Scripts/`, pero **no se ejecuta en el build actual**: la escena que arranca es la de
> escondidas. Para volver al modo pistas hay que rehabilitar `MainModel.unity` en
> *File → Build Settings*.
>
> Ten en cuenta que las secciones de *Tecnologías utilizadas* y *Requisitos* de abajo están
> desactualizadas: el proyecto usa hoy **Unity 6.3 (6000.3.10f1)**, no 2022.3.

---

## Tabla de contenidos

- [Características principales](#características-principales)
- [Características importantes para desarrolladores](#características-importantes-para-desarrolladores)
- [Tecnologías utilizadas](#tecnologías-utilizadas)
- [Arquitectura y estructura](#arquitectura-y-estructura)
- [Módulo de Optimización](#módulo-de-optimización)
- [Requisitos](#requisitos)
- [Debug con Quest Link](#debug-con-quest-link)
- [Guía de instalación y ejecución](#guía-de-instalación-y-ejecución)
- [Compilación para Quest/Android](#compilación-para-questandroid)
- [Descarga de APKs](#descarga-de-apks)
- [Cómo colaborar](#cómo-colaborar)
- [Solución de problemas](#solución-de-problemas)

---

## Características principales

- **Tres modos de juego:**
  - *Fácil*: Cronómetro ascendente (CountUp) + ayuda Teleports activos.
  - *Normal*: Cronómetro ascendente (CountUp)
  - *Competitivo*: Cronómetro descendente (CountDown) desde el mejor tiempo. Extensión automática si el tiempo llega a 00:00. Sin ayudas adicionales de Teleports.
- **Leaderboard Top 10:**  
  - Persistencia local (PlayerPrefs/JSON) 
  - Formato mm:ss  
  - Resalta el nombre del jugador actual
- **Estadísticas finales:** Nombre, tiempo y posición.
- **UI completa:** Paneles de registro, instrucciones, temporizador, pistas dinámicas, game over, ranking y estadísticas.
- **Transiciones suaves:** Fundido “blink” de cámara y enfoque al leaderboard.
- **Audio feedback** al resolver la clave.
- **Código modular:** GameController, CodeManager, PlayerDataManager, HighScoreTable, TimerDef, CameraBlink, GameStatistics

---

> [!NOTE]
> Para una experiencia visual coherente, utilizar prefabs y materiales compatibles con el pipeline URP o Standard según la configuración de proyecto.

---

## Características importantes para desarrolladores

## I. Módulo Core del Juego (Lógica y Control)

Esta sección contiene la inteligencia central del juego: la gestión del puzle, el tiempo, la dificultad y la persistencia.

- **Assets/Scripts/RetoLoader.cs**  
  *Responsabilidad:* Gestor de contenido que carga los desafíos desde *Resources/Retos.json*. Determina el reto actual basándose en el nivel de dificultad (Easy, Normal, Competitive) definido en `Difficulty.cs`. Implementa modos de carga como secuencial, aleatorio o primer reto fijo. Además, parsea y expone los dígitos de las pistas (`TripleDigits`) y actualiza la UI de las mismas.

- **Assets/Scripts/Reto.cs**  
  *Responsabilidad:* Modelo de datos serializable que define la estructura de cada reto (`idReto`, `pista1..pista5`, `respuesta`).

- **Assets/Scripts/CodeManager.cs**  
  *Responsabilidad:* Controlador principal del reto. Gestiona la entrada de 3 dígitos del jugador, valida la respuesta frente al reto activo (`RetoLoader`) y genera el feedback correspondiente (correcto, cercano o clave). Emite el evento `OnCodeSuccessEvent` al completarse el código e integra el tiempo medido por el temporizador.

- **Assets/Scripts/TimerDef.cs**  
  *Responsabilidad:* Temporizador reutilizable con modos *CountUp* y *CountDown*. Permite iniciar, detener y resetear el tiempo, formateándolo en `MM:SS` mediante `FormatMMSS()`. Dispara un evento `OnTimerFinished` y usa señales visuales (colores) para indicar urgencia.

- **Assets/Scripts/PlayerDataManager.cs**  
  *Responsabilidad:* Singleton de persistencia encargado de crear y seleccionar jugadores (`CreateOrSelectPlayer`), registrar estadísticas (`UpdateCurrentSessionStats`) y serializar/deserializar los datos (`PlayerDataList`) a JSON con `PlayerPrefs` como almacenamiento local.

- **Assets/Scripts/HighScoreTable.cs**  
  *Responsabilidad:* Controla la visualización del ranking. Recupera la lista ordenada de jugadores (`GetRanking`) desde `PlayerDataManager`, instancia las filas de la plantilla (`rowTemplate`) y aplica formato de tiempo con `TimerDef.FormatMMSS()`.


## II. Módulo de Flujo y Navegación

Esta sección gestiona las transiciones entre estados y escenas.

- **Assets/Scripts/GameController.cs**  
  *Responsabilidad:* Orquestador global del flujo del juego (Singleton). Controla la sesión (inicio, fin, pausa), enlaza eventos entre `TimerDef`, `CodeManager` y `PlayerDataManager`, y regula la lógica general de la partida.

- **Assets/Scripts/PlayerRegistration.cs**  
  *Responsabilidad:* Controla la interfaz de registro y selección de jugador. Valida nombre y dificultad, y almacena las preferencias iniciales en `PlayerPrefs` antes de iniciar la partida.

- **Assets/Scripts/RegistrationFlow.cs**  
  *Responsabilidad:* Controla los paneles de la escena de registro (Inicial, Registro, Instrucciones, Highscore). Decide qué mostrar al inicio según si ya existe un jugador guardado.

- **Assets/Scripts/SceneLoader.cs**  
  *Responsabilidad:* Helper estático para la gestión de escenas. Centraliza la carga por nombre (`LoadRegistration()`, `LoadMain()`), evitando redundancia en llamadas a `SceneManager`.


## III. Módulo de Interacción Inmersiva y UI

Estos componentes controlan el movimiento del jugador, el feedback contextual y las transiciones visuales.

- **Assets/Scripts/TeleportManager.cs**  
  *Responsabilidad:* Singleton que gestiona todos los puntos de teletransporte de la escena, permitiendo activarlos o desactivarlos en conjunto como hint general.

- **Assets/Scripts/CameraBlink.cs**  
  *Responsabilidad:* Aplica efectos de transición visual mediante corrutinas (`FadeIn()`, `FadeOut()`) ajustando la alpha de una imagen UI para simular parpadeos o transiciones rápidas.

- **Assets/Scripts/HintActivation.cs**  
  *Responsabilidad:* Controlan la visibilidad de objetos o pistas (`imageObject`, `hint`) basándose en la proximidad del jugador o la activación de *triggers* de cámara (`CenterEyeAnchor`).

- **Assets/Scripts/TutorialController.cs**  
  *Responsabilidad:* Singleton que coordina el flujo del tutorial guiado. Escucha eventos de interacción (por ejemplo, `NotifyFaceProximity` del `HandTrigger`) y guía al jugador paso a paso en las etapas del aprendizaje inicial.
---

## Tecnologías utilizadas

- [Unity 2022.3.5f1](https://unity.com/releases/editor/whats-new/2022.3.5)
- [Unity Hub 3.12.1](https://unity.com/download)
- [C#](https://learn.microsoft.com/es-es/dotnet/csharp/) + [TextMeshPro](https://docs.unity3d.com/Packages/com.unity.textmeshpro@latest/)
- [Meta/Oculus XR Interaction SDK (Meta XR All-In-One SDK)](https://developers.meta.com/horizon/downloads/package/meta-xr-sdk-all-in-one-upm/)
- [URP (Universal Render Pipeline)](https://unity.com/universal-render-pipeline)
- [Android Build Support](https://developer.android.com/studio)

> [!NOTE]
> Si algún modelo/prefab aparece rosado, revisa y actualiza el material/shader en el Inspector (Standard/URP/HDRP).

---

## Arquitectura y estructura
<p align="center">
<img width="500" height="600" alt="image" src="https://github.com/user-attachments/assets/161af3bf-cf06-4a81-acae-3eef17d5604a" />
</p>
<p align="center">
  <img width="500" height="600" alt="image" src="https://github.com/user-attachments/assets/64f2fbb6-1795-49d1-a2bd-163964bf95b5" />
</p>


---

## Módulo de Optimización

Este módulo agrupa las estrategias aplicadas para reducir el uso excesivo de GPU y mejorar la fluidez general del demo en Meta Quest y PC, manteniendo la fidelidad visual sin comprometer el rendimiento.

---

### Ajustes de Sombras

- **Desactivación de Cast Shadows**  
  Se desactivó la opción **Cast Shadows** en varios objetos del gemelo digital donde las sombras proyectadas no aportaban información visual relevante (mesas, sillas,etc).  
  Esto reduce el cálculo de iluminación en tiempo real y libera GPU.

- **Desactivación de Receive Shadows en materiales**  
  En materiales no críticos se deshabilitó **Receive Shadows**, evitando cálculos de sombreado innecesarios y mejorando el frame rate.

---

### Optimización de Materiales y Render

- **Oclusión Culling activada**  
  Se habilitó **Occlusion Culling** para que Unity oculte automáticamente objetos fuera del campo de visión del jugador.  
  Esto reduce significativamente el número de polígonos renderizados por cuadro.

- **Subpixel Rendering**  
  En el objeto `CenterEyeAnchor` se habilitó la opción **Subpixel Rendering**, lo que mejora la nitidez de los bordes en VR sin requerir un supersampling costoso.

---

### Configuración de Calidad en Project Settings

- En **Edit → Project Settings → Quality**:
  - Se modificó el perfil **High Fidelity** desactivando **Texture Streaming**, evitando sobrecargas en memoria de video cuando se alternan texturas de alta resolución.
- En el mismo panel se revisó la opción **Render Shadows**, ajustando su activación según la necesidad del entorno para lograr un balance entre realismo y rendimiento.

> [!TIP]  
> Para mantener la estabilidad, se recomienda aplicar un *Frame Debugger* o el *Profiler* de Unity tras cada actualización de materiales o geometrías importadas al gemelo.

---

## Requisitos

- **Unity Hub** + Unity LTS (2022.3+, 2023.2+)
- **Paquetes necesarios:**
  - TextMeshPro
  - Meta/Oculus XR (Interaction SDK all-in-one)
- **Proyecto configurado en URP** 
- **Android Build Support** (para Quest/Android)

> [!WARNING] 
> La compilación para Quest solo funciona en ARM64 y XR Plug-in Management configurado correctamente. No olvidar agregar el módulo Android Build Support en Unity Hub, además de configurar en Project settings los permisos de ocuclus tanto para PC como android.

---
## Debug con Quest Link
Durante el desarrollo, es posible probar y depurar el proyecto directamente desde el editor de Unity utilizando **Quest Link** (Meta Quest Link). Esto permite iterar rápidamente sin compilar un APK en cada cambio.
Aquí puedes acceder a un tutorial sobre cómo usar el [Quest Link](https://codelabs.virtual.uniandes.edu.co/codelabs/activar-oculus-link/#0)

### Requisitos previos

- Visor **Meta Quest 2/Pro/3** en modo desarrollador  
- **Cable USB-C** 
- Aplicación **Meta Quest Link** instalada en el PC  
- Paquetes XR correctamente configurados en el proyecto

### Pasos para activar Quest Link en Unity

1. Conectar el visor al PC mediante cable USB-C o activa Air Link en Configuración del quest.  
2. En el visor, **acepta la solicitud de conexión y acceso a datos**.   
3. En Unity:
   - Asegurar de que el **XR Plug-in Management** esté habilitado para la plataforma **PC, Mac & Linux Standalone** con **Oculus/Meta** activo.
   - Cambiar la plataforma a **PC** (*File → Build Settings → PC, Mac & Linux Standalone → Switch Platform*).
5. Pulsar **Play** en Unity para probar directamente la escena en VR.

---

## Guía de instalación y ejecución

1. **Clona el repositorio:**
   ```bash
   git clone https://github.com/imagine-uniandes/ColivriDigitalTwin_VR
   ```
2. Abre la carpeta con Unity Hub y selecciona la versión LTS compatible.
3. Instala los paquetes necesarios desde Window → Package Manager.
4. Abre la escena principal: `Assets/Scenes/MainModel`.
5. Verifica asignaciones en el Inspector:
   - **GameController:** Paneles (Registro, Instrucciones, PanelClave, Teleports Hostpots, Timer, GameOver Panel, HighScorePanel, StatsRankingPanel, Audio Source Asignado)
   - **TimerDef:** Arrastra el componente del TimerPanel, asigna el TextMeshProUGUI del reloj
   - **Audio:** Asigna AudioSource y successClip
   - **CameraBlink:** Crea un FadePanel (UI → Panel) y asigna su Image a fadeImage
6. Pulsar **Play** para iniciar el flujo: Registro → Dificultad → Juego → Ranking/Estadísticas → Reset

---

## Compilación para Quest/Android

1. Instala **Android Build Support** (Unity Hub → Installs → Add modules)
2. Ve a **File → Build Settings → Android** y haz *Switch Platform*
3. Añade la escena principal a *Scenes In Build*
4. Configura en **Project Settings → XR-Plug-in Managment**:
   - XR Plug-in Management: habilita Oculus/Meta para Android y PC
   - Ajusta materiales según tu pipeline
5. Haz *Build* o *Build And Run* para generar el .apk
6. Instala en Quest:
   - Activa *Developer Mode* en el visor (si no lo tiene)
   - Si selecciona *Build And Run* al completar la carga del demo, el apk se ejecuta despues de terminar la compilación, además este se guardará en las demos del casco correspondiente. (Habrá una copia guardada en el pc en el que se esté desarrollando)
<div align="center">
  <img src="https://github.com/user-attachments/assets/b0648467-ef62-4755-93cf-c5a2f40f091b" width="300"/>
  <img src="https://github.com/user-attachments/assets/0efa0762-efa3-4802-ab78-b891655c7919"  width="300"/>



</div>


---

> [!IMPORTANT]
> Si tienes problemas con shaders o materiales al exportar a Quest/Android, convierte los materiales a URP/Lit y actualiza los prefabs antes de compilar.

---
## Descarga de APKs

Puedes descargar los archivos APK de las releases oficiales del proyecto. Cada versión publicada incluye su respectivo APK listo para instalar en dispositivos Android.

- [Demo01 v0.0.1](https://github.com/imagine-uniandes/ColivriDigitalTwin_VR/releases/tag/0.0.1)  
  &nbsp;&nbsp;└─ [PruebaDigitalTwinColivri2.apk](https://github.com/imagine-uniandes/ColivriDigitalTwin_VR/releases/download/0.0.1/PruebaDigitalTwinColivri2.apk)

> Para ver más versiones y APKs, visita la sección [Releases](https://github.com/imagine-uniandes/ColivriDigitalTwin_VR/releases).
---

## Cómo colaborar

- **Ramas:**  
  - `main`: estable
    Para integración de nuevas funcionalidades con permisos crea una nueva rama llamada:
    - `develop/<nombre>`: nuevas funcionalidades
- **Pull Requests:**  
  - Descripción clara de los cambios realizados
  - Screenshots/GIFs si afecta UI/VR  
  - Pruebas manuales en Editor/dispositivo
- **Estilo:**  
  - C# camelCase para campos del código  (Nombramiento de funciones, variables, etc)
  - Nombres claros en prefabs/escenas
  - Organización de carpetas (Ej: Assets/Models para los modelos 3D en la escena, Assets/Scripts para agregar nuevos archivos .cs al proyecto )
- **Issues:**  
  - Usa etiquetas (`bug`, `question`, `VR`, `UI`, `build`)  
  - Incluye pasos de reproducción, logs/stacktrace y versión de Unity/paquetes

---

> [!TIP] 
> Antes de abrir un proyecto, revisa que no haya duplicados y que el código compile tanto en Editor como en Android/Quest.

---

##  Solución de problemas

- **Material rosado:** Convierte materiales a Standard o URP/Lit y reasigna en el prefab
- **Timer no actualiza:** Verifica que `TimerDef.timerText` esté asignado y evita duplicados
- **Leaderboard no resalta/actualiza:** Confirma llamada a `highScoreTable.RefreshTable()` tras guardar sesión; limita correctamente el Top 10
- **Panel que no apaerece:** Revisar si GameController tiene asignado los paneles de pistas correspondientes.

---
- Para explorar más sobre la documentación de Meta XR All-In-One ingrese al siguiente link: https://developers.meta.com/horizon/downloads/package/meta-xr-sdk-all-in-one-upm/ 
- Si quieres investigar más sobre el funcionamiento del paquete teleport ingrese al siguiente link: https://developers.meta.com/horizon/documentation/unity/unity-isdk-teleport-interaction/

> [!WARNING] 
> Si experimentas errores al compilar para Quest, revisa que todos los paquetes estén actualizados y que los materiales sean compatibles con Android y URP.

---
