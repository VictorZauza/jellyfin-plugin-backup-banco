using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.BackupBanco.Models;
using Jellyfin.Plugin.BackupBanco.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BackupBanco.Api;

/// <summary>
/// Endpoints do painel do plugin. Todos exigem administrador.
/// </summary>
[ApiController]
[Route("BackupBanco")]
[Authorize(Policy = Policies.RequiresElevation)]
public class BackupController : ControllerBase
{
    // Só nomes gerados pelo próprio plugin são aceitos. Isso impede que um
    // nome manipulado escape da pasta de backups e alcance outro arquivo.
    private static readonly Regex _nomeValido = new Regex(
        @"^jellyfin-backup-\d{8}-\d{6}\.zip$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly BackupService _service;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<BackupController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackupController"/> class.
    /// </summary>
    /// <param name="service">Serviço de backup.</param>
    /// <param name="taskManager">Gerenciador de tarefas do Jellyfin.</param>
    /// <param name="logger">Logger.</param>
    public BackupController(BackupService service, ITaskManager taskManager, ILogger<BackupController> logger)
    {
        _service = service;
        _taskManager = taskManager;
        _logger = logger;
    }

    /// <summary>
    /// Situação atual do plugin.
    /// </summary>
    /// <returns>O status.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<StatusDto> GetStatus()
    {
        var config = Plugin.Instance?.Configuration;
        return Ok(new StatusDto
        {
            DestinationPath = _service.PastaDestino,
            LastRunUtc = config?.LastRunUtc,
            LastResult = config?.LastResult ?? string.Empty,
            OnlineBackupAvailable = BackupService.BackupOnlineDisponivel()
        });
    }

    /// <summary>
    /// Lista os backups existentes.
    /// </summary>
    /// <returns>Os arquivos, do mais novo para o mais antigo.</returns>
    [HttpGet("Backups")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<BackupDto>> GetBackups() => Ok(_service.Listar());

    /// <summary>
    /// Enfileira um backup imediato.
    /// </summary>
    /// <returns>Sem conteúdo.</returns>
    /// <remarks>
    /// Enfileira em vez de executar na própria requisição: um backup grande
    /// levaria mais tempo que o limite do navegador. O andamento aparece em
    /// Painel → Tarefas Agendadas, e o resultado no status deste plugin.
    /// </remarks>
    [HttpPost("Run")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Run()
    {
        _logger.LogInformation("Backup solicitado manualmente pelo painel.");
        _taskManager.QueueScheduledTask<BackupScheduledTask>(new TaskOptions());
        return NoContent();
    }

    /// <summary>
    /// Baixa um backup.
    /// </summary>
    /// <param name="fileName">Nome do arquivo.</param>
    /// <returns>O arquivo .zip.</returns>
    [HttpGet("Backups/{fileName}/Download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Download([FromRoute] string fileName)
    {
        if (!_nomeValido.IsMatch(fileName ?? string.Empty))
        {
            return BadRequest("Nome de arquivo inválido.");
        }

        var caminho = Path.Combine(_service.PastaDestino, fileName!);
        if (!System.IO.File.Exists(caminho))
        {
            return NotFound();
        }

        var fluxo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(fluxo, "application/zip", fileName);
    }

    /// <summary>
    /// Apaga um backup.
    /// </summary>
    /// <param name="fileName">Nome do arquivo.</param>
    /// <returns>Sem conteúdo.</returns>
    [HttpDelete("Backups/{fileName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Delete([FromRoute] string fileName)
    {
        if (!_nomeValido.IsMatch(fileName ?? string.Empty))
        {
            return BadRequest("Nome de arquivo inválido.");
        }

        var caminho = Path.Combine(_service.PastaDestino, fileName!);
        if (!System.IO.File.Exists(caminho))
        {
            return NotFound();
        }

        try
        {
            System.IO.File.Delete(caminho);
            _logger.LogInformation("Backup removido pelo painel: {Arquivo}", fileName);
            return NoContent();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Não foi possível remover {Arquivo}.", fileName);
            return Problem("Não foi possível remover o arquivo.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
