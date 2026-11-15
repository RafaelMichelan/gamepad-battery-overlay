===============================================================================
                       GAMEPAD BATTERY OVERLAY  -  v1.7.1
===============================================================================

  Author ......: Rafael Gonçalez
  Version .....: 1.7.1  (2026-11-15)
  System ......: Windows 10 / 11
  Controllers .: DualSense (PS5), DualShock 4 (PS4) and Xbox
                 Bluetooth (tested) and USB
  Languages ...: English and Portuguese (Brazil), chosen in the tray menu

  This program was made entirely with VIBE-CODING: all of the code was
  written in conversation with an artificial intelligence (Claude, by
  Anthropic), with the ideas, testing and decisions made by Rafael Gonçalez.

  (Versão em português: Leia-me.txt)

  +---------------------------------------------------------------------+
  |  FREE PROGRAM - SELLING IT IS PROHIBITED                            |
  |                                                                     |
  |  The only official place to download it is GitHub:                  |
  |  https://github.com/RafaelMichelan/gamepad-battery-overlay          |
  |  If someone charged you for this program, you were scammed.         |
  |  PolyForm Noncommercial 1.0.0 license (LICENSE file): using,        |
  |  copying, changing and sharing it for free is allowed; selling it,  |
  |  charging for the download or commercial use is not.                |
  +---------------------------------------------------------------------+

  Up to v1.5 the program was called "DualSense Battery Overlay". The name
  changed because it now works with PS5, PS4 and Xbox controllers.

-------------------------------------------------------------------------------
 CONTENTS
-------------------------------------------------------------------------------

   1. What it is
   2. Requirements and supported controllers
   3. Opening it for the first time
   4. The overlay (the small battery window)
   5. Critical battery alert (below 10%)
   6. Notifications
   7. Controller shortcut: show/hide the overlay
   8. Tray icon menu
   9. Language
  10. Test tool (testar-bateria.ps1)
  11. Known limitations
  12. How to uninstall
  13. Next steps (plans for future versions)
  14. How to report problems
  15. Build it yourself
  16. Version history

-------------------------------------------------------------------------------
 1. WHAT IT IS
-------------------------------------------------------------------------------

  A small program that shows the battery percentage of your PlayStation and
  Xbox controllers on screen, and tells you when they connect, disconnect,
  start charging or reach a critical battery level.

  It does NOT need any third-party program (DS4Windows, DSX, Steam...) and
  it doesn't need to be installed: it only uses what already comes with
  Windows. The battery is read straight from the controller, through the
  same channel Windows uses to receive the buttons.

-------------------------------------------------------------------------------
 2. REQUIREMENTS AND SUPPORTED CONTROLLERS
-------------------------------------------------------------------------------

  - Windows 10 or 11 (64-bit).
  - .NET Framework 4.x — already installed on Windows 10/11.

  Controllers:
    DualSense (PS5) ...................... tested (Bluetooth and USB)
    DualShock 4 1st gen (CUH-ZCT1) ....... tested (Bluetooth and USB)
    DualShock 4 2nd gen (CUH-ZCT2) ....... supported, not tested yet
    DualSense Edge ....................... supported, not tested yet
    Xbox Series X|S ...................... tested (Bluetooth)
    Xbox One S / Elite 2 ................. supported, not tested yet
    Xbox on a USB cable .................. no battery: "USB mode" (section 4)

  Several controllers at the same time: yes (one per line in the overlay).

-------------------------------------------------------------------------------
 3. OPENING IT FOR THE FIRST TIME
-------------------------------------------------------------------------------

  1) Connect the controller to the PC (Bluetooth or cable).
  2) Double-click  GamepadBatteryOverlay.exe.
  3) A "DualSense connected" (or "DualShock 4 connected") notification
     appears and, in the top-right corner of the screen, the controller's
     battery.
  4) A controller icon with a small battery bar appears in the Windows tray
     (near the clock). If you don't see it, click the "^" arrow in the
     tray to show hidden icons. All the options are in that icon's
     right-click menu.

  LANGUAGE: the program starts in Portuguese if Windows is in Portuguese,
  and in English otherwise. To change it: tray menu > "Idioma / Language"
  (section 9).

  WINDOWS WARNING (SmartScreen):
  The program doesn't have a paid digital signature, so Windows may show
  "Windows protected your PC". To open it anyway, click "More info" and then
  "Run anyway".
  If you'd rather not trust the ready-made .exe, build it yourself from the
  source code (section 15). It takes a few seconds.

  The program only opens once: if you open it again, nothing happens,
  because it's already running in the tray.

