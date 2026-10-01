using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.BackupBanco.Configuration;

/// <summary>
/// Configuração do plugin de backup.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a pasta onde os arquivos de backup são gravados.
    /// Vazio usa a pasta "plugin-backups" ao lado da configuração do servidor.
    /// </summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets quantos backups manter. Os mais antigos são apagados.
    /// Zero desliga a limpeza automática.
    /// </summary>
    public int RetentionCount { get; set; } = 7;

    /// <summary>
    /// Gets or sets a value indicating whether inclui os bancos de dados.
    /// </summary>
    public bool IncludeDatabases { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether inclui os arquivos de configuração do servidor.
    /// </summary>
    public bool IncludeConfig { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether inclui as configurações dos plugins instalados.
    /// </summary>
    public bool IncludePluginConfigs { get; set; } = true;

    /// <summary>
    /// Gets or sets a hora do dia, em formato 24h "HH:mm", do backup automático.
    /// </summary>
    public string DailyTime { get; set; } = "03:30";

    /// <summary>
    /// Gets or sets a data e hora (UTC) da última execução.
    /// </summary>
    public DateTime? LastRunUtc { get; set; }

    /// <summary>
    /// Gets or sets o resultado da última execução, para exibição no painel.
    /// </summary>
    public string LastResult { get; set; } = string.Empty;
}
