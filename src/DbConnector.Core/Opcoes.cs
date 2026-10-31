using System.Globalization;

namespace DbConnector;

/// <summary>
/// Opções de uma execução: as conexões (as cadastradas no aplicativo, ou as de um arquivo), as escolhidas nos
/// argumentos e as opções de teste.
/// </summary>
public sealed record Opcoes(
    IReadOnlyList<Conexao> Conexoes,
    IReadOnlyList<Conexao> Escolhidas,
    bool AceitarCertificadoInvalido = false,
    string? SenhaLocal = null,
    string? IdpHintForcado = null,
    string? Arquivo = null)
{
    /// <summary>Nome do aplicativo (janela, atalhos, instalador).</summary>
    public const string NomeDoAplicativo = "DB Connector";

    /// <summary>Porta do retorno do login no navegador: http://127.0.0.1:15431/callback (cadastrada nos clients).</summary>
    public const int PortaRetornoPadrao = 15431;

    public int PortaRetorno => PortaRetornoPadrao;

    public string? IdpHintDe(Conexao conexao) => IdpHintForcado ?? conexao.IdpHint;

    /// <summary>
    /// Lê os argumentos; null quando pedem a ajuda. Conexões citadas pelo nome são as escolhidas; sem nenhuma, a primeira
    /// (com <paramref name="nenhumaSemArgumento"/>, nenhuma: o aplicativo mostra todas e o usuário escolhe).
    /// </summary>
    public static Opcoes? Ler(string[] args, bool nenhumaSemArgumento = false, IReadOnlyList<Conexao>? salvas = null)
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
                case "--arquivo": arquivo = Valor(); break;
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

        IReadOnlyList<Conexao> conexoes;
        if (gateway is not null)
        {
            // Uma conexão avulsa (teste local), no lugar das cadastradas.
            var (host, portaGateway) = gateway.Split(':') is [var h, var p] ? (h, int.Parse(p, CultureInfo.InvariantCulture)) : (gateway, 5432);
            var avulsa = new Conexao("local", emissor ?? throw new ArgumentException("--gateway precisa de --emissor"),
                client ?? throw new ArgumentException("--gateway precisa de --client"), host, portaGateway, porta ?? 15432);
            if (avulsa.Problemas() is [var problema, ..]) throw new ArgumentException(problema);
            conexoes = [avulsa];
            nomes = ["local"];
        }
        else
        {
            conexoes = arquivo is not null
                ? ArquivoDeConexoes.Ler(File.Exists(arquivo) ? File.ReadAllText(arquivo) : throw new ArgumentException($"arquivo não encontrado: {arquivo}"), arquivo)
                : salvas ?? ArquivoDeConexoes.Carregar();
        }

        var escolhidas = nomes.Select(n => conexoes.FirstOrDefault(c => string.Equals(c.Nome, n, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(conexoes.Count == 0
                    ? $"conexão desconhecida: {n} (nenhuma cadastrada)"
                    : $"conexão desconhecida: {n} ({string.Join(", ", conexoes.Select(c => c.Nome))})"))
            .DistinctBy(c => c.Nome).ToList();
        if (escolhidas.Count == 0 && !nenhumaSemArgumento && conexoes.Count > 0) escolhidas.Add(conexoes[0]);
        return new Opcoes(conexoes, escolhidas, inseguro, senhaLocal, idp, arquivo);
    }
}

/// <summary>Mensagem para quem usa (o terminal do CLI ou a lista de atividade do aplicativo).</summary>
public delegate void Registrar(Conexao conexao, string mensagem);
