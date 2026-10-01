using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.BackupBanco.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.BackupBanco;

/// <summary>
/// Plugin que gera cópias de segurança dos bancos e das configurações do Jellyfin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Caminhos da aplicação.</param>
    /// <param name="xmlSerializer">Serializador XML.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets a instância atual do plugin.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Backup do Banco";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("7c2a1e94-5b6d-4f08-9a31-2e8c4d7b15af");

    /// <inheritdoc />
    public override string Description =>
        "Copia os bancos de dados e as configurações do Jellyfin para um arquivo .zip, "
        + "diariamente ou sob demanda, mantendo apenas os mais recentes.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = Name,
                EnableInMainMenu = true,
                MenuIcon = "backup",
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        };
    }

    /// <summary>
    /// Grava a configuração atual em disco.
    /// </summary>
    public void Save() => SaveConfiguration();
}
