using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RaamTekenaar.Core;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T veld, T waarde, [CallerMemberName] string? naam = null)
    {
        if (EqualityComparer<T>.Default.Equals(veld, waarde)) return false;
        veld = waarde;
        OnPropertyChanged(naam);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? naam = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(naam));
}

/// <summary>Soort vak. De naam geeft de scharnierzijde aan, gezien in het buitenaanzicht.</summary>
public enum VakType
{
    Vast,
    DraaiScharnierLinks,
    DraaiScharnierRechts,
    KiepScharnierOnder,
    ValScharnierBoven,
    DraaiKiepScharnierLinks,
    DraaiKiepScharnierRechts
}

public enum Aanzicht { Voor, Zij, Boven }

public sealed class Vak : ObservableObject
{
    private VakType _type;
    private double _breedte;
    private bool _naarBinnen = true;

    public VakType Type { get => _type; set => Set(ref _type, value); }

    /// <summary>Dagmaat van het vak in mm. 0 = automatisch (restbreedte gelijk verdeeld).</summary>
    public double Breedte { get => _breedte; set => Set(ref _breedte, value); }

    /// <summary>Naar binnen draaiend: symbool als stippellijn (gezien van buiten).</summary>
    public bool NaarBinnen { get => _naarBinnen; set => Set(ref _naarBinnen, value); }
}

public sealed class RaamType : ObservableObject
{
    private string _naam = "Raam 1";
    private double _breedte = 1200, _hoogte = 1400, _borstwering = 900;
    private double _profielBreedte = 67, _vleugelBreedte = 54, _diepte = 114, _positieInMuur;
    private double _bovenlichtDagmaat;
    private VakType _bovenlichtType = VakType.Vast;
    private bool _bovenlichtNaarBinnen = true;
    private ObservableCollection<Vak> _vakken = new();

    /// <summary>Wordt afgevuurd bij elke wijziging van het raam of een van de vakken.</summary>
    public event EventHandler? Gewijzigd;

    public RaamType()
    {
        Koppel(_vakken);
        PropertyChanged += (_, _) => Gewijzigd?.Invoke(this, EventArgs.Empty);
    }

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Naam { get => _naam; set => Set(ref _naam, value); }
    /// <summary>Buitenwerkse breedte kozijn (mm).</summary>
    public double Breedte { get => _breedte; set => Set(ref _breedte, value); }
    /// <summary>Buitenwerkse hoogte kozijn (mm).</summary>
    public double Hoogte { get => _hoogte; set => Set(ref _hoogte, value); }
    /// <summary>Vloerpeil tot onderkant kozijn (mm).</summary>
    public double Borstwering { get => _borstwering; set => Set(ref _borstwering, value); }
    /// <summary>Aanzichtbreedte van stijlen en dorpels (mm).</summary>
    public double ProfielBreedte { get => _profielBreedte; set => Set(ref _profielBreedte, value); }
    /// <summary>Aanzichtbreedte van de vleugel/raamhout in draaiende vakken (mm).</summary>
    public double VleugelBreedte { get => _vleugelBreedte; set => Set(ref _vleugelBreedte, value); }
    /// <summary>Diepte van het kozijn (mm).</summary>
    public double Diepte { get => _diepte; set => Set(ref _diepte, value); }
    /// <summary>Afstand buitenkant gevel tot buitenkant kozijn (mm).</summary>
    public double PositieInMuur { get => _positieInMuur; set => Set(ref _positieInMuur, value); }
    /// <summary>Dagmaat (hoogte) van het bovenlicht in mm. 0 = geen bovenlicht.</summary>
    public double BovenlichtDagmaat { get => _bovenlichtDagmaat; set => Set(ref _bovenlichtDagmaat, value); }
    public VakType BovenlichtType { get => _bovenlichtType; set => Set(ref _bovenlichtType, value); }
    public bool BovenlichtNaarBinnen { get => _bovenlichtNaarBinnen; set => Set(ref _bovenlichtNaarBinnen, value); }

    public ObservableCollection<Vak> Vakken
    {
        get => _vakken;
        set
        {
            Ontkoppel(_vakken);
            _vakken = value ?? new ObservableCollection<Vak>();
            Koppel(_vakken);
            OnPropertyChanged();
        }
    }

    private void Koppel(ObservableCollection<Vak> lijst)
    {
        lijst.CollectionChanged += VakkenGewijzigd;
        foreach (var v in lijst) v.PropertyChanged += VakGewijzigd;
    }

    private void Ontkoppel(ObservableCollection<Vak> lijst)
    {
        lijst.CollectionChanged -= VakkenGewijzigd;
        foreach (var v in lijst) v.PropertyChanged -= VakGewijzigd;
    }

    private void VakkenGewijzigd(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (Vak v in e.OldItems) v.PropertyChanged -= VakGewijzigd;
        if (e.NewItems != null) foreach (Vak v in e.NewItems) v.PropertyChanged += VakGewijzigd;
        Gewijzigd?.Invoke(this, EventArgs.Empty);
    }

