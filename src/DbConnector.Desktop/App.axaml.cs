using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace DbConnector.Desktop;

public partial class App : Application
{
    /// <summary>Opções da linha de comando e as conexões (Program).</summary>
    public static Opcoes Opcoes { get; set; } = new([], []);

    /// <summary>Conexões pedidas nos argumentos: ligadas assim que a janela abre.</summary>
    public static IReadOnlyList<Conexao> LigarAoAbrir { get; set; } = [];

    /// <summary>Argumentos ou arquivo de conexões inválidos: a janela abre mostrando o erro.</summary>
    public static string? Erro { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new JanelaPrincipal(Opcoes, LigarAoAbrir, Erro);
        base.OnFrameworkInitializationCompleted();
    }
}
