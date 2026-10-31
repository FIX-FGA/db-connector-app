using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DbConnector;

/// <summary>
/// Um ambiente: o gateway dele (PostgreSQL com TLS, que recebe o access token como senha), o client OIDC e a porta local
/// onde o aplicativo SQL conecta.
/// </summary>
public sealed record Ambiente(
    string Nome,
    string Host,
    [property: JsonPropertyName("porta")] int PortaGateway,
    string ClientId,
    int PortaLocal,
    [property: JsonPropertyName("somenteLeitura")] bool SoLeitura = false,
    string? BancoPadrao = null)
{
    public string Permissao => SoLeitura ? "só leitura" : "leitura e escrita";

    /// <summary>URL para colar no aplicativo SQL (no DBeaver: Nova conexão → PostgreSQL → URL).</summary>
    public string UrlJdbc => $"jdbc:postgresql://localhost:{PortaLocal}/{BancoPadrao ?? "postgres"}?sslmode=disable";
}

/// <summary>
/// O que o aplicativo mostra e onde ele conecta. Não há nada fixo no código: quem distribui o aplicativo embute um
/// config.json no build (o pipeline gera a partir de uma variável) ou quem usa passa --config &lt;arquivo&gt;. Sem
/// nenhum dos dois, o aplicativo abre sem ambientes.
/// </summary>
public sealed record Configuracao(
    string Nome = "DB Connector",
    string? Descricao = null,
    string Emissor = "",
    string? IdpHint = null,
    int PortaRetorno = 15431,
    string? BancoPadrao = null,
    IReadOnlyList<Ambiente>? Ambientes = null)
{
    public const string RecursoEmbutido = "config.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string DescricaoOuPadrao => Descricao
        ?? "Acesso aos bancos pelo seu aplicativo SQL (DBeaver, DataGrip, psql…), com o seu login (SSO) no navegador.";

    /// <summary>Ambientes com o database padrão da configuração quando o ambiente não define o seu.</summary>
    public IReadOnlyList<Ambiente> AmbientesEfetivos =>
        [.. (Ambientes ?? []).Select(a => a.BancoPadrao is null && BancoPadrao is not null ? a with { BancoPadrao = BancoPadrao } : a)];

    /// <summary>
    /// De um arquivo (--config), senão do config.json ao lado do executável, senão do embutido no build, senão a
    /// configuração vazia.
    /// </summary>
    public static Configuracao Carregar(string? arquivo = null)
    {
        if (arquivo is not null)
        {
            if (!File.Exists(arquivo)) throw new ArgumentException($"configuração não encontrada: {arquivo}");
            return Ler(File.ReadAllText(arquivo), arquivo);
        }
        var aoLado = Path.Combine(AppContext.BaseDirectory, RecursoEmbutido);
        if (File.Exists(aoLado)) return Ler(File.ReadAllText(aoLado), aoLado);
        using var recurso = typeof(Configuracao).Assembly.GetManifestResourceStream(RecursoEmbutido);
        if (recurso is null) return new Configuracao();
        using var leitor = new StreamReader(recurso);
        return Ler(leitor.ReadToEnd(), "config.json embutido");
    }

    public static Configuracao Ler(string json, string origem = "configuração")
    {
        Configuracao? config;
        try
        {
            config = JsonSerializer.Deserialize<Configuracao>(json, Json);
        }
        catch (JsonException e)
        {
            throw new ArgumentException($"{origem} inválida: {e.Message}", e);
        }
        config ??= new Configuracao();
        config.Validar(origem);
        return config with { Emissor = config.Emissor.TrimEnd('/') };
    }

    private void Validar(string origem)
    {
        var ambientes = Ambientes ?? [];
        if (ambientes.Count > 0 && !Uri.TryCreate(Emissor, UriKind.Absolute, out _))
            throw new ArgumentException($"{origem}: \"emissor\" precisa ser a URL do realm OIDC (ex.: https://sso.exemplo.com/realms/interno)");
        foreach (var a in ambientes)
        {
            if (string.IsNullOrWhiteSpace(a.Nome) || string.IsNullOrWhiteSpace(a.Host) || string.IsNullOrWhiteSpace(a.ClientId))
                throw new ArgumentException($"{origem}: cada ambiente precisa de nome, host e clientId");
            if (a.PortaGateway is <= 0 or > 65535 || a.PortaLocal is <= 1024 or > 65535)
                throw new ArgumentException($"{origem}: portas inválidas no ambiente {a.Nome}");
        }
        if (ambientes.DistinctBy(a => a.Nome, StringComparer.OrdinalIgnoreCase).Count() != ambientes.Count)
            throw new ArgumentException($"{origem}: nomes de ambiente repetidos");
        if (ambientes.Select(a => a.PortaLocal).Append(PortaRetorno).Distinct().Count() != ambientes.Count + 1)
            throw new ArgumentException($"{origem}: as portas locais (e a portaRetorno) precisam ser diferentes");
    }
}