-------------------------------------------------------------------------------
 4. THE OVERLAY (THE SMALL BATTERY WINDOW)
-------------------------------------------------------------------------------

  Shows one line per connected controller, with the model, a battery icon
  and the percentage:

        PS5  [■■■□ ]  65%
        PS4  [■■□□ ]  45%
        XBOX [■■■□ ]  77%

  Controllers appear in the order they connected.
  If the same controller is connected by cable and by Bluetooth at the same
  time, it only appears once.

  CONTROLLER WITH NO BATTERY INFORMATION ("USB mode"): when a controller
  doesn't report its battery, the line shows a text instead of the icon and
  the percentage:

        XBOX       USB mode

  This is the case of an Xbox controller on a USB cable: Windows doesn't
  report its battery while it's on the cable. A line like this never
  triggers the low battery alert and doesn't appear in "Only show when
  battery is low" mode. The other controllers keep showing their battery
  normally.

  XBOX ON BLUETOOTH: the battery comes from the same information that the
  "Bluetooth & devices" page in Windows Settings shows, and it's updated
  every 30 seconds (the controller itself only updates this value every
  now and then). It may differ from Settings by 1 or 2% for a few moments.
  The Xbox controller doesn't report whether it's charging.

  Bar colors:
    Green ........ above 50%
    Yellow ....... 21% to 50%
    Red .......... 20% or less
    Blue + bolt .. charging (cable connected)

  Behavior:
  - It always stays on top of other windows.
  - Mouse clicks go through it: it doesn't get in the way of anything below.
  - It doesn't appear in Alt+Tab or the taskbar and doesn't steal the game's
    focus.
  - A controller's line disappears when it disconnects and comes back when
    it reconnects.
  - The battery is updated in real time (a change shows up in ~1 second).

  ABOUT ACCURACY: PlayStation controllers don't report a percentage, but a
  LEVEL. On battery, the level goes from 0 to 8 (8 = full). Each level is
  worth 12.5%, so the overlay shows 100, 87, 75, 62, 50, 37, 25, 12 and 0%.
  There is no more precise reading than that from these controllers.

  BATTERY SCALE (corrected in v1.2.1): up to v1.2 the program assumed the
  level went up to 10, like DSX does, so a FULL controller showed as 85%
  (and 80% in DSX). Measuring the controllers made it clear that on
  battery they stop at level 8. This was confirmed with DS4Windows, which
  also shows 100% for the same controller. The old scale is still
  available: tray menu > Overlay > "Battery scale" > "0 to 10 — old".

-------------------------------------------------------------------------------
 5. CRITICAL BATTERY ALERT (BELOW 10%)
-------------------------------------------------------------------------------

  When a controller's battery drops below 10% (and it isn't charging):

  - that controller's line ALWAYS APPEARS on screen, even if you hid the
    overlay (from the menu or with the controller shortcut) or turned on
    "Only show when battery is low";
  - a red "Battery very low" notification appears.

  When you plug in the cable, the controller leaves the critical state and
  the overlay goes back to what you had chosen.

  To turn it off: tray menu > Overlay > uncheck
  "Always show when battery is below 10%".

-------------------------------------------------------------------------------
 6. NOTIFICATIONS
