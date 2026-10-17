<!-- Generated from Tools/Docs/player_guide.json. Do not edit. -->

# Weiterreisen & nach Hause kommen

[Visuelles Wiki](index.html) | [Alle Kapitel](README.md)

Du klickst keinen Warp-Knopf. Du steuerst die Nostos durch eine Sprungzone am Systemrand. Die Reise selbst fliegst du anschließend weiter.

**Dein Ziel:** Du unterscheidest normalen Flug, schnellen Reiseflug und Warp – und kannst einen Sprung vorbereiten.

## Drei Arten zu fliegen

### Normaler Flug

Für Start, Landung, Andocken und Kämpfe. Kleine, kontrollierte Bewegungen statt Höchstgeschwindigkeit.

### System-Reiseflug

Halte C ungefähr 2 Sekunden: Das Schiff beschleunigt für weite Strecken innerhalb des Systems. X, S oder C beenden ihn. Planeten, Hindernisse und Gefechte können ihn ebenfalls beenden.

### Warp

Ein geflogener Korridor zum Nachbarsystem oder zur nächsten Galaxie. W / S steuern hier den Schub; C regelt ihn automatisch, G nutzt passende Signale.

## Einen Sprung vorbereiten

1. **Ziel auf M ansehen.** Die Galaxiekarte zeigt Systeme und die zugehörigen Sprungzonen. Nachbarsysteme erreichst du über deren Zone. Galactic Rim und die Skip-Zone gibt es nur im äußersten System der Galaxie.

2. **Crew einsammeln.** Alle lebenden Crewmitglieder müssen in einem Schiff sitzen oder im geschützten Innenraum stehen. Wer noch draußen ist, blockiert den Sprung.

3. **Die Nostos steuern.** Setze dich mit F an ihr Steuer. Eine freie Dart löst den Sprung nicht aus. Für Galaxienwarps muss außerdem genug Exotic Matter im gemeinsamen Tank sein.

4. **Aus der Schwerkraft und aus dem Gefecht kommen.** Fliege aus dem Schwerkraftbereich des Planeten und löse dich von angreifenden Gegnern. Lies bei einer Blockade den Grund in der Bildschirmmitte.

Weiterlesen: [Nostos, Dart & Zusammenspiel](nostos.md) · [Beute nutzen & besser ausrüsten](equipment.md) · [Ich komme gerade nicht weiter](help.md)

## Durch die Sprungzone fliegen

1. **Markierung anfliegen.** Suche im HUD die Zone zum gewünschten Ziel und fliege die Nostos dorthin.

2. **Nach außen ausrichten.** Durchquere die Zonenebene im Reiseflug nach außen. Alternativ halte innerhalb der Zone die Nase etwa 3 Sekunden nach außen, bis ALIGNING vollständig ist.

3. **Einflug abwarten.** Bei WARP ENTRY beginnt der Übergang. Die Crew geht gemeinsam in die Warpstrecke; freie kleine Schiffe werden für die Reise an die Nostos geholt.

**Das solltest du sehen:** Ein Sprung zum Nachbarsystem benötigt kein Exotic Matter. Ein Galaxienwarp verbraucht es und führt in eine Richtung weiter.

## Was tue ich während des Warps?

1. **Schub und Hindernisse beobachten.** W erhöht, S senkt den Schub; mit der Maus lenkst du seitlich an Hindernissen vorbei. X nimmt den Schub heraus. Kollisionen können schaden und einen Notstopp auslösen.

2. **Hitze kontrollieren.** Zu viel Schub über längere Zeit überhitzt den Antrieb. Nimm rechtzeitig Schub zurück. Bessere Kühlung erlaubt dauerhaft mehr Leistung, bessere Navigation hilft in Feldern.

3. **Reiseautomatik bewusst nutzen.** C hält den Warpschub und schaltet sich bei der nächsten Hinderniswarnung ab. Bleibe deshalb aufmerksam.

4. **Ein Signal besuchen.** Wenn ein erreichbares Signal angezeigt wird, kann der führende Pilot mit G den Warp verlassen. Nach der Erkundung bringt G die Crew nach einem kurzen Countdown zurück auf die Strecke.

> Nahe Gegner oder gegnerische Treffer können den Warp unterbrechen. Kümmere dich um die Bedrohung, statt nur mehr Schub zu geben.

## Der lange Weg nach Sol

Erkunde eine Galaxie, verbessere den Antrieb und senke die Home ETA. Von der LMC geht es auf den letzten Flug nach Hause. Das letzte Stück ist die dichteste Strecke des Spiels: Gas geben, Hindernisse bei hohem Tempo durchfliegen und auf die Hitze achten. Unterwegs gibt es dort keine Signale zum Aussteigen.

M33 → M31 → NGC 6822 → Leo I → LMC → Sol

> Die vollständige Erdannäherung und Landung gehören noch zum geplanten Ausbau. Der dokumentierte spielbare Abschluss ist derzeit ein Heimkehr-Ergebnisbildschirm.

---

Quellenabgleich: 2026-10-17. Kein neuer vollständiger Spieltest.

[WarpFlight.Jumps.cs](../../Assets/_Project/Scripts/World/WarpFlight.Jumps.cs) · [JumpZones.cs](../../Assets/_Project/Scripts/Core/JumpZones.cs) · [ShipFlight.cs](../../Assets/_Project/Scripts/Ship/ShipFlight.cs) · [WarpFlight.cs](../../Assets/_Project/Scripts/World/WarpFlight.cs) · [HudView.cs](../../Assets/_Project/Scripts/UI/HudView.cs)
