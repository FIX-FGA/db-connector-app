using Avalonia;

namespace Fix.Bancos.Desktop;

internal static class Program
{
    /// <summary>
    /// Aceita as mesmas opções do fix-bancos-cli. Ambientes nos argumentos (ex.: "fix-bancos dev") são ligados ao abrir;
    /// a janela mostra sempre os três (ou só o de --gateway, para teste local).
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (Opcoes.Ler(args, padrao: []) is { } opcoes)
            {
                App.LigarAoAbrir = opcoes.Ambientes;
                App.Opcoes = opcoes.Ambientes is [{ Nome: "personalizado" }] ? opcoes : opcoes with { Ambientes = [.. Ambiente.Conhecidos.Values] };
            }
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Usado também pelo designer do Avalonia.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
