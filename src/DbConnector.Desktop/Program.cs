using Avalonia;

namespace DbConnector.Desktop;

internal static class Program
{
    /// <summary>
    /// Abre com as conexões cadastradas (conexoes.json na pasta de dados do usuário). Aceita as mesmas opções do
    /// db-connector-cli (--arquivo, --idp, --gateway…); conexões citadas pelo nome são ligadas ao abrir.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (Opcoes.Ler(args, nenhumaSemArgumento: true) is { } opcoes)
            {
                App.LigarAoAbrir = opcoes.Escolhidas;
                App.Opcoes = opcoes;
            }
        }
        catch (Exception e) when (e is ArgumentException or FormatException or IOException)
        {
            App.Erro = $"Não consegui ler as conexões: {e.Message}";
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Usado também pelo designer do Avalonia.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
