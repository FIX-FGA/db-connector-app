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

/// <summary>A janela: senha local, os ambientes e a atividade.</summary>
public sealed class JanelaVm : Observavel
{
    private const int LimiteAtividade = 500;
    private string? _aviso;

    public JanelaVm(Conector conector, Opcoes opcoes, string? erroDeConfiguracao, Func<string, Task> copiar)
    {
        var config = opcoes.Config;
        Nome = config.Nome;
        Descricao = config.DescricaoOuPadrao;
        SenhaLocal = conector.SenhaLocal.Valor;
        Ambientes = [.. config.AmbientesEfetivos.Select(a => new AmbienteVm(a, conector, this, copiar))];
        Portas = string.Join(", ", config.AmbientesEfetivos.Select(a => $"{a.Nome} {a.PortaLocal}"));
        Banco = config.AmbientesEfetivos.Select(a => a.BancoPadrao).Distinct().ToList() is [{ } unico]
            ? $"{unico}, com “Show all databases” marcado para ver os outros"
            : "o database que você quer abrir (“Show all databases” mostra os outros)";
        SemAmbientes = erroDeConfiguracao is not null
            ? $"Configuração inválida: {erroDeConfiguracao}"
            : Ambientes.Count == 0
                ? "Nenhum ambiente configurado. Este build não traz configuração: abra com --config <arquivo> (veja o README do db-connector-app)."
                : null;
        CopiarSenha = new Comando(async () =>
        {
            await copiar(SenhaLocal);
            await MostrarAvisoAsync("Senha local copiada.");
        });
    }

    public string Nome { get; }
    public string Descricao { get; }
    /// <summary>Portas locais, para o guia ("dev 15432, qa 15433").</summary>
    public string Portas { get; }
    public string Banco { get; }
    /// <summary>Aviso no lugar dos ambientes quando não há nenhum (ou a configuração é inválida).</summary>
    public string? SemAmbientes { get; }
    public string SenhaLocal { get; }
    public IReadOnlyList<AmbienteVm> Ambientes { get; }
    public ObservableCollection<string> Atividade { get; } = [];
    public ICommand CopiarSenha { get; }

    /// <summary>Mensagem curta no rodapé (ex.: "copiado").</summary>
    public string? Aviso { get => _aviso; private set => Definir(ref _aviso, value); }

    public void Registrar(string ambiente, string mensagem)
    {
        Atividade.Insert(0, $"{DateTime.Now:HH:mm:ss}  {ambiente,-5} {mensagem}");
        while (Atividade.Count > LimiteAtividade) Atividade.RemoveAt(Atividade.Count - 1);
    }

    public async Task MostrarAvisoAsync(string texto)
    {
        Aviso = texto;
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (Aviso == texto) Aviso = null;
    }
}

/// <summary>Um ambiente na janela: ligar/desligar, estado do login e conexões abertas.</summary>
public sealed class AmbienteVm : Observavel
{
    private static readonly IBrush Cinza = new SolidColorBrush(Color.Parse("#8A909C"));
    private static readonly IBrush Amarelo = new SolidColorBrush(Color.Parse("#E0A100"));
    private static readonly IBrush Verde = new SolidColorBrush(Color.Parse("#2E9E5B"));
    private static readonly IBrush Vermelho = new SolidColorBrush(Color.Parse("#D64545"));

    private readonly Ambiente _ambiente;
    private Situacao _situacao = new(Estado.Desligado);
    private string? _erroAoLigar;

    public AmbienteVm(Ambiente ambiente, Conector conector, JanelaVm janela, Func<string, Task> copiar)
    {
        _ambiente = ambiente;
        Alternar = new Comando(async () =>
        {
            if (Ligado)
            {
                await conector.DesligarAsync(ambiente);
                return;
            }
            try
            {
                _erroAoLigar = null;
                conector.Ligar(ambiente);
            }
            catch (InvalidOperationException e)
            {
                _erroAoLigar = e.Message;
                janela.Registrar(ambiente.Nome, e.Message);
                Avisar(nameof(Detalhe));
            }
        });
        EntrarDeNovo = new Comando(() => conector.EntrarAsync(ambiente));
        CopiarUrl = new Comando(async () =>
        {
            await copiar(ambiente.UrlJdbc);
            await janela.MostrarAvisoAsync($"URL do {ambiente.Nome} copiada: no DBeaver, Nova conexão → PostgreSQL → URL.");
        });
    }

    public string Nome => _ambiente.Nome;
    public string Permissao => _ambiente.Permissao;
    public string Endereco => $"localhost:{_ambiente.PortaLocal}";
    public string Host => _ambiente.Host;

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
        Avisar(nameof(Ligado), nameof(Acao), nameof(PodeEntrarDeNovo), nameof(Cor), nameof(Rotulo), nameof(Detalhe));
    }

    /// <summary>A cada minuto: a sessão pode ter acabado sem nenhum evento.</summary>
    public void Tique() => Avisar(nameof(Detalhe));
}
