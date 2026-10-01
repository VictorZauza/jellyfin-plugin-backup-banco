using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BackupBanco.Configuration;
using Jellyfin.Plugin.BackupBanco.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BackupBanco.Services;

/// <summary>
/// Monta o arquivo de backup: bancos, configuração do servidor e configuração dos plugins.
/// </summary>
public class BackupService
{
    private static readonly SemaphoreSlim _emExecucao = new SemaphoreSlim(1, 1);

    private readonly IApplicationPaths _paths;
    private readonly ILogger<BackupService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackupService"/> class.
    /// </summary>
    /// <param name="paths">Caminhos da aplicação.</param>
    /// <param name="logger">Logger.</param>
    public BackupService(IApplicationPaths paths, ILogger<BackupService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    private static PluginConfiguration? Config => Plugin.Instance?.Configuration;

    /// <summary>
    /// Gets a pasta onde os backups são gravados, já resolvida.
    /// </summary>
    public string PastaDestino
    {
        get
        {
            var configurado = Config?.DestinationPath;
            if (!string.IsNullOrWhiteSpace(configurado))
            {
                return configurado.Trim();
            }

            var raiz = Path.GetDirectoryName(_paths.ConfigurationDirectoryPath?.TrimEnd(Path.DirectorySeparatorChar))
                       ?? _paths.ProgramDataPath;
            return Path.Combine(raiz, "plugin-backups");
        }
    }

    /// <summary>
    /// Lista os backups já existentes, do mais novo para o mais antigo.
    /// </summary>
    /// <returns>Os arquivos encontrados.</returns>
    public IReadOnlyList<BackupDto> Listar()
    {
        var pasta = PastaDestino;
        if (!Directory.Exists(pasta))
        {
            return Array.Empty<BackupDto>();
        }

        try
        {
            return Directory.GetFiles(pasta, "jellyfin-backup-*.zip")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new BackupDto
                {
                    FileName = f.Name,
                    SizeBytes = f.Length,
                    CreatedUtc = f.LastWriteTimeUtc
                })
                .ToList();
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Não foi possível listar os backups em {Pasta}.", pasta);
            return Array.Empty<BackupDto>();
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Executa um backup completo.
    /// </summary>
    /// <param name="progresso">Progresso de 0 a 100, ou nulo.</param>
    /// <param name="cancelamento">Token de cancelamento.</param>
    /// <returns>O resultado da operação.</returns>
    public async Task<ResultadoBackupDto> ExecutarAsync(IProgress<double>? progresso, CancellationToken cancelamento)
    {
        // Um backup por vez: dois simultâneos disputariam o mesmo arquivo temporário.
        if (!await _emExecucao.WaitAsync(0, cancelamento).ConfigureAwait(false))
        {
            return new ResultadoBackupDto { Success = false, Message = "Já existe um backup em andamento." };
        }

        var config = Config;
        if (config is null)
        {
            _emExecucao.Release();
            return new ResultadoBackupDto { Success = false, Message = "O plugin não está inicializado." };
        }

        var temporaria = Path.Combine(Path.GetTempPath(), "jf-backup-" + Guid.NewGuid().ToString("N"));
        var destino = PastaDestino;
        var carimbo = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var arquivo = Path.Combine(destino, $"jellyfin-backup-{carimbo}.zip");
        var relato = new StringBuilder();
        var itens = 0;

        try
        {
            Directory.CreateDirectory(temporaria);
            Directory.CreateDirectory(destino);

            progresso?.Report(5);

            if (config.IncludeDatabases)
            {
                itens += CopiarBancos(temporaria, destino, relato, cancelamento);
            }

            progresso?.Report(50);

            if (config.IncludeConfig)
            {
                itens += CopiarXmls(_paths.ConfigurationDirectoryPath, Path.Combine(temporaria, "config"), relato);
            }

            progresso?.Report(65);

            if (config.IncludePluginConfigs)
            {
                itens += CopiarXmls(_paths.PluginConfigurationsPath, Path.Combine(temporaria, "plugin-configs"), relato);
            }

            progresso?.Report(75);

            if (itens == 0)
            {
                return Finalizar(config, false, "Nada foi selecionado para backup.");
            }

            File.WriteAllText(
                Path.Combine(temporaria, "manifesto.txt"),
                MontarManifesto(carimbo, relato),
                Encoding.UTF8);

            cancelamento.ThrowIfCancellationRequested();

            if (File.Exists(arquivo))
            {
                File.Delete(arquivo);
            }

            ZipFile.CreateFromDirectory(temporaria, arquivo, CompressionLevel.Optimal, false);
            progresso?.Report(90);

            var tamanho = new FileInfo(arquivo).Length;
            var removidos = AplicarRetencao(destino, config.RetentionCount);
            progresso?.Report(100);

            var mensagem = string.Format(
                CultureInfo.InvariantCulture,
                "{0} criado com {1} item(ns), {2:0.0} MB{3}.",
                Path.GetFileName(arquivo),
                itens,
                tamanho / 1024d / 1024d,
                removidos > 0 ? $"; {removidos} antigo(s) removido(s)" : string.Empty);

            _logger.LogInformation("Backup concluído: {Mensagem}", mensagem);
            return Finalizar(config, true, mensagem, Path.GetFileName(arquivo), tamanho);
        }
        catch (OperationCanceledException)
        {
            return Finalizar(config, false, "Backup cancelado.");
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao gerar o backup.");
            return Finalizar(config, false, "Falha: " + ex.Message);
        }
#pragma warning restore CA1031
        finally
        {
            try
            {
                if (Directory.Exists(temporaria))
                {
                    Directory.Delete(temporaria, true);
                }
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível limpar a pasta temporária {Pasta}.", temporaria);
            }
#pragma warning restore CA1031

            _emExecucao.Release();
        }
    }

    private ResultadoBackupDto Finalizar(
        PluginConfiguration config,
        bool sucesso,
        string mensagem,
        string? nomeArquivo = null,
        long tamanho = 0)
    {
        config.LastRunUtc = DateTime.UtcNow;
        config.LastResult = mensagem;
        try
        {
            Plugin.Instance?.Save();
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível gravar o resultado na configuração do plugin.");
        }
#pragma warning restore CA1031

        return new ResultadoBackupDto
        {
            Success = sucesso,
            Message = mensagem,
            FileName = nomeArquivo ?? string.Empty,
            SizeBytes = tamanho
        };
    }

    /// <summary>
    /// Copia os bancos SQLite usando o backup online do próprio SQLite, que gera
    /// uma cópia consistente mesmo com o servidor em uso. Se a biblioteca não
    /// estiver acessível, cai para a cópia dos arquivos .db, -wal e -shm.
    /// </summary>
    private int CopiarBancos(string temporaria, string destinoBackups, StringBuilder relato, CancellationToken ct)
    {
        var alvo = Path.Combine(temporaria, "data");
        Directory.CreateDirectory(alvo);

        var tipoSqlite = TipoSqliteConnection();
        if (tipoSqlite is null)
        {
            _logger.LogWarning(
                "Microsoft.Data.Sqlite não encontrado no servidor; usando cópia de arquivo em vez do backup online.");
            relato.AppendLine("AVISO: backup online indisponivel, bancos copiados como arquivo.");
        }

        var copiados = 0;
        foreach (var banco in LocalizarBancos(destinoBackups))
        {
            ct.ThrowIfCancellationRequested();
            var nome = Path.GetFileName(banco);
            var saida = Path.Combine(alvo, nome);

            if (tipoSqlite is not null && BackupOnline(tipoSqlite, banco, saida))
            {
                relato.AppendLine(CultureInfo.InvariantCulture, $"banco (online): {nome}");
                copiados++;
                continue;
            }

            if (CopiarArquivoBanco(banco, alvo))
            {
                relato.AppendLine(CultureInfo.InvariantCulture, $"banco (arquivo): {nome}");
                copiados++;
            }
        }

        return copiados;
    }

    private IEnumerable<string> LocalizarBancos(string destinoBackups)
    {
        var raiz = _paths.DataPath;
        if (string.IsNullOrEmpty(raiz) || !Directory.Exists(raiz))
        {
            yield break;
        }

        string[] encontrados;
        try
        {
            encontrados = Directory.GetFiles(raiz, "*.db", SearchOption.AllDirectories);
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Não foi possível varrer {Raiz} em busca de bancos.", raiz);
            yield break;
        }
#pragma warning restore CA1031

        foreach (var caminho in encontrados)
        {
            // Nunca inclui a própria pasta de backup, nem caches e capas,
            // que são recriáveis e tornariam o arquivo enorme.
            if (caminho.StartsWith(destinoBackups, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativo = Path.GetRelativePath(raiz, caminho).Replace('\\', '/');
            if (relativo.Contains("/cache/", StringComparison.OrdinalIgnoreCase)
                || relativo.Contains("/metadata/", StringComparison.OrdinalIgnoreCase)
                || relativo.Contains("/transcodes/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return caminho;
        }
    }

    /// <summary>
    /// Indica se o backup online do SQLite está disponível neste servidor.
    /// </summary>
    /// <returns>Verdadeiro quando a biblioteca foi encontrada.</returns>
    public static bool BackupOnlineDisponivel() => TipoSqliteConnection() is not null;

    private static Type? TipoSqliteConnection()
    {
        var tipo = Type.GetType("Microsoft.Data.Sqlite.SqliteConnection, Microsoft.Data.Sqlite");
        if (tipo is not null)
        {
            return tipo;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            tipo = assembly.GetType("Microsoft.Data.Sqlite.SqliteConnection", false);
            if (tipo is not null)
            {
                return tipo;
            }
        }

        return null;
    }

    private bool BackupOnline(Type tipo, string origem, string saida)
    {
        object? conexaoOrigem = null;
        object? conexaoDestino = null;
        try
        {
            conexaoOrigem = Activator.CreateInstance(tipo, "Data Source=" + origem + ";Mode=ReadOnly");
            conexaoDestino = Activator.CreateInstance(tipo, "Data Source=" + saida);

            var abrir = tipo.GetMethod("Open", Type.EmptyTypes);
            var copiar = tipo.GetMethod("BackupDatabase", new[] { tipo });
            if (abrir is null || copiar is null)
            {
                return false;
            }

            abrir.Invoke(conexaoOrigem, null);
            abrir.Invoke(conexaoDestino, null);
            copiar.Invoke(conexaoOrigem, new[] { conexaoDestino });
            return true;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backup online falhou para {Banco}; tentando cópia de arquivo.", origem);
            return false;
        }
#pragma warning restore CA1031
        finally
        {
            (conexaoDestino as IDisposable)?.Dispose();
            (conexaoOrigem as IDisposable)?.Dispose();
        }
    }

    private bool CopiarArquivoBanco(string banco, string alvo)
    {
        var ok = false;
        foreach (var sufixo in new[] { string.Empty, "-wal", "-shm" })
        {
            var origem = banco + sufixo;
            if (!File.Exists(origem))
            {
                continue;
            }

            try
            {
                File.Copy(origem, Path.Combine(alvo, Path.GetFileName(origem)), true);
                ok = true;
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                _logger.LogError(ex, "Não foi possível copiar {Arquivo}.", origem);
            }
#pragma warning restore CA1031
        }

        return ok;
    }

    private int CopiarXmls(string? origem, string alvo, StringBuilder relato)
    {
        if (string.IsNullOrEmpty(origem) || !Directory.Exists(origem))
        {
            return 0;
        }

        Directory.CreateDirectory(alvo);
        var copiados = 0;
        try
        {
            foreach (var arquivo in Directory.GetFiles(origem, "*.xml", SearchOption.TopDirectoryOnly))
            {
                File.Copy(arquivo, Path.Combine(alvo, Path.GetFileName(arquivo)), true);
                relato.AppendLine(CultureInfo.InvariantCulture, $"xml: {Path.GetFileName(arquivo)}");
                copiados++;
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao copiar os XML de {Origem}.", origem);
        }
#pragma warning restore CA1031

        return copiados;
    }

    private string MontarManifesto(string carimbo, StringBuilder relato)
    {
        var texto = new StringBuilder();
        texto.AppendLine("Backup do Jellyfin gerado pelo plugin \"Backup do Banco\".");
        texto.AppendLine(CultureInfo.InvariantCulture, $"Data: {carimbo}");
        texto.AppendLine(CultureInfo.InvariantCulture, $"DataPath: {_paths.DataPath}");
        texto.AppendLine(CultureInfo.InvariantCulture, $"ConfigurationDirectoryPath: {_paths.ConfigurationDirectoryPath}");
        texto.AppendLine();
        texto.AppendLine("Conteudo:");
        texto.Append(relato);
        texto.AppendLine();
        texto.AppendLine("Para restaurar: pare o Jellyfin, substitua os arquivos nas pastas");
        texto.AppendLine("de origem acima pelos deste pacote, e suba o servidor de novo.");
        return texto.ToString();
    }

    private int AplicarRetencao(string pasta, int manter)
    {
        if (manter <= 0)
        {
            return 0;
        }

        try
        {
            var antigos = Directory.GetFiles(pasta, "jellyfin-backup-*.zip")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(manter)
                .ToList();

            foreach (var arquivo in antigos)
            {
                arquivo.Delete();
                _logger.LogInformation("Backup antigo removido: {Arquivo}", arquivo.Name);
            }

            return antigos.Count;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao aplicar a retenção em {Pasta}.", pasta);
            return 0;
        }
#pragma warning restore CA1031
    }
}
