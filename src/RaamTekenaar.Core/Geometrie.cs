namespace RaamTekenaar.Core;

public readonly record struct Pt(double X, double Y);

/// <summary>Een tekenelement in lokale raamcoördinaten (werkelijke mm, x naar rechts, y omhoog).</summary>
public sealed class Prim
{
    public required List<Pt> Punten { get; init; }
    public bool Gesloten { get; init; }
    public bool WitVullen { get; init; }
    public bool Lijn { get; init; } = true;
    /// <summary>Lijndikte op papier in mm.</summary>
    public double DikteMm { get; init; }
    public bool Gestreept { get; init; }
}

public static class Lijndikte
{
    public const double Kozijn = 0.35;
    public const double Opening = 0.25;
    public const double Vleugel = 0.18;
    public const double Symbool = 0.18;
    public const double Glas = 0.13;
}

public static class RaamGeometrie
{
    public static List<Prim> Bouw(RaamType r, Aanzicht a, bool afdekken, double muurdikte, double afdekMarge) => a switch
    {
        Aanzicht.Voor => Voor(r, afdekken),
        Aanzicht.Zij => Zij(r, afdekken),
        _ => Boven(r, afdekken, muurdikte, afdekMarge)
    };

    public static IReadOnlyList<Vak> EffectieveVakken(RaamType r) =>
        r.Vakken.Count > 0 ? r.Vakken.ToList() : new List<Vak> { new() { Type = VakType.Vast } };

    /// <summary>Dagmaten van de vakken van links naar rechts.</summary>
    public static double[] VakDagmaten(RaamType r, out string? waarschuwing)
    {
        var vakken = EffectieveVakken(r);
        int n = vakken.Count;
        double beschikbaar = r.Breedte - (n + 1) * r.ProfielBreedte;
        double vast = vakken.Where(v => v.Breedte > 0).Sum(v => v.Breedte);
        int auto = vakken.Count(v => v.Breedte <= 0);
        double autoBreedte = auto > 0 ? (beschikbaar - vast) / auto : 0;

        waarschuwing = null;
        if (auto == 0 && Math.Abs(vast - beschikbaar) > 0.5)
            waarschuwing = $"Vakbreedtes samen {vast:0} mm, beschikbaar is {beschikbaar:0} mm (kozijnbreedte min {n + 1} stijlen).";
        else if (auto > 0 && autoBreedte <= 0)
            waarschuwing = "De ingevulde vakbreedtes zijn te groot voor de kozijnbreedte.";

        return vakken.Select(v => v.Breedte > 0 ? v.Breedte : Math.Max(autoBreedte, 0)).ToArray();
    }

    public static List<string> Controleer(RaamType r)
    {
        var lijst = new List<string>();
        if (r.Breedte <= 0 || r.Hoogte <= 0) lijst.Add("Breedte en hoogte moeten groter dan 0 zijn.");
        if (r.ProfielBreedte < 0 || r.VleugelBreedte < 0 || r.Diepte <= 0) lijst.Add("Profielmaten en diepte moeten positief zijn.");
        double hoofdHoogte = HoofdBoven(r) - (r.Borstwering + r.ProfielBreedte);
        if (hoofdHoogte <= 0) lijst.Add("Het kozijn is te laag voor profielen en bovenlicht.");
        var maten = VakDagmaten(r, out var w);
        if (w != null) lijst.Add(w);
        var vakken = EffectieveVakken(r);
        for (int i = 0; i < vakken.Count; i++)
            if (vakken[i].Type != VakType.Vast && (maten[i] <= 2 * r.VleugelBreedte || hoofdHoogte <= 2 * r.VleugelBreedte))
                lijst.Add($"Vak {i + 1} is te klein voor de vleugelbreedte.");
        return lijst;
    }

    /// <summary>Bovenkant van de dagmaat van de hoofdvakken (absolute y, mm vanaf vloerpeil).</summary>
    private static double HoofdBoven(RaamType r) =>
        r.BovenlichtDagmaat > 0
            ? r.Borstwering + r.Hoogte - 2 * r.ProfielBreedte - r.BovenlichtDagmaat
            : r.Borstwering + r.Hoogte - r.ProfielBreedte;

