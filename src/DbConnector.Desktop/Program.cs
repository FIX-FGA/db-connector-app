using Avalonia;

namespace DbConnector.Desktop;

internal static class Program
{
    /// <summary>
    /// Aceita as mesmas opções do db-connector-cli (--config, --idp, --gateway…). Ambientes nos argumentos (ex.:
    /// "db-connector dev") são ligados ao abrir; a janela mostra todos os da configuração.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (Opcoes.Ler(args, nenhumSemArgumento: true) is { } opcoes)
            {
                App.LigarAoAbrir = opcoes.Ambientes;
                App.Opcoes = opcoes;
            }
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            App.ErroDeConfiguracao = e.Message;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Usado também pelo designer do Avalonia.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
