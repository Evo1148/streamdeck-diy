<p align="center">
  <a href="./README.md">🇬🇧 English</a> · <strong>🇪🇸 Español</strong>
</p>

<h1 align="center">DIY Stream Deck</h1>

<p align="center">
  Un macro pad / Stream Deck personalizado basado en RP2040 y construido desde cero:
  electrónica, firmware, protocolo USB, software para Windows y carcasa impresa en 3D.
</p>

<p align="center">
  <strong>12 teclas mecánicas · encoder · TFT 480×320 · táctil · USB HID · app WinUI</strong>
</p>

---

## Descripción

Este proyecto comenzó como un macro pad DIY sencillo y ha evolucionado hasta convertirse en un sistema completo de hardware y software.

El dispositivo está construido alrededor de un **Waveshare RP2040-Zero** y combina una matriz 4×3 de teclas mecánicas, encoder rotatorio, TFT 480×320, controlador táctil XPT2046, configuración persistente en el dispositivo y una aplicación para Windows que se comunica mediante un protocolo Vendor HID propio.

> **Estado:** desarrollo activo. El código del firmware y de la aplicación Windows ya es público. El prototipo físico, la matriz, el encoder, USB HID, los bindings persistentes, el pipeline de pantalla y el flujo de configuración desde Windows son funcionales. La carcasa/CAD sigue en iteración antes de publicarla.

## Características

- **12 teclas mecánicas** en matriz 4×3.
- **Encoder rotatorio** con acciones configurables en ambos sentidos y pulsación.
- **TFT 480×320** con backend ST7796.
- Soporte táctil **XPT2046**.
- **USB HID Keyboard** y **Consumer Control** estándar.
- Protocolo **Vendor HID propio** con reports fijos de 64 bytes.
- Actualizaciones de configuración transaccionales y persistencia A/B en flash con CRC32.
- Canal **DisplayLink** entre software de escritorio y firmware.
- **Aplicación de configuración para Windows** desarrollada con C# / .NET / WinUI.
- Perfiles, host actions, automatización, estadísticas del sistema y editor de pantalla.
- Tests automatizados tanto de firmware como del lado de escritorio.

## Código

- 🧠 [Firmware RP2040](./firmware/)
- 🪟 [Aplicación Windows](./software/)
- 🔌 [Documentación del protocolo](./docs/protocol.md)
- 🧩 [Arquitectura](./docs/architecture.md)
- ⚡ [Hardware / mapa GPIO](./docs/hardware.md)

## Arquitectura

```text
┌──────────────────────────┐
│ Aplicación Windows       │
│ C# / .NET / WinUI        │
│                          │
│ Perfiles · Host Actions  │
│ Display · Automatización │
└────────────┬─────────────┘
             │ Vendor HID / DisplayLink
             │ reports de 64 bytes
┌────────────▼─────────────┐
│ RP2040-Zero              │
│                          │
│ USB HID                  │
│ Config + flash           │
│ Inputs + display runtime │
└────┬────────┬────────┬───┘
     │        │        │
     │        │        └────► TFT 480×320 + táctil
     │        └─────────────► Encoder
     └──────────────────────► Matriz 4×3
```

## Hardware

| Componente | Función |
| --- | --- |
| Waveshare RP2040-Zero | Microcontrolador principal |
| 12× switches compatibles con MX | Matriz 4×3 |
| Diodos 1N4148 | Aislamiento de la matriz |
| Encoder rotatorio | Entrada configurable / navegación |
| TFT ST7796 480×320 | Pantalla |
| XPT2046 | Controlador táctil |
| Perfboard | Electrónica del prototipo |
| Carcasa impresa en 3D | Montaje mecánico |

El mapa GPIO actual está contrastado contra el firmware y documentado en [docs/hardware.md](./docs/hardware.md).

## Protocolo USB

El dispositivo expone HID estándar para teclado/multimedia y una interfaz Vendor HID dedicada.

El protocolo actual incluye:

