namespace Fix.Bancos;

/// <summary>Um ambiente: o gateway dele, o client do Keycloak e a porta local onde o DBeaver conecta.</summary>
public sealed record Ambiente(string Nome, string Host, int PortaGateway, string ClientId, int PortaLocal, bool SoLeitura = false)
{
    public const string Emissor = "https://sso.fix.webpassos.com.br/realms/internal-trusted";

    /// <summary>Database que o DBeaver abre primeiro (com "Show all databases", aparecem os outros).</summary>
    public const string BancoPadrao = "fix_core";

    public static readonly IReadOnlyDictionary<string, Ambiente> Conhecidos = new Dictionary<string, Ambiente>(StringComparer.OrdinalIgnoreCase)
    {
        ["dev"] = new("dev", "dev.bancos.fix.webpassos.com.br", 5432, "bancos-dev", 15432),
        ["qa"] = new("qa", "qa.bancos.fix.webpassos.com.br", 5432, "bancos-qa", 15433),
        ["prod"] = new("prod", "bancos.fix.webpassos.com.br", 5432, "bancos-prod", 15434, SoLeitura: true),
    };

    public string Permissao => SoLeitura ? "só leitura" : "leitura e escrita";

    /// <summary>URL para colar no DBeaver (Nova conexão → PostgreSQL → URL).</summary>
    public string UrlJdbc => $"jdbc:postgresql://localhost:{PortaLocal}/{BancoPadrao}?sslmode=disable";
}

/// <summary>Opções da linha de comando (as de teste só servem para o ambiente local).</summary>
public sealed record Opcoes(IReadOnlyList<Ambiente> Ambientes, string Emissor, string? IdpHint, bool AceitarCertificadoInvalido, int PortaRetorno, string? SenhaLocal = null)
{
    /// <summary>Lê os argumentos; null quando pedem a ajuda. Sem ambiente, <paramref name="padrao"/> (o CLI usa só dev).</summary>
    public static Opcoes? Ler(string[] args, IReadOnlyList<Ambiente>? padrao = null)
    {
        var ambientes = new List<Ambiente>();
        string emissor = Ambiente.Emissor;
        string? idp = null, gateway = null, client = null, senhaLocal = null;
        int? porta = null;
        var inseguro = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Valor() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} precisa de um valor");
            switch (args[i])
            {
                case "-h" or "--help" or "--ajuda": return null;
                case "--idp": idp = Valor(); break;
                case "--emissor": emissor = Valor(); break;
                case "--gateway": gateway = Valor(); break;
                case "--client": client = Valor(); break;
                case "--porta": porta = int.Parse(Valor(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--inseguro": inseguro = true; break;
                case "--senha-local": senhaLocal = Valor(); break;
                default:
                    ambientes.Add(Ambiente.Conhecidos.TryGetValue(args[i], out var a) ? a : throw new ArgumentException($"ambiente desconhecido: {args[i]} (dev, qa ou prod)"));
                    break;
            }
        }
        if (gateway is not null)
        {
            var (host, portaGateway) = gateway.Split(':') is [var h, var p] ? (h, int.Parse(p, System.Globalization.CultureInfo.InvariantCulture)) : (gateway, 5432);
            ambientes = [new Ambiente("personalizado", host, portaGateway, client ?? throw new ArgumentException("--gateway precisa de --client"), porta ?? 15432)];
        }
        if (ambientes.Count == 0) ambientes.AddRange(padrao ?? [Ambiente.Conhecidos["dev"]]);
        return new Opcoes(ambientes.DistinctBy(a => a.Nome).ToList(), emissor.TrimEnd('/'), idp, inseguro, 15431, senhaLocal);
    }
}

/// <summary>Mensagem para quem usa (a janela do CLI ou a lista de atividade do aplicativo).</summary>
public delegate void Registrar(Ambiente ambiente, string mensagem);
