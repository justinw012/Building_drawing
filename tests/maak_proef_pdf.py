"""Maakt proef-PDF's van de uitvoer van RaamTekenaar.Proef en zet de raam-tekenopdrachten erin.

Gebruik: python3 maak_proef_pdf.py proef.json uitvoer.pdf

Elke pagina krijgt:
- een rood kruisje op elk referentiepunt (berekend in weergavecoördinaten -> gebruikersruimte),
- op pagina 1 een vloerlijn (y=600) en een muur in plattegrond (buitenkant y=780),
- daarna, net als in het programma, "q" vooraf en de raamopdrachten achteraf.
"""
import json
import sys

import pikepdf


def main(invoer: str, uitvoer: str) -> None:
    data = json.load(open(invoer, encoding="utf-8"))
    wand = data["wand"]
    pdf = pikepdf.new()

    for i, p in enumerate(data["paginas"]):
        x0, y0, x1, y1 = p["media"]
        achtergrond = []
        # grijze rand van de MediaBox
        achtergrond.append(f"0.7 G 1 w {x0+2} {y0+2} {x1-x0-4} {y1-y0-4} re S")
        if i == 0:
            # Rotate 0, origin 0: weergave y -> gebruiker y = hoogte - y
            h = y1
            achtergrond.append(f"0 G 1 w 20 {h-600} m 1170 {h-600} l S")  # vloerpeil gevel
            for y in (780, 780 - wand):  # plattegrondmuur
                achtergrond.append(f"0 G 0.8 w 20 {h-y} m 1170 {h-y} l S")
            # arcering in de muur, moet bij de ramen weggevallen zijn
            x = 20
            while x < 1170:
                achtergrond.append(f"0.4 G 0.3 w {x} {h-780} m {x+wand} {h-780+wand} l S")
                x += 6
        for ux, uy in p["ankersGebruiker"]:
            achtergrond.append(f"1 0 0 RG 0.5 w {ux-6} {uy} m {ux+6} {uy} l {ux} {uy-6} m {ux} {uy+6} l S")

        # Bestaande inhoud laat expres een gewijzigde grafische staat achter (dikke rode lijn, cm):
        achtergrond.append("1 0 0 RG 5 w 2 0 0 2 0 0 cm")

        page = pikepdf.Dictionary(
            Type=pikepdf.Name.Page,
            MediaBox=p["media"],
            Rotate=p["rotate"],
            Resources=pikepdf.Dictionary(),
        )
        if p["crop"]:
            page.CropBox = p["crop"]
        page = pdf.make_indirect(page)
        voor = pdf.make_stream(b"q\n")
        oud = pdf.make_stream("\n".join(achtergrond).encode() + b"\n")
        raam = pdf.make_stream(p["inhoud"].encode("ascii"))
        page.Contents = pikepdf.Array([voor, oud, raam])
        pdf.pages.append(pikepdf.Page(page))

    pdf.save(uitvoer)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
