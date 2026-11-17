# Raam Tekenaar

Windows-programma om ramen (kozijnen) in een PDF-bouwtekening te tekenen. Je voert de raamgegevens één keer in en plaatst het raam daarna in het vooraanzicht, zijaanzicht en bovenaanzicht. Het programma slaat een **kopie** van de PDF op met de ramen als echte vectorlijnen. Het origineel blijft ongewijzigd.

## Installeren

Je hoeft niets te installeren. Download `RaamTekenaar.exe` (zie *Bouwen*) en start het programma. Het werkt op Windows 10 en 11.

## Werkwijze

1. **📂 PDF openen**: kies de bouwtekening. Scroll om te zoomen, sleep met de rechter- of middelste muisknop om te schuiven.
2. **Schaal instellen**: vul rechtsboven de schaal in (bijvoorbeeld 1:50), óf gebruik **📏 Kalibreren**. Klik dan twee punten aan waarvan je de afstand weet (bijvoorbeeld een maatlijn) en vul de werkelijke maat in mm in. Kalibreren is het nauwkeurigst. Elk aanzicht kan een eigen schaal hebben.
3. **Raamtype invullen** (links):
   - breedte, hoogte, borstwering (vloerpeil tot onderkant kozijn)
   - profielbreedte (stijlen/dorpels), vleugelbreedte, kozijndiepte, positie in de muur
   - bovenlicht (dagmaat, type, naar binnen)
   - vakken van links naar rechts: type (vast, draai, kiep, val, draai-kiep met scharnierzijde), breedte (0 = automatisch) en naar binnen draaiend
   Het voorbeeld eronder laat direct het vooraanzicht zien.
4. **Plaatsen** (rechts): kies het aanzicht, klik op **➕ Plaats raam op tekening** en klik op het referentiepunt:
   - *Vooraanzicht*: het vloerpeil, recht onder de linkerkant van het kozijn
   - *Zijaanzicht*: het vloerpeil, aan de buitenkant van de gevel
   - *Bovenaanzicht*: de buitenkant van de gevel, bij de linkerkant van het kozijn
   Met **Draaien** en **Spiegelen** zet je het raam goed voor elke gevel. *Onderliggende lijnen wit afdekken* verbergt de lijnen van de tekening onder het raam. In het bovenaanzicht tekent het programma met *Muurdikte* ook de sparing en de dagkanten.
5. **Bijstellen**: klik op een geplaatst raam om het te selecteren. Je kunt het verslepen, verschuiven met de pijltjestoetsen of de knoppen (stap in werkelijke mm, Shift = 10×) en de eigenschappen aanpassen. Een wijziging in het raamtype past automatisch alle aanzichten aan. Ctrl+Z maakt het plaatsen en verschuiven ongedaan, Delete verwijdert het raam.
6. **💾 PDF met ramen opslaan**: slaat de kopie op als `<naam>_ramen.pdf`.
7. **Project opslaan** (`.raamproj`): bewaart de raamtypes en plaatsingen, zodat je later verder kunt.

### Tekenwijze

- Lijndiktes op papier: kozijn 0,35 mm, dagmaten 0,25 mm, vleugels en symbolen 0,18 mm, glas 0,13 mm.
- Draairichting in het vooraanzicht: de punt van de driehoek wijst naar de scharnierzijde. Naar binnen draaiend wordt gestippeld, naar buiten draaiend doorgetrokken.
- Op het scherm zijn geplaatste ramen blauw (geselecteerd: oranje). In de opgeslagen PDF zijn ze zwart.

## Bouwen

- **Visual Studio / MSBuild**: open `RaamTekenaar.sln` en bouw `RaamTekenaar.App`.
- **Eén exe**: `dotnet publish src/RaamTekenaar.App/RaamTekenaar.App.csproj -c Release -r win-x64 -o publish`
- **GitHub**: elke push naar `main` bouwt de exe via *Actions → Bouwen*. Je downloadt hem onder *Artifacts*.

## Opbouw

| Map | Inhoud |
|---|---|
| `src/RaamTekenaar.Core` | Model, raamgeometrie per aanzicht, schaal/rotatie-omrekening, PDF-export (PDFsharp) |
| `src/RaamTekenaar.App` | WPF-programma; PDF-weergave met de ingebouwde Windows PDF-renderer |
| `tests/RaamTekenaar.Proef` | Proef van de rekenkern zonder PDFsharp; `tests/maak_proef_pdf.py` zet de uitvoer in proef-PDF's (ook gedraaide pagina's en CropBox) |

Het programma maakt geen logbestanden.