-------------------------------------------------------------------------------

  Animated cards that appear right below the overlay, stay for about 4
  seconds and disappear on their own. If several appear at once, they stack
  (up to 4) and rearrange themselves when one leaves. Notifications of the
  same kind (the shortcut, or the state of the same controller) replace
  each other instead of stacking.
  Clicks also go through them. All of them say which controller it is.

    Color   Notification               When it appears
    ------  -------------------------  ------------------------------------
    Blue    (controller) connected     the controller turns on / connects
                                       (Xbox on cable: "via USB · USB mode")
    Red     (controller) disconnected  the controller turns off / disconnects
    Green   Charging                   the cable is plugged in
    Green   Fully charged              the battery fills up on the cable
    Orange  Charging stopped           the cable is unplugged
    Red     Battery very low           the battery drops below 10%
    Gray    Overlay hidden             you used the shortcut (section 7)
    Blue    Overlay visible            you used the shortcut again

  SOUNDS: each notification has a short, quiet sound effect (under 1.3 s).
  The sounds were created by the program itself, by synthesis: there are no
  sound files and nothing was copied from other programs.
    Connected ........... two rising notes
    Disconnected ........ two falling notes
    Charging ............ three quick rising notes
    Fully charged ....... an opening chord
    Charging stopped .... two short falling notes
    Battery very low .... two low tones
    Overlay hidden ...... falling "tick"
    Overlay visible ..... rising "tick"
  To turn them off: tray menu > Notifications > "Notification sounds".
  The volume follows the Windows Volume Mixer (the program shows up there).

  Connecting and disconnecting are detected right away (Windows notifies
  the program). Before saying "disconnected", the program waits 1.5 seconds
  to confirm, avoiding false alerts.

  To see the notifications without touching the controller: right-click the
  tray icon > Notifications > "Test notifications".

-------------------------------------------------------------------------------
 7. CONTROLLER SHORTCUT: SHOW/HIDE THE OVERLAY
-------------------------------------------------------------------------------

    DualSense (PS5) ....... MUTE button (the small microphone button,
                            below the PS button)
    DualShock 4 (PS4) ..... L3 + R3 together (press both sticks in at the
                            same time)
    Xbox .................. L3 + R3 together, same as the DualShock 4
                            (Bluetooth and USB cable)

    once ........ hides the overlay    (gray "Overlay hidden" notification)
    again ....... shows it again       (blue "Overlay visible" notification)

  Holding it doesn't keep toggling: only the moment of the press counts.
  If a game uses these buttons, turn the shortcut off in the tray menu
  ("Controller shortcut").
  Reminder: a controller with battery below 10% appears even when the
  overlay is hidden (section 5).

-------------------------------------------------------------------------------
 8. TRAY ICON MENU (right-click)
