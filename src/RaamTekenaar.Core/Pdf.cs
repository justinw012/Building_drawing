using System.Text;
using System.Globalization;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace RaamTekenaar.Core;

/// <summary>Het deel dat PDFsharp gebruikt: pagina's uitlezen en ramen in een kopie van de PDF schrijven.</summary>
public static class PdfBestand
{
    public static List<PaginaInfo> LeesPaginas(string pad)
    {
        using var doc = PdfReader.Open(pad, PdfDocumentOpenMode.Import);
        var lijst = new List<PaginaInfo>();
        for (int i = 0; i < doc.PageCount; i++) lijst.Add(Info(doc.Pages[i]));
        return lijst;
    }

    /// <summary>Schrijft een kopie van <paramref name="invoer"/> met alle geplaatste ramen als vectorlijnen.</summary>
    public static int Exporteer(string invoer, string uitvoer, Project project)
    {
        if (string.Equals(Path.GetFullPath(invoer), Path.GetFullPath(uitvoer), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Kies een andere bestandsnaam: het origineel wordt niet overschreven.");

        var ramen = project.Ramen.ToDictionary(r => r.Id);
        int aantal = 0;

        using var doc = PdfReader.Open(invoer, PdfDocumentOpenMode.Modify);
        foreach (var groep in project.Plaatsingen.GroupBy(p => p.Pagina))
        {
            if (groep.Key < 0 || groep.Key >= doc.PageCount) continue;
            var lijst = groep.Where(p => ramen.ContainsKey(p.RaamId)).ToList();
            if (lijst.Count == 0) continue;

            var page = doc.Pages[groep.Key];
            string inhoud = PdfInhoud.Bouw(lijst, ramen, Info(page));
            page.Contents.PrependContent().CreateStream(Encoding.ASCII.GetBytes(PdfInhoud.Voorloop));
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(inhoud));
            aantal += lijst.Count;
        }

        doc.Save(uitvoer);
        return aantal;
    }

    private static PaginaInfo Info(PdfPage page)
    {
        var media = LeesRechthoek(page, "/MediaBox");
        if (media == null)
        {
            var mb = page.MediaBox;
            media = new[] { mb.X1, mb.Y1, mb.X2, mb.Y2 };
        }
        var crop = LeesRechthoek(page, "/CropBox");
        var rot = LeesGeerfd(page, "/Rotate");
        int rotatie = rot switch
        {
            PdfInteger i => i.Value,
            PdfReal r => (int)Math.Round(r.Value),
            null => 0,
            _ => int.TryParse(rot.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0
        };
        return PaginaInfo.Maak(media, crop, rotatie);
    }

    /// <summary>Zoekt een (overerfbare) waarde op de pagina of in de bovenliggende paginaboom.</summary>
    private static PdfItem? LeesGeerfd(PdfPage page, string sleutel)
    {
        PdfDictionary? d = page;
        for (int diepte = 0; d != null && diepte < 32; diepte++)
        {
            var item = d.Elements.GetValue(sleutel);
            if (item is PdfReference rf) item = rf.Value;
            if (item != null) return item;
            d = d.Elements.GetValue("/Parent") switch
            {
                PdfDictionary pd => pd,
                PdfReference pr => pr.Value as PdfDictionary,
                _ => null
            };
        }
        return null;
    }

    private static double[]? LeesRechthoek(PdfPage page, string sleutel)
    {
        if (LeesGeerfd(page, sleutel) is not PdfArray a || a.Elements.Count != 4) return null;
        var w = new double[4];
        for (int i = 0; i < 4; i++)
        {
            var item = a.Elements[i];
            if (item is PdfReference rf) item = rf.Value;
            w[i] = item switch
            {
                PdfInteger n => n.Value,
                PdfReal r => r.Value,
                _ => double.TryParse(item?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN
            };
            if (double.IsNaN(w[i])) return null;
        }
        return w;
    }
}