/// <summary>Opções de uma execução: a configuração, os ambientes escolhidos e as opções de teste.</summary>
public sealed record Opcoes(Configuracao Config, IReadOnlyList<Ambiente> Ambientes, bool AceitarCertificadoInvalido = false, string? SenhaLocal = null, string? IdpHintForcado = null)
{
    public string Emissor => Config.Emissor;
    public string? IdpHint => IdpHintForcado ?? Config.IdpHint;
    public int PortaRetorno => Config.PortaRetorno;
    public string NomeDoAplicativo => Config.Nome;

    /// <summary>
    /// Lê os argumentos; null quando pedem a ajuda. Os ambientes citados nos argumentos são os escolhidos; sem nenhum,
    /// o primeiro da configuração (com <paramref name="nenhumSemArgumento"/>, nenhum: o aplicativo mostra todos).
    /// </summary>
    public static Opcoes? Ler(string[] args, bool nenhumSemArgumento = false, Configuracao? config = null)
    {
        string? arquivo = null, idp = null, gateway = null, client = null, senhaLocal = null, emissor = null;
        int? porta = null;
        var inseguro = false;
        var nomes = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            string Valor() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} precisa de um valor");
            switch (args[i])
            {
                case "-h" or "--help" or "--ajuda": return null;
                case "--config": arquivo = Valor(); break;
                case "--idp": idp = Valor(); break;
                case "--emissor": emissor = Valor(); break;
                case "--gateway": gateway = Valor(); break;
                case "--client": client = Valor(); break;
                case "--porta": porta = int.Parse(Valor(), CultureInfo.InvariantCulture); break;
                case "--inseguro": inseguro = true; break;
                case "--senha-local": senhaLocal = Valor(); break;
                default: nomes.Add(args[i]); break;
            }
        }

        config ??= Configuracao.Carregar(arquivo);
        if (emissor is not null) config = config with { Emissor = emissor.TrimEnd('/') };
        if (gateway is not null)
        {
            // Um ambiente avulso (teste local): substitui os da configuração.
            var (host, portaGateway) = gateway.Split(':') is [var h, var p] ? (h, int.Parse(p, CultureInfo.InvariantCulture)) : (gateway, 5432);
            var avulso = new Ambiente("local", host, portaGateway, client ?? throw new ArgumentException("--gateway precisa de --client"), porta ?? 15432);
            config = config with { Ambientes = [avulso] };
            nomes = ["local"];
        }

        var disponiveis = config.AmbientesEfetivos;
        var escolhidos = nomes.Select(n => disponiveis.FirstOrDefault(a => string.Equals(a.Nome, n, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(disponiveis.Count == 0
                    ? $"ambiente desconhecido: {n} (a configuração não tem ambientes)"
                    : $"ambiente desconhecido: {n} ({string.Join(", ", disponiveis.Select(a => a.Nome))})"))
            .DistinctBy(a => a.Nome).ToList();
        if (escolhidos.Count == 0 && !nenhumSemArgumento && disponiveis.Count > 0) escolhidos.Add(disponiveis[0]);
        return new Opcoes(config, escolhidos, inseguro, senhaLocal, idp);
    }
}

/// <summary>Mensagem para quem usa (o terminal do CLI ou a lista de atividade do aplicativo).</summary>
public delegate void Registrar(Ambiente ambiente, string mensagem);