-------------------------------------------------------------------------------

  The icon is a controller with a small battery bar below it, showing the
  MOST DISCHARGED controller: the bar fills up with the % and changes color
  (green, yellow, red; blue when charging). With no controller connected,
  the icon is dimmed with an empty bar. The controller is light on a dark
  taskbar and dark on a light taskbar, and it follows right away when you
  switch the Windows theme.
  Hovering over it shows each controller's exact %.

  The menu is organized like this (the most used items are at the top):

    Show overlay
    Overlay  >          Only show when battery is low / Always show below
                        10% / Screen position / Battery scale
    Notifications  >    Show notifications / Sounds / Notice when starting
                        with Windows / Test notifications
    Controller  >       Controller shortcut / Search for controllers now
    RivaTuner (RTSS)  >
    Idioma / Language  >
    Start with Windows
    Exit

  - Show overlay
      Turns the overlay on/off (same as the controller shortcut).
      Notifications keep showing while the overlay is hidden, including
      "Fully charged".

  OVERLAY
  - Only show when battery is low (≤20%)
      Each controller only appears when its battery drops to 20% or less
      (and it isn't charging).
  - Always show when battery is below 10%
      Turns the mandatory alert from section 5 on/off. It's ON by default.
  - Screen position
      Chooses the screen corner: top right, top left, bottom right or
      bottom left. The notifications follow.
  - Battery scale
      "0 to 8 — corrected" (default): full = 100%.
      "0 to 10 — old": the calculation from v1.2 and earlier, same as DSX
      (a full controller shows as 85%). It's there so you can go back if
      the new scale looks wrong on some controller. The change applies
      immediately.

  NOTIFICATIONS
  - Show notifications (connection, charging, low battery)
      Turns all automatic notifications on/off (section 6).
  - Notification sounds
      Turns the notification sound effects on/off. When turned on, it plays
      a sample "tick".
  - Notice when starting with Windows
      When the program opens with Windows, it shows "Gamepad Battery Overlay
      is running" about 8 seconds after the desktop appears (with no
      controller connected the program only lives in the tray icon, hence
      the notice). It's ON by default. Uncheck it if you don't want it.
  - Test notifications
      Shows every kind of notification in sequence.

  CONTROLLER
  - Controller shortcut (Mute on PS5 · L3+R3 on PS4 and Xbox)
      Turns the shortcut from section 7 on/off.
  - Search for controllers now
      Forces a new search for controllers (normally it's automatic).

  - RivaTuner (RTSS)
      Shows the battery INSIDE the game, drawn by RivaTuner (it must be
      running; it comes with MSI Afterburner). It's for games in exclusive
      full screen, where no window can appear. The program only sends the
      text through RivaTuner's official channel (the same one HWiNFO and
      AIDA64 use): it doesn't inject anything into the game. The submenu
      shows whether RivaTuner is running.
        Off
        Only when the game hides the overlay (default): only when the game
          really takes over the screen (exclusive mode, where the window
          wouldn't appear) do the "PS5 75%   XBOX 77%" line and the
          notifications go to the game, with the overlay window hidden. A
          fullscreen game where the overlay shows (fullscreen optimizations
          on, or DX12) keeps the normal overlay, even with RivaTuner open.
        Always: shows in RivaTuner all the time (in windowed games it may
          appear alongside the overlay).
      It shows the same as the overlay (it follows the shortcut and the
      below-10% alert) plus the latest notification for 5 seconds. When the
      program closes, the text is removed from RivaTuner.

  - Idioma / Language
      Português (Brasil) or English. See section 9.

  - Start with Windows
      Makes the program open by itself when you turn on the PC.
      The choice is saved: every time the program opens, it checks the
      startup entry and fixes it by itself if it disappeared (e.g. a
      "cleanup" program) or if you moved the .exe to another folder.
      If it's disabled in Task Manager > Startup apps, the tray shows
      "(disabled in Task Manager)"; checking the option again re-enables it.
      The last 20 starts are logged in
      %LOCALAPPDATA%\GamepadBatteryOverlay\inicializacoes.log (useful when
      reporting problems).

  - Exit
      Closes the program.

  Your choices are saved and come back the next time the program opens.

-------------------------------------------------------------------------------
 9. LANGUAGE
-------------------------------------------------------------------------------

  The program speaks English and Portuguese (Brazil).

  - The first time, it follows the Windows language: Portuguese if Windows
    is in Portuguese, English for any other language.
  - To change it: right-click the tray icon > "Idioma / Language" >
    "English" or "Português (Brasil)". This menu item is written in both
    languages so anyone can find it.
  - The change applies immediately (menu, notifications and tray tooltip),
    with no need to restart, and it's saved for next time.

  The test tool (section 10) and the program's source code comments are
  only in Portuguese for now.

-------------------------------------------------------------------------------
 10. TEST TOOL  (tools\testar-bateria.ps1)
-------------------------------------------------------------------------------

  A script to check that the reading is correct (its messages are in
  Portuguese). Open PowerShell in the program's folder and run one of
  these commands:

    powershell -ExecutionPolicy Bypass -File tools\testar-bateria.ps1
      CHECK (default). For EACH connected PlayStation controller, it reads
      500 packets and checks:
      - CRC32 (on Bluetooth, a checksum the controller sends with every
        packet: proves the data arrived without errors);
      - whether the controller's internal clock is advancing (new data);
      - whether the sticks at rest stay near 128 (correct byte positions);
      - whether the battery byte is stable.
      At the end it shows "OK" in green or "Atenção" (warning) in yellow.
      Keep the controllers still during this test.

    powershell -ExecutionPolicy Bypass -File tools\testar-bateria.ps1 -Modo AoVivo
      LIVE. Shows the data in real time: sticks, shortcut (Mute or L3+R3)
      and battery. Move the sticks (only they change), press the shortcut
      ("SIM" = yes appears), plug in the cable (the state changes to
      "carregando" = charging). Ctrl+C to exit.
      With more than one controller, choose which with  -Controle PS4  or
      -Controle PS5

    powershell -ExecutionPolicy Bypass -File tools\testar-bateria.ps1 -Modo Log
      LOG. Records the battery of all controllers every minute to a
      log-bateria-*.csv file in the tools folder. Leave it running while
      you play: the level should drop one step at a time at similar
      intervals. Use -IntervaloSeg 30 to record every 30 seconds, for
      example.

-------------------------------------------------------------------------------
 11. KNOWN LIMITATIONS (version 1.7.1)
-------------------------------------------------------------------------------

  - Xbox: there's no battery reading on a USB cable ("USB mode"). On
    Bluetooth there are no "Charging" and "Fully charged" notifications.
    The virtual Xbox 360 controller created by DS4Windows/DSX is ignored
    (including for the L3+R3 shortcut); because of that, a real wired
    Xbox 360 controller is also left out.
  - The DualSense Edge and the 2nd gen DS4 are implemented but haven't been
    tested yet.
  - EXCLUSIVE FULL SCREEN:
    * DirectX 12 games (e.g. Resident Evil 4 Remake): the overlay and
      notifications ALWAYS appear, because with DX12 Windows itself emulates
      exclusive full screen. Tested with fullscreen optimizations on and off.
    * DirectX 9/10/11 games (e.g. Resident Evil Revelations, DX9): they
      appear with "fullscreen optimizations" ON (the Windows default). With
      them OFF (game .exe Properties > Compatibility > "Disable fullscreen
      optimizations"), the game takes over the whole screen and no window
      can appear on top, including this program's.
      SOLUTION: if you use RivaTuner (RTSS, it comes with MSI Afterburner),
      the battery shows INSIDE the game, like the Steam overlay (section 8,
      "RivaTuner (RTSS)"). TESTED: Resident Evil Revelations (DX9) with the
      optimizations off, the battery and notifications show in the game.
      Without RivaTuner, use the game's "borderless
      window" mode or keep the optimizations on. Notification sounds play
      either way.
  - TIP (not a limitation of the program): if the DualSense lights up its
    charging light on the PC's USB port but the battery doesn't go up (and
    the overlay still says "via Bluetooth"), the cable is probably
    charge-only or has a bad contact on the data wires. Without being
    recognized by the PC, the DualSense limits itself to 0.1 A, as the USB
    standard requires. Use a USB-C cable with data. With a full battery,
    0.1 A is normal (it's only keeping the charge).
  - It only appears on the main monitor.
  - Only tested at 100% display scaling. At higher scaling it may look
    slightly blurry.
  - DS4Windows: TESTED, they work together. With DS4Windows open, the
    overlay keeps reading the controllers, as long as the "Hide DS4
    Controller" option (exclusive mode) is OFF in DS4Windows.
  - DSX and HidHide: if HidHide is hiding the controller, this program
    can't see it either. Working alongside these programs hasn't been
    tested yet (see section 13).
  - On Bluetooth, to be able to read the battery, the program asks the
    PlayStation controller for the "full report mode" (the same thing Steam
    and games with controller support do). This doesn't change anything in
    normal use.

-------------------------------------------------------------------------------
 12. HOW TO UNINSTALL
-------------------------------------------------------------------------------

  1) Right-click the tray icon and uncheck "Start with Windows" (if it's
     checked). Then click "Exit".
  2) Delete the program's folder.
  3) (Optional) To delete the saved settings, open the Registry Editor and
     delete the key:
        HKEY_CURRENT_USER\Software\GamepadBatteryOverlay
     (if you used v1.5 or earlier, also delete the old key:
        HKEY_CURRENT_USER\Software\DualSenseOverlay )

  4) (Optional) Delete the %LOCALAPPDATA%\GamepadBatteryOverlay folder (it
     only has the startup log).

  The program doesn't install anything else.

