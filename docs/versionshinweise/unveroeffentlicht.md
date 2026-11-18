## Neu

## Datenschutz

### Keine Schriften und Bibliotheken mehr von fremden Servern
- Schriften (JetBrains Mono, Symbole) und Bibliotheken (Leaflet, Leaflet.draw, Proj4, hls.js) liefert der Server jetzt selbst aus. Bisher lud jeder Browser sie von Google Fonts, jsdelivr, cdnjs und unpkg; dabei gingen die IP-Adressen eurer Nutzer an diese Anbieter.
- Nebeneffekt: Symbole, Schriften und die Kartenbedienung funktionieren auch ohne Internet, z. B. nur im VPN. Die Kartenkacheln selbst kommen weiter aus dem Internet.
