<a id="readme"></a>
<div align="center">

<img src="assets/readme/header-generated.png" width="100%" alt="DS5Link — Your DualSense. Wirelessly.">

<p align="center"><a href="https://github.com/Q1W1RTY/DS5Link/releases/latest/download/DS5Link-Setup.exe"><img src="assets/readme/nav/download.svg" height="36" alt="Download"></a> <a href="#quick-start"><img src="assets/readme/nav/quick-start.svg" height="36" alt="Quick start"></a> <a href="#screenshots"><img src="assets/readme/nav/screenshots.svg" height="36" alt="Screenshots"></a> <a href="docs/releases/CHANGELOG.md"><img src="assets/readme/nav/changelog.svg" height="36" alt="Changelog"></a> <a href="https://github.com/Q1W1RTY/DS5Link/issues"><img src="assets/readme/nav/issues.svg" height="36" alt="Report an issue"></a></p>

[![Latest release](assets/readme/nav/release.svg)](https://github.com/Q1W1RTY/DS5Link/releases/latest) ![Windows 11 x64](https://img.shields.io/badge/Windows-11%20x64-0078D4?style=flat-square) ![DualSense](https://img.shields.io/badge/controller-DualSense-30363d?style=flat-square)

**English** · [Русский ↓](#russian)

</div>

## Your controller, more ways to play

DS5Link brings a virtual USB DualSense to Windows over Bluetooth. Use native adaptive triggers, HD haptics and controller audio in supported games, or switch to Xbox / XInput compatibility. Manage everything from a compact desktop app that stays in your tray.

| Mode | Connection | What you get |
| :--- | :--- | :--- |
| **PS5** | Bluetooth | Virtual USB DualSense, native triggers, HD haptics and controller audio in supported games |
| **Xbox** | Bluetooth or USB | Xbox 360 / XInput compatibility and standard rumble |

### Make it yours

- **Game profiles** — apply settings when a game's EXE is in the foreground. Import and export profiles as JSON.
- **Controller input** — bind touchpad clicks and gestures to keys, remap buttons, and calibrate sticks with independent deadzones and a live preview.
- **Mouse control** — move the cursor with the touchpad or aim with the gyro while holding L2. Sensitivity is adjustable per profile.
- **Adaptive triggers** — tune L2 and R2 separately or use a preset. Preview effects for three seconds when the game isn't controlling the trigger.
- **Lighting** — solid colors, pulse, rainbow and battery-level indication.
- **Audio → haptics** — use a Windows audio output or capture only the active profiled game's audio. Adjust sensitivity, strength and noise gate.
- **Everyday essentials** — tray controls, optional startup with Windows, battery status, configurable Bluetooth idle timers and diagnostic ZIP export.

Games retain priority over manual effects on the channels they control. Audio → haptics is available in PS5 mode over Bluetooth.

<a id="screenshots"></a>
## A closer look

Compact previews — click any image to open it at full size. UI previews from development build 0.1.36; release builds may differ.

<table>
<tr><th>Main window</th><th>Connection diagnostics</th></tr>
<tr>
<td align="center" valign="top"><a href="assets/readme/main-en.png"><img src="assets/readme/main-en.png" height="250" alt="DS5Link main window: controller status, PS5 and Xbox modes"></a></td>
<td align="center" valign="top"><a href="assets/readme/diagnostics-en.png"><img src="assets/readme/diagnostics-en.png" height="250" alt="Connection metrics, recent events and diagnostic export"></a></td>
</tr>
<tr><th>Touchpad & sticks</th><th>Triggers, lighting & audio</th></tr>
<tr>
<td align="center" valign="top"><a href="assets/readme/input-en.png"><img src="assets/readme/input-en.png" height="250" alt="Touchpad keyboard bindings and stick deadzone preview"></a></td>
<td align="center" valign="top"><a href="assets/readme/effects-en.png"><img src="assets/readme/effects-en.png" height="250" alt="Independent adaptive triggers, lighting and audio haptics settings"></a></td>
</tr>
</table>

<details>
<summary>Installer & tray menu</summary>
<br>
<table>
<tr><th>Guided setup</th><th>Tray controls</th></tr>
<tr>
<td align="center" valign="top"><a href="docs/design/setup.png"><img src="docs/design/setup.png" width="360" alt="DS5Link installer and dependency checks"></a></td>
<td align="center" valign="top"><a href="docs/design/tray.png"><img src="docs/design/tray.png" width="190" alt="DS5Link tray menu"></a></td>
</tr>
</table>
</details>

<a id="quick-start"></a>
## Up and running

**Requirements:** Windows 11 x64, one standard DualSense and a Bluetooth adapter for PS5 mode. Administrator rights are required for installation. DualSense Edge is not supported by this profile.

1. **[Download DS5Link-Setup.exe](https://github.com/Q1W1RTY/DS5Link/releases/latest/download/DS5Link-Setup.exe)** and run it.
2. Let setup check the bundled drivers, then install or repair any missing dependencies. Restart Windows if prompted.
3. Hold **Create + PS** until the controller flashes, then pair it in Windows Bluetooth settings.
4. Open DS5Link and select **PS5** or **Xbox**. For PS5 emulation, connect over Bluetooth; over USB, use the controller directly or select Xbox mode.

[Detailed installation guide (Русский)](docs/user/INSTALL.md) · [All releases](https://github.com/Q1W1RTY/DS5Link/releases)

Already installed? Use **Update** in the main window and confirm the Windows prompt.

### Good to know

- Native DualSense features depend on game support. If Steam Input interferes, disable it for that game; close other controller emulators such as DSX.
- Windows may select DualSense as the default audio device. Choose your usual speakers, headphones or microphone again if needed.
- The app installer is currently unsigned; bundled drivers use their own signed installers. Keep Windows security enabled.
- Game compatibility is still being tested. When reporting a problem, include the game, PS5/Xbox mode, connection type and a diagnostic ZIP.

## Built on good foundations

The controller engine is based on **[vDS](https://github.com/hurryman2212/vds)**, with **[usbip-win2](https://github.com/vadimgrn/usbip-win2)**, **[HidHide](https://github.com/nefarius/HidHide)** and **[ViGEmBus](https://github.com/nefarius/ViGEmBus)**.

[Build & test (Русский)](docs/development/BUILD.md) · [Architecture (Русский)](docs/architecture/ARCHITECTURE.md) · [Changelog](docs/releases/CHANGELOG.md) · [Issues](https://github.com/Q1W1RTY/DS5Link/issues)

---

<a id="russian"></a>
## Русский

[↑ English](#readme) · **[Скачать](https://github.com/Q1W1RTY/DS5Link/releases/latest/download/DS5Link-Setup.exe)** · [Скриншоты](#screenshots) · [Инструкция](docs/user/INSTALL.md) · [Сообщить о проблеме](https://github.com/Q1W1RTY/DS5Link/issues)

**DS5Link — возможности проводного DualSense по Bluetooth на Windows.** В PS5-режиме приложение создаёт виртуальный USB DualSense для нативных курков, HD haptics и звука в поддерживаемых играх. Xbox-режим даёт совместимость с Xbox 360 / XInput по Bluetooth и USB.

### Что умеет

- **Профили игр:** применение настроек по EXE активной игры, импорт и экспорт в JSON.
- **Управление:** назначения клавиш на нажатия и жесты тачпада, переназначение кнопок, калибровка стиков, отдельные мёртвые зоны и предпросмотр ввода.
- **Управление мышью:** перемещение курсора тачпадом или гироскопом при удержании L2. Чувствительность сохраняется в профиле.
- **Адаптивные курки:** отдельная настройка L2 и R2, пресеты и трёхсекундный тест эффектов на свободных курках.
- **Подсветка:** постоянный цвет, пульсация, радуга и индикация заряда.
- **Звук → haptics:** отдача от выбранного выхода Windows или только от звука активной игры из профиля. Доступно в PS5-режиме по Bluetooth, с настройкой чувствительности, силы и порога тишины.
- **На каждый день:** управление из трея, автозапуск, заряд батареи, настройка интервалов автоотключения Bluetooth и экспорт диагностики в ZIP.

Игра имеет приоритет над ручными эффектами для каналов, которыми она управляет. [Галерея интерфейса ↑](#screenshots)

### Быстрый старт

Нужны **Windows 11 x64**, один обычный DualSense и Bluetooth-адаптер для PS5-режима. Для установки требуются права администратора. DualSense Edge этим профилем не поддерживается.

1. **[Скачайте DS5Link-Setup.exe](https://github.com/Q1W1RTY/DS5Link/releases/latest/download/DS5Link-Setup.exe)** и запустите его.
2. Установщик проверит комплект драйверов. Установите или восстановите недостающие компоненты и перезагрузите Windows, если мастер попросит.
3. Удерживайте **Create + PS** до мигания и добавьте DualSense в параметрах Bluetooth Windows.
4. Откройте DS5Link и выберите **PS5** или **Xbox**. Эмуляция PS5 работает по Bluetooth; по USB используйте контроллер напрямую или выберите Xbox.

[Подробная инструкция](docs/user/INSTALL.md) · [Все релизы](https://github.com/Q1W1RTY/DS5Link/releases)

Если DS5Link уже установлен, нажмите **Обновить** в главном окне и подтвердите запрос Windows.

### Что учитывать

- Нативные функции DualSense зависят от поддержки игры. Если мешает Steam Input, отключите его для этой игры; другие эмуляторы, включая DSX, закройте.
- Windows может выбрать DualSense основным звуковым устройством. При необходимости верните привычный выход или микрофон в параметрах звука.
- Установщик приложения пока без подписи издателя; установщики драйверов используют собственные подписи. Отключать защиту Windows не нужно.
- Проверка совместимости с играми продолжается. В сообщении о проблеме укажите игру, режим PS5/Xbox, тип подключения и приложите диагностический ZIP.

[Сборка и тесты](docs/development/BUILD.md) · [Архитектура](docs/architecture/ARCHITECTURE.md) · [История изменений](docs/releases/CHANGELOG.md)

<div align="center">

**[Download / Скачать](https://github.com/Q1W1RTY/DS5Link/releases/latest/download/DS5Link-Setup.exe)** · [Releases](https://github.com/Q1W1RTY/DS5Link/releases) · [Issues](https://github.com/Q1W1RTY/DS5Link/issues)

</div>
