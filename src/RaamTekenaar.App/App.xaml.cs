using System.Windows;
using System.Windows.Threading;

namespace RaamTekenaar;

public partial class App : Application
{
    // Bewust geen logbestand: foutmeldingen alleen op het scherm, zonder bestandsnamen of tekeninggegevens.
    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "Er ging iets mis:\n\n" + e.Exception.GetType().Name + ": " + e.Exception.Message,
            "Raam Tekenaar", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