-------------------------------------------------------------------------------
 13. NEXT STEPS (plans for future versions)
-------------------------------------------------------------------------------

  [done in v1.1] PS4 controller support (DualShock 4).
  [done in v1.3] Xbox controller support (battery over Bluetooth,
                 "USB mode" on cable, L3+R3 shortcut).
  [done in v1.4] English translation.
  - Work together with input translation programs, such as DS4Windows and
    DSX, without interfering with them: the overlay will keep only READING
    the controller, without sending commands, and the goal is for it to
    keep seeing the controller even while those programs are active.

-------------------------------------------------------------------------------
 14. HOW TO REPORT PROBLEMS
-------------------------------------------------------------------------------

  If something doesn't work, open an "Issue" on the project's GitHub page
  (English or Portuguese is fine) and include:
    - the program version and Windows version;
    - the controller model and whether it was on Bluetooth or USB;
    - whether you use DS4Windows, DSX, Steam or HidHide;
    - what happened and what you expected;
    - the output of  tools\testar-bateria.ps1  (Check mode).

-------------------------------------------------------------------------------
 15. BUILD IT YOURSELF
-------------------------------------------------------------------------------

  You don't need to install Visual Studio: Windows already has the
  compiler. Double-click  compilar.bat. It closes the program if it's open
  and builds a new GamepadBatteryOverlay.exe from the files in the src folder.

