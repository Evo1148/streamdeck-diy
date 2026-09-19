<p align="center">
  <a href="./README.md">🇬🇧 English</a> · <strong>🇪🇸 Español</strong>
</p>

<h1 align="center">DIY Stream Deck</h1>

<p align="center">
  Un macro pad / Stream Deck personalizado construido desde cero alrededor del RP2040:
  electrónica, firmware USB HID, software de configuración para Windows y carcasa impresa en 3D.
</p>

<p align="center">
  <strong>12 teclas mecánicas · encoder rotatorio · pantalla 480×320 · USB HID · app para Windows · carcasa propia</strong>
</p>

---

## Descripción

Este proyecto comenzó como un macro pad DIY sencillo y ha ido creciendo hasta convertirse en un sistema completo de hardware y software.

El objetivo es construir un dispositivo realmente útil para el escritorio diario y, al mismo tiempo, utilizarlo como proyecto práctico de sistemas embebidos, protocolos USB, software de escritorio, electrónica y diseño mecánico.

El dispositivo está construido alrededor de un **RP2040-Zero** y combina una matriz 4×3 de teclas mecánicas, un encoder rotatorio, una pantalla TFT de 480×320 y un protocolo USB HID propio utilizado por una aplicación de configuración para Windows.

> **Estado del proyecto:** desarrollo activo. El prototipo físico, la matriz de teclas, el encoder, la comunicación USB HID y el flujo de configuración desde Windows son funcionales. Este repositorio público se está preparando ahora para importar el código y los diseños CAD completos.

## Características

- **12 teclas mecánicas** en matriz 4×3.
- **Encoder rotatorio** con pulsación.
- **Pantalla TFT 480×320**.
- Soporte **USB HID Keyboard**.
- **Consumer Control HID** para acciones multimedia.
- **Protocolo Vendor HID propio** para configuración y comunicación con el host.
- **Bindings persistentes** almacenados en el dispositivo.
- **Aplicación de configuración para Windows** desarrollada con .NET / WinUI.
- **Carcasa impresa en 3D** con pedestales de diferentes ángulos.
- Hardware, firmware, software y diseño mecánico evolucionan como un único sistema.

## Arquitectura

```text
┌──────────────────────┐
│   Aplicación Windows │
│   C# / WinUI         │
└──────────┬───────────┘
           │ Vendor HID
           │ reports de 64 bytes
┌──────────▼───────────┐
│     RP2040-Zero      │
│                     │
│  Firmware USB HID   │
│  Persistencia       │
│  Procesado inputs   │
└───┬────────┬────────┘
    │        │
    │        └──────────────► TFT 480×320
    │
    ├───────────────────────► Encoder
    │
    └───────────────────────► Matriz 4×3
```

Hay más detalle en [docs/architecture.md](./docs/architecture.md).

## Protocolo USB

El firmware expone funcionalidad HID estándar para teclado y multimedia, además de una interfaz **Vendor HID** para configuración.

El protocolo actual utiliza reports fijos de **64 bytes** e incluye operaciones para:

- obtener información del dispositivo;
- configurar un binding;
- leer un binding;
- informar sobre bindings;
- probar una acción;
- respuestas ACK / NACK.

Consulta [docs/protocol.md](./docs/protocol.md).

## Hardware

El prototipo actual utiliza:

| Componente | Función |
| --- | --- |
| RP2040-Zero | Microcontrolador principal |
| 12× switches compatibles con MX | Matriz 4×3 |
| Diodos 1N4148 | Aislamiento de la matriz |
| Encoder rotatorio | Navegación / entrada configurable |
| TFT 480×320 | Pantalla del dispositivo |
| Perfboard | Electrónica del prototipo |
| Carcasa impresa en 3D | Montaje mecánico |

Las notas de hardware están en [docs/hardware.md](./docs/hardware.md).

## Stack de software

| Capa | Tecnologías |
| --- | --- |
| Firmware | C/C++ · Pico SDK · TinyUSB |
| USB | HID Keyboard · Consumer Control · Vendor HID |
| App de escritorio | C# · .NET · WinUI |
| Transporte | HidSharp |
| Diseño mecánico | CAD paramétrico / impresión 3D |
| Control de versiones | Git · GitHub / Forgejo |

## Estructura del repositorio

El repositorio se está preparando con esta organización:

```text
streamdeck-diy/
├── firmware/        # Firmware RP2040
├── software/        # Aplicación de configuración Windows
├── hardware/        # Cableado y documentación electrónica
├── cad/             # Carcasa y soportes
├── docs/            # Arquitectura y protocolo
├── assets/          # Fotografías, capturas y multimedia
├── .gitignore
├── README.md
└── README.es.md
```

Las carpetas de código se irán poblando a medida que limpiemos e importemos el árbol de desarrollo actual.

## Estado actual

| Área | Estado |
| --- | --- |
| Matriz de teclas 4×3 | ✅ Funcional |
| Encoder + pulsación | ✅ Funcional |
| USB Keyboard HID | ✅ Funcional |
| Consumer Control HID | ✅ Funcional |
| Transporte Vendor HID | ✅ Funcional |
| Bindings persistentes | ✅ Implementado |
| App de configuración Windows | ✅ Funcional |
| Pipeline de pantalla 480×320 | ✅ Funcional |
| Carcasa final | 🚧 En iteración |
| Importación pública del código | 🚧 En progreso |
| Guía de montaje | ⏳ Planificada |

## Objetivos

El proyecto pretende ser algo más que una caja con botones. El objetivo a largo plazo es conseguir un dispositivo compacto y pulido con:

- firmware fiable para uso diario;
- acciones configurables sin reflashear;
- una pantalla realmente útil;
- hardware reproducible;
- una experiencia de configuración limpia en Windows;
- una carcasa que pueda imprimirse, montarse y mantenerse de forma práctica.

## Filosofía de desarrollo

Algunos principios del proyecto:

- mantener el comportamiento del firmware explícito y comprobable;
- evitar acoplar el dispositivo a una única aplicación de escritorio;
- tratar el protocolo USB como una interfaz estable;
- diseñar hardware y carcasa teniendo en cuenta restricciones reales de montaje;
- documentar fallos y revisiones en lugar de ocultarlos.

## Licencia

Todavía no se ha seleccionado una licencia. Hasta que se añada una, se aplican las reglas de copyright por defecto.

---

Construido en España 🇪🇸 como proyecto personal de ingeniería.

*Este es un proyecto DIY independiente y no está afiliado ni respaldado por Elgato o Corsair.*