    private void VakGewijzigd(object? s, PropertyChangedEventArgs e) => Gewijzigd?.Invoke(this, EventArgs.Empty);

    public static RaamType Standaard(string naam) => new()
    {
        Naam = naam,
        Vakken = new ObservableCollection<Vak>
        {
            new() { Type = VakType.Vast },
            new() { Type = VakType.DraaiKiepScharnierRechts }
        }
    };
}

public sealed class Plaatsing : ObservableObject
{
    private Guid _raamId;
    private int _pagina;
    private Aanzicht _aanzicht;
    private double _x, _y, _mmPerPt = Schaal.NaarMmPerPt(50), _muurdikte;
    private int _rotatie;
    private bool _gespiegeld, _afdekken = true;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RaamId { get => _raamId; set => Set(ref _raamId, value); }
    /// <summary>Paginanummer, 0-gebaseerd.</summary>
    public int Pagina { get => _pagina; set => Set(ref _pagina, value); }
    public Aanzicht Aanzicht { get => _aanzicht; set => Set(ref _aanzicht, value); }
    /// <summary>Referentiepunt in punten (1/72 inch) vanaf linksboven van de pagina zoals weergegeven.</summary>
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    /// <summary>Rotatie tegen de klok in: 0, 90, 180 of 270 graden.</summary>
    public int Rotatie { get => _rotatie; set => Set(ref _rotatie, ((value % 360) + 360) % 360); }
    public bool Gespiegeld { get => _gespiegeld; set => Set(ref _gespiegeld, value); }
    /// <summary>Onderliggende lijnen van de tekening wit afdekken.</summary>
    public bool Afdekken { get => _afdekken; set => Set(ref _afdekken, value); }
    /// <summary>Alleen bovenaanzicht: muurdikte voor sparing en dagkanten (mm). 0 = niet tekenen.</summary>
    public double Muurdikte { get => _muurdikte; set => Set(ref _muurdikte, value); }

    /// <summary>Werkelijke millimeters per PDF-punt.</summary>
    public double MmPerPt
    {
        get => _mmPerPt;
        set { if (value > 0 && Set(ref _mmPerPt, value)) OnPropertyChanged(nameof(SchaalGetal)); }
    }

    /// <summary>Schaal als 1:N.</summary>
    [JsonIgnore]
    public double SchaalGetal
    {
        get => Math.Round(Schaal.NaarGetal(MmPerPt), 3);
        set { if (value > 0) MmPerPt = Schaal.NaarMmPerPt(value); }
    }
}

public sealed class Project
{
    public string? PdfPad { get; set; }
    public ObservableCollection<RaamType> Ramen { get; set; } = new();
    public ObservableCollection<Plaatsing> Plaatsingen { get; set; } = new();
}

public static class Schaal
{
    private const double MmPerPunt = 25.4 / 72.0;
    public static double NaarMmPerPt(double schaalGetal) => schaalGetal * MmPerPunt;
    public static double NaarGetal(double mmPerPt) => mmPerPt / MmPerPunt;
    public static double PapierMmNaarPt(double mm) => mm / MmPerPunt;
}

public static class Labels
{
    public static IReadOnlyList<KeyValuePair<VakType, string>> VakTypes { get; } = new List<KeyValuePair<VakType, string>>
    {
        new(VakType.Vast, "Vast"),
        new(VakType.DraaiScharnierLinks, "Draai – scharnier links"),
        new(VakType.DraaiScharnierRechts, "Draai – scharnier rechts"),
        new(VakType.KiepScharnierOnder, "Kiep – scharnier onder"),
        new(VakType.ValScharnierBoven, "Val/klep – scharnier boven"),
        new(VakType.DraaiKiepScharnierLinks, "Draai-kiep – scharnier links"),
        new(VakType.DraaiKiepScharnierRechts, "Draai-kiep – scharnier rechts"),
    };

    public static IReadOnlyList<KeyValuePair<Aanzicht, string>> Aanzichten { get; } = new List<KeyValuePair<Aanzicht, string>>
    {
        new(Aanzicht.Voor, "Vooraanzicht (gevel)"),
        new(Aanzicht.Zij, "Zijaanzicht / doorsnede"),
        new(Aanzicht.Boven, "Bovenaanzicht (plattegrond)"),
    };

    public static IReadOnlyList<int> Rotaties { get; } = new[] { 0, 90, 180, 270 };

    public static string Kort(Aanzicht a) => a switch
    {
        Aanzicht.Voor => "voor",
        Aanzicht.Zij => "zij",
        _ => "boven"
    };

    public static string Referentie(Aanzicht a) => a switch
    {
        Aanzicht.Voor => "Klik op het vloerpeil, recht onder de linkerkant van het kozijn.",
        Aanzicht.Zij => "Klik op het vloerpeil aan de buitenkant van de gevel.",
        _ => "Klik op de buitenkant van de gevel, bij de linkerkant van het kozijn."
    };
}
