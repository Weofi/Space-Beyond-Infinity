<!-- Generated from Tools/Docs/player_guide.json. Do not edit. -->

# Steuerung & Anzeigen

[Visuelles Wiki](index.html) | [Alle Kapitel](README.md)

Die wichtigsten Tasten für deinen nächsten Schritt. Die Belegung ist der Standard; in Settings kannst du sie ändern. Im Spiel angezeigte Tasten haben Vorrang.

**Dein Ziel:** Du findest die richtige Taste und erkennst die Anzeigen, auf die es gerade ankommt.

## Immer nützlich

| Taste / Begriff | Bedeutung |
| --- | --- |
| F | Mit dem anvisierten Objekt interagieren; im Sitz: aufstehen. |
| Tab | Inventory, Ship, Crafting und Warp Drive öffnen. |
| M | Galaxiekarte öffnen und Reiseziele / Sprungzonen ansehen. |
| T / mittlere Maustaste | Ein Ziel für die Crew markieren. |
| Esc | Menü öffnen. |

## Zu Fuß & im Raumanzug

| Taste / Begriff | Bedeutung |
| --- | --- |
| W A S D + Maus | Laufen und umsehen. |
| Space / Strg | Springen / Jetpack; im freien Raum auf- und absteigen. |
| Shift | Sprint / Boost. |
| Q / E | Im freien Raum rollen. |
| L | Helmlampe umschalten. |
| 1 + linke Maustaste halten | Bohrer wählen und ein Vorkommen abbauen. |
| 2 + linke Maustaste | Blaster wählen und schießen. |
| 3 + linke Maustaste halten | Gewehr benutzen, wenn du eines freigeschaltet hast. |
| 4 + linke Maustaste | Ein Medkit aus dem Rucksack verbrauchen. |

## Im Pilotensitz

| Taste / Begriff | Bedeutung |
| --- | --- |
| Maus | Nase des Schiffs steuern. |
| W / S | Vorwärts / rückwärts; im Warp: Schub erhöhen / senken. |
| A / D | Seitwärts bewegen. |
| Space / Strg | Auf- und absteigen. |
| Q / E | Schiff rollen. |
| X | Bremsen; im Warp den Schub abschalten. |
| Shift | Boost im normalen Flug. |
| C halten | Im System den schnellen Reiseflug aufladen, ungefähr 2 Sekunden. |
| C drücken | Im Warp die Schubregelung umschalten; im aktiven System-Reiseflug: beenden. |
| G | Im Warp bei einem Signal aussteigen; nach dem Ausstieg den Warp fortsetzen. |
| Linke / rechte Maustaste | Schiffswaffe 1 / 2 abfeuern. |

## Welche Anzeigen sind wichtig?

### HOME ETA

Eure geschätzte Flugzeit bis nach Hause mit dem eingebauten Antrieb. Bessere Teile sollen diese Zeit senken.

### Gesundheit & Sauerstoff

Zeigen, wie lange du draußen bleiben kannst. Bei knapper Luft frühzeitig zum Schiff zurückkehren.

### Hull / Shield / Fuel

Zustand und normaler Treibstoff deines Schiffs. Exotic Matter ist der getrennte Vorrat für Galaxienwarps.

### Markierungen & Warnungen

Weisen auf Fundstellen, Stationen und Sprungzonen hin. Hinweise in der Bildschirmmitte erklären zum Beispiel, warum ein Sprung blockiert ist.

![Spielansichten mit Zielanzeige, Kompass sowie Anzug- und Schiffswerten](../../Docs/screenshots/issue-115/after_ingame_ice.jpg)

Entwicklungsaufnahme aus #115: Ziel und Navigation oben, persönliche Werte unten links, Schiffswerte unten rechts. Die Anordnung hilft bei der Orientierung; der heutige Build kann abweichen.

---

Quellenabgleich: 2026-10-17. Kein neuer vollständiger Spieltest.

[VwInput.cs](../../Assets/_Project/Scripts/Core/VwInput.cs) · [HudView.cs](../../Assets/_Project/Scripts/UI/HudView.cs) · [PlayerPawn.cs](../../Assets/_Project/Scripts/Player/PlayerPawn.cs) · [ShipSeat.cs](../../Assets/_Project/Scripts/Ship/ShipSeat.cs)
