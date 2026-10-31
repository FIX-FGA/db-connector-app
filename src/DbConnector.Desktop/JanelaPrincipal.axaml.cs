using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace DbConnector.Desktop;

public partial class JanelaPrincipal : Window, IJanela
{
    private static readonly FilePickerFileType ArquivoJson = new("Conexões (.json)") { Patterns = ["*.json"], MimeTypes = ["application/json"] };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Conector _conector;
    private readonly JanelaVm _vm;
    private readonly DispatcherTimer _relogio;
    private bool _encerrado;

    // O designer do Avalonia precisa de um construtor sem parâmetros.
    public JanelaPrincipal() : this(App.Opcoes, [], null)
    {
    }

    public JanelaPrincipal(Opcoes opcoes, IReadOnlyList<Conexao> ligarAoAbrir, string? erro)
    {
        InitializeComponent();
        _conector = new Conector(opcoes, _http, new SenhaLocal(opcoes.SenhaLocal));
        // Conexões vindas dos argumentos (--arquivo, --gateway) não são salvas nem editadas; com erro ao ler as
        // cadastradas, também não (salvar apagaria o arquivo que não foi lido).
        var somenteLeitura = erro is not null || opcoes.Arquivo is not null || opcoes.Conexoes is [{ Nome: "local" }];
        _vm = new JanelaVm(_conector, opcoes.Conexoes, somenteLeitura ? null : ArquivoDeConexoes.CaminhoPadrao, erro, this);
        DataContext = _vm;

        // Os eventos do conector vêm de outras threads.
        _conector.Registro += (conexao, mensagem) => Dispatcher.UIThread.Post(() => _vm.Registrar(conexao.Nome, mensagem));
        _conector.Mudou += (conexao, situacao) => Dispatcher.UIThread.Post(() =>
            _vm.Conexoes.FirstOrDefault(c => c.Nome == conexao.Nome)?.Atualizar(situacao));

        _relogio = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) =>
        {
            foreach (var c in _vm.Conexoes) c.Tique();
        });
        _relogio.Start();

        Opened += (_, _) =>
        {
            CaberNaTela();
            foreach (var conexao in ligarAoAbrir)
                _vm.Conexoes.FirstOrDefault(c => c.Nome == conexao.Nome)?.Alternar.Execute(null);
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

    public Task<Conexao?> EditarAsync(Conexao? atual, IReadOnlyList<Conexao> outras) =>
        new EditorDeConexao(atual, outras).ShowDialog<Conexao?>(this);

    public async Task<bool> ConfirmarAsync(string titulo, string texto, string acao)
    {
        var dialogo = new Window
        {
            Title = titulo,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = Icon,
        };
        var sim = new Button { Content = acao, Classes = { "principal" }, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var nao = new Button { Content = "Cancelar", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, IsCancel = true };
        sim.Click += (_, _) => dialogo.Close(true);
        nao.Click += (_, _) => dialogo.Close(false);
        dialogo.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = texto, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { nao, sim } },
            },
        };
        return await dialogo.ShowDialog<bool?>(this) == true;
    }

    public async Task<string?> EscolherArquivoParaAbrirAsync()
    {
        var arquivos = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar conexões",
            AllowMultiple = false,
            FileTypeFilter = [ArquivoJson, FilePickerFileTypes.All],
        });
        return arquivos is [var arquivo] ? arquivo.TryGetLocalPath() : null;
    }

    public async Task<string?> EscolherArquivoParaSalvarAsync(string sugestao)
    {
        var arquivo = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar conexões",
            SuggestedFileName = sugestao,
            DefaultExtension = "json",
            FileTypeChoices = [ArquivoJson],
        });
        return arquivo?.TryGetLocalPath();
    }

    public async Task CopiarAsync(string texto)
    {
        if (Clipboard is { } area) await area.SetTextAsync(texto);
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
}