    // ---------- Vooraanzicht: x = langs de gevel, y = hoogte vanaf vloerpeil ----------
    private static List<Prim> Voor(RaamType r, bool afdekken)
    {
        var l = new List<Prim>();
        double W = r.Breedte, H = r.Hoogte, B = r.Borstwering, P = r.ProfielBreedte;
        if (afdekken) l.Add(Wit(0, B, W, B + H));
        l.Add(Rechthoek(0, B, W, B + H, Lijndikte.Kozijn));

        double yOnder = B + P, yBoven = HoofdBoven(r);
        var vakken = EffectieveVakken(r);
        var maten = VakDagmaten(r, out _);
        double x = P;
        for (int i = 0; i < vakken.Count; i++)
        {
            VakVoor(l, x, yOnder, x + maten[i], yBoven, vakken[i].Type, vakken[i].NaarBinnen, r.VleugelBreedte);
            x += maten[i] + P;
        }

        if (r.BovenlichtDagmaat > 0)
            VakVoor(l, P, B + H - P - r.BovenlichtDagmaat, W - P, B + H - P, r.BovenlichtType, r.BovenlichtNaarBinnen, r.VleugelBreedte);

        return l;
    }

    private enum Zijde { Links, Rechts, Onder, Boven }

    private static void VakVoor(List<Prim> l, double x0, double y0, double x1, double y1, VakType type, bool binnen, double V)
    {
        if (x1 - x0 <= 0 || y1 - y0 <= 0) return;
        l.Add(Rechthoek(x0, y0, x1, y1, Lijndikte.Opening));
        if (type == VakType.Vast) return;

        double ix0 = x0 + V, iy0 = y0 + V, ix1 = x1 - V, iy1 = y1 - V;
        if (ix1 - ix0 <= 0 || iy1 - iy0 <= 0) { ix0 = x0; iy0 = y0; ix1 = x1; iy1 = y1; }
        else l.Add(Rechthoek(ix0, iy0, ix1, iy1, Lijndikte.Vleugel));

        switch (type)
        {
            case VakType.DraaiScharnierLinks: l.Add(Driehoek(ix0, iy0, ix1, iy1, Zijde.Links, binnen)); break;
            case VakType.DraaiScharnierRechts: l.Add(Driehoek(ix0, iy0, ix1, iy1, Zijde.Rechts, binnen)); break;
            case VakType.KiepScharnierOnder: l.Add(Driehoek(ix0, iy0, ix1, iy1, Zijde.Onder, binnen)); break;
            case VakType.ValScharnierBoven: l.Add(Driehoek(ix0, iy0, ix1, iy1, Zijde.Boven, binnen)); break;
            case VakType.DraaiKiepScharnierLinks:
                l.Add(Driehoek(ix0, iy0, ix1, iy1, Zijde.Links, binnen));
                l.Add(Driehoek(ix0, iy0, ix1, iy1, Zijde.Onder, binnen));
                break;
            case VakType.DraaiKiepScharnierRechts:
                l.Add(Driehoek(ix0, iy0, ix1, iy1, Zijde.Rechts, binnen));
                l.Add(Driehoek(ix0, iy0, ix1, iy1, Zijde.Onder, binnen));
                break;
        }
    }

    /// <summary>Draairichtingssymbool: de punt van de driehoek wijst naar de scharnierzijde.</summary>
    private static Prim Driehoek(double x0, double y0, double x1, double y1, Zijde scharnier, bool gestreept)
    {
        double xm = (x0 + x1) / 2, ym = (y0 + y1) / 2;
        var p = scharnier switch
        {
            Zijde.Links => new List<Pt> { new(x1, y0), new(x0, ym), new(x1, y1) },
            Zijde.Rechts => new List<Pt> { new(x0, y0), new(x1, ym), new(x0, y1) },
            Zijde.Onder => new List<Pt> { new(x0, y1), new(xm, y0), new(x1, y1) },
            _ => new List<Pt> { new(x0, y0), new(xm, y1), new(x1, y0) },
        };
        return new Prim { Punten = p, DikteMm = Lijndikte.Symbool, Gestreept = gestreept };
    }

