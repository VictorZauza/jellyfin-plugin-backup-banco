using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.BackupBanco.Services;

/// <summary>
/// Tarefa agendada que gera o backup. Aparece em Painel → Tarefas Agendadas,
/// onde também pode ser disparada à mão.
/// </summary>
public class BackupScheduledTask : IScheduledTask
{
    private readonly BackupService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackupScheduledTask"/> class.
    /// </summary>
    /// <param name="service">Serviço de backup.</param>
    public BackupScheduledTask(BackupService service)
    {
        _service = service;
    }

    /// <inheritdoc />
    public string Name => "Backup do banco e das configurações";

    /// <inheritdoc />
    public string Key => "BackupBancoJellyfin";

    /// <inheritdoc />
    public string Description =>
        "Grava os bancos de dados e os arquivos de configuração em um .zip, mantendo os mais recentes.";

    /// <inheritdoc />
    public string Category => "Manutenção";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _service.ExecutarAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = HorarioConfigurado().Ticks
        };
    }

    /// <summary>
    /// Lê o horário da configuração. Valor inválido cai para 03:30, em vez de
    /// deixar a tarefa sem gatilho.
    /// </summary>
    private static TimeSpan HorarioConfigurado()
    {
        var texto = Plugin.Instance?.Configuration?.DailyTime;
        if (!string.IsNullOrWhiteSpace(texto)
            && TimeSpan.TryParseExact(texto.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out var hora)
            && hora >= TimeSpan.Zero
            && hora < TimeSpan.FromDays(1))
        {
            return hora;
        }

        return new TimeSpan(3, 30, 0);
    }
}
