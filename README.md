# Backup do Banco — plugin para Jellyfin

Copia os bancos de dados e os arquivos de configuração do Jellyfin para um `.zip`,
diariamente ou sob demanda, mantendo apenas os mais recentes.

Feito para o caso em que uma atualização do servidor ou de um plugin leva junto
usuários, bibliotecas ou progresso de reprodução.

## O que entra no backup

- **Bancos de dados** — usuários, bibliotecas, progresso, favoritos.
- **Configuração do servidor** — os XML de rede, codificação e bibliotecas.
- **Configuração dos plugins** — o que cada plugin instalado guardou.

Capas, metadados e cache ficam **de fora de propósito**: são recriáveis pelo próprio
Jellyfin e deixariam o arquivo grande demais para servir como cópia de segurança.

## Como os bancos são copiados

Pelo *backup online* do SQLite, que produz uma cópia consistente mesmo com o
servidor em uso — diferente de copiar o arquivo aberto, que pode gerar um banco
corrompido.

A biblioteca do SQLite é localizada **em tempo de execução**, no que o próprio
servidor já tem carregado, em vez de ser trazida como dependência do plugin. Isso
evita conflito de versão quando o Jellyfin é atualizado. Se ela não for encontrada,
o plugin copia os arquivos `.db`, `-wal` e `-shm` e avisa no painel que a
consistência não está garantida.

## Instalação

No Jellyfin, em *Painel → Plugins → Repositórios*, adicione:

```
https://raw.githubusercontent.com/SEU-USUARIO/jellyfin-plugin-backup-banco/main/manifest.json
```

O plugin aparece no catálogo. Instale e reinicie o servidor.

## Uso

O painel fica em *Painel → Backup do Banco*, onde dá para:

- ver a pasta de destino, a última execução e o resultado;
- disparar um backup na hora;
- baixar ou apagar os backups existentes;
- escolher a pasta, o horário diário, a retenção e o que incluir.

A execução automática também aparece em *Painel → Tarefas Agendadas*, como
"Backup do banco e das configurações", e pode ser reagendada por lá.

### Pasta de destino

O caminho é **dentro do contêiner**. Vazio usa `plugin-backups` ao lado da pasta de
configuração do servidor. Para guardar fora do contêiner, aponte para um caminho
que esteja montado por volume.

## Restaurar

1. Pare o Jellyfin.
2. Abra o `.zip` e leia o `manifesto.txt`, que registra de onde cada arquivo veio.
3. Substitua os arquivos nas pastas de origem.
4. Suba o servidor.

## Compilar e publicar

```bash
./publicar.sh SEU-USUARIO-GITHUB
git add -A && git commit -m "v1.0.0.0" && git push
```

O script compila, gera o zip em `dist/`, calcula o checksum e escreve o
`manifest.json` preservando as versões anteriores.