- información del dispositivo;
- lectura/escritura de bindings;
- prueba de acciones;
- actualizaciones de configuración transaccionales;
- entrada al bootloader;
- eventos asíncronos de host actions;
- comandos, respuestas y eventos DisplayLink;
- manejo de errores ACK / NACK.

Consulta [docs/protocol.md](./docs/protocol.md), [firmware/src/protocol](./firmware/src/protocol/) y [software/StreamDeckDIY.Protocol](./software/StreamDeckDIY.Protocol/).

## Stack tecnológico

| Capa | Tecnologías |
| --- | --- |
| Firmware | C/C++ · Pico SDK 2.3.1 · TinyUSB |
| USB | HID Keyboard · Consumer Control · Vendor HID |
| Pantalla / táctil | ST7796 · XPT2046 · DisplayLink |
| App de escritorio | C# · .NET 10 · WinUI · Windows App SDK |
| Transporte | HidSharp |
| Telemetría de hardware | LibreHardwareMonitor |
| Diseño mecánico | CAD · impresión 3D FDM |
| Control de versiones | Git · GitHub · Forgejo |

## Estructura del repositorio

```text
streamdeck-diy/
├── firmware/        # Código RP2040, diagnósticos y tests
├── software/        # App Windows, protocolo, transporte y tests
├── docs/            # Arquitectura, hardware y protocolo
├── assets/          # Multimedia pública del proyecto
├── tools/           # Herramientas del repositorio/importación
├── .gitignore
├── .gitattributes
├── README.md
└── README.es.md
```

## Compilación

### Firmware

Requisitos:

- Raspberry Pi Pico SDK **2.3.1**
- CMake
- toolchain ARM compatible con Pico SDK
- placa objetivo: `waveshare_rp2040_zero`

Desde la raíz del repositorio:

```bash
cmake -S firmware -B firmware/build
cmake --build firmware/build
```

La salida principal es `StreamDeck_Firmware.uf2`. CMake incluye además targets de diagnóstico independientes para la matriz y el encoder.

### Aplicación Windows

Requisitos:

- Windows
- .NET **10 SDK**
- x64
- dependencias de Windows App SDK restauradas mediante NuGet

```powershell
dotnet restore .\software\StreamDeckDIY.sln
dotnet build .\software\StreamDeckDIY.sln -c Debug -p:Platform=x64
```

Los tests del lado de escritorio pueden ejecutarse con:

```powershell
dotnet run --project .\software\StreamDeckDIY.Protocol.Tests\StreamDeckDIY.Protocol.Tests.csproj
```

## Estado actual

| Área | Estado |
| --- | --- |
| Matriz 4×3 | ✅ Funcional |
| Encoder + pulsación | ✅ Funcional |
| USB Keyboard HID | ✅ Funcional |
| Consumer Control HID | ✅ Funcional |
| Transporte Vendor HID | ✅ Funcional |
| Configuración persistente | ✅ Implementada |
| Aplicación Windows | ✅ Funcional |
| Pipeline de pantalla 480×320 | ✅ Funcional |
| Soporte táctil | ✅ Implementado |
| Tests firmware / escritorio | ✅ Incluidos |
| Código público del firmware | ✅ Publicado |
| Código público de la app Windows | ✅ Publicado |
| Carcasa final / CAD | 🚧 En iteración |
| Guía de montaje | ⏳ Planificada |

## Principios de desarrollo

- Mantener el comportamiento del firmware explícito y comprobable.
- Tratar el protocolo USB como interfaz estable entre componentes que pueden evolucionar por separado.
- Mantener las funciones importantes del dispositivo disponibles sin depender de que la app esté abierta.
- Diseñar electrónica y carcasa teniendo en cuenta montaje y mantenimiento reales.
- Conservar tests de regresión conforme evoluciona el proyecto.
- Documentar fallos y revisiones en vez de ocultarlos.

## Licencia

Todavía no se ha seleccionado una licencia. Hasta que se añada una, se aplican las reglas de copyright por defecto.

---

Construido en España 🇪🇸 como proyecto personal de ingeniería.

*Este es un proyecto DIY independiente y no está afiliado ni respaldado por Elgato o Corsair.*
