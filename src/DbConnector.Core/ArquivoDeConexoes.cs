using System.Text.Json;
using System.Text.Json.Serialization;

namespace DbConnector;

/// <summary>
/// As conexões cadastradas: salvas em conexoes.json na pasta de dados do usuário (Windows: %APPDATA%\DB Connector) e
/// exportadas/importadas no mesmo formato, para passar uma conexão pronta a outra pessoa (sem segredo: cada um faz o
/// próprio login).
/// </summary>
public static class ArquivoDeConexoes
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string CaminhoPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DB Connector", "conexoes.json");

    /// <summary>As conexões salvas; nenhuma se o arquivo ainda não existe.</summary>
    public static IReadOnlyList<Conexao> Carregar(string? caminho = null)
    {
        caminho ??= CaminhoPadrao;
        return File.Exists(caminho) ? Ler(File.ReadAllText(caminho), caminho) : [];
    }

    /// <summary>Grava (arquivo temporário + troca, para não deixar o arquivo pela metade).</summary>
    public static void Salvar(IEnumerable<Conexao> conexoes, string? caminho = null)
    {
        caminho ??= CaminhoPadrao;
        var lista = conexoes.ToList();
        if (Validar(lista) is [var problema, ..]) throw new ArgumentException(problema);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        var temporario = caminho + ".tmp";
        File.WriteAllText(temporario, Escrever(lista));
        File.Move(temporario, caminho, overwrite: true);
    }

    public static string Escrever(IEnumerable<Conexao> conexoes) =>
        JsonSerializer.Serialize(new Documento(conexoes.ToList()), Json);

    /// <summary>
    /// Lê um arquivo de conexões: { "conexoes": [...] } ou só a lista. Aceita também o formato de configuração antigo
    /// ({ "emissor", "idpHint", "bancoPadrao", "ambientes": [...] }), que vira uma conexão por ambiente.
    /// </summary>
    public static IReadOnlyList<Conexao> Ler(string json, string origem = "arquivo de conexões")
    {
        List<Conexao> conexoes;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var raiz = doc.RootElement;
            if (raiz.ValueKind == JsonValueKind.Array)
                conexoes = raiz.Deserialize<List<Conexao>>(Json) ?? [];
            else if (Propriedade(raiz, "conexoes") is { } lista)
                conexoes = lista.Deserialize<List<Conexao>>(Json) ?? [];
            else if (Propriedade(raiz, "ambientes") is { } ambientes)
                conexoes = DoFormatoAntigo(raiz, ambientes);
            else
                throw new ArgumentException($"{origem}: esperava \"conexoes\" (lista de conexões)");
        }
        catch (JsonException e)
        {
            throw new ArgumentException($"{origem} inválido: {e.Message}", e);
        }
        if (Validar(conexoes) is [var problema, ..]) throw new ArgumentException($"{origem}: {problema}");
        return conexoes;
    }

    /// <summary>Problemas da lista: os de cada conexão, nomes repetidos e portas locais repetidas.</summary>
    public static IReadOnlyList<string> Validar(IReadOnlyList<Conexao> conexoes)
    {
        var problemas = conexoes.SelectMany(c => c.Problemas().Select(p => $"{(string.IsNullOrWhiteSpace(c.Nome) ? "conexão sem nome" : c.Nome)}: {p}")).ToList();
        problemas.AddRange(conexoes.GroupBy(c => c.Nome.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .Select(g => $"há mais de uma conexão chamada {g.Key}"));
        problemas.AddRange(conexoes.GroupBy(c => c.PortaLocal).Where(g => g.Count() > 1)
            .Select(g => $"a porta local {g.Key} está em {string.Join(" e ", g.Select(c => c.Nome))}: cada conexão precisa da sua"));
        return problemas;
    }

    /// <summary>Junta as importadas às existentes: mesmo nome substitui; porta local ocupada por outra recebe a próxima livre.</summary>
    public static IReadOnlyList<Conexao> Juntar(IReadOnlyList<Conexao> existentes, IReadOnlyList<Conexao> importadas)
    {
        var resultado = existentes.Where(e => !importadas.Any(i => string.Equals(i.Nome.Trim(), e.Nome.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var conexao in importadas)
        {
            var porta = conexao.PortaLocal;
            while (resultado.Any(r => r.PortaLocal == porta) || porta == Opcoes.PortaRetornoPadrao) porta++;
            resultado.Add(conexao with { PortaLocal = porta });
        }
        return resultado;
    }

    /// <summary>Próxima porta local livre para uma conexão nova.</summary>
    public static int PortaLivre(IEnumerable<Conexao> conexoes)
    {
        var usadas = conexoes.Select(c => c.PortaLocal).ToHashSet();
        var porta = 15432;
        while (usadas.Contains(porta) || porta == Opcoes.PortaRetornoPadrao) porta++;
        return porta;
    }

    private static JsonElement? Propriedade(JsonElement objeto, string nome)
    {
        if (objeto.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in objeto.EnumerateObject())
            if (string.Equals(p.Name, nome, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static List<Conexao> DoFormatoAntigo(JsonElement raiz, JsonElement ambientes)
    {
        string? Texto(JsonElement e, string nome) => Propriedade(e, nome) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
        var emissor = Texto(raiz, "emissor") ?? "";
        var idp = Texto(raiz, "idpHint");
        var banco = Texto(raiz, "bancoPadrao");
        return [.. ambientes.EnumerateArray().Select(a => new Conexao(
            Texto(a, "nome") ?? "",
            emissor,
            Texto(a, "clientId") ?? "",
            Texto(a, "host") ?? "",
            Propriedade(a, "porta") is { ValueKind: JsonValueKind.Number } porta ? porta.GetInt32() : 5432,
            Propriedade(a, "portaLocal") is { ValueKind: JsonValueKind.Number } local ? local.GetInt32() : 15432,
            Texto(a, "bancoPadrao") ?? banco,
            Propriedade(a, "somenteLeitura") is { ValueKind: JsonValueKind.True },
            idp))];
    }

    private sealed record Documento(IReadOnlyList<Conexao> Conexoes);
}
