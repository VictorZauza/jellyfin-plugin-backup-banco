using System;

namespace Jellyfin.Plugin.BackupBanco.Models;

/// <summary>
/// Um arquivo de backup já existente.
/// </summary>
public class BackupDto
{
    /// <summary>
    /// Gets or sets o nome do arquivo.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets o tamanho em bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Gets or sets a data e hora (UTC) de criação.
    /// </summary>
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// Resultado de uma execução de backup.
/// </summary>
public class ResultadoBackupDto
{
    /// <summary>
    /// Gets or sets a value indicating whether o backup foi concluído.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets a descrição do que aconteceu.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets o nome do arquivo gerado.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets o tamanho do arquivo gerado, em bytes.
    /// </summary>
    public long SizeBytes { get; set; }
}

/// <summary>
/// Situação atual do plugin, para o painel.
/// </summary>
public class StatusDto
{
    /// <summary>
    /// Gets or sets a pasta onde os backups são gravados.
    /// </summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a data e hora (UTC) da última execução.
    /// </summary>
    public DateTime? LastRunUtc { get; set; }

    /// <summary>
    /// Gets or sets o resultado da última execução.
    /// </summary>
    public string LastResult { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether o backup online do SQLite está disponível.
    /// </summary>
    public bool OnlineBackupAvailable { get; set; }
}
