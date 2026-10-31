using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace DbConnector.Desktop;

public partial class App : Application
{
    /// <summary>Opções da linha de comando e a configuração (Program); sem configuração, nenhum ambiente.</summary>
    public static Opcoes Opcoes { get; set; } = new(new Configuracao(), []);

    /// <summary>Ambientes pedidos nos argumentos: ligados assim que a janela abre.</summary>
    public static IReadOnlyList<Ambiente> LigarAoAbrir { get; set; } = [];

    /// <summary>Configuração ou argumentos inválidos: a janela abre mostrando o erro.</summary>
    public static string? ErroDeConfiguracao { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new JanelaPrincipal(Opcoes, LigarAoAbrir, ErroDeConfiguracao);
        base.OnFrameworkInitializationCompleted();
    }
}
