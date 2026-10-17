<!-- Generated from Tools/Docs/player_guide.json. Do not edit. -->

# Beute nutzen & besser ausrüsten

[Visuelles Wiki](index.html) | [Alle Kapitel](README.md)

Beute hilft erst, wenn du sie benutzt. Im Tab-Menü sortierst du den Rucksack, verbesserst dein Schiff, stellst Gegenstände her und rüstest den Warp-Antrieb aus.

**Dein Ziel:** Du kannst ein gefundenes Teil einbauen, Material ins Cargo legen und Treibstoff richtig zuordnen.

## Welcher Reiter macht was?

### Inventory · Rucksack & Cargo

Gegenstand anklicken. Je nach Typ erscheinen Use oder Install. To cargo lagert ihn am nahen Schiff ein; To backpack holt ihn wieder heraus.

### Ship · Dein nahes Schiff

Module installieren oder entfernen, mit Scrap reparieren und mit Fuel auftanken. Welches Schiff angezeigt wird, hängt davon ab, worin du sitzt oder welchem du nahe bist.

### Crafting · Herstellen

Ein Rezept zeigt benötigte Materialien und deinen Vorrat. Craft funktioniert, wenn die Zutaten im Rucksack liegen und ein Fabricator in Reichweite ist.

### Warp Drive · Gemeinsamer Antrieb

Hier tauscht ihr Antriebsteile und ladet Exotic Matter in den gemeinsamen Tank der Nostos.

## Diese Vorräte solltest du unterscheiden

| Taste / Begriff | Bedeutung |
| --- | --- |
| Ore / Crystal / Scrap | Baumaterialien für Rezepte; Scrap wird auch für Reparaturen genutzt. |
| Fuel | Normaler Schiffstreibstoff. Im Reiter Ship über Refuel nachfüllen. |
| Exotic Matter | Treibstoff für den Warp zwischen Galaxien. Im Reiter Warp Drive ausdrücklich in den Tank laden. |
| Salvage | Währung für Handel und Schiffsservice während des Runs. |
| Memory fragments | Fortschritt über den einzelnen Run hinaus; im Hangar für Freischaltungen nutzbar. |

## So verkürzt du den Heimweg

1. **Ein Antriebsteil finden oder kaufen.** Du suchst Warp Core, Nav Computer, Cooling und Exotic Tank. Kerne gibt es besonders in Warden-Enklaven, andere Teile auch in Kisten, Wracks oder bei Händlern.

2. **Mit dem Teil zur Nostos zurückkehren.** Nimm es in den Rucksack, setze dich in die Nostos oder gehe an Bord und nutze den Antrieb im Maschinenraum. In einer laufenden Warpstrecke kannst du keine Teile tauschen.

3. **Tab → Warp Drive öffnen.** Vergleiche die angezeigte Home ETA mit dem gefundenen Teil. Klicke Install beim gewünschten Teil. Das alte Teil erhältst du zurück.

4. **Die Komponenten zusammen verbessern.** Core bestimmt die mögliche Geschwindigkeit. Nav bestimmt den sicheren Schub in Feldern, Cooling den dauerhaft möglichen Schub. Ein großer Tank ermöglicht längere Warps, macht euch allein aber nicht schneller.

**Das solltest du sehen:** Die Home ETA zeigt den Effekt eurer Ausrüstung. Ein schneller Kern lässt sich mit passender Navigation und Kühlung besser nutzen.

## Exotic Matter ist im Rucksack – reicht das?

1. **Zur Nostos gehen.** Zum Laden des gemeinsamen Antriebs musst du an oder in der Nostos sein.

2. **Den Tank wirklich befüllen.** Tab → Warp Drive → Load Exotic Matter. Die Zahl im Tank steigt, dein Rucksackvorrat sinkt.

3. **Vor dem Galaxienwechsel prüfen.** Vergleiche den Tankinhalt mit next warp needs. Ist der Tank selbst zu klein, brauchst du einen größeren Exotic Tank.

> Cargo, Rucksack und Warp-Tank sind verschiedene Aufbewahrungsorte. Material im Cargo musst du für diese Aktionen zuerst in den Rucksack holen.

Weiterlesen: [Weiterreisen & nach Hause kommen](travel.md) · [Ich komme gerade nicht weiter](help.md)

## Handeln & reparieren

Gehe an einer Station zum Handelsterminal, schaue darauf und drücke F bei Trade. Du kannst Waren kaufen, Rucksackgegenstände verkaufen und den angebotenen Schiffsservice gegen Salvage nutzen. Alternativ reparierst du das nahe Schiff im Reiter Ship mit Scrap und tankst mit Fuel nach.

---

Quellenabgleich: 2026-10-17. Kein neuer vollständiger Spieltest.

[InventoryView.cs](../../Assets/_Project/Scripts/UI/InventoryView.cs) · [WarpDrive.cs](../../Assets/_Project/Scripts/World/WarpDrive.cs) · [TradeView.cs](../../Assets/_Project/Scripts/UI/TradeView.cs) · [PlayerGear.cs](../../Assets/_Project/Scripts/Inventory/PlayerGear.cs) · [Meta.cs](../../Assets/_Project/Scripts/Progression/Meta.cs)
