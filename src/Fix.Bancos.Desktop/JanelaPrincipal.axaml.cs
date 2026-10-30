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

    private async Task CopiarAsync(string texto)
    {
        if (Clipboard is { } area) await area.SetTextAsync(texto);
    }
}
