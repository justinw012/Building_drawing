using RaamTekenaar.Core;

// Gebruik: dotnet run -- <project.raamproj> <uitvoer.pdf>
// Leest de PDF uit het project, toont de pagina-afmetingen en exporteert de ramen.
if (args.Length < 2)
{
    Console.WriteLine("Gebruik: <project.raamproj> <uitvoer.pdf>");
    return 2;
}

try
{
    var project = ProjectOpslag.Laden(args[0]);
    var pdf = project.PdfPad ?? throw new InvalidOperationException("Project bevat geen PDF-pad.");

    var paginas = PdfBestand.LeesPaginas(pdf);
    for (int i = 0; i < paginas.Count; i++)
    {
        var p = paginas[i];
        Console.WriteLine($"Pagina {i + 1}: {p.Breedte:0.##} x {p.Hoogte:0.##} pt, rotatie {p.Rotatie}");
    }

    int n = PdfBestand.Exporteer(pdf, args[1], project);
    Console.WriteLine($"Geëxporteerd: {n} plaatsingen");

    // Het resultaat moet weer leesbaar zijn
    Console.WriteLine($"Opnieuw gelezen: {PdfBestand.LeesPaginas(args[1]).Count} pagina's");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("FOUT: " + ex);
    return 1;
}
