<!-- Generated from Tools/Docs/player_guide.json. Do not edit. -->

# Erkunden, sammeln & überleben

[Visuelles Wiki](index.html) | [Alle Kapitel](README.md)

Planeten und Wracks liefern Rohstoffe, Ausrüstung und Teile für den Heimweg. Starte mit einem überschaubaren Ausflug und bringe deine Funde sicher zurück.

**Dein Ziel:** Du weißt, welche Ziele sich lohnen, wie du Beute bekommst und wann du umkehren solltest.

## Was kann ich entdecken?

### Planeten & Fundstellen

HUD-Markierungen führen unter anderem zu Ruinen, Absturzstellen, Vorratskisten und Rohstoffvorkommen.

### Wracks & Asteroidenfelder

Suche nach Bergungsgut und Kisten. Langsam anfliegen, bevor du dich zum Erkunden aus dem Schiff wagst.

### Stationen & Nomaden

Dort kannst du handeln und Schiffsservice nutzen. Im ersten System am Wormhole Exit gibt es eine Station.

### Warden-Enklaven

Gefährliche Orte mit besonders wertvollen Antriebskernen. Für einen ersten Lernflug ist ein leichteres Ziel die bessere Übung.

## Ankommen und sicher aussteigen

1. **Vor dem Ziel langsamer werden.** Nutze X und beende schnellen Reiseflug. Halte die Oberfläche im Blick und suche eine freie, ebene Stelle.

2. **Sanft sinken.** Steuere mit kleinen Bewegungen. Die schwere Nostos braucht besonders flachen Boden und eine langsame Landung; die Dart eignet sich für kurze Erkundungsflüge.

3. **Erst dann den Sitz verlassen.** F ist auch im Flug die Aussteigen-Taste. Prüfe deshalb zuerst deine Position und Geschwindigkeit. Behalte im Freien Sauerstoff und Rückweg im Blick.

**Das solltest du sehen:** Zu Fuß kannst du dich einer Fundstelle nähern und die Interaktionshinweise lesen.

## Rohstoffe abbauen und Kisten öffnen

1. **Vorkommen anvisieren.** Gehe nah an Erz, Kristall, Schrott oder eine Treibstoffquelle. Der Hinweis nennt das Material und verweist auf den Drill.

2. **Bohrer benutzen.** Drücke 1. Halte die linke Maustaste auf das Vorkommen, bis du Material erhältst. Der Bohrer ist zum Abbau da, nicht F.

3. **Kisten öffnen.** Schaue aus der Nähe auf eine ungeöffnete Kiste und drücke F bei Open crate. Eine bereits geöffnete Kiste zeigt Empty.

4. **Rucksack prüfen.** Öffne Tab → Inventory. Wenn du am Schiff bist, kannst du Gegenstände auswählen und mit To cargo in den Frachtraum verschieben.

Weiterlesen: [Beute nutzen & besser ausrüsten](equipment.md)

## Luft, Heilung und Gegner

### Luft ist dein Rückweg-Timer

Außerhalb geschützter Bereiche sinkt dein Sauerstoff. Im Schiff oder in einer atembaren Atmosphäre füllt er sich wieder. Warte mit dem Rückflug nicht bis auf null.

### Selbst heilen

Wähle 4 und drücke die linke Maustaste. Das verbraucht ein Medkit aus deinem Rucksack. Ohne Medkit kannst du keines einsetzen.

### Auf Gegner reagieren

Mit 2 wählst du den Blaster. Das Gewehr auf 3 musst du erst herstellen oder kaufen und freischalten. Markiere Bedrohungen mit T für die anderen.

> Du musst nicht jeden Gegner bekämpfen. Wenn Vorräte knapp sind, brich die Expedition ab und kehre zum Schiff zurück.

Weiterlesen: [Ich komme gerade nicht weiter](help.md)

## Vom Orbit bis zur Oberfläche

Die gleiche Welt sieht aus dem Orbit und am Boden sehr unterschiedlich aus. Orientiere dich beim Anflug an der Oberfläche und den HUD-Markierungen; der Kontaktbogen zeigt die unterschiedlichen Betrachtungsabstände.

![Mehrere Planetentypen jeweils aus Orbit, Anflug und am Boden](../../Docs/screenshots/issue-115/provinces_all_biomes.jpg)

Entwicklungsaufnahmen aus #115: jede Zeile eine Welt, von links nach rechts näher an der Oberfläche. Die Bilder zeigen Landschaften, keine garantierten Fundorte.

---

Quellenabgleich: 2026-10-17. Kein neuer vollständiger Spieltest.

[ResourceNode.cs](../../Assets/_Project/Scripts/Inventory/ResourceNode.cs) · [LootCrate.cs](../../Assets/_Project/Scripts/Inventory/LootCrate.cs) · [PlayerPawn.cs](../../Assets/_Project/Scripts/Player/PlayerPawn.cs) · [ShipFlight.cs](../../Assets/_Project/Scripts/Ship/ShipFlight.cs) · [SystemDirector.cs](../../Assets/_Project/Scripts/World/SystemDirector.cs)
