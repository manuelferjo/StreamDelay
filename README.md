# StreamDelay

**Delay dinámico y gratuito para tus directos con OBS** · *Free dynamic stream delay for OBS*

OBS envía el directo a StreamDelay, que lo retrasa los segundos que elijas y lo reenvía a Twitch, Kick, YouTube o cualquier servidor RTMP. El delay se puede cambiar, activar o quitar **en pleno directo**, sin cortar el stream.

## Funciones

- **Delay dinámico**: elige los segundos y cámbialos en directo. Al subirlo se repiten los últimos segundos emitidos; al bajarlo, el stream salta hacia delante. Atajos de teclado globales.
- **Multistream**: Twitch, Kick, YouTube y otro destino a la vez, con el mismo delay. OBS codifica una sola vez.
- **Varias calidades en Twitch** (Transmisión mejorada de OBS).
- **Chat conjunto** de Twitch, Kick y YouTube con emotes de 7TV, BetterTTV y FrankerFaceZ, avisos de subs, regalos, raids, bits, Super Chats, follows y donaciones (StreamElements y Streamlabs), espectadores de cada plataforma, escribir en todos los chats a la vez y moderar.
- **Mando** desde el móvil, el Stream Deck o un panel de OBS.
- **Overlays** para OBS: delay, espectadores y chat en pantalla.
- **Título y categoría** en Twitch, Kick y YouTube a la vez.
- Ligero: el vídeo no se vuelve a codificar (casi no usa CPU).

## Descarga

Descarga la última versión en **[Releases](https://github.com/manuelferjo/StreamDelay/releases)**, descomprime el ZIP y abre `StreamDelay.exe`. La guía completa está en `GUIA.html`, dentro del ZIP.

Requisitos: Windows 10 u 11 y OBS Studio.

## Compilar desde el código

No hace falta instalar nada: `build.bat` usa el compilador de C# (.NET Framework 4) que ya trae Windows.

```
build.bat
```

Crea `StreamDelay.exe` en la misma carpeta. El código está en `src/` y los recursos (página del chat, overlays, fuentes, icono) en `res/`.

GitHub compila el programa y pasa las pruebas del chat con cada cambio (pestaña **Actions**).

## Publicar una versión

1. Cambia `Version` en `src/MainForm.cs` (por ejemplo a `3.1`) y añade las novedades al `LEEME.txt`.
2. Sube los cambios a GitHub.
3. En **Releases → Draft a new release**, crea la etiqueta `v3.1` (la misma versión con una `v` delante) y pulsa **Publish release**.

GitHub compila, comprueba que la versión coincide con la etiqueta y **adjunta el ZIP de descarga a la Release** él solo.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- **Committers and reviewers:** [manuelferjo](https://github.com/manuelferjo)
- **Approvers:** [manuelferjo](https://github.com/manuelferjo)

Only binaries built from this repository's source code are signed.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. StreamDelay only connects to the streaming platforms and services that the user configures (Twitch, Kick, YouTube, StreamElements, Streamlabs, and emote providers 7TV, BetterTTV and FrankerFaceZ). Keys and tokens are stored locally, encrypted with Windows DPAPI for the current Windows user; nothing is sent to the developer.

## English summary

StreamDelay is a free Windows tool for streamers. OBS sends the stream to StreamDelay, which delays it by a configurable number of seconds (changeable live, without restarting the stream) and forwards it to Twitch, Kick, YouTube or any RTMP server at the same time. It also includes a unified chat for Twitch, Kick and YouTube with 7TV/BTTV/FFZ emotes, a phone remote control and OBS overlays. The user interface is in Spanish.

## Licencia

[MIT](LICENSE) · Las fuentes incluidas (Outfit, Plus Jakarta Sans y JetBrains Mono) usan la licencia SIL Open Font License 1.1 (`res/OFL-*.txt`).
