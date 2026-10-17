<!-- Generated from Tools/Docs/player_guide.json. Do not edit. -->

# Nostos, Dart & Zusammenspiel

[Visuelles Wiki](index.html) | [Alle Kapitel](README.md)

Die Nostos bringt die Crew nach Hause. Die kleinen Darts sind eure Erkundungsschiffe. Du kannst zwischen Fliegen, Bordarbeit und Expeditionen wechseln.

**Dein Ziel:** Du findest dich an Bord zurecht und weißt, welches Schiff welche Aufgabe erfüllt.

## Welches Schiff benutze ich wofür?

### Dart · Der kurze Ausflug

Persönliches kleines Schiff zum Erkunden und Kämpfen. Du startest angedockt an der Nostos und kannst dort wieder anlegen.

### Nostos · Gemeinsam weiterreisen

Gemeinsames Heimatschiff mit Brücke, Frachtraum, Fabricator, Kryo-Bucht und Warp-Antrieb. Ihr Pilot löst die Sprünge der Crew aus.

> Eine Dart allein öffnet keine Sprungzone, solange die Nostos vorhanden ist. Für den nächsten System- oder Galaxiensprung setzt sich jemand an ihr Steuer.

## Wo finde ich was an Bord?

### Maschinenraum

Warp-Antrieb und Antriebsterminal, Ingenieurstation sowie hinteres Geschütz.

### Frachtraum

Cargo, Fabricator, Luftschleuse und Zugänge zu den Andockklammern der Darts.

### Kryo-Bucht

Hier wachen gefallene Crewmitglieder wieder auf, solange die Nostos in Betrieb ist.

### Brücke

Am Pilotensitz erscheint Take the helm. Außerdem gibt es ein vorderes Geschütz.

Heck: Maschinenraum → Frachtraum → Kryo-Bucht → Bug: Brücke

## Starten, andocken und ins Innere wechseln

1. **Starten.** Sitze in deiner angedockten Dart und halte W ungefähr eine Sekunde. Sie löst sich von der Klammer. Im Warp kannst du nicht abdocken.

2. **Wieder anlegen.** Fliege langsam an eine freie Andockklammer der Nostos. Bei höchstens etwa 14 m/s Relativgeschwindigkeit und unter etwa 16 m Abstand kann die Klammer die Dart automatisch greifen. Wenn es nicht klappt: abbremsen und vorsichtig neu anfliegen.

3. **An Bord aussteigen.** Während die Dart angedockt ist, führt F aus dem Sitz zum Zugang im Inneren der Nostos. Willst du draußen frei aussteigen, achte vorher auf Geschwindigkeit und Sauerstoff.

## Was können die anderen währenddessen tun?

### Pilot

Steuert die Nostos, fliegt Sprungzonen an und führt die Warpstrecke.

### Geschütz

An einem freien Geschütz mit F Platz nehmen und die Crew gegen Bedrohungen unterstützen.

### Ingenieur & Expedition

Station bedienen, Vorräte organisieren, Teile einbauen oder außerhalb des Warps auf Erkundung gehen. Rollen sind keine dauerhafte Klassenwahl.

**Das solltest du sehen:** Auch allein kannst du Rollen wechseln. Im Co-op hilft es, vor Abflug kurz zu klären, wer fliegt und ob alle wieder an Bord sind.

---

Quellenabgleich: 2026-10-17. Kein neuer vollständiger Spieltest.

[NostosBuilder.cs](../../Assets/_Project/Editor/NostosBuilder.cs) · [ShipSeat.cs](../../Assets/_Project/Scripts/Ship/ShipSeat.cs) · [ShipFlight.cs](../../Assets/_Project/Scripts/Ship/ShipFlight.cs) · [DockClamp.cs](../../Assets/_Project/Scripts/Ship/DockClamp.cs) · [WarpFlight.Jumps.cs](../../Assets/_Project/Scripts/World/WarpFlight.Jumps.cs)
