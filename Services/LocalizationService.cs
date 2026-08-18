using System;
using System.Collections.Generic;
using Microsoft.Maui.Storage;

namespace Aplicacion_SCA.Services
{
    public static class LocalizationService
    {
        public enum Language
        {
            Spanish,
            English,
            French,
            German
        }

        private const string LanguagePreferenceKey = "UserSelectedLanguage";

        private static Language _currentLanguage = Language.Spanish;

        public static Language CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    Preferences.Set(LanguagePreferenceKey, (int)value);
                }
            }
        }

        static LocalizationService()
        {
            // Load saved language or default to Spanish
            int savedLanguage = Preferences.Get(LanguagePreferenceKey, (int)Language.Spanish);
            if (Enum.IsDefined(typeof(Language), savedLanguage))
            {
                _currentLanguage = (Language)savedLanguage;
            }
        }

        private static readonly Dictionary<string, Dictionary<Language, string>> Translations = new()
        {
            // PlantSelectionPage
            { "PLANT_SELECT_TITLE", new() {
                { Language.Spanish, "SELECCIONA LA PLANTA" },
                { Language.English, "SELECT PLANT" },
                { Language.French, "SÉLECTIONNEZ L'USINE" },
                { Language.German, "WERK AUSWÄHLEN" }
            } },
            { "PLANT_SELECT_SUBTITLE", new() {
                { Language.Spanish, "Elige la planta cuya auditoría C-DPV quieres realizar" },
                { Language.English, "Choose the plant whose C-DPV audit you want to run" },
                { Language.French, "Choisissez l'usine dont vous souhaitez réaliser l'audit C-DPV" },
                { Language.German, "Wählen Sie das Werk, dessen C-DPV-Audit Sie durchführen möchten" }
            } },
            { "MSG_CARGANDO_VEHICULOS", new() {
                { Language.Spanish, "Cargando vehículos de la planta..." },
                { Language.English, "Loading plant vehicles..." },
                { Language.French, "Chargement des véhicules de l'usine..." },
                { Language.German, "Fahrzeuge des Werks werden geladen..." }
            } },
            { "ERR_VEHICULOS_PLANTA", new() {
                { Language.Spanish, "No se pudieron descargar los vehículos de esta planta. Comprueba que exista el archivo Vehiculos.xlsx en su carpeta de SharePoint." },
                { Language.English, "Could not download this plant's vehicles. Check that Vehiculos.xlsx exists in its SharePoint folder." },
                { Language.French, "Impossible de télécharger les véhicules de cette usine. Vérifiez que Vehiculos.xlsx existe dans son dossier SharePoint." },
                { Language.German, "Die Fahrzeuge dieses Werks konnten nicht heruntergeladen werden. Prüfen Sie, ob Vehiculos.xlsx im SharePoint-Ordner vorhanden ist." }
            } },

            // MainPage
            { "MAIN_TITLE", new() { 
                { Language.Spanish, "SISTEMA DE CONTROL DE AUDITORÍA" }, 
                { Language.English, "AUDIT CONTROL SYSTEM" }, 
                { Language.French, "SYSTÈME DE CONTRÔLE D'AUDIT" }, 
                { Language.German, "AUDIT-KONTROLLSYSTEM" } 
            } },
            { "BTN_EMPEZAR", new() { 
                { Language.Spanish, "EMPEZAR" }, 
                { Language.English, "START" }, 
                { Language.French, "COMMENCER" }, 
                { Language.German, "STARTEN" } 
            } },
            { "MSG_CARGANDO_DATOS", new() { 
                { Language.Spanish, "CARGANDO DATOS..." }, 
                { Language.English, "LOADING DATA..." }, 
                { Language.French, "CHARGEMENT DES DONNÉES..." }, 
                { Language.German, "DATEN LADEN..." } 
            } },
            { "ERR_CONEXION_TITULO", new() { 
                { Language.Spanish, "Error de Conexión" }, 
                { Language.English, "Connection Error" }, 
                { Language.French, "Erreur de Connexion" }, 
                { Language.German, "Verbindungsfehler" } 
            } },
            { "ERR_CONEXION_MSG", new() { 
                { Language.Spanish, "Hubo un problema al descargar los datos iniciales:\n\n" }, 
                { Language.English, "There was a problem downloading initial data:\n\n" }, 
                { Language.French, "Un problème est survenu lors du téléchargement des données initiales :\n\n" }, 
                { Language.German, "Beim Herunterladen der Anfangsdaten ist ein Problem aufgetreten:\n\n" } 
            } },
            { "BTN_REINTENTAR", new() { 
                { Language.Spanish, "Reintentar" }, 
                { Language.English, "Retry" }, 
                { Language.French, "Réessayer" }, 
                { Language.German, "Wiederholen" } 
            } },

            // LoginPage
            { "LOGIN_TITLE", new() { 
                { Language.Spanish, "ACCESO AUDITOR" }, 
                { Language.English, "AUDITOR ACCESS" }, 
                { Language.French, "ACCÈS AUDITEUR" }, 
                { Language.German, "AUDITOR-ZUGANG" } 
            } },
            { "PLACEHOLDER_USER", new() { 
                { Language.Spanish, "Usuario" }, 
                { Language.English, "Username" }, 
                { Language.French, "Utilisateur" }, 
                { Language.German, "Benutzername" } 
            } },
            { "PLACEHOLDER_PASS", new() { 
                { Language.Spanish, "Contraseña" }, 
                { Language.English, "Password" }, 
                { Language.French, "Mot de passe" }, 
                { Language.German, "Kennwort" } 
            } },
            { "BTN_ENTRAR", new() { 
                { Language.Spanish, "ENTRAR" }, 
                { Language.English, "ENTER" }, 
                { Language.French, "ENTRER" }, 
                { Language.German, "EINTRETEN" } 
            } },
            { "MSG_CONECTANDO", new() { 
                { Language.Spanish, "CONECTANDO..." }, 
                { Language.English, "CONNECTING..." }, 
                { Language.French, "CONNEXION..." }, 
                { Language.German, "VERBINDEN..." } 
            } },
            { "ALERT_ATENCION", new() { 
                { Language.Spanish, "Atención" }, 
                { Language.English, "Warning" }, 
                { Language.French, "Attention" }, 
                { Language.German, "Achtung" } 
            } },
            { "ALERT_LOGIN_VACIO", new() { 
                { Language.Spanish, "Escribe tu usuario y contraseña." }, 
                { Language.English, "Please enter your username and password." }, 
                { Language.French, "Veuillez saisir votre utilisateur et mot de passe." }, 
                { Language.German, "Bitte geben Sie Ihren Benutzernamen und Ihr Passwort ein." } 
            } },
            { "ALERT_LOGIN_DENEGADO", new() { 
                { Language.Spanish, "Acceso Denegado" }, 
                { Language.English, "Access Denied" }, 
                { Language.French, "Accès Refusé" }, 
                { Language.German, "Zugriff Verweigert" } 
            } },
            { "ALERT_LOGIN_ERR", new() { 
                { Language.Spanish, "Credenciales incorrectas o usuario no registrado." }, 
                { Language.English, "Incorrect credentials or unregistered user." }, 
                { Language.French, "Identifiants incorrects ou utilisateur non enregistré." }, 
                { Language.German, "Ungültige Anmeldedaten oder nicht registrierter Benutzer." } 
            } },
            { "ALERT_BIENVENIDO", new() { 
                { Language.Spanish, "Bienvenido" }, 
                { Language.English, "Welcome" }, 
                { Language.French, "Bienvenue" }, 
                { Language.German, "Willkommen" } 
            } },
            { "BTN_ENTRAR_SISTEMA", new() { 
                { Language.Spanish, "Entrar al Sistema" }, 
                { Language.English, "Enter System" }, 
                { Language.French, "Entrer dans le Système" }, 
                { Language.German, "System betreten" } 
            } },

            // AuditModePage
            { "MSG_HOLA", new() { 
                { Language.Spanish, "Hola, " }, 
                { Language.English, "Hello, " }, 
                { Language.French, "Bonjour, " }, 
                { Language.German, "Hallo, " } 
            } },
            { "MSG_SIN_TURNO", new() { 
                { Language.Spanish, "Sin turno definido" }, 
                { Language.English, "No shift defined" }, 
                { Language.French, "Aucune équipe définie" }, 
                { Language.German, "Keine Schicht definiert" } 
            } },
            { "MSG_BIENVENIDA_CENTRO", new() { 
                { Language.Spanish, "Bienvenida/o al Centro de Control" }, 
                { Language.English, "Welcome to the Control Center" }, 
                { Language.French, "Bienvenue au Centre de Contrôle" }, 
                { Language.German, "Willkommen im Kontrollzentrum" } 
            } },
            { "HEADER_MODO_AUDITORIA", new() { 
                { Language.Spanish, "MODO DE AUDITORÍA" }, 
                { Language.English, "AUDIT MODE" }, 
                { Language.French, "MODE D'AUDIT" }, 
                { Language.German, "AUDIT-MODUS" } 
            } },
            { "BTN_JAPONES", new() { 
                { Language.Spanish, "CONTROL JAPÓN" }, 
                { Language.English, "JAPAN CONTROL" }, 
                { Language.French, "CONTRÔLE JAPON" }, 
                { Language.German, "JAPAN-KONTROLLE" } 
            } },
            { "BTN_FORMACION", new() { 
                { Language.Spanish, "FORMACIÓN SCA" }, 
                { Language.English, "SCA TRAINING" }, 
                { Language.French, "FORMATION SCA" }, 
                { Language.German, "SCA-SCHULUNG" } 
            } },
            { "CONFIRM_LOGOUT_TITLE", new() { 
                { Language.Spanish, "Cerrar Sesión" }, 
                { Language.English, "Logout" }, 
                { Language.French, "Se déconnecter" }, 
                { Language.German, "Abmelden" } 
            } },
            { "CONFIRM_LOGOUT_MSG", new() { 
                { Language.Spanish, "¿Estás seguro de que quieres salir y volver al inicio?" }, 
                { Language.English, "Are you sure you want to logout and return to start?" }, 
                { Language.French, "Êtes-vous sûr de vouloir vous déconnecter et revenir au début?" }, 
                { Language.German, "Sind Sie sicher, dass Sie sich abmelden und zum Start zurückkehren möchten?" } 
            } },
            { "BTN_SI_SALIR", new() { 
                { Language.Spanish, "Sí, Salir" }, 
                { Language.English, "Yes, Exit" }, 
                { Language.French, "Oui, Quitter" }, 
                { Language.German, "Ja, Verlassen" } 
            } },
            { "BTN_CANCELAR", new() { 
                { Language.Spanish, "Cancelar" }, 
                { Language.English, "Cancel" }, 
                { Language.French, "Annuler" }, 
                { Language.German, "Abbrechen" } 
            } },
            { "OVERLAY_DOCUMENTOS", new() { 
                { Language.Spanish, "DOCUMENTOS" }, 
                { Language.English, "DOCUMENTS" }, 
                { Language.French, "DOCUMENTS" }, 
                { Language.German, "DOKUMENTE" } 
            } },
            { "BTN_CERRAR", new() { 
                { Language.Spanish, "CERRAR" }, 
                { Language.English, "CLOSE" }, 
                { Language.French, "FERMER" }, 
                { Language.German, "SCHLIESSEN" } 
            } },
            { "OVERLAY_AVISOS", new() { 
                { Language.Spanish, "AVISOS Y NOVEDADES" }, 
                { Language.English, "NOTICES & NEWS" }, 
                { Language.French, "AVIS ET NOUVEAUTÉS" }, 
                { Language.German, "HINWEISE & NEUIGKEITEN" } 
            } },
            { "BTN_ENTENDIDO", new() { 
                { Language.Spanish, "ENTENDIDO" }, 
                { Language.English, "UNDERSTOOD" }, 
                { Language.French, "COMPRIS" }, 
                { Language.German, "VERSTANDEN" } 
            } },
            { "MSG_NO_NOVEDADES", new() { 
                { Language.Spanish, "No hay novedades por el momento." }, 
                { Language.English, "There are no updates at the moment." }, 
                { Language.French, "Il n'y a pas de nouveautés pour le moment." }, 
                { Language.German, "Im Moment gibt es keine Neuigkeiten." } 
            } },
            { "MSG_CARGANDO_MANUALES", new() { 
                { Language.Spanish, "Cargando manuales..." }, 
                { Language.English, "Loading manuals..." }, 
                { Language.French, "Chargement des manuels..." }, 
                { Language.German, "Handbücher werden geladen..." } 
            } },
            { "TITLE_VER_MANUAL", new() { 
                { Language.Spanish, "Ver Manual" }, 
                { Language.English, "View Manual" }, 
                { Language.French, "Voir le manuel" }, 
                { Language.German, "Handbuch anzeigen" } 
            } },
            { "ERR_DESCARGA_MANUAL", new() { 
                { Language.Spanish, "No se pudo descargar el manual:\n" }, 
                { Language.English, "Could not download the manual:\n" }, 
                { Language.French, "Impossible de télécharger le manuel :\n" }, 
                { Language.German, "Handbuch konnte nicht heruntergeladen werden:\n" } 
            } },

            // SelectionPage
            { "SELECTION_TITLE", new() { 
                { Language.Spanish, "MODO SELECCIONADO" }, 
                { Language.English, "SELECTED MODE" }, 
                { Language.French, "MODE SÉLECTIONNÉ" }, 
                { Language.German, "AUSGEWÄHLTER MODUS" } 
            } },
            { "BTN_VOLVER", new() { 
                { Language.Spanish, "← Volver" }, 
                { Language.English, "← Back" }, 
                { Language.French, "← Retour" }, 
                { Language.German, "← Zurück" } 
            } },
            { "HEADER_RODAJE", new() { 
                { Language.Spanish, "1. RODAJE EXTERIOR" }, 
                { Language.English, "1. EXTERIOR ROAD TEST" }, 
                { Language.French, "1. ESSAI ROUTIER EXTÉRIEUR" }, 
                { Language.German, "1. STRASSENTEST EXTERN" } 
            } },
            { "SWITCH_RODAJE", new() { 
                { Language.Spanish, "Incluir prueba exterior" }, 
                { Language.English, "Include exterior test" }, 
                { Language.French, "Inclure essai extérieur" }, 
                { Language.German, "Außentest einschließen" } 
            } },
            { "HEADER_MODELO", new() { 
                { Language.Spanish, "2. SELECCIONE MODELO" }, 
                { Language.English, "2. SELECT MODEL" }, 
                { Language.French, "2. SÉLECTIONNER MODÈLE" }, 
                { Language.German, "2. MODELL AUSWÄHLEN" } 
            } },
            { "HEADER_MOTOR", new() { 
                { Language.Spanish, "3. SELECCIONE MOTORIZACIÓN" }, 
                { Language.English, "3. SELECT ENGINE" }, 
                { Language.French, "3. SÉLECTIONNER MOTORISATION" }, 
                { Language.German, "3. MOTORISIERUNG AUSWÄHLEN" } 
            } },
            { "BTN_COMENZAR", new() { 
                { Language.Spanish, "COMENZAR INSPECCIÓN" }, 
                { Language.English, "START INSPECTION" }, 
                { Language.French, "COMMENCER L'INSPECTION" }, 
                { Language.German, "INSPEKTION STARTEN" } 
            } },
            { "MSG_DESCARGANDO_DATOS", new() { 
                { Language.Spanish, "DESCARGANDO DATOS..." }, 
                { Language.English, "DOWNLOADING DATA..." }, 
                { Language.French, "TÉLÉCHARGEMENT DES DONNÉES..." }, 
                { Language.German, "DATEN WERDEN HERUNTERGELADEN..." } 
            } },
            { "ALERT_FALTAN_DATOS", new() { 
                { Language.Spanish, "Faltan Datos" }, 
                { Language.English, "Missing Data" }, 
                { Language.French, "Données Manquantes" }, 
                { Language.German, "Fehlende Daten" } 
            } },
            { "ALERT_FALTAN_DATOS_MSG", new() { 
                { Language.Spanish, "Por favor, selecciona el modelo y la motorización." }, 
                { Language.English, "Please select the model and the engine." }, 
                { Language.French, "Veuillez sélectionner le modèle et la motorisation." }, 
                { Language.German, "Bitte wählen Sie das Modell und die Motorisierung aus." } 
            } },
            { "CONFIRMATION", new() { 
                { Language.Spanish, "Confirmación" }, 
                { Language.English, "Confirmation" }, 
                { Language.French, "Confirmation" }, 
                { Language.German, "Bestätigung" } 
            } },
            { "CONFIRM_EXIT_SELECTION", new() { 
                { Language.Spanish, "¿Estás seguro de que quieres salir de {0}?" }, 
                { Language.English, "Are you sure you want to exit {0}?" }, 
                { Language.French, "Êtes-vous sûr de vouloir quitter {0}?" }, 
                { Language.German, "Sind Sie sicher, dass Sie {0} verlassen möchten?" } 
            } },
            { "ERR_FASES_SHAREPOINT", new() { 
                { Language.Spanish, "No se pudo descargar el archivo de fases desde SharePoint. Revisa tu conexión." }, 
                { Language.English, "Could not download phases file from SharePoint. Check your connection." }, 
                { Language.French, "Impossible de télécharger le fichier de phases depuis SharePoint. Vérifiez votre connexion." }, 
                { Language.German, "Die Phasendatei konnte nicht von SharePoint heruntergeladen werden. Überprüfen Sie Ihre Verbindung." } 
            } },
            { "ALERT_INTERRUMPIDO", new() { 
                { Language.Spanish, "Auditoría Interrumpida" }, 
                { Language.English, "Interrupted Audit" }, 
                { Language.French, "Audit Interrompu" }, 
                { Language.German, "Unterbrochenes Audit" } 
            } },
            { "ALERT_INTERRUMPIDO_MSG", new() { 
                { Language.Spanish, "Se ha detectado una inspección sin terminar tras un cierre inesperado. ¿Deseas recuperar el progreso?" }, 
                { Language.English, "An unfinished inspection has been detected after an unexpected close. Do you want to recover progress?" }, 
                { Language.French, "Une inspection inachevée a été détectée après une fermeture inattendue. Voulez-vous récupérer la progression ?" }, 
                { Language.German, "Nach einem unerwarteten Schließen wurde eine unvollständige Inspektion erkannt. Möchten Sie den Fortschritt wiederherstellen?" } 
            } },
            { "BTN_RECUPERAR_PROGRESO", new() { 
                { Language.Spanish, "Sí, recuperar progreso" }, 
                { Language.English, "Yes, recover progress" }, 
                { Language.French, "Oui, récupérer la progression" }, 
                { Language.German, "Ja, Fortschritt wiederherstellen" } 
            } },
            { "BTN_EMPEZAR_CERO", new() { 
                { Language.Spanish, "No, empezar de cero" }, 
                { Language.English, "No, start fresh" }, 
                { Language.French, "Non, recommencer à zéro" }, 
                { Language.German, "Nein, neu starten" } 
            } },
            { "MSG_RECUPERANDO", new() { 
                { Language.Spanish, "RECUPERANDO SESIÓN..." }, 
                { Language.English, "RECOVERING SESSION..." }, 
                { Language.French, "RÉCUPÉRATION DE LA SESSION..." }, 
                { Language.German, "SITZUNG WIEDERHERSTELLEN..." } 
            } },
            { "ERR_RRU_SHAREPOINT", new() { 
                { Language.Spanish, "No se pudo descargar el archivo de RRU desde SharePoint. Revisa tu conexión." }, 
                { Language.English, "Could not download RRU file from SharePoint. Check your connection." }, 
                { Language.French, "Impossible de télécharger le fichier RRU depuis SharePoint. Vérifiez votre connexion." }, 
                { Language.German, "Die RRU-Datei konnte nicht von SharePoint heruntergeladen werden. Überprüfen Sie Ihre Verbindung." }
            } },

            // Common alert buttons / titles
            { "BTN_ACEPTAR", new() {
                { Language.Spanish, "Aceptar" },
                { Language.English, "Accept" },
                { Language.French, "Accepter" },
                { Language.German, "Akzeptieren" }
            } },
            { "BTN_ENTENDIDO_OK", new() {
                { Language.Spanish, "Entendido" },
                { Language.English, "Got it" },
                { Language.French, "Compris" },
                { Language.German, "Verstanden" }
            } },
            { "ERR_TITULO", new() {
                { Language.Spanish, "Error" },
                { Language.English, "Error" },
                { Language.French, "Erreur" },
                { Language.German, "Fehler" }
            } },
            { "BTN_SI_ENVIAR", new() {
                { Language.Spanish, "Sí, enviar" },
                { Language.English, "Yes, send" },
                { Language.French, "Oui, envoyer" },
                { Language.German, "Ja, senden" }
            } },

            // MenuEstandarPage
            { "ERR_ABRIR_MANUAL", new() {
                { Language.Spanish, "No se pudo abrir el manual: " },
                { Language.English, "Could not open the manual: " },
                { Language.French, "Impossible d'ouvrir le manuel : " },
                { Language.German, "Handbuch konnte nicht geöffnet werden: " }
            } },
            { "ALERT_NO_APLICA", new() {
                { Language.Spanish, "No Aplica" },
                { Language.English, "Not Applicable" },
                { Language.French, "Non Applicable" },
                { Language.German, "Nicht Zutreffend" }
            } },
            { "ALERT_RODAJE_DESACTIVADO", new() {
                { Language.Spanish, "El rodaje exterior está desactivado en la configuración actual." },
                { Language.English, "The exterior road test is disabled in the current configuration." },
                { Language.French, "L'essai routier extérieur est désactivé dans la configuration actuelle." },
                { Language.German, "Der externe Straßentest ist in der aktuellen Konfiguration deaktiviert." }
            } },
            { "ALERT_FASE_COMPLETADA", new() {
                { Language.Spanish, "Fase Completada" },
                { Language.English, "Phase Completed" },
                { Language.French, "Phase Terminée" },
                { Language.German, "Phase Abgeschlossen" }
            } },
            { "ALERT_FASE_COMPLETADA_MSG", new() {
                { Language.Spanish, "Ya has completado esta fase. Si vuelves a entrar, perderás el progreso que tenías guardado de la misma. ¿Estás seguro de que deseas repetirla?" },
                { Language.English, "You have already completed this phase. If you enter again, you will lose the progress saved for it. Are you sure you want to repeat it?" },
                { Language.French, "Vous avez déjà terminé cette phase. Si vous entrez à nouveau, vous perdrez la progression enregistrée. Êtes-vous sûr de vouloir la répéter ?" },
                { Language.German, "Sie haben diese Phase bereits abgeschlossen. Wenn Sie erneut eintreten, geht der gespeicherte Fortschritt verloren. Möchten Sie sie wirklich wiederholen?" }
            } },
            { "BTN_SI_REPETIR", new() {
                { Language.Spanish, "Sí, repetir" },
                { Language.English, "Yes, repeat" },
                { Language.French, "Oui, répéter" },
                { Language.German, "Ja, wiederholen" }
            } },
            { "ALERT_FALTA_CHASIS", new() {
                { Language.Spanish, "Falta el Chasis" },
                { Language.English, "Chassis Missing" },
                { Language.French, "Châssis Manquant" },
                { Language.German, "Fahrgestell Fehlt" }
            } },
            { "ALERT_FALTA_CHASIS_MSG", new() {
                { Language.Spanish, "Escribe el VIN antes de finalizar." },
                { Language.English, "Enter the VIN before finishing." },
                { Language.French, "Saisissez le VIN avant de terminer." },
                { Language.German, "Geben Sie die FIN ein, bevor Sie abschließen." }
            } },
            { "ALERT_CONFIRMAR_SALIDA", new() {
                { Language.Spanish, "Confirmar salida" },
                { Language.English, "Confirm exit" },
                { Language.French, "Confirmer la sortie" },
                { Language.German, "Beenden bestätigen" }
            } },
            { "ALERT_SALIR_PROGRESO_MSG", new() {
                { Language.Spanish, "¿Estás seguro? Se perderá el progreso." },
                { Language.English, "Are you sure? Progress will be lost." },
                { Language.French, "Êtes-vous sûr ? La progression sera perdue." },
                { Language.German, "Sind Sie sicher? Der Fortschritt geht verloren." }
            } },
            { "TITLE_GUIA_RAPIDA", new() {
                { Language.Spanish, "GUÍA RÁPIDA DE AUDITORÍA" },
                { Language.English, "QUICK AUDIT GUIDE" },
                { Language.French, "GUIDE RAPIDE D'AUDIT" },
                { Language.German, "SCHNELLANLEITUNG AUDIT" }
            } },
            { "MSG_GUIA_RAPIDA", new() {
                { Language.Spanish, "Audio y Reproducción: El sistema guiará los pasos. Cuando el indicador cambie a verde y aparezca el botón de validación, el sistema espera tu instrucción.\r\n\r\nComandos de Voz: Usa frases cortas y claras para avanzar:\r\n\r\n\"Siguiente\", \"Ok\", \"Vale\", \"Continuar\" para validar.\r\n\r\n\"Repetir\" para escuchar la instrucción de nuevo.\r\n\r\n\"Atrás\" o \"Anterior\" para volver al paso previo.\r\n\r\nConexión Bluetooth: Asegúrate de conectar tus auriculares antes de pulsar \"COMENZAR\". Si cambias de dispositivo, reinicia la app.\r\n\r\nControles Telemáticos: Si necesitas realizar pruebas externas (telemática) que no siguen el flujo, pulsa PAUSAR. Esto detendrá el cronómetro y la navegación automática hasta que reanudes." },
                { Language.English, "Audio & Playback: The system will guide the steps. When the indicator turns green and the validation button appears, the system awaits your instruction.\r\n\r\nVoice Commands: Use short, clear phrases to advance:\r\n\r\n\"Next\", \"Ok\", \"Okay\", \"Continue\" to validate.\r\n\r\n\"Repeat\" to hear the instruction again.\r\n\r\n\"Back\" or \"Previous\" to return to the previous step.\r\n\r\nBluetooth Connection: Make sure to connect your headset before pressing \"START\". If you change device, restart the app.\r\n\r\nTelematic Controls: If you need to run external (telematic) tests that do not follow the flow, press PAUSE. This will stop the timer and automatic navigation until you resume." },
                { Language.French, "Audio et Lecture : Le système guidera les étapes. Lorsque l'indicateur passe au vert et que le bouton de validation apparaît, le système attend votre instruction.\r\n\r\nCommandes Vocales : Utilisez des phrases courtes et claires pour avancer :\r\n\r\n\"Suivant\", \"Ok\", \"D'accord\", \"Continuer\" pour valider.\r\n\r\n\"Répéter\" pour réécouter l'instruction.\r\n\r\n\"Retour\" ou \"Précédent\" pour revenir à l'étape précédente.\r\n\r\nConnexion Bluetooth : Assurez-vous de connecter votre casque avant d'appuyer sur \"COMMENCER\". Si vous changez d'appareil, redémarrez l'application.\r\n\r\nContrôles Télématiques : Si vous devez effectuer des tests externes (télématique) hors du flux, appuyez sur PAUSE. Cela arrêtera le chronomètre et la navigation automatique jusqu'à la reprise." },
                { Language.German, "Audio & Wiedergabe: Das System führt durch die Schritte. Wenn die Anzeige grün wird und die Validierungsschaltfläche erscheint, wartet das System auf Ihre Anweisung.\r\n\r\nSprachbefehle: Verwenden Sie kurze, klare Sätze zum Fortfahren:\r\n\r\n\"Weiter\", \"Ok\", \"Okay\", \"Fortfahren\" zum Bestätigen.\r\n\r\n\"Wiederholen\", um die Anweisung erneut zu hören.\r\n\r\n\"Zurück\" oder \"Vorherige\", um zum vorherigen Schritt zurückzukehren.\r\n\r\nBluetooth-Verbindung: Verbinden Sie Ihr Headset, bevor Sie \"STARTEN\" drücken. Wenn Sie das Gerät wechseln, starten Sie die App neu.\r\n\r\nTelematik-Kontrollen: Wenn Sie externe (Telematik-)Tests außerhalb des Ablaufs durchführen müssen, drücken Sie PAUSE. Dies stoppt den Timer und die automatische Navigation, bis Sie fortfahren." }
            } },

            // EstandarPage
            { "ERR_DESCARGAR", new() {
                { Language.Spanish, "No se pudo descargar:\n" },
                { Language.English, "Could not download:\n" },
                { Language.French, "Impossible de télécharger :\n" },
                { Language.German, "Herunterladen fehlgeschlagen:\n" }
            } },
            { "ALERT_INSTRUCCIONES_CURSO", new() {
                { Language.Spanish, "Instrucciones en curso" },
                { Language.English, "Instructions in progress" },
                { Language.French, "Instructions en cours" },
                { Language.German, "Anweisungen laufen" }
            } },
            { "ALERT_INSTRUCCIONES_CURSO_MSG", new() {
                { Language.Spanish, "El audio aún no ha terminado. ¿Estás seguro de que deseas finalizar esta fase?" },
                { Language.English, "The audio has not finished yet. Are you sure you want to finish this phase?" },
                { Language.French, "L'audio n'est pas encore terminé. Êtes-vous sûr de vouloir terminer cette phase ?" },
                { Language.German, "Das Audio ist noch nicht beendet. Möchten Sie diese Phase wirklich abschließen?" }
            } },
            { "BTN_SI_FINALIZAR", new() {
                { Language.Spanish, "Sí, finalizar" },
                { Language.English, "Yes, finish" },
                { Language.French, "Oui, terminer" },
                { Language.German, "Ja, abschließen" }
            } },
            { "BTN_NO_ESPERAR", new() {
                { Language.Spanish, "No, esperar" },
                { Language.English, "No, wait" },
                { Language.French, "Non, attendre" },
                { Language.German, "Nein, warten" }
            } },

            // ResultsPage
            { "ALERT_FORMATO_INVALIDO", new() {
                { Language.Spanish, "Formato Inválido" },
                { Language.English, "Invalid Format" },
                { Language.French, "Format Invalide" },
                { Language.German, "Ungültiges Format" }
            } },
            { "ALERT_CHASIS_FORMATO_MSG", new() {
                { Language.Spanish, "El chasis debe tener 2 letras seguidas de 6 números (Ej: AB123456)." },
                { Language.English, "The chassis must have 2 letters followed by 6 numbers (e.g. AB123456)." },
                { Language.French, "Le châssis doit comporter 2 lettres suivies de 6 chiffres (ex : AB123456)." },
                { Language.German, "Das Fahrgestell muss 2 Buchstaben gefolgt von 6 Ziffern haben (z. B. AB123456)." }
            } },
            { "ALERT_FALTAN_DATOS_MODELO_MSG", new() {
                { Language.Spanish, "Por favor, selecciona un Modelo y un Motor." },
                { Language.English, "Please select a Model and an Engine." },
                { Language.French, "Veuillez sélectionner un modèle et un moteur." },
                { Language.German, "Bitte wählen Sie ein Modell und einen Motor aus." }
            } },
            { "ALERT_GUARDA_VEHICULO_MSG", new() {
                { Language.Spanish, "Por favor, guarda los datos del vehículo (pulsa el tick verde) antes de finalizar." },
                { Language.English, "Please save the vehicle data (press the green tick) before finishing." },
                { Language.French, "Veuillez enregistrer les données du véhicule (appuyez sur la coche verte) avant de terminer." },
                { Language.German, "Bitte speichern Sie die Fahrzeugdaten (grünes Häkchen drücken), bevor Sie abschließen." }
            } },
            { "ALERT_INTRODUCE_VIN_MSG", new() {
                { Language.Spanish, "Introduce el número de chasis (VIN) antes de finalizar." },
                { Language.English, "Enter the chassis number (VIN) before finishing." },
                { Language.French, "Saisissez le numéro de châssis (VIN) avant de terminer." },
                { Language.German, "Geben Sie die Fahrgestellnummer (FIN) ein, bevor Sie abschließen." }
            } },
            { "ALERT_FINALIZAR", new() {
                { Language.Spanish, "Finalizar" },
                { Language.English, "Finish" },
                { Language.French, "Terminer" },
                { Language.German, "Abschließen" }
            } },
            { "ALERT_CONFIRMA_ENVIO_MSG", new() {
                { Language.Spanish, "¿Confirmas el envío de la auditoría a SharePoint?" },
                { Language.English, "Do you confirm sending the audit to SharePoint?" },
                { Language.French, "Confirmez-vous l'envoi de l'audit vers SharePoint ?" },
                { Language.German, "Bestätigen Sie das Senden des Audits an SharePoint?" }
            } },
            { "ALERT_EXITO", new() {
                { Language.Spanish, "Éxito" },
                { Language.English, "Success" },
                { Language.French, "Succès" },
                { Language.German, "Erfolg" }
            } },
            { "ALERT_AUDITORIA_GUARDADA_MSG", new() {
                { Language.Spanish, "Auditoría finalizada y guardada correctamente." },
                { Language.English, "Audit finished and saved successfully." },
                { Language.French, "Audit terminé et enregistré avec succès." },
                { Language.German, "Audit abgeschlossen und erfolgreich gespeichert." }
            } },
            { "ALERT_ERROR_ENVIO_MSG", new() {
                { Language.Spanish, "Ocurrió un problema inesperado al enviar los datos. Inténtalo de nuevo." },
                { Language.English, "An unexpected problem occurred while sending the data. Please try again." },
                { Language.French, "Un problème inattendu est survenu lors de l'envoi des données. Veuillez réessayer." },
                { Language.German, "Beim Senden der Daten ist ein unerwartetes Problem aufgetreten. Bitte versuchen Sie es erneut." }
            } },
            { "ALERT_AVISO_NUBE", new() {
                { Language.Spanish, "Aviso de Nube" },
                { Language.English, "Cloud Notice" },
                { Language.French, "Avis Cloud" },
                { Language.German, "Cloud-Hinweis" }
            } },
            { "ALERT_AVISO_NUBE_MSG", new() {
                { Language.Spanish, "Hubo un error guardando en SharePoint.\n" },
                { Language.English, "There was an error saving to SharePoint.\n" },
                { Language.French, "Une erreur s'est produite lors de l'enregistrement sur SharePoint.\n" },
                { Language.German, "Beim Speichern in SharePoint ist ein Fehler aufgetreten.\n" }
            } },

            // RRUPage
            { "ALERT_PERMISO_DENEGADO", new() {
                { Language.Spanish, "Permiso Denegado" },
                { Language.English, "Permission Denied" },
                { Language.French, "Autorisation Refusée" },
                { Language.German, "Berechtigung Verweigert" }
            } },
            { "ALERT_GPS_SIN_ACCESO_MSG", new() {
                { Language.Spanish, "No se puede validar sin acceso al GPS. Revisa los permisos." },
                { Language.English, "Cannot validate without GPS access. Check the permissions." },
                { Language.French, "Impossible de valider sans accès au GPS. Vérifiez les autorisations." },
                { Language.German, "Ohne GPS-Zugriff kann nicht validiert werden. Überprüfen Sie die Berechtigungen." }
            } },
            { "ALERT_ERROR_GPS", new() {
                { Language.Spanish, "Error GPS" },
                { Language.English, "GPS Error" },
                { Language.French, "Erreur GPS" },
                { Language.German, "GPS-Fehler" }
            } },
            { "ALERT_GPS_OBTENER_MSG", new() {
                { Language.Spanish, "No se pudo obtener la ubicación. Compruebe la señal." },
                { Language.English, "Could not get the location. Check the signal." },
                { Language.French, "Impossible d'obtenir la localisation. Vérifiez le signal." },
                { Language.German, "Standort konnte nicht ermittelt werden. Überprüfen Sie das Signal." }
            } },
            { "ALERT_AVISO", new() {
                { Language.Spanish, "Aviso" },
                { Language.English, "Notice" },
                { Language.French, "Avis" },
                { Language.German, "Hinweis" }
            } },
            { "ALERT_GPS_NO_SOPORTA_MSG", new() {
                { Language.Spanish, "Este dispositivo no soporta GPS." },
                { Language.English, "This device does not support GPS." },
                { Language.French, "Cet appareil ne prend pas en charge le GPS." },
                { Language.German, "Dieses Gerät unterstützt kein GPS." }
            } },
            { "ALERT_AVISO_GPS", new() {
                { Language.Spanish, "Aviso GPS" },
                { Language.English, "GPS Notice" },
                { Language.French, "Avis GPS" },
                { Language.German, "GPS-Hinweis" }
            } },
            { "ALERT_GPS_LEER_MSG", new() {
                { Language.Spanish, "No se pudo leer la ubicación. Asegúrate de tener la ubicación activada." },
                { Language.English, "Could not read the location. Make sure location is enabled." },
                { Language.French, "Impossible de lire la localisation. Assurez-vous que la localisation est activée." },
                { Language.German, "Standort konnte nicht gelesen werden. Stellen Sie sicher, dass der Standort aktiviert ist." }
            } },
            { "ALERT_SALIR_RRU_MSG", new() {
                { Language.Spanish, "¿Estás seguro de que deseas salir? Los datos de RRU se perderán." },
                { Language.English, "Are you sure you want to exit? The RRU data will be lost." },
                { Language.French, "Êtes-vous sûr de vouloir quitter ? Les données RRU seront perdues." },
                { Language.German, "Möchten Sie wirklich beenden? Die RRU-Daten gehen verloren." }
            } },
            { "ERR_DESCARGAR_NUBE", new() {
                { Language.Spanish, "No se pudo descargar de la nube:\n" },
                { Language.English, "Could not download from the cloud:\n" },
                { Language.French, "Impossible de télécharger depuis le cloud :\n" },
                { Language.German, "Herunterladen aus der Cloud fehlgeschlagen:\n" }
            } },
            { "ALERT_RUTA_PAUSA", new() {
                { Language.Spanish, "Ruta en pausa detectada" },
                { Language.English, "Paused route detected" },
                { Language.French, "Itinéraire en pause détecté" },
                { Language.German, "Pausierte Route erkannt" }
            } },
            { "ALERT_RUTA_PAUSA_MSG", new() {
                { Language.Spanish, "Se ha encontrado una ruta incompleta para el VIN {0}. ¿Deseas continuar por donde la dejaste?" },
                { Language.English, "An incomplete route was found for VIN {0}. Do you want to continue where you left off?" },
                { Language.French, "Un itinéraire incomplet a été trouvé pour le VIN {0}. Voulez-vous continuer là où vous vous êtes arrêté ?" },
                { Language.German, "Für die FIN {0} wurde eine unvollständige Route gefunden. Möchten Sie dort fortfahren, wo Sie aufgehört haben?" }
            } },
            { "BTN_SI_CONTINUAR", new() {
                { Language.Spanish, "Sí, continuar" },
                { Language.English, "Yes, continue" },
                { Language.French, "Oui, continuer" },
                { Language.German, "Ja, fortfahren" }
            } },

            // ControlJapon
            { "ALERT_ERROR_RED", new() {
                { Language.Spanish, "Error de Red" },
                { Language.English, "Network Error" },
                { Language.French, "Erreur Réseau" },
                { Language.German, "Netzwerkfehler" }
            } },
            { "ALERT_JAPON_DESCARGA_MSG", new() {
                { Language.Spanish, "No se han podido descargar los datos de Japón.\n" },
                { Language.English, "Could not download the Japan data.\n" },
                { Language.French, "Impossible de télécharger les données du Japon.\n" },
                { Language.German, "Die Japan-Daten konnten nicht heruntergeladen werden.\n" }
            } },
            { "ALERT_JAPON_SIN_PASOS_MSG", new() {
                { Language.Spanish, "No se encontraron pasos de audio para Japón en el Excel." },
                { Language.English, "No audio steps for Japan were found in the Excel." },
                { Language.French, "Aucune étape audio pour le Japon n'a été trouvée dans l'Excel." },
                { Language.German, "Im Excel wurden keine Audioschritte für Japan gefunden." }
            } },
            { "ALERT_REPRODUCTOR_INCOMPLETO", new() {
                { Language.Spanish, "Reproductor Incompleto" },
                { Language.English, "Incomplete Player" },
                { Language.French, "Lecteur Incomplet" },
                { Language.German, "Unvollständiger Player" }
            } },
            { "ALERT_REPRODUCTOR_INCOMPLETO_MSG", new() {
                { Language.Spanish, "No puedes finalizar sin haber completado y validado todos los pasos." },
                { Language.English, "You cannot finish without completing and validating all the steps." },
                { Language.French, "Vous ne pouvez pas terminer sans avoir complété et validé toutes les étapes." },
                { Language.German, "Sie können nicht abschließen, ohne alle Schritte abgeschlossen und validiert zu haben." }
            } },
            { "ALERT_VALIDACION_PENDIENTE", new() {
                { Language.Spanish, "Validación Pendiente" },
                { Language.English, "Validation Pending" },
                { Language.French, "Validation en Attente" },
                { Language.German, "Validierung Ausstehend" }
            } },
            { "ALERT_VALIDACION_PENDIENTE_MSG", new() {
                { Language.Spanish, "Debes confirmar la acción en el reproductor." },
                { Language.English, "You must confirm the action in the player." },
                { Language.French, "Vous devez confirmer l'action dans le lecteur." },
                { Language.German, "Sie müssen die Aktion im Player bestätigen." }
            } },
            { "ALERT_CHECKLIST_INCOMPLETO", new() {
                { Language.Spanish, "Checklist Incompleto" },
                { Language.English, "Incomplete Checklist" },
                { Language.French, "Liste de Contrôle Incomplète" },
                { Language.German, "Unvollständige Checkliste" }
            } },
            { "ALERT_CHECKLIST_INCOMPLETO_MSG", new() {
                { Language.Spanish, "Abre el checklist y contesta todos los puntos obligatorios (OK / NO) antes de generar el informe." },
                { Language.English, "Open the checklist and answer all mandatory items (OK / NO) before generating the report." },
                { Language.French, "Ouvrez la liste de contrôle et répondez à tous les points obligatoires (OK / NON) avant de générer le rapport." },
                { Language.German, "Öffnen Sie die Checkliste und beantworten Sie alle Pflichtpunkte (OK / NEIN), bevor Sie den Bericht erstellen." }
            } },
            { "ALERT_SALIR_PROGRESO_TODO_MSG", new() {
                { Language.Spanish, "¿Estás seguro de que deseas salir? Todo el progreso se perderá." },
                { Language.English, "Are you sure you want to exit? All progress will be lost." },
                { Language.French, "Êtes-vous sûr de vouloir quitter ? Toute la progression sera perdue." },
                { Language.German, "Möchten Sie wirklich beenden? Der gesamte Fortschritt geht verloren." }
            } },
            { "ALERT_CONFIRMAR", new() {
                { Language.Spanish, "Confirmar" },
                { Language.English, "Confirm" },
                { Language.French, "Confirmer" },
                { Language.German, "Bestätigen" }
            } },
            { "ALERT_VOLVER_SELECCION_MSG", new() {
                { Language.Spanish, "¿Deseas volver a la selección de vehículo? Perderás el progreso de esta auditoría." },
                { Language.English, "Do you want to return to vehicle selection? You will lose the progress of this audit." },
                { Language.French, "Voulez-vous revenir à la sélection du véhicule ? Vous perdrez la progression de cet audit." },
                { Language.German, "Möchten Sie zur Fahrzeugauswahl zurückkehren? Sie verlieren den Fortschritt dieses Audits." }
            } },
            { "BTN_SI_VOLVER", new() {
                { Language.Spanish, "Sí, volver" },
                { Language.English, "Yes, go back" },
                { Language.French, "Oui, revenir" },
                { Language.German, "Ja, zurück" }
            } },
            { "ALERT_SALIR_COMPLETO_MSG", new() {
                { Language.Spanish, "¿Deseas salir por completo? Volverás al menú de modos de auditoría." },
                { Language.English, "Do you want to exit completely? You will return to the audit modes menu." },
                { Language.French, "Voulez-vous quitter complètement ? Vous reviendrez au menu des modes d'audit." },
                { Language.German, "Möchten Sie vollständig beenden? Sie kehren zum Menü der Audit-Modi zurück." }
            } },

            // ResultsJapon
            { "ALERT_BLOQUEADO", new() {
                { Language.Spanish, "Bloqueado" },
                { Language.English, "Blocked" },
                { Language.French, "Bloqué" },
                { Language.German, "Blockiert" }
            } },
            { "ALERT_BLOQUEADO_MSG", new() {
                { Language.Spanish, "No puedes retroceder. Por favor, finaliza y guarda la auditoría." },
                { Language.English, "You cannot go back. Please finish and save the audit." },
                { Language.French, "Vous ne pouvez pas revenir en arrière. Veuillez terminer et enregistrer l'audit." },
                { Language.German, "Sie können nicht zurückgehen. Bitte schließen Sie das Audit ab und speichern Sie es." }
            } },
            { "ALERT_CHASIS_FORMATO_SIMPLE_MSG", new() {
                { Language.Spanish, "El chasis debe tener 2 letras seguidas de 6 números." },
                { Language.English, "The chassis must have 2 letters followed by 6 numbers." },
                { Language.French, "Le châssis doit comporter 2 lettres suivies de 6 chiffres." },
                { Language.German, "Das Fahrgestell muss 2 Buchstaben gefolgt von 6 Ziffern haben." }
            } },
            { "ALERT_EDICION_PENDIENTE", new() {
                { Language.Spanish, "Edición pendiente" },
                { Language.English, "Pending edit" },
                { Language.French, "Modification en attente" },
                { Language.German, "Ausstehende Bearbeitung" }
            } },
            { "ALERT_EDICION_PENDIENTE_MSG", new() {
                { Language.Spanish, "Guarda los cambios del vehículo (pulsa el tick verde) antes de finalizar." },
                { Language.English, "Save the vehicle changes (press the green tick) before finishing." },
                { Language.French, "Enregistrez les modifications du véhicule (appuyez sur la coche verte) avant de terminer." },
                { Language.German, "Speichern Sie die Fahrzeugänderungen (grünes Häkchen drücken), bevor Sie abschließen." }
            } },
            { "ALERT_VIN_VALIDO_MSG", new() {
                { Language.Spanish, "Introduce un VIN (Chasis) válido (2 letras y 6 números) antes de guardar el informe." },
                { Language.English, "Enter a valid VIN (Chassis) (2 letters and 6 numbers) before saving the report." },
                { Language.French, "Saisissez un VIN (châssis) valide (2 lettres et 6 chiffres) avant d'enregistrer le rapport." },
                { Language.German, "Geben Sie eine gültige FIN (Fahrgestell) ein (2 Buchstaben und 6 Ziffern), bevor Sie den Bericht speichern." }
            } },
            { "ALERT_CONFIRMAR_ENVIO_SIMPLE_MSG", new() {
                { Language.Spanish, "¿Confirmar envío a SharePoint?" },
                { Language.English, "Confirm sending to SharePoint?" },
                { Language.French, "Confirmer l'envoi vers SharePoint ?" },
                { Language.German, "Senden an SharePoint bestätigen?" }
            } },
            { "BTN_SI_ENVIAR_CAPS", new() {
                { Language.Spanish, "SÍ, ENVIAR" },
                { Language.English, "YES, SEND" },
                { Language.French, "OUI, ENVOYER" },
                { Language.German, "JA, SENDEN" }
            } },
            { "BTN_REVISAR", new() {
                { Language.Spanish, "REVISAR" },
                { Language.English, "REVIEW" },
                { Language.French, "RÉVISER" },
                { Language.German, "PRÜFEN" }
            } },
            { "ALERT_JAPON_GUARDADA_MSG", new() {
                { Language.Spanish, "Auditoría Japón guardada correctamente." },
                { Language.English, "Japan audit saved successfully." },
                { Language.French, "Audit Japon enregistré avec succès." },
                { Language.German, "Japan-Audit erfolgreich gespeichert." }
            } },
            { "ERR_SHAREPOINT_TITULO", new() {
                { Language.Spanish, "Error SharePoint" },
                { Language.English, "SharePoint Error" },
                { Language.French, "Erreur SharePoint" },
                { Language.German, "SharePoint-Fehler" }
            } },

            // SelectionPage - título de cabecera por modo
            { "AUDITORIA_CDPV_TITLE", new() {
                { Language.Spanish, "AUDITORÍA C-DPV" },
                { Language.English, "C-DPV AUDIT" },
                { Language.French, "AUDIT C-DPV" },
                { Language.German, "C-DPV-AUDIT" }
            } },
            { "AUDITORIA_RRU_TITLE", new() {
                { Language.Spanish, "AUDITORÍA RRU" },
                { Language.English, "RRU AUDIT" },
                { Language.French, "AUDIT RRU" },
                { Language.German, "RRU-AUDIT" }
            } },

            // MenuEstandarPage / EstandarPage / RodajeExterior - cabeceras comunes
            { "HEADER_HOJA_RUTA_JAPON", new() {
                { Language.Spanish, "HOJA DE RUTA JAPÓN" },
                { Language.English, "JAPAN ROUTE SHEET" },
                { Language.French, "FEUILLE DE ROUTE JAPON" },
                { Language.German, "JAPAN-LAUFZETTEL" }
            } },
            { "HEADER_HOJA_RUTA_FORMACION", new() {
                { Language.Spanish, "HOJA DE RUTA FORMACIÓN" },
                { Language.English, "TRAINING ROUTE SHEET" },
                { Language.French, "FEUILLE DE ROUTE FORMATION" },
                { Language.German, "SCHULUNGS-LAUFZETTEL" }
            } },
            { "HEADER_HOJA_RUTA_AUDITORIA", new() {
                { Language.Spanish, "HOJA DE RUTA AUDITORÍA" },
                { Language.English, "AUDIT ROUTE SHEET" },
                { Language.French, "FEUILLE DE ROUTE AUDIT" },
                { Language.German, "AUDIT-LAUFZETTEL" }
            } },
            { "HEADER_HOJA_RUTA_DPV", new() {
                { Language.Spanish, "HOJA DE RUTA DPV" },
                { Language.English, "DPV ROUTE SHEET" },
                { Language.French, "FEUILLE DE ROUTE DPV" },
                { Language.German, "DPV-LAUFZETTEL" }
            } },
            { "HEADER_HOJA_RUTA_GENERICA", new() {
                { Language.Spanish, "HOJA DE RUTA" },
                { Language.English, "ROUTE SHEET" },
                { Language.French, "FEUILLE DE ROUTE" },
                { Language.German, "LAUFZETTEL" }
            } },
            { "HEADER_CHASIS_VEHICULO", new() {
                { Language.Spanish, "CHASIS DEL VEHÍCULO" },
                { Language.English, "VEHICLE VIN" },
                { Language.French, "CHÂSSIS DU VÉHICULE" },
                { Language.German, "FAHRGESTELLNUMMER" }
            } },
            { "PLACEHOLDER_CHASIS", new() {
                { Language.Spanish, "VIN" },
                { Language.English, "VIN" },
                { Language.French, "VIN" },
                { Language.German, "FIN" }
            } },
            { "MSG_VIN_PENDIENTE", new() {
                { Language.Spanish, "VIN: PENDIENTE" },
                { Language.English, "VIN: PENDING" },
                { Language.French, "VIN : EN ATTENTE" },
                { Language.German, "FIN: AUSSTEHEND" }
            } },
            { "MSG_VIN_NO_REGISTRADO", new() {
                { Language.Spanish, "VIN: No registrado" },
                { Language.English, "VIN: Not registered" },
                { Language.French, "VIN : Non enregistré" },
                { Language.German, "FIN: Nicht registriert" }
            } },
            { "MSG_AUDITOR_DESCONOCIDO", new() {
                { Language.Spanish, "Auditor Desconocido" },
                { Language.English, "Unknown Auditor" },
                { Language.French, "Auditeur Inconnu" },
                { Language.German, "Unbekannter Prüfer" }
            } },
            { "LBL_MODELO_PREFIJO", new() {
                { Language.Spanish, "Modelo: " },
                { Language.English, "Model: " },
                { Language.French, "Modèle : " },
                { Language.German, "Modell: " }
            } },
            { "LBL_MOTOR_PREFIJO", new() {
                { Language.Spanish, "Motor: " },
                { Language.English, "Engine: " },
                { Language.French, "Motorisation : " },
                { Language.German, "Motor: " }
            } },
            { "BTN_FINALIZAR_AUDITORIA", new() {
                { Language.Spanish, "FINALIZAR AUDITORÍA" },
                { Language.English, "FINISH AUDIT" },
                { Language.French, "TERMINER L'AUDIT" },
                { Language.German, "AUDIT ABSCHLIESSEN" }
            } },
            { "BTN_FINALIZAR_ESTANDAR", new() {
                { Language.Spanish, "FINALIZAR ESTÁNDAR" },
                { Language.English, "FINISH STANDARD" },
                { Language.French, "TERMINER LA NORME" },
                { Language.German, "STANDARD ABSCHLIESSEN" }
            } },
            { "MSG_PASO_DE", new() {
                { Language.Spanish, "Paso {0} de {1}" },
                { Language.English, "Step {0} of {1}" },
                { Language.French, "Étape {0} sur {1}" },
                { Language.German, "Schritt {0} von {1}" }
            } },
            { "BTN_PAUSAR", new() {
                { Language.Spanish, "⏸ PAUSAR" },
                { Language.English, "⏸ PAUSE" },
                { Language.French, "⏸ PAUSE" },
                { Language.German, "⏸ PAUSIEREN" }
            } },
            { "BTN_REPRODUCIR", new() {
                { Language.Spanish, "▶ REPRODUCIR" },
                { Language.English, "▶ PLAY" },
                { Language.French, "▶ LIRE" },
                { Language.German, "▶ ABSPIELEN" }
            } },
            { "BTN_REINICIAR", new() {
                { Language.Spanish, "↻ REINICIAR" },
                { Language.English, "↻ RESTART" },
                { Language.French, "↻ REDÉMARRER" },
                { Language.German, "↻ NEU STARTEN" }
            } },
            { "BTN_PAUSAR_RUTA", new() {
                { Language.Spanish, "PAUSAR RUTA" },
                { Language.English, "PAUSE ROUTE" },
                { Language.French, "PAUSE ITINÉRAIRE" },
                { Language.German, "ROUTE PAUSIEREN" }
            } },
            { "BTN_REANUDAR", new() {
                { Language.Spanish, "▶ REANUDAR" },
                { Language.English, "▶ RESUME" },
                { Language.French, "▶ REPRENDRE" },
                { Language.German, "▶ FORTSETZEN" }
            } },
            { "BTN_SALIR", new() {
                { Language.Spanish, "✕ Salir" },
                { Language.English, "✕ Exit" },
                { Language.French, "✕ Quitter" },
                { Language.German, "✕ Beenden" }
            } },
            { "BTN_COMENZAR_PASO", new() {
                { Language.Spanish, "▶ COMENZAR" },
                { Language.English, "▶ START" },
                { Language.French, "▶ COMMENCER" },
                { Language.German, "▶ STARTEN" }
            } },
            { "LBL_FASE_PREFIJO", new() {
                { Language.Spanish, "Fase: " },
                { Language.English, "Phase: " },
                { Language.French, "Phase : " },
                { Language.German, "Phase: " }
            } },
            { "LBL_AUDIOFORMACION_PREFIJO", new() {
                { Language.Spanish, "Formación: " },
                { Language.English, "Training: " },
                { Language.French, "Formation : " },
                { Language.German, "Schulung: " }
            } },
            { "BTN_VALIDAR_CONTINUAR", new() {
                { Language.Spanish, "VALIDAR Y CONTINUAR" },
                { Language.English, "VALIDATE AND CONTINUE" },
                { Language.French, "VALIDER ET CONTINUER" },
                { Language.German, "BESTÄTIGEN UND WEITER" }
            } }
        };

        public static string Translate(string key)
        {
            if (Translations.TryGetValue(key, out var dict))
            {
                if (dict.TryGetValue(CurrentLanguage, out var text))
                {
                    return text;
                }
            }
            return key;
        }

        public static string TranslateFormat(string key, params object[] args)
        {
            string format = Translate(key);
            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format;
            }
        }
    }
}
