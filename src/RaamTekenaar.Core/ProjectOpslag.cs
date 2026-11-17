using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaamTekenaar.Core;

public static class ProjectOpslag
{
    private static readonly JsonSerializerOptions Opties = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string NaarJson(Project p) => JsonSerializer.Serialize(p, Opties);

    public static Project VanJson(string json) =>
        JsonSerializer.Deserialize<Project>(json, Opties) ?? new Project();

    public static void Opslaan(Project p, string pad) => File.WriteAllText(pad, NaarJson(p));

    public static Project Laden(string pad) => VanJson(File.ReadAllText(pad));

    public static string PlaatsingenNaarJson(IEnumerable<Plaatsing> lijst) => JsonSerializer.Serialize(lijst, Opties);

    public static List<Plaatsing> PlaatsingenVanJson(string json) =>
        JsonSerializer.Deserialize<List<Plaatsing>>(json, Opties) ?? new List<Plaatsing>();

    public static RaamType Kopie(RaamType r)
    {
        var kopie = JsonSerializer.Deserialize<RaamType>(JsonSerializer.Serialize(r, Opties), Opties)!;
        kopie.Id = Guid.NewGuid();
        kopie.Naam = r.Naam + " (kopie)";
        return kopie;
    }
}