    // ---------- Zijaanzicht: x = de muur in vanaf buitenkant gevel, y = hoogte vanaf vloerpeil ----------
    private static List<Prim> Zij(RaamType r, bool afdekken)
    {
        var l = new List<Prim>();
        double H = r.Hoogte, B = r.Borstwering, P = r.ProfielBreedte, D = r.Diepte, x0 = r.PositieInMuur, x1 = x0 + D;
        if (afdekken) l.Add(Wit(x0, B, x1, B + H));
        l.Add(Rechthoek(x0, B, x1, B + H, Lijndikte.Kozijn));
        l.Add(Lijn(x0, B + P, x1, B + P, Lijndikte.Opening));
        l.Add(Lijn(x0, B + H - P, x1, B + H - P, Lijndikte.Opening));

        double xm = x0 + D / 2, yBoven = HoofdBoven(r);
        if (yBoven > B + P) l.Add(Lijn(xm, B + P, xm, yBoven, Lijndikte.Glas));
        if (r.BovenlichtDagmaat > 0)
        {
            double yt0 = B + H - 2 * P - r.BovenlichtDagmaat, yt1 = B + H - P - r.BovenlichtDagmaat;
            l.Add(Lijn(x0, yt0, x1, yt0, Lijndikte.Opening));
            l.Add(Lijn(x0, yt1, x1, yt1, Lijndikte.Opening));
            l.Add(Lijn(xm, yt1, xm, B + H - P, Lijndikte.Glas));
        }
        return l;
    }

    // ---------- Bovenaanzicht: x = langs de gevel, y = de muur in vanaf buitenkant gevel ----------
    private static List<Prim> Boven(RaamType r, bool afdekken, double muurdikte, double marge)
    {
        var l = new List<Prim>();
        double W = r.Breedte, D = r.Diepte, y0 = r.PositieInMuur, y1 = y0 + D;
        if (afdekken)
        {
            if (muurdikte > 0) l.Add(Wit(0, -marge, W, muurdikte + marge));
            l.Add(Wit(0, y0, W, y1));
        }
        if (muurdikte > 0)
        {
            l.Add(Lijn(0, 0, 0, muurdikte, Lijndikte.Kozijn));
            l.Add(Lijn(W, 0, W, muurdikte, Lijndikte.Kozijn));
        }
        l.Add(Rechthoek(0, y0, W, y1, Lijndikte.Kozijn));

        var maten = VakDagmaten(r, out _);
        double x = r.ProfielBreedte, ym = y0 + D / 2;
        foreach (var m in maten)
        {
            l.Add(Lijn(x, y0, x, y1, Lijndikte.Opening));
            l.Add(Lijn(x + m, y0, x + m, y1, Lijndikte.Opening));
            if (m > 0) l.Add(Lijn(x, ym, x + m, ym, Lijndikte.Glas));
            x += m + r.ProfielBreedte;
        }
        return l;
    }

    // ---------- hulpjes ----------
    private static Prim Rechthoek(double x0, double y0, double x1, double y1, double dikte) => new()
    {
        Punten = new() { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) },
        Gesloten = true,
        DikteMm = dikte
    };

    private static Prim Wit(double x0, double y0, double x1, double y1) => new()
    {
        Punten = new() { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) },
        Gesloten = true,
        WitVullen = true,
        Lijn = false
    };

    private static Prim Lijn(double x0, double y0, double x1, double y1, double dikte) => new()
    {
        Punten = new() { new(x0, y0), new(x1, y1) },
        DikteMm = dikte
    };
}

public static class Plaatsingstransform
{
    /// <summary>Lokale raamcoördinaat (mm, y omhoog) naar paginacoördinaat (pt, y omlaag).</summary>
    public static Pt NaarPagina(Plaatsing p, Pt lokaal)
    {
        double x = p.Gespiegeld ? -lokaal.X : lokaal.X, y = lokaal.Y;
        (double rx, double ry) = p.Rotatie switch
        {
            90 => (-y, x),
            180 => (-x, -y),
            270 => (y, -x),
            _ => (x, y)
        };
        double k = 1.0 / p.MmPerPt;
        return new Pt(p.X + rx * k, p.Y - ry * k);
    }

    /// <summary>Extra afdekrand van 0,5 mm op papier, omgerekend naar werkelijke mm.</summary>
    public static double AfdekMarge(Plaatsing p) => 0.5 * Schaal.NaarGetal(p.MmPerPt);

    public static List<Prim> Bouw(Plaatsing p, RaamType r) =>
        RaamGeometrie.Bouw(r, p.Aanzicht, p.Afdekken, p.Muurdikte, AfdekMarge(p));
}