-------------------------------------------------------------------------------
 16. VERSION HISTORY
-------------------------------------------------------------------------------

  v1.7.1 - Fixed: "Start with Windows" now repairs itself if the startup
           entry disappears or the .exe is moved, and shows a notification
           when it opens with Windows ("Notice when starting with Windows"
           option).
         - Tray menu reorganized into groups: Overlay, Notifications,
           Controller, RivaTuner and Language.
         - Fixed: with RivaTuner open, fullscreen games where the overlay
           shows normally (optimizations on, DX12) went to RivaTuner. Now
           RivaTuner is only used when the game really hides the overlay.

  v1.7  - Optional RivaTuner (RTSS) integration: the battery shows inside
          the game, even in exclusive full screen with fullscreen
          optimizations disabled.

  v1.6.1 - New license: PolyForm Noncommercial 1.0.0. The program is free
           and selling it is prohibited (up to v1.6 it was MIT).

  v1.6  - New name: Gamepad Battery Overlay (formerly DualSense Battery
          Overlay), since the program works with PS5, PS4 and Xbox.
          The .exe is now GamepadBatteryOverlay.exe.
        - Updating loses nothing: settings and "Start with Windows" move to
          the new name by themselves, and the old version, if open, is closed
          so nothing shows up twice. You can delete the old
          DualSenseOverlay.exe.

  v1.5  - New program icon (the .exe), made from the project's artwork.
        - New tray icon: a controller with a battery bar that fills up with
          the % and follows the Windows light/dark theme.

  v1.4  - English translation of the whole program, with an
          "Idioma / Language" option in the tray menu.
        - English guide (this ReadMe.txt).

  v1.3  - Xbox controller support (Series, One S, Elite 2): battery over
          Bluetooth, "USB mode" on cable, L3+R3 shortcut.
        - New rule: a controller with no battery information shows the text
          instead of the battery and never triggers the low battery alert.
        - The virtual Xbox 360 controller from DS4Windows/DSX is ignored.

  v1.2.1 - Corrected battery scale: on battery the controllers go up to
           level 8, not 10. A full controller now shows 100% (was 85%).
         - Tray option to go back to the old scale (same as DSX).

  v1.2  - Subtle sound effects for each notification, generated by the
          program itself, with an option to turn them off in the tray.
        - Test protocol and automated test suite.
        - Lower CPU usage with several controllers.

  v1.1  - DualShock 4 (PS4) support, with L3+R3 shortcut.
        - Several controllers at the same time (one line per controller).
        - Mandatory alert when the battery is below 10%, which can be turned
          off in the tray menu.
        - "Battery very low" notification.
        - Stacked notifications with the controller's name.
        - Battery updated in real time.

  v1.0  - First version: DualSense, overlay, notifications, Mute button,
          tray icon and test tool.

===============================================================================
  Gamepad Battery Overlay v1.7.1  —  © 2026 Rafael Gonçalez
  Free. Selling it is prohibited (PolyForm Noncommercial 1.0.0 license).
  Made with vibe-coding.
  "PlayStation", "DualSense" and "DualShock" are trademarks of Sony
  Interactive Entertainment. "Xbox" is a trademark of Microsoft. This
  project is not affiliated with Sony or Microsoft.
===============================================================================
