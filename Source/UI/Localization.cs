using System;
using System.Collections.Generic;
using System.Globalization;

namespace MacroDeck
{
    internal static class Localization
    {
        public static string DetectDefaultLanguage()
        {
            string language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return String.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "pt-BR";
        }

        private static readonly Dictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Grave, organize e reproduza ações.", "Record, organize, and replay actions." },
            { "Claro", "Light" }, { "Escuro", "Dark" }, { "Menu", "Menu" },
            { "GRAVAÇÃO E REPRODUÇÃO", "RECORDING AND PLAYBACK" },
            { "Gravar", "Record" }, { "Reproduzir", "Play" }, { "Pronto", "Ready" },
            { "BIBLIOTECA DE MACROS", "MACRO LIBRARY" },
            { "Nome da nova gravação", "New recording name" }, { "Nova gravação", "New recording" },
            { "Macros salvos", "Saved macros" }, { "Nenhum macro salvo", "No saved macros" }, { "Selecione um macro", "Select a macro" },
            { "Atualizar macros", "Refresh saved macros" }, { "Apagar macro selecionado", "Delete selected macro" },
            { "Apagar todos os macros", "Delete all macros" }, { "Salvar gravação", "Save recording" },
            { "Usar Macro Selecionado", "Use Selected Macro" }, { "Macro ativado", "Macro activated" },
            { "Macro selecionado. Clique em Usar Macro Selecionado para ativar.", "Macro selected. Click Use Selected Macro to activate it." },
            { "Carregando macro...", "Loading macro..." },
            { "Salvar alterações", "Save changes" }, { "Salvar como novo macro", "Save as new macro" }, { "Alterações salvas", "Changes saved" },
            { "Já existe um macro com esse nome. Deseja substituir os dados e as opções salvas?", "A macro with this name already exists. Replace its data and saved options?" },
            { "Substituir macro?", "Replace macro?" }, { "Não foi possível salvar as alterações do macro.", "The macro changes could not be saved." },
            { "PASTA DE MACROS", "MACRO FOLDER" }, { "Escolha onde organizar seus macros.", "Choose where to store your macros." },
            { "Escolher pasta", "Choose folder" },
            { "Gravações novas só são salvas ao clicar em Salvar gravação.", "New recordings are saved only when you click Save recording." },
            { "ATALHOS GLOBAIS", "GLOBAL SHORTCUTS" }, { "Gravar/Parar", "Record/Stop" },
            { "Reproduzir/Parar", "Play/Stop" }, { "Definir", "Set" }, { "REPETIÇÃO", "REPEAT" },
            { "Ciclo Infinito", "Repeat indefinitely" }, { "Pausa após cada ciclo", "Pause after each cycle" },
            { "Auto-Clicker", "Auto-Clicker" }, { "Em desenvolvimento", "In development" },
            { "Apaga somente o macro selecionado. O aplicativo pedirá confirmação.", "Deletes only the selected macro. You will be asked to confirm." },
            { "Apaga todos os macros válidos desta pasta. O aplicativo pedirá confirmação.", "Deletes all valid macros in this folder. You will be asked to confirm." },
            { "Salva a gravação atual como um novo macro.", "Saves the current recording as a new macro." },
            { "Atualiza a lista de macros salvos.", "Refreshes the list of saved macros." },
            { "Não definido", "Not set" }, { "Alteração cancelada", "Change canceled" },
            { "Escolha dois atalhos diferentes", "Choose two different shortcuts" }, { "Atalho atualizado", "Shortcut updated" },
            { "Pressione...", "Press..." }, { "Pressione uma tecla ou Esc para cancelar", "Press a key or Esc to cancel" },
            { "Pare a reprodução antes de gravar", "Stop playback before recording" }, { "Gravando", "Recording" },
            { "Gravação vazia", "Empty recording" }, { "Gravação não salva · pronta para testar ou salvar", "Unsaved recording · ready to test or save" },
            { "Gravação pronta para testar ou salvar", "Recording ready to test or save" }, { "Gravação salva", "Recording saved" },
            { "Macro salvo", "Macro saved" }, { "Erro ao salvar", "Save error" },
            { "Não foi possível salvar a gravação.", "The recording could not be saved." }, { "Erro ao salvar gravação", "Save recording error" },
            { "Limite de ações atingido", "Action limit reached" }, { "Pare a gravação antes de reproduzir", "Stop recording before playback" },
            { "Este arquivo não contém ações válidas para reproduzir.", "This file does not contain valid actions to play." },
            { "Macro inválido", "Invalid macro" }, { "Reproduzindo", "Playing" }, { "Erro na reprodução", "Playback error" },
            { "Reprodução parada", "Playback stopped" }, { "Macro finalizado", "Macro finished" }, { "Parar gravação", "Stop recording" },
            { "Parar reprodução", "Stop playback" }, { "Testar gravação", "Test recording" }, { "Abrindo...", "Opening..." },
            { "Não foi possível abrir a seleção de pasta do Windows.", "The Windows folder picker could not be opened." },
            { "Pasta indisponível", "Folder unavailable" }, { "Apagar macro", "Delete macro" }, { "Apagar o macro", "Delete macro" },
            { "Macro apagado", "Macro deleted" }, { "Não foi possível apagar o macro.", "The macro could not be deleted." },
            { "Erro ao apagar", "Delete error" }, { "Erro ao listar macros", "Error listing macros" },
            { "Apagar permanentemente os ", "Permanently delete the " }, { " macros válidos desta pasta?", " valid macros in this folder?" },
            { "Alguns arquivos não puderam ser apagados:", "Some files could not be deleted:" },
            { "Macros apagados", "Macros deleted" }, { "Exclusão incompleta", "Some files were not deleted" },
            { "Opções", "Settings" }, { "Sobre MacroDeck", "About MacroDeck" },
            { "Personalize as cores do tema atual.", "Customize the current theme colors." },
            { "Fundo do aplicativo", "App background" }, { "Caixas e cartões", "Panels and cards" },
            { "Textos", "Text" }, { "Cor de destaque", "Accent color" }, { "Fonte do aplicativo", "App font" },
            { "Idioma do aplicativo", "App language" }, { "Português (Brasil)", "Portuguese (Brazil)" },
            { "Inglês", "English" }, { "A mudança de idioma será aplicada imediatamente.", "The language change will take effect immediately." },
            { "Restaurar cores padrão", "Restore default colors" }, { "Cancelar", "Cancel" }, { "Salvar", "Save" },
            { "Versão 1.1.4", "Version 1.1.4" }, { "Criado por shamanss07", "Created by shamanss07" },
            { "Gravador e reprodutor de macros para Windows.\r\nSeus macros e preferências ficam no computador.", "A macro recorder and player for Windows.\r\nYour macros and preferences stay on your computer." },
            { "Fonte Montserrat · SIL Open Font License 1.1", "Montserrat font · SIL Open Font License 1.1" },
            { "Ver licença da fonte", "View font license" }, { "Fechar", "Close" }, { "Licença Montserrat", "Montserrat License" },
            { "Escolher cor", "Choose color" }, { "Personalizar cor", "Customize color" },
            { "Digite um valor hexadecimal ou ajuste matiz, saturação e brilho.", "Enter a hex value or adjust hue, saturation, and brightness." },
            { "Prévia", "Preview" }, { "Matiz", "Hue" }, { "Saturação", "Saturation" }, { "Brilho", "Brightness" },
            { "Cores rápidas", "Quick colors" }, { "Usar cor", "Use color" },
            { "Captura indisponível", "Input capture unavailable" },
            { "Não foi possível iniciar a captura global de teclado e mouse.", "Global keyboard and mouse capture could not be started." },
            { "Execute este programa em uma sessão interativa do Windows.", "Run this application in an interactive Windows session." },
            { "Fechar janela", "Close window" }, { "Maximizar ou restaurar janela", "Maximize or restore window" },
            { "Minimizar janela", "Minimize window" },
            { "Aumentar valor", "Increase value" }, { "Diminuir valor", "Decrease value" },
            { "permanentemente?", "permanently?" }
        };

        public static bool IsEnglish(string language)
        {
            return String.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase);
        }

        public static string Translate(string language, string portugueseText)
        {
            if (!IsEnglish(language) || String.IsNullOrEmpty(portugueseText)) return portugueseText;
            string translated;
            return English.TryGetValue(portugueseText, out translated) ? translated : portugueseText;
        }

        public static string LanguageName(string language)
        {
            return IsEnglish(language) ? "English" : "Português (Brasil)";
        }
    }
}
