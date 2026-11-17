using System.Collections.ObjectModel;
using System.Text.Json;
using RaamTekenaar.Core;

// Gebruik: dotnet run -- <uitvoer.json>
// Schrijft per proefpagina de paginagegevens en de tekenopdrachten weg.
string uitvoer = args.Length > 0 ? args[0] : "proef.json";

var raamA = new RaamType
{
    Naam = "K01", Breedte = 1800, Hoogte = 1600, Borstwering = 900,
    BovenlichtDagmaat = 400, BovenlichtType = VakType.KiepScharnierOnder,
    Vakken = new ObservableCollection<Vak>
    {
        new() { Type = VakType.Vast },
        new() { Type = VakType.DraaiKiepScharnierRechts }
    }
};
var raamB = new RaamType
{
    Naam = "K02", Breedte = 2400, Hoogte = 1400, Borstwering = 600, PositieInMuur = 60,
    Vakken = new ObservableCollection<Vak>
    {
        new() { Type = VakType.DraaiScharnierLinks, Breedte = 600, NaarBinnen = false },
        new() { Type = VakType.Vast },
        new() { Type = VakType.DraaiScharnierRechts, Breedte = 600, NaarBinnen = false }
    }
};
var ramen = new Dictionary<Guid, RaamType> { [raamA.Id] = raamA, [raamB.Id] = raamB };

// Controle-uitvoer
foreach (var r in ramen.Values)
{
    var maten = RaamGeometrie.VakDagmaten(r, out _);
    Console.WriteLine($"{r.Naam}: dagmaten {string.Join(" / ", maten.Select(m => m.ToString("0.#")))}; meldingen: {string.Join(" | ", RaamGeometrie.Controleer(r))}");
}

double s50 = Schaal.NaarMmPerPt(50);
double wand = 300 / s50; // 300 mm muur in pt bij 1:50

var paginas = new List<object>();

void Pagina(string naam, double[] media, double[]? crop, int rotate, params Plaatsing[] pl)
{
    var info = PaginaInfo.Maak(media, crop, rotate);
    paginas.Add(new
    {
        naam, media, crop, rotate,
        weergave = new[] { info.Breedte, info.Hoogte },
        ankers = pl.Select(p => new[] { p.X, p.Y }).ToArray(),
        ankersGebruiker = pl.Select(p => { var u = info.NaarGebruiker(new Pt(p.X, p.Y)); return new[] { u.X, u.Y }; }).ToArray(),
        inhoud = PdfInhoud.Bouw(pl, ramen, info)
    });
}

Plaatsing P(RaamType r, Aanzicht a, double x, double y, int rot = 0, bool spiegel = false, double muur = 0) =>
    new() { RaamId = r.Id, Aanzicht = a, X = x, Y = y, Rotatie = rot, Gespiegeld = spiegel, Muurdikte = muur, MmPerPt = s50 };

// 1: A3 liggend, geen rotatie. Gevel met vloerpeil op y=600, plattegrondmuur met buitenkant op y=780.
Pagina("A3 liggend", new[] { 0, 0, 1190.55, 841.89 }, null, 0,
    P(raamA, Aanzicht.Voor, 150, 600),
    P(raamB, Aanzicht.Voor, 400, 600),
    P(raamA, Aanzicht.Zij, 750, 600),
    P(raamB, Aanzicht.Zij, 900, 600, spiegel: true),
    P(raamA, Aanzicht.Boven, 150, 780, muur: 300),
    P(raamB, Aanzicht.Boven, 400, 780, muur: 300));

// 2: A4 staand met /Rotate 90 -> liggend weergegeven
Pagina("Rotate 90", new[] { 0, 0, 595.28, 841.89 }, null, 90,
    P(raamA, Aanzicht.Voor, 100, 450));

// 3: verschoven MediaBox met kleinere CropBox
Pagina("CropBox", new[] { 100, 100, 695.28, 941.89 }, new double[] { 150, 150, 645.28, 891.89 }, 0,
    P(raamA, Aanzicht.Voor, 50, 400));

// 4: /Rotate 270 met geroteerde en gespiegelde plattegrond (muur verticaal, buitenkant links op x=200)
Pagina("Rotate 270", new[] { 0, 0, 595.28, 841.89 }, null, 270,
    P(raamB, Aanzicht.Boven, 200, 450, rot: 270, spiegel: true, muur: 300));

File.WriteAllText(uitvoer, JsonSerializer.Serialize(new { wand, paginas }, new JsonSerializerOptions { WriteIndented = true }));

// Opslaan en weer laden van een project moet dezelfde tekenopdrachten opleveren
var project = new Project { Ramen = new ObservableCollection<RaamType>(ramen.Values) };
project.Plaatsingen.Add(P(raamA, Aanzicht.Voor, 150, 600));
var terug = ProjectOpslag.VanJson(ProjectOpslag.NaarJson(project));
var info1 = PaginaInfo.Maak(new[] { 0, 0, 1190.55, 841.89 }, null, 0);
bool gelijk = PdfInhoud.Bouw(project.Plaatsingen, project.Ramen.ToDictionary(r => r.Id), info1)
           == PdfInhoud.Bouw(terug.Plaatsingen, terug.Ramen.ToDictionary(r => r.Id), info1);
Console.WriteLine($"Project opslaan/laden geeft dezelfde tekening: {gelijk}");
Console.WriteLine($"Geschreven: {uitvoer}");
