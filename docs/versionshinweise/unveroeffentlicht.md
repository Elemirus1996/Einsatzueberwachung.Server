## Wichtig vor dem Update

### Öffentliche Adresse selbst eintragen
- Der Server bringt **keine voreingestellte öffentliche Adresse** mehr mit. Bisher stand dort die Adresse eines bestimmten Staffel-Servers. Wer sie nicht geändert hatte, dessen Team-QR-Codes zeigten auf diesen fremden Server.
- Tragt unter **Einstellungen → Team-Mobile → Basis-URL** die Adresse ein, unter der eure Teams euren Server erreichen (z. B. `https://staffel-musterstadt.mywire.org`). Wer dort schon eine Adresse hat, muss nichts tun.
- Ohne Adresse zeigt der Monitor statt des QR-Codes einen Hinweis.

## Neu

### Amtliche Luftbilder der Länder
- Neue Kartenebene **„Luftbild (Länder)“** auf der Karte, im Kartendruck und im Einsatzbericht: die amtlichen Luftbilder (20 cm Auflösung) der Landesvermessungen von **14 Bundesländern** – alle außer Hamburg und Sachsen-Anhalt, deren Dienste noch nicht passen.
- Alle Länder bilden **eine** Ebene. An Landesgrenzen erscheinen die Bilder der Nachbarländer automatisch mit, ihr könnt also im Ernstfall länderübergreifend arbeiten. Geladen werden nur die Länder im sichtbaren Ausschnitt.
- Die Luftbilder sind Open Data der Länder, ohne Konto und ohne Kosten. Der vorgeschriebene Quellenvermerk steht unten in der Karte und im Bericht, jeweils nur für die Länder im Ausschnitt.
- An Landesgrenzen können Bilder aus verschiedenen Jahren und Jahreszeiten aneinanderstoßen.

### Esri-Satellitenbilder nur noch mit eigenem Schlüssel
- „Satellit (Esri)“ gibt es nur noch, wenn eure Staffel einen **eigenen, kostenlosen Esri-Schlüssel** einträgt: **Einstellungen → Einsatzbetrieb → Satellitenbilder (Esri)**. Dort steht Schritt für Schritt, wie ihr ihn bei Esri bekommt, und ein Knopf „Prüfen“ testet ihn.
- Bisher wurden die Esri-Bilder ohne Konto geladen; ob das erlaubt ist, ist nicht geklärt.
- Ohne Schlüssel bekommt, wer bisher „Satellit (Esri)“ gewählt hatte, die Luftbilder der Länder. Die Esri-Beschriftungsebene („Hybrid“ in der mobilen Karte) entfällt.

### Karten nach den Regeln von OpenStreetMap und OpenTopoMap
- Kacheln von OpenStreetMap kommen nur noch von `tile.openstreetmap.org` (die alten Adressen a/b/c.tile… können wegfallen).
- Quellenvermerke wie von OpenStreetMap und OpenTopoMap verlangt, mit Link; auch die kleine Übersichtskarte zeigt ihn jetzt.
- Der Knopf **„Für Funkloch sichern“** auf der Karte entfällt. OpenStreetMap verbietet inzwischen das Vorabladen von Kartenkacheln. Bereits angesehene Kartenausschnitte bleiben weiter auf dem Gerät verfügbar.

## Datenschutz

### Keine Schriften und Bibliotheken mehr von fremden Servern
- Schriften (JetBrains Mono, Symbole) und Bibliotheken (Leaflet, Leaflet.draw, Proj4, hls.js) liefert der Server jetzt selbst aus. Bisher lud jeder Browser sie von Google Fonts, jsdelivr, cdnjs und unpkg; dabei gingen die IP-Adressen eurer Nutzer an diese Anbieter.
- Nebeneffekt: Symbole, Schriften und die Kartenbedienung funktionieren auch ohne Internet, z. B. nur im VPN. Die Kartenkacheln selbst kommen weiter aus dem Internet.

### Google-Karten entfernt
- Die Ebenen **„Satellit (Google)“** und **„Hybrid (Google)“** gibt es nicht mehr, weder auf der Karte noch im Kartendruck. Die Kacheln wurden ohne Vertrag direkt von Google geladen, das erlauben die Nutzungsbedingungen von Google nicht.
- Wer eine dieser Ebenen gewählt hatte, sieht stattdessen „Luftbild (Länder)“.

## Sicherheit

### Updates werden vor der Installation geprüft
- Ab dieser Version installiert der Server ein Update nur, wenn das Paket die **digitale Signatur der Entwickler** trägt und die Prüfsumme stimmt. Ein verändertes oder untergeschobenes Paket wird abgelehnt; das steht dann unter Einstellungen → System → Updates und im Audit-Log.
- Das Update auf diese Version selbst wird noch nicht so geprüft, erst die folgenden.
