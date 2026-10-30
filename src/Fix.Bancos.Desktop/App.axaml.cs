using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Fix.Bancos.Desktop;

public partial class App : Application
{
    /// <summary>Opções da linha de comando (Program); sem argumentos, os três ambientes.</summary>
    public static Opcoes Opcoes { get; set; } = new([.. Ambiente.Conhecidos.Values], Ambiente.Emissor, null, false, 15431);

    /// <summary>Ambientes pedidos nos argumentos: ligados assim que a janela abre.</summary>
    public static IReadOnlyList<Ambiente> LigarAoAbrir { get; set; } = [];

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new JanelaPrincipal(Opcoes, LigarAoAbrir);
        base.OnFrameworkInitializationCompleted();
    }
}
