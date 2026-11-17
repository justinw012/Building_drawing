using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using RaamTekenaar.Core;

namespace RaamTekenaar;

/// <summary>Tekst voor een regel in de lijst met geplaatste ramen.</summary>
public sealed class PlaatsingTekstConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Plaatsing p) return "";
        string naam = MainWindow.HuidigProject?.Ramen.FirstOrDefault(r => r.Id == p.RaamId)?.Naam ?? "(onbekend raam)";
        return $"{naam} – {Labels.Kort(p.Aanzicht)}aanzicht – pagina {p.Pagina + 1}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Eenvoudig invoervenster met één tekstveld.</summary>
public static class InvoerDialoog
{
    public static string? Vraag(Window eigenaar, string titel, string vraag, string standaard)
    {
        var invoer = new TextBox { Text = standaard, Margin = new Thickness(0, 8, 0, 12), MinWidth = 260 };
        var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var annuleer = new Button { Content = "Annuleren", IsCancel = true, Width = 80 };

        var knoppen = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        knoppen.Children.Add(ok);
        knoppen.Children.Add(annuleer);

        var paneel = new StackPanel { Margin = new Thickness(14) };
        paneel.Children.Add(new TextBlock { Text = vraag, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 });
        paneel.Children.Add(invoer);
        paneel.Children.Add(knoppen);

        var venster = new Window
        {
            Title = titel,
            Owner = eigenaar,
            Content = paneel,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        ok.Click += (_, _) => venster.DialogResult = true;
        venster.Loaded += (_, _) => { invoer.Focus(); invoer.SelectAll(); Keyboard.Focus(invoer); };

        return venster.ShowDialog() == true ? invoer.Text : null;
    }
}
