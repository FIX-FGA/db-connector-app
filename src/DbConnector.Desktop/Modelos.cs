using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;

namespace DbConnector.Desktop;

public abstract class Observavel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Avisar(params string[] propriedades)
    {
        foreach (var p in propriedades) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    protected bool Definir<T>(ref T campo, T valor, [CallerMemberName] string propriedade = "")
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor)) return false;
        campo = valor;
        Avisar(propriedade);
        return true;
    }
}

/// <summary>Comando de um botão; enquanto roda, o botão fica desabilitado.</summary>
public sealed class Comando(Func<Task> acao) : ICommand
{
    private bool _rodando;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_rodando;

    public async void Execute(object? parameter)
    {
        _rodando = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await acao();
        }
        finally
        {
            _rodando = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>O que a janela faz pelo modelo: diálogos, arquivos e área de transferência.</summary>
public interface IJanela
{
    /// <summary>Abre o cadastro (nova, se <paramref name="atual"/> é null); null se cancelou.</summary>
    Task<Conexao?> EditarAsync(Conexao? atual, IReadOnlyList<Conexao> outras);
    Task<bool> ConfirmarAsync(string titulo, string texto, string acao);
    Task<string?> EscolherArquivoParaAbrirAsync();
    Task<string?> EscolherArquivoParaSalvarAsync(string sugestao);
    Task CopiarAsync(string texto);
}

/// <summary>A janela: senha local, as conexões cadastradas e a atividade.</summary>
public sealed class JanelaVm : Observavel
{
    private const int LimiteAtividade = 500;
    private readonly string? _arquivo;
    private string? _aviso;
    private string? _erro;

    /// <param name="arquivo">Onde salvar as conexões; null quando vieram dos argumentos (--arquivo, --gateway): não salva.</param>
    public JanelaVm(Conector conector, IEnumerable<Conexao> conexoes, string? arquivo, string? erro, IJanela janela)
    {
        Conector = conector;
        Janela = janela;
        _arquivo = arquivo;
        _erro = erro;
        SenhaLocal = conector.SenhaLocal.Valor;
        foreach (var c in conexoes) Conexoes.Add(new ConexaoVm(c, this));
        Conexoes.CollectionChanged += (_, _) => Avisar(nameof(Vazio), nameof(Portas), nameof(Mensagem));
        CopiarSenha = new Comando(async () =>
        {
            await janela.CopiarAsync(SenhaLocal);
            await MostrarAvisoAsync("Senha local copiada.");
        });
        NovaConexao = new Comando(async () =>
        {
            if (await janela.EditarAsync(null, ListaDeConexoes()) is { } nova)
                await GuardarAsync(() => Conexoes.Add(new ConexaoVm(nova, this)), $"Conexão {nova.Nome} criada.");
        });
        Importar = new Comando(ImportarAsync);
        Exportar = new Comando(ExportarAsync);
    }

    public string Nome => Opcoes.NomeDoAplicativo;
    public string Descricao => "Acesso aos bancos pelo seu aplicativo SQL (DBeaver, DataGrip, psql…), com o seu login (SSO) no navegador.";
    public string SenhaLocal { get; }
    public ObservableCollection<ConexaoVm> Conexoes { get; } = [];
    public ObservableCollection<string> Atividade { get; } = [];
    public ICommand CopiarSenha { get; }
    public ICommand NovaConexao { get; }
    public ICommand Importar { get; }
    public ICommand Exportar { get; }
    public bool PodeEditar => _arquivo is not null;
    public bool Vazio => Conexoes.Count == 0;
    internal Conector Conector { get; }
    internal IJanela Janela { get; }

    /// <summary>Portas locais, para o guia ("FIX dev 15432, FIX prod 15434").</summary>
    public string Portas => Conexoes.Count == 0
        ? "a porta local da conexão"
        : string.Join(", ", Conexoes.Select(c => $"{c.Nome} {c.Conexao.PortaLocal}"));

    /// <summary>Aviso no lugar das conexões: erro ao carregar, ou nenhuma cadastrada.</summary>
    public string? Mensagem => _erro ?? (Vazio
        ? "Nenhuma conexão cadastrada. Clique em Nova conexão e informe o emissor (realm do Keycloak), o client e o gateway, ou em Importar para usar o arquivo que alguém exportou."
        : null);

    /// <summary>Mensagem curta no rodapé (ex.: "copiado").</summary>
    public string? Aviso { get => _aviso; private set => Definir(ref _aviso, value); }

    public IReadOnlyList<Conexao> ListaDeConexoes(ConexaoVm? exceto = null) =>
        [.. Conexoes.Where(c => c != exceto).Select(c => c.Conexao)];

    public void Registrar(string conexao, string mensagem)
    {
        Atividade.Insert(0, $"{DateTime.Now:HH:mm:ss}  {conexao,-10} {mensagem}");
        while (Atividade.Count > LimiteAtividade) Atividade.RemoveAt(Atividade.Count - 1);
    }

    public async Task MostrarAvisoAsync(string texto)
    {
        Aviso = texto;
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (Aviso == texto) Aviso = null;
    }

    /// <summary>Aplica a mudança e salva; se não der para salvar, desfaz (<paramref name="desfazer"/>) e avisa.</summary>
    internal async Task<bool> GuardarAsync(Action mudanca, string aviso, Action? desfazer = null)
    {
        var antes = Conexoes.ToList();
        mudanca();
        try
        {
            if (_arquivo is not null) ArquivoDeConexoes.Salvar(ListaDeConexoes(), _arquivo);
            _erro = null;
            Avisar(nameof(Mensagem), nameof(Portas));
            _ = MostrarAvisoAsync(aviso);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            desfazer?.Invoke();
            Conexoes.Clear();
            foreach (var c in antes) Conexoes.Add(c);
            Registrar("conexões", $"não salvei: {e.Message}");
            await MostrarAvisoAsync($"Não salvei: {e.Message}");
            return false;
        }
    }

    private async Task ImportarAsync()
    {
        if (await Janela.EscolherArquivoParaAbrirAsync() is not { } caminho) return;
        IReadOnlyList<Conexao> importadas;
        try
        {
            importadas = ArquivoDeConexoes.Ler(await File.ReadAllTextAsync(caminho), Path.GetFileName(caminho));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Registrar("importar", e.Message);
            await MostrarAvisoAsync($"Não importei: {e.Message}");
            return;
        }
        var ligadas = Conexoes.Where(c => c.Ligado && importadas.Any(i => string.Equals(c.Nome, i.Nome, StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Nome).ToList();
        if (ligadas.Count > 0)
        {
            await MostrarAvisoAsync($"Desligue antes de importar: {string.Join(", ", ligadas)}.");
            return;
        }
        var juntas = ArquivoDeConexoes.Juntar(ListaDeConexoes(), importadas);
        var mantidas = Conexoes.ToDictionary(c => c.Conexao);
        await GuardarAsync(() =>
        {
            Conexoes.Clear();
            foreach (var c in juntas) Conexoes.Add(mantidas.TryGetValue(c, out var vm) ? vm : new ConexaoVm(c, this));
        }, $"{importadas.Count} conexão(ões) importada(s) de {Path.GetFileName(caminho)}.");
    }

    private async Task ExportarAsync()
    {
        if (Conexoes.Count == 0)
        {
            await MostrarAvisoAsync("Nada para exportar: cadastre ou importe uma conexão.");
            return;
        }
        if (await Janela.EscolherArquivoParaSalvarAsync("conexoes.json") is not { } caminho) return;
        try
        {
            await File.WriteAllTextAsync(caminho, ArquivoDeConexoes.Escrever(ListaDeConexoes()));
            await MostrarAvisoAsync($"{Conexoes.Count} conexão(ões) exportada(s) para {Path.GetFileName(caminho)} (sem senha nem token).");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await MostrarAvisoAsync($"Não exportei: {e.Message}");
        }
    }
}

/// <summary>Uma conexão na janela: ligar/desligar, estado do login, sessões abertas, editar e remover.</summary>
public sealed class ConexaoVm : Observavel
{
    private static readonly IBrush Cinza = new SolidColorBrush(Color.Parse("#8A909C"));
    private static readonly IBrush Amarelo = new SolidColorBrush(Color.Parse("#E0A100"));
    private static readonly IBrush Verde = new SolidColorBrush(Color.Parse("#2E9E5B"));
    private static readonly IBrush Vermelho = new SolidColorBrush(Color.Parse("#D64545"));

    private Conexao _conexao;
    private Situacao _situacao = new(Estado.Desligado);
    private string? _erroAoLigar;

    public ConexaoVm(Conexao conexao, JanelaVm janela)
    {
        _conexao = conexao;
        var conector = janela.Conector;
        Alternar = new Comando(async () =>
        {
            if (Ligado)
            {
                await conector.DesligarAsync(_conexao);
                return;
            }
            try
            {
                _erroAoLigar = null;
                conector.Ligar(_conexao);
            }
            catch (InvalidOperationException e)
            {
                _erroAoLigar = e.Message;
                janela.Registrar(_conexao.Nome, e.Message);
                Avisar(nameof(Detalhe));
            }
        });
        EntrarDeNovo = new Comando(() => conector.EntrarAsync(_conexao));
        CopiarUrl = new Comando(async () =>
        {
            await janela.Janela.CopiarAsync(_conexao.UrlJdbc);
            await janela.MostrarAvisoAsync($"URL de {_conexao.Nome} copiada: no DBeaver, Nova conexão → PostgreSQL → URL.");
        });
        Editar = new Comando(async () =>
        {
            if (Ligado || await janela.Janela.EditarAsync(_conexao, janela.ListaDeConexoes(this)) is not { } editada) return;
            var antes = _conexao;
            await janela.GuardarAsync(() => { _conexao = editada; AvisarTudo(); }, $"Conexão {editada.Nome} salva.",
                desfazer: () => { _conexao = antes; AvisarTudo(); });
        });
        Remover = new Comando(async () =>
        {
            if (Ligado || !await janela.Janela.ConfirmarAsync("Remover conexão", $"Remover a conexão {_conexao.Nome} deste computador?", "Remover")) return;
            await janela.GuardarAsync(() => janela.Conexoes.Remove(this), $"Conexão {_conexao.Nome} removida.");
        });
    }

    public Conexao Conexao => _conexao;
    public string Nome => _conexao.Nome;
    public string Permissao => _conexao.Permissao;
    public string Endereco => $"localhost:{_conexao.PortaLocal}";
    public string Destino => $"{_conexao.Host}:{_conexao.PortaGateway} · client {_conexao.ClientId}";
    public ICommand Editar { get; }
    public ICommand Remover { get; }
    public bool Desligado => !Ligado;

    public ICommand Alternar { get; }
    public ICommand EntrarDeNovo { get; }
    public ICommand CopiarUrl { get; }

    public bool Ligado => _situacao.Estado != Estado.Desligado;
    public string Acao => Ligado ? "Desligar" : "Conectar";
    public bool PodeEntrarDeNovo => _situacao.Estado == Estado.Falhou;

    public IBrush Cor => _situacao.Estado switch
    {
        Estado.AguardandoLogin => Amarelo,
        Estado.Conectado => Verde,
        Estado.Falhou => Vermelho,
        _ => Cinza,
    };

    public string Rotulo => _situacao.Estado switch
    {
        Estado.AguardandoLogin => "aguardando o login",
        Estado.Conectado => "conectado",
        Estado.Falhou => "login não concluído",
        _ => "desligado",
    };

    public string Detalhe
    {
        get
        {
            var s = _situacao;
            switch (s.Estado)
            {
                case Estado.AguardandoLogin:
                    return "Termine o login no navegador que abriu.";
                case Estado.Falhou:
                    return $"{s.Erro}. Clique em Entrar ou conecte pelo DBeaver para tentar de novo.";
                case Estado.Conectado:
                    var partes = new List<string> { s.Email is { Length: > 0 } email ? email : "conectado", $"DBeaver em {Endereco}" };
                    partes.Add(s.Conexoes switch { 0 => "nenhuma conexão aberta", 1 => "1 conexão aberta", var n => $"{n} conexões abertas" });
                    if (s.SessaoAte is { } ate)
                        partes.Add(ate > DateTimeOffset.UtcNow
                            ? $"sessão até {ate.ToLocalTime():HH:mm}"
                            : "sessão encerrada: o navegador abre de novo na próxima conexão");
                    return string.Join(" · ", partes);
                default:
                    return _erroAoLigar ?? $"Conecte para abrir {Endereco} ao DBeaver (o navegador abre para o login).";
            }
        }
    }

    public void Atualizar(Situacao situacao)
    {
        _situacao = situacao;
        Avisar(nameof(Ligado), nameof(Desligado), nameof(Acao), nameof(PodeEntrarDeNovo), nameof(Cor), nameof(Rotulo), nameof(Detalhe));
    }

    private void AvisarTudo() => Avisar(nameof(Conexao), nameof(Nome), nameof(Permissao), nameof(Endereco), nameof(Destino), nameof(Detalhe));

    /// <summary>A cada minuto: a sessão pode ter acabado sem nenhum evento.</summary>
    public void Tique() => Avisar(nameof(Detalhe));
}
