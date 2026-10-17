<!-- Generated from Tools/Docs/player_guide.json. Do not edit. -->

# Ich komme gerade nicht weiter

[Visuelles Wiki](index.html) | [Alle Kapitel](README.md)

Beginne mit dem sichtbaren Problem. Hier findest du kurze Ursachen und den nächsten sinnvollen Schritt – ohne Entwicklerbegriffe.

**Dein Ziel:** Du kannst häufige Blockaden lösen und weißt, was bei Tod, Schiffsverlust und einer Spielpause passiert.

## Die Sprungzone lässt mich nicht durch

### Ich sitze in der Dart

Wechsle auf die Nostos und übernimm dort das Steuer. Solange sie existiert, führt sie die Sprünge an.

### Crew fehlt

Hole alle lebenden Mitspieler an Bord. Im Innenraum der Nostos dürfen sie stehen; sie müssen nicht alle einen Sitz belegen.

### Exotic Matter fehlt

Tab → Warp Drive öffnen und an der Nostos Load Exotic Matter benutzen. Vorräte im Cargo oder Rucksack zählen noch nicht als Tankinhalt.

### Tank zu klein

Ein größerer Exotic Tank ist nötig. Mehr Material im Rucksack vergrößert den Tank nicht.

### Schwerkraft / Kampf

Entferne dich vom Planeten oder löse die Bedrohung durch angreifende Gegner. Lies den konkreten Grund bei JUMP ZONE HOLDS.

### Die Ausrichtung fehlt

Fliege die richtige Zone nach außen durch oder halte darin die Nase nach außen. Achte auf ALIGNING und anschließend WARP ENTRY.

Weiterlesen: [Weiterreisen & nach Hause kommen](travel.md)

## Interagieren, Craften oder Einbauen klappt nicht

### Kein [F]-Hinweis

Gehe näher an das Objekt und schaue direkt darauf. Sitze und Terminals sind kleine Ziele. Besetzte Sitze kannst du nicht übernehmen.

### Craft ist ausgegraut

Prüfe die Zutaten im Rucksack und ob ein Fabricator in Reichweite ist. Teile im Cargo zuerst über To backpack holen.

### Antriebsteil wird abgewiesen

Gehe zur Nostos und beende zuerst die Warpstrecke. Antriebsteile kannst du unterwegs im Warp nicht tauschen.

### Gewehr auf 3 fehlt

Es ist kein Startwerkzeug. Stelle es her oder kaufe es und benutze es über Use im Rucksack.

### Medkit tut nichts

Du brauchst ein Medkit im Rucksack. 4 wählt die Funktion, erst die linke Maustaste setzt eines ein.

### Schneller Flug bricht ab

Hindernisse, Planetennähe, Treffer oder ein Gefecht können den System-Reiseflug beenden. Zuerst Abstand und eine freie Flugbahn schaffen.

## Was passiert, wenn ich sterbe?

1. **Du gehst zu Boden.** Bei 0 Leben liegst du am Boden und blutest etwa 45 Sekunden aus. Ein Mitspieler in deiner Nähe hält F gedrückt (etwa 4 Sekunden) und holt dich mit 40 % Leben zurück. Solange er hält, blutest du nicht weiter.

2. **Du wachst in der Kryo-Kapsel auf.** Blutest du aus, wachst du nach einer kurzen Wartezeit in deiner Kryo-Kapsel an Bord der Nostos auf, solange sie in Betrieb ist.

3. **Die ganze Landegruppe ist am Boden.** Dann ist die Landung gescheitert: Was ihr im Rucksack hattet, ist weg (Salvage und getragene Ausrüstung bleiben). Alle wachen in der Kryo-Bucht auf. Dein gelandetes Schiff bleibt als Wrack am Landeplatz liegen, mit Fracht; du bekommst nach etwa 20 Sekunden eine Ersatz-Dart. Das Wrack kannst du reparieren, solange ihr im System bleibt.

4. **Die eigene Dart ist zerstört.** Nach ungefähr 20 Sekunden wird eine Ersatz-Dart bereitgestellt. Wenn eine Klammer frei ist, wartet sie dort an der Nostos.

5. **Die Nostos fällt aus.** Bei 0 Rumpfpunkten ist die Nostos ausgefallen, nicht zerstört: kein Warp, keine Werkbank, kein Kryo-Respawn. Repariere sie an ihrem Terminal (Salvage); alles, was an Bord lagert, bleibt erhalten.

> Einen Neustart der Reise gibt es nicht mehr: Die Zeitschleife ist gestrichen. Wiederbelebt wirst du von Mitspielern. Ist die ganze Landegruppe am Boden, ist die Landung gescheitert.

## Kann ich eine Pause machen?

Der Host speichert die Reise automatisch beim Eintritt in eine Galaxie, wenn die Nostos ausfällt und beim Verlassen der Sitzung. In der Lobby setzt Continue Journey diesen Spielstand fort. Nach der Heimkehr wird er gelöscht.

> Ein Speichern während einer Warpstrecke setzt die Crew beim Fortsetzen vor diese Warpstrecke zurück. Es ist kein freies Quicksave-System.

Weiterlesen: [Deine erste Expedition](first-steps.md)

## Was ist schon spielbar?

Die Anleitungen wurden am 17.10.2026 mit dem lokalen Code und den Projektnotizen abgeglichen. Das ist kein neuer kompletter Solo- oder Multiplayer-Spieltest.

### Jetzt beschrieben

Erkunden, abbauen, looten, kämpfen, handeln, craften, die Nostos nutzen, den Antrieb verbessern, geflogene Sprünge und der finale Flug nach Hause.

### Noch im Ausbau

Story-Intro, der spielbare Prolog und die vollständige Erdlandung sind im dokumentierten Stand noch nicht fertig. Die Hilfen versprechen diese Szenen nicht.

---

Quellenabgleich: 2026-10-17. Kein neuer vollständiger Spieltest.

[RunManager.cs](../../Assets/_Project/Scripts/World/RunManager.cs) · [WarpDrive.cs](../../Assets/_Project/Scripts/World/WarpDrive.cs) · [RunSave.cs](../../Assets/_Project/Scripts/World/RunSave.cs) · [PlayerGear.cs](../../Assets/_Project/Scripts/Inventory/PlayerGear.cs) · [PlayerPawn.cs](../../Assets/_Project/Scripts/Player/PlayerPawn.cs) · [InventoryView.cs](../../Assets/_Project/Scripts/UI/InventoryView.cs)
