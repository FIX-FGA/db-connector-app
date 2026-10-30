using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Fix.Bancos.Desktop;

public partial class JanelaPrincipal : Window
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Conector _conector;
    private readonly JanelaVm _vm;
    private readonly DispatcherTimer _relogio;
    private bool _encerrado;

    // O designer do Avalonia precisa de um construtor sem parâmetros.
    public JanelaPrincipal() : this(App.Opcoes, [])
    {
    }

    public JanelaPrincipal(Opcoes opcoes, IReadOnlyList<Ambiente> ligarAoAbrir)
    {
        InitializeComponent();
        _conector = new Conector(opcoes, _http, new SenhaLocal(opcoes.SenhaLocal));
        _vm = new JanelaVm(_conector, opcoes.Ambientes, CopiarAsync);
        DataContext = _vm;

        // Os eventos do conector vêm de outras threads.
        _conector.Registro += (ambiente, mensagem) => Dispatcher.UIThread.Post(() => _vm.Registrar(ambiente.Nome, mensagem));
        _conector.Mudou += (ambiente, situacao) => Dispatcher.UIThread.Post(() =>
            _vm.Ambientes.FirstOrDefault(a => a.Nome == ambiente.Nome)?.Atualizar(situacao));

        _relogio = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) =>
        {
            foreach (var a in _vm.Ambientes) a.Tique();
        });
        _relogio.Start();

        Opened += (_, _) =>
        {
            CaberNaTela();
            foreach (var ambiente in ligarAoAbrir)
                _vm.Ambientes.FirstOrDefault(a => a.Nome == ambiente.Nome)?.Alternar.Execute(null);
        };
        Closing += async (_, e) =>
        {
            if (_encerrado) return;
            // Fecha as portas locais e as conexões antes de sair.
            e.Cancel = true;
            _encerrado = true;
            _relogio.Stop();
            await _conector.DisposeAsync();
            _http.Dispose();
            Close();
        };
    }

    /// <summary>
    /// Em tela menor que o tamanho padrão, a janela nasceria com o topo (cabeçalho e senha local) fora da tela:
    /// encolhe para a área útil do monitor e centraliza.
    /// </summary>
    private void CaberNaTela()
    {
        if (Screens.ScreenFromWindow(this) is not { } tela) return;
        var area = tela.WorkingArea;
        var escala = tela.Scaling;
        const double Folga = 24;
        var largura = Math.Min(Width, area.Width / escala - Folga);
        var altura = Math.Min(Height, area.Height / escala - Folga - 32); // 32: barra de título
        if (largura == Width && altura == Height && Position.Y >= area.Y) return;
        Width = largura;
        Height = altura;
        Position = new PixelPoint(
            area.X + (int)((area.Width - largura * escala) / 2),
            area.Y + (int)Math.Max(0, (area.Height - (altura + 32) * escala) / 2));
    }

    private async Task CopiarAsync(string texto)
    {
        if (Clipboard is { } area) await area.SetTextAsync(texto);
    }
}
