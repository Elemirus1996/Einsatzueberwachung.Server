## Wichtig vor dem Update

### Öffentliche Adresse selbst eintragen
- Der Server bringt **keine voreingestellte öffentliche Adresse** mehr mit. Bisher stand dort die Adresse eines bestimmten Staffel-Servers. Wer sie nicht geändert hatte, dessen Team-QR-Codes zeigten auf diesen fremden Server.
- Tragt unter **Einstellungen → Team-Mobile → Basis-URL** die Adresse ein, unter der eure Teams euren Server erreichen (z. B. `https://staffel-musterstadt.mywire.org`). Wer dort schon eine Adresse hat, muss nichts tun.
- Ohne Adresse zeigt der Monitor statt des QR-Codes einen Hinweis.

## Datenschutz

### Keine Schriften und Bibliotheken mehr von fremden Servern
- Schriften (JetBrains Mono, Symbole) und Bibliotheken (Leaflet, Leaflet.draw, Proj4, hls.js) liefert der Server jetzt selbst aus. Bisher lud jeder Browser sie von Google Fonts, jsdelivr, cdnjs und unpkg; dabei gingen die IP-Adressen eurer Nutzer an diese Anbieter.
- Nebeneffekt: Symbole, Schriften und die Kartenbedienung funktionieren auch ohne Internet, z. B. nur im VPN. Die Kartenkacheln selbst kommen weiter aus dem Internet.

### Google-Karten entfernt
- Die Ebenen **„Satellit (Google)“** und **„Hybrid (Google)“** gibt es nicht mehr, weder auf der Karte noch im Kartendruck. Die Kacheln wurden ohne Vertrag direkt von Google geladen, das erlauben die Nutzungsbedingungen von Google nicht.
- Wer eine dieser Ebenen gewählt hatte, sieht stattdessen „Satellit (Esri)“.
