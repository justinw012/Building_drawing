using System.Globalization;
using System.Text;

namespace RaamTekenaar.Core;

/// <summary>
/// Afmetingen van een pagina zoals weergegeven (CropBox, met /Rotate toegepast) en de omrekening
/// van weergavecoördinaten (pt, linksboven, y omlaag) naar PDF-gebruikerscoördinaten.
/// </summary>
public sealed class PaginaInfo
{
    public double X0 { get; init; }
    public double Y0 { get; init; }
    public double X1 { get; init; }
    public double Y1 { get; init; }
    /// <summary>0, 90, 180 of 270 (met de klok mee, zoals /Rotate in PDF).</summary>
    public int Rotatie { get; init; }

    public double Breedte => Rotatie is 90 or 270 ? Y1 - Y0 : X1 - X0;
    public double Hoogte => Rotatie is 90 or 270 ? X1 - X0 : Y1 - Y0;

    public Pt NaarGebruiker(Pt d) => Rotatie switch
    {
        90 => new Pt(X0 + d.Y, Y0 + d.X),
        180 => new Pt(X1 - d.X, Y0 + d.Y),
        270 => new Pt(X1 - d.Y, Y1 - d.X),
        _ => new Pt(X0 + d.X, Y1 - d.Y)
    };

    /// <summary>Bepaalt de zichtbare pagina uit MediaBox, CropBox en /Rotate.</summary>
    public static PaginaInfo Maak(double[] media, double[]? crop, int rotate)
    {
        double mx0 = Math.Min(media[0], media[2]), mx1 = Math.Max(media[0], media[2]);
        double my0 = Math.Min(media[1], media[3]), my1 = Math.Max(media[1], media[3]);
        double x0 = mx0, y0 = my0, x1 = mx1, y1 = my1;
        if (crop is { Length: 4 })
        {
            x0 = Math.Max(x0, Math.Min(crop[0], crop[2]));
            y0 = Math.Max(y0, Math.Min(crop[1], crop[3]));
            x1 = Math.Min(x1, Math.Max(crop[0], crop[2]));
            y1 = Math.Min(y1, Math.Max(crop[1], crop[3]));
            if (x1 <= x0 || y1 <= y0) { x0 = mx0; y0 = my0; x1 = mx1; y1 = my1; }
        }
        int rot = ((rotate % 360) + 360) % 360;
        if (rot % 90 != 0) rot = 0;
        return new PaginaInfo { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Rotatie = rot };
    }
}

/// <summary>Maakt de PDF-tekenopdrachten (content stream) voor de ramen op één pagina.</summary>
public static class PdfInhoud
{
    /// <summary>
    /// Opdrachten die ná de bestaande pagina-inhoud komen. De bestaande inhoud moet vooraf
    /// met "q" worden geopend; hier wordt die eerst met "Q" afgesloten.
    /// </summary>
    public static string Bouw(IEnumerable<Plaatsing> plaatsingen, IReadOnlyDictionary<Guid, RaamType> ramen, PaginaInfo info)
    {
        var sb = new StringBuilder();
        sb.Append("Q\nq\n1 J 1 j 0 G 1 g\n");
        foreach (var pl in plaatsingen)
        {
            if (!ramen.TryGetValue(pl.RaamId, out var raam)) continue;
            foreach (var prim in Plaatsingstransform.Bouw(pl, raam))
                SchrijfPrim(sb, prim, pl, info);
        }
        sb.Append("Q\n");
        return sb.ToString();
    }

    public const string Voorloop = "q\n";

    private static void SchrijfPrim(StringBuilder sb, Prim prim, Plaatsing pl, PaginaInfo info)
    {
        if (prim.Punten.Count < 2) return;
        var pts = prim.Punten.Select(lp => info.NaarGebruiker(Plaatsingstransform.NaarPagina(pl, lp))).ToList();

        if (prim.WitVullen)
        {
            sb.Append("1 g\n");
            Pad(sb, pts, true);
            sb.Append("f\n");
        }

        if (prim.Lijn)
        {
            sb.Append("0 G ").Append(G(Schaal.PapierMmNaarPt(prim.DikteMm))).Append(" w ");
            sb.Append(prim.Gestreept
                ? $"[{G(Schaal.PapierMmNaarPt(1.5))} {G(Schaal.PapierMmNaarPt(1.0))}] 0 d\n"
                : "[] 0 d\n");
            Pad(sb, pts, prim.Gesloten);
            sb.Append("S\n");
        }
    }

    private static void Pad(StringBuilder sb, List<Pt> pts, bool gesloten)
    {
        sb.Append(G(pts[0].X)).Append(' ').Append(G(pts[0].Y)).Append(" m\n");
        for (int i = 1; i < pts.Count; i++)
            sb.Append(G(pts[i].X)).Append(' ').Append(G(pts[i].Y)).Append(" l\n");
        if (gesloten) sb.Append("h\n");
    }

    private static string G(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
