namespace DynamicIslandWin;

public partial class MainWindow
{
    // Russian source, English, Ukrainian, German, Spanish. User content is never
    // sent to an online translator; this catalog ships with the application.
    private static readonly Dictionary<string, string[]> AdditionalTranslations = TranslationCatalog
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line => line.Split('¦')).ToDictionary(parts => parts[0], parts => parts, StringComparer.Ordinal);

    private const string TranslationCatalog = """
Закрепить на месте¦Lock position¦Закріпити на місці¦Position sperren¦Fijar posición
У верхнего края (macOS)¦Top edge (macOS)¦Біля верхнього краю (macOS)¦Oberer Bildschirmrand (macOS)¦Borde superior (macOS)
Выключено — плавающая капсула. Включено — остров у верхнего края экрана.¦Off: floating pill. On: island docked to the top edge.¦Вимкнено: плаваюча капсула. Увімкнено: острів біля верхнього краю.¦Aus: schwebende Kapsel. An: Insel am oberen Bildschirmrand.¦Desactivado: cápsula flotante. Activado: isla en el borde superior.
Проверять обновления автоматически¦Check for updates automatically¦Автоматично перевіряти оновлення¦Automatisch nach Updates suchen¦Buscar actualizaciones automáticamente
Проверка выполняется при запуске и периодически. Загрузка и установка — только вручную.¦Checks run at startup and periodically. Downloads and installation are manual.¦Перевірка відбувається під час запуску та періодично. Завантаження й встановлення — лише вручну.¦Beim Start und regelmäßig prüfen. Download und Installation erfolgen manuell.¦Se comprueba al iniciar y periódicamente. La descarga y la instalación son manuales.
Трассировка кадров недоступна. Температура CPU проверяется отдельно.¦Frame tracing is unavailable. CPU temperature is checked separately.¦Трасування кадрів недоступне. Температура ЦП перевіряється окремо.¦Frame-Aufzeichnung nicht verfügbar. CPU-Temperatur wird separat geprüft.¦El seguimiento de fotogramas no está disponible. La temperatura de la CPU se comprueba por separado.
Сборщик показателей отключился. Переключите игровой режим.¦The metrics helper disconnected. Toggle game mode off and on.¦Збирач показників відключився. Перемкніть ігровий режим.¦Die Messhilfe wurde getrennt. Schalten Sie den Spielmodus aus und wieder ein.¦El servicio de métricas se desconectó. Desactiva y vuelve a activar el modo juego.
Температура CPU недоступна: установите PawnIO в игровом центре.¦CPU temperature is unavailable: install PawnIO in Game Center.¦Температура ЦП недоступна: встановіть PawnIO в ігровому центрі.¦CPU-Temperatur nicht verfügbar: PawnIO im Spielecenter installieren.¦La temperatura de la CPU no está disponible: instala PawnIO en el Centro de juegos.
Датчик CPU пока не вернул температуру.¦The CPU sensor has not reported a temperature yet.¦Датчик ЦП ще не повернув температуру.¦Der CPU-Sensor hat noch keine Temperatur gemeldet.¦El sensor de la CPU aún no ha indicado la temperatura.
Ожидаю показания датчика CPU.¦Waiting for the CPU sensor.¦Очікую на показання датчика ЦП.¦Warte auf den CPU-Sensor.¦Esperando al sensor de la CPU.
Включите игровой режим, чтобы измерить температуру CPU.¦Enable game mode to measure CPU temperature.¦Увімкніть ігровий режим, щоб виміряти температуру ЦП.¦Spielmodus aktivieren, um die CPU-Temperatur zu messen.¦Activa el modo juego para medir la temperatura de la CPU.
Не удалось сделать скриншот. Перезапустите «Ножницы» и повторите попытку.¦Could not capture a screenshot. Restart Snipping Tool and try again.¦Не вдалося зробити знімок. Перезапустіть Ножиці та спробуйте знову.¦Screenshot fehlgeschlagen. Snipping Tool neu starten und erneut versuchen.¦No se pudo capturar. Reinicia Recortes e inténtalo de nuevo.
Скриншот скопирован и сохранён: Изображения / OC - Dynamic Island¦Screenshot copied and saved: Pictures / OC - Dynamic Island¦Знімок скопійовано та збережено: Зображення / OC - Dynamic Island¦Screenshot kopiert und gespeichert: Bilder / OC - Dynamic Island¦Captura copiada y guardada: Imágenes / OC - Dynamic Island
Стекло-линза · эксперимент¦Lens glass · experimental¦Скло-лінза · експеримент¦Linsenglas · experimentell¦Cristal de lente · experimental
Преломление живого фона во всех капсулах и меню. Работает при любом режиме скриншотов.¦Refracts the live backdrop in all capsules and menus, with either screenshot setting.¦Заломлення живого тла в усіх капсулах і меню. Працює за будь-якого режиму знімків екрана.¦Bricht den Live-Hintergrund in allen Kapseln und Menüs, bei beiden Screenshot-Einstellungen.¦Refracta el fondo en vivo en todas las cápsulas y menús, con cualquier ajuste de capturas.
Прозрачность фона¦Background transparency¦Прозорість тла¦Hintergrundtransparenz¦Transparencia del fondo
Громкость регулируется прямо в капсуле справа: ползунок, колесо мыши и кнопка звука.¦Adjust volume in the right capsule with its slider, mouse wheel and mute button.¦Регулюйте гучність у капсулі праворуч: повзунок, колесо миші та кнопка звуку.¦Lautstärke direkt in der rechten Kapsel: Regler, Mausrad und Stummschalttaste.¦Ajusta el volumen en la cápsula derecha con el deslizador, la rueda y el botón de silencio.
Прозрачность фона настраивается в разделе «Оформление».¦Adjust background transparency in Appearance.¦Прозорість тла налаштовується в розділі «Оформлення».¦Die Hintergrundtransparenz lässt sich unter Darstellung einstellen.¦Ajusta la transparencia del fondo en Apariencia.
В Telegram и exDesktop нужно включить системные уведомления Windows.¦Enable Windows system notifications in Telegram and exDesktop.¦У Telegram та exDesktop потрібно ввімкнути системні сповіщення Windows.¦In Telegram und exDesktop müssen Windows-Systembenachrichtigungen aktiviert sein.¦Activa las notificaciones del sistema Windows en Telegram y exDesktop.
Установщик запущен. Приложение закроется на время обновления.¦Installer started. The app will close during the update.¦Інсталятор запущено. Програма закриється на час оновлення.¦Installer gestartet. Die App wird während des Updates geschlossen.¦Instalador iniciado. La aplicación se cerrará durante la actualización.
Не удалось запустить установщик обновления. Повторите попытку или откройте релиз.¦Could not start the update installer. Retry or open the release.¦Не вдалося запустити інсталятор оновлення. Повторіть спробу або відкрийте реліз.¦Update-Installer konnte nicht gestartet werden. Erneut versuchen oder Version öffnen.¦No se pudo iniciar el instalador. Reintenta o abre la versión.
Для температуры CPU нужен драйвер PawnIO. Он включён в установщик; Windows запросит права администратора.¦CPU temperature requires the bundled PawnIO driver. Windows will request administrator access.¦Для температури CPU потрібен драйвер PawnIO. Він є в інсталяторі; Windows запитає права адміністратора.¦Die CPU-Temperatur benötigt den enthaltenen PawnIO-Treiber. Windows fragt nach Administratorrechten.¦La temperatura de CPU requiere el controlador PawnIO incluido. Windows solicitará permisos de administrador.
Установить драйвер температуры CPU¦Install CPU temperature driver¦Встановити драйвер температури CPU¦CPU-Temperaturtreiber installieren¦Instalar controlador de temperatura de CPU
Не удалось установить драйвер температуры CPU.¦Could not install the CPU temperature driver.¦Не вдалося встановити драйвер температури CPU.¦CPU-Temperaturtreiber konnte nicht installiert werden.¦No se pudo instalar el controlador de temperatura de CPU.
⌂  Главная¦⌂  Home¦⌂  Головна¦⌂  Start¦⌂  Inicio
◉  Система¦◉  System¦◉  Система¦◉  System¦◉  Sistema
▦  Календарь¦▦  Calendar¦▦  Календар¦▦  Kalender¦▦  Calendario
◷  Таймеры¦◷  Timers¦◷  Таймери¦◷  Timer¦◷  Temporizadores
♫  Музыка¦♫  Music¦♫  Музика¦♫  Musik¦♫  Música
✧  Чат¦✧  Chat¦✧  Чат¦✧  Chat¦✧  Chat
▤  Раскладка¦▤  Layout¦▤  Компонування¦▤  Layout¦▤  Diseño
▦  Раскладка¦▦  Layout¦▦  Компонування¦▦  Layout¦▦  Diseño
◈  Своя тема¦◈  Custom theme¦◈  Власна тема¦◈  Eigenes Thema¦◈  Tema propio
✓  Диагностика¦✓  Diagnostics¦✓  Діагностика¦✓  Diagnose¦✓  Diagnóstico
↓  Обновления¦↓  Updates¦↓  Оновлення¦↓  Updates¦↓  Actualizaciones
↗  Что нового¦↗  What's new¦↗  Що нового¦↗  Neuigkeiten¦↗  Novedades
Не удалось выполнить действие. Подробности — в диагностике.¦Action failed. See diagnostics for details.¦Не вдалося виконати дію. Докладніше в діагностиці.¦Aktion fehlgeschlagen. Details unter Diagnose.¦La acción falló. Consulta el diagnóstico.
Размер и форма¦Size and shape¦Розмір і форма¦Größe und Form¦Tamaño y forma
Для ручной настройки потяните правый или нижний край свёрнутого островка.¦Drag the right or bottom edge of the compact island to resize it.¦Потягніть правий або нижній край згорнутого острівця.¦Zum Anpassen den rechten oder unteren Rand der kompakten Insel ziehen.¦Arrastra el borde derecho o inferior de la isla compacta.
Изменить мышью¦Resize with mouse¦Змінити мишею¦Mit Maus anpassen¦Ajustar con el ratón
Ширина¦Width¦Ширина¦Breite¦Ancho
Высота¦Height¦Висота¦Höhe¦Alto
Скругление¦Corner radius¦Заокруглення¦Abrundung¦Redondeo
Непрозрачность в покое¦Idle opacity¦Непрозорість у спокої¦Deckkraft im Ruhezustand¦Opacidad en reposo
Тень¦Shadow¦Тінь¦Schatten¦Sombra
Сбросить размер¦Reset size¦Скинути розмір¦Größe zurücksetzen¦Restablecer tamaño
Что показывать в покое¦Idle content¦Вміст у спокої¦Inhalt im Ruhezustand¦Contenido en reposo
По центру¦Center¦По центру¦Mitte¦Centro
Время¦Time¦Час¦Uhrzeit¦Hora
Время и дата¦Time and date¦Час і дата¦Uhrzeit und Datum¦Hora y fecha
Дата¦Date¦Дата¦Datum¦Fecha
Справа¦Right¦Праворуч¦Rechts¦Derecha
Погода¦Weather¦Погода¦Wetter¦Tiempo
Батарея¦Battery¦Батарея¦Akku¦Batería
Ничего¦Nothing¦Нічого¦Nichts¦Nada
Шрифт¦Font¦Шрифт¦Schrift¦Fuente
12-часовой формат¦12-hour clock¦12-годинний формат¦12-Stunden-Format¦Formato de 12 horas
Показывать секунды¦Show seconds¦Показувати секунди¦Sekunden anzeigen¦Mostrar segundos
Задержка раскрытия, мс¦Hover delay, ms¦Затримка розкриття, мс¦Verzögerung, ms¦Retraso al abrir, ms
Звук таймеров и встреч¦Timer and meeting sounds¦Звук таймерів і зустрічей¦Timer- und Besprechungstöne¦Sonidos de temporizadores y reuniones
Поведение¦Behavior¦Поведінка¦Verhalten¦Comportamiento
Показ громкости в динамическом острове¦Show volume in the island¦Гучність у динамічному острівці¦Lautstärke auf der Insel¦Mostrar volumen en la isla
Заменяет индикатор Windows при нажатии клавиш громкости. Выключите, чтобы вернуть системный показ.¦Replaces the Windows volume indicator for volume keys. Turn off to restore it.¦Замінює індикатор Windows для клавіш гучності. Вимкніть, щоб повернути його.¦Ersetzt die Windows-Anzeige für Lautstärketasten. Ausschalten stellt sie wieder her.¦Sustituye el indicador de Windows al usar las teclas de volumen. Desactívalo para restaurarlo.
Без ожидания опроса. Заменяет индикатор Windows для клавиш громкости.¦Instant volume updates. Replaces the Windows indicator for volume keys.¦Миттєві зміни гучності. Замінює індикатор Windows для клавіш гучності.¦Sofortige Aktualisierung. Ersetzt die Windows-Lautstärkeanzeige.¦Actualización inmediata. Sustituye el indicador de volumen de Windows.
Показывать уровень яркости¦Show brightness level¦Показувати рівень яскравості¦Helligkeit anzeigen¦Mostrar brillo
Следовать за курсором на другой монитор¦Follow cursor to another monitor¦Переходити за курсором на інший монітор¦Cursor auf anderen Monitor folgen¦Seguir al cursor a otro monitor
Счётчик непрочитанных без доступа к уведомлениям¦Unread count without notification access¦Лічильник непрочитаних без доступу до сповіщень¦Ungelesene ohne Benachrichtigungszugriff zählen¦Contador de no leídos sin acceso a notificaciones
Прогресс других программ¦Other apps' progress¦Прогрес інших програм¦Fortschritt anderer Apps¦Progreso de otras aplicaciones
Прогресс других программ справа¦Other apps' progress on the right¦Прогрес інших програм праворуч¦App-Fortschritt rechts¦Progreso de aplicaciones a la derecha
Отдельная капсула, не закрывающая часы и погоду¦Separate capsule beside the clock and weather¦Окрема капсула поруч із годинником і погодою¦Separate Kapsel neben Uhr und Wetter¦Cápsula separada junto al reloj y el tiempo
Текст и скриншоты в буфере обмена¦Clipboard text and screenshots¦Текст і знімки екрана в буфері обміну¦Text und Screenshots in der Zwischenablage¦Texto y capturas en el portapapeles
Разделы¦Sections¦Розділи¦Bereiche¦Secciones
Перетащите строку для изменения порядка. Галочка показывает или скрывает раздел.¦Drag rows to reorder. Check a row to show its section.¦Перетягуйте рядки для зміни порядку. Позначка показує розділ.¦Zeilen zum Sortieren ziehen. Häkchen zeigt den Bereich.¦Arrastra filas para reordenar. Marca una fila para mostrar su sección.
Кнопки главной¦Home buttons¦Кнопки головної¦Start-Buttons¦Botones de inicio
Перетаскивайте строки. Скрытые кнопки можно вернуть в настройках главной.¦Drag rows to reorder. Restore hidden buttons in Home settings.¦Перетягуйте рядки. Приховані кнопки можна повернути в налаштуваннях головної.¦Zeilen ziehen. Ausgeblendete Buttons in den Start-Einstellungen wiederherstellen.¦Arrastra las filas. Restaura los botones ocultos en los ajustes de inicio.
Область экрана¦Screen region¦Область екрана¦Bildschirmbereich¦Región de pantalla
Скриншот¦Screenshot¦Знімок екрана¦Screenshot¦Captura de pantalla
Перевод¦Translate¦Переклад¦Übersetzen¦Traducir
Запись¦Record¦Запис¦Aufnahme¦Grabar
Команда¦Command¦Команда¦Befehl¦Comando
Не беспокоить¦Do Not Disturb¦Не турбувати¦Nicht stören¦No molestar
Ваша палитра¦Your palette¦Ваша палітра¦Ihre Palette¦Tu paleta
Цвета задаются как #RRGGBB. Стекло и все дополнительные панели используют выбранную палитру.¦Use #RRGGBB colors. Glass and auxiliary panels use this palette.¦Кольори у форматі #RRGGBB. Скло й додаткові панелі використовують цю палітру.¦Farben als #RRGGBB. Glas und Zusatzpanels nutzen diese Palette.¦Usa colores #RRGGBB. El cristal y los paneles usan esta paleta.
Название¦Name¦Назва¦Name¦Nombre
Моя тема¦My theme¦Моя тема¦Mein Thema¦Mi tema
Островок¦Island¦Острівець¦Insel¦Isla
Второй цвет¦Second color¦Другий колір¦Zweite Farbe¦Segundo color
Акцент¦Accent¦Акцент¦Akzent¦Acento
Светлая палитра¦Light palette¦Світла палітра¦Helle Palette¦Paleta clara
Проверьте название и цвета #RRGGBB.¦Check the name and #RRGGBB colors.¦Перевірте назву й кольори #RRGGBB.¦Name und #RRGGBB-Farben prüfen.¦Comprueba el nombre y los colores #RRGGBB.
Сохранить и применить¦Save and apply¦Зберегти й застосувати¦Speichern und anwenden¦Guardar y aplicar
Код темы¦Theme code¦Код теми¦Themencode¦Código del tema
Импортировать¦Import¦Імпортувати¦Importieren¦Importar
Сохранённые темы¦Saved themes¦Збережені теми¦Gespeicherte Themen¦Temas guardados
Создайте первую тему выше.¦Create your first theme above.¦Створіть першу тему вище.¦Erstellen Sie oben Ihr erstes Thema.¦Crea tu primer tema arriba.
Код¦Code¦Код¦Code¦Código
Удалить¦Delete¦Видалити¦Löschen¦Eliminar
Копировать код темы¦Copy theme code¦Копіювати код теми¦Themencode kopieren¦Copiar código del tema
Игровой центр¦Game center¦Ігровий центр¦Spielcenter¦Centro de juegos
Игровой режим включает HUD, FPS и температуру CPU. Windows может запросить права администратора.¦Game mode enables the HUD, FPS, and CPU temperature. Windows may request administrator access.¦Ігровий режим вмикає HUD, FPS і температуру ЦП. Windows може запитати права адміністратора.¦Der Spielmodus aktiviert HUD, FPS und CPU-Temperatur. Windows kann Administratorrechte anfordern.¦El modo juego activa el HUD, los FPS y la temperatura de la CPU. Windows puede solicitar permisos de administrador.
FPS DWM — кадры рабочего стола, не FPS игры.¦DWM FPS measures desktop frames, not game FPS.¦FPS DWM — кадри робочого столу, не FPS гри.¦DWM-FPS misst Desktop-Bilder, nicht Spiel-FPS.¦Los FPS de DWM miden el escritorio, no los FPS del juego.
Автоматический HUD в полноэкранной игре¦Automatic HUD in fullscreen games¦Автоматичний HUD у повноекранній грі¦Automatisches HUD in Vollbildspielen¦HUD automático en juegos a pantalla completa
Включить игровой режим¦Enable game mode¦Увімкнути ігровий режим¦Spielmodus aktivieren¦Activar modo juego
Выключить игровой режим¦Disable game mode¦Вимкнути ігровий режим¦Spielmodus deaktivieren¦Desactivar modo juego
FPS измеряется по событиям отправки кадров активной игры. Если игра не передаёт такие события, показание недоступно.¦FPS counts frame submissions from the active game. If the game does not emit these events, no reading is available.¦FPS вимірюється за подіями передавання кадрів активної гри. Без таких подій показник недоступний.¦FPS zählt Frame-Übermittlungen des aktiven Spiels. Ohne diese Ereignisse steht kein Messwert zur Verfügung.¦Los FPS se calculan a partir de los fotogramas enviados por el juego activo. Sin esos eventos no hay lectura.
Громкость приложений¦App volumes¦Гучність програм¦App-Lautstärke¦Volumen de aplicaciones
Регуляторы управляют настоящими звуковыми сессиями Windows.¦Controls actual Windows audio sessions.¦Регулятори керують звуковими сеансами Windows.¦Steuert echte Windows-Audiositzungen.¦Controla las sesiones de audio de Windows.
Читаю аудиоустройства…¦Reading audio devices…¦Читаю аудіопристрої…¦Audiogeräte werden gelesen…¦Leyendo dispositivos de audio…
Общая громкость, %¦Master volume, %¦Загальна гучність, %¦Gesamtlautstärke, %¦Volumen general, %
Яркость, %¦Brightness, %¦Яскравість, %¦Helligkeit, %¦Brillo, %
Включить звук¦Unmute¦Увімкнути звук¦Ton einschalten¦Activar sonido
Выключить звук¦Mute¦Вимкнути звук¦Stummschalten¦Silenciar
Громкость, %¦Volume, %¦Гучність, %¦Lautstärke, %¦Volumen, %
Запустите воспроизведение в приложении, затем обновите список.¦Play audio in an app, then refresh the list.¦Запустіть відтворення в програмі й оновіть список.¦Audio in einer App starten und Liste aktualisieren.¦Reproduce audio en una aplicación y actualiza la lista.
Обновить список¦Refresh list¦Оновити список¦Liste aktualisieren¦Actualizar lista
Нет доступного устройства вывода. Откройте настройки звука Windows.¦No output device available. Open Windows sound settings.¦Немає пристрою виводу. Відкрийте налаштування звуку Windows.¦Kein Ausgabegerät. Windows-Soundeinstellungen öffnen.¦No hay dispositivo de salida. Abre los ajustes de sonido de Windows.
Настройки звука¦Sound settings¦Налаштування звуку¦Soundeinstellungen¦Ajustes de sonido
Ближайшие встречи¦Upcoming meetings¦Найближчі зустрічі¦Nächste Besprechungen¦Próximas reuniones
Импортируйте календарь .ics. Повторы и часовые пояса учитываются. Ссылка на звонок появится за 10 минут и в момент начала.¦Import an .ics calendar with recurrence and time zones. Join links appear 10 minutes before and at the start.¦Імпортуйте .ics із повторами й часовими поясами. Посилання з’явиться за 10 хвилин і на початку.¦.ics-Kalender mit Wiederholungen und Zeitzonen importieren. Anruflink 10 Minuten vorher und zum Beginn.¦Importa un calendario .ics con repeticiones y zonas horarias. El enlace aparece 10 minutos antes y al inicio.
Импортировать .ics¦Import .ics¦Імпортувати .ics¦.ics importieren¦Importar .ics
Календарь iCalendar|*.ics¦iCalendar calendar|*.ics¦Календар iCalendar|*.ics¦iCalendar-Kalender|*.ics¦Calendario iCalendar|*.ics
Календарь больше 4 МБ.¦Calendar exceeds 4 MB.¦Календар більший за 4 МБ.¦Kalender größer als 4 MB.¦El calendario supera 4 MB.
Не удалось прочитать календарь. Проверьте файл .ics.¦Could not read calendar. Check the .ics file.¦Не вдалося прочитати календар. Перевірте .ics.¦Kalender nicht lesbar. .ics-Datei prüfen.¦No se pudo leer el calendario. Comprueba el archivo .ics.
Удалить календарь¦Remove calendar¦Видалити календар¦Kalender entfernen¦Eliminar calendario
На ближайшие 30 дней встреч нет.¦No meetings in the next 30 days.¦Немає зустрічей на найближчі 30 днів.¦Keine Besprechungen in den nächsten 30 Tagen.¦No hay reuniones en los próximos 30 días.
Присоединиться¦Join¦Приєднатися¦Beitreten¦Unirse
Таймеры и напоминания¦Timers and reminders¦Таймери й нагадування¦Timer und Erinnerungen¦Temporizadores y recordatorios
Продолжают отсчитывать время после перезапуска приложения.¦Continue counting after the app restarts.¦Продовжують відлік після перезапуску програми.¦Laufen nach einem App-Neustart weiter.¦Continúan después de reiniciar la aplicación.
Пора сделать перерыв¦Time for a break¦Час зробити перерву¦Zeit für eine Pause¦Hora de un descanso
Через сколько минут¦Minutes from now¦Через скільки хвилин¦In wie vielen Minuten¦Dentro de cuántos minutos
Запустить таймер¦Start timer¦Запустити таймер¦Timer starten¦Iniciar temporizador
Введите от 0,05 до 525600 минут.¦Enter 0.05 to 525600 minutes.¦Введіть від 0,05 до 525600 хвилин.¦0,05 bis 525600 Minuten eingeben.¦Introduce de 0,05 a 525600 minutos.
Таймер¦Timer¦Таймер¦Timer¦Temporizador
Сработает¦Due¦Спрацює¦Fällig¦Se activa
Отменить¦Cancel¦Скасувати¦Abbrechen¦Cancelar
Нет активного медиа¦No active media¦Немає активного медіа¦Keine aktiven Medien¦Sin multimedia activa
Микрофон передаётся Google только после нажатия «Голос». Остановите сеанс той же кнопкой. Используется ваш ключ и тариф Gemini API.¦Microphone audio is sent to Google only after pressing Voice. Press again to stop. Uses your Gemini API key and plan.¦Мікрофон передається Google після натискання «Голос». Натисніть знову для зупинки. Використовує ваш ключ і тариф Gemini API.¦Mikrofon wird nach Klick auf Stimme an Google übertragen. Erneut klicken zum Stoppen. Nutzt Ihren Gemini-API-Schlüssel und Tarif.¦El micrófono se envía a Google al pulsar Voz. Pulsa de nuevo para detenerlo. Usa tu clave y tarifa de Gemini API.
Ключ не задан. Введите API-ключ выше или используйте GEMINI_API_KEY.¦No key set. Enter an API key above or use GEMINI_API_KEY.¦Ключ не задано. Введіть API-ключ або GEMINI_API_KEY.¦Kein Schlüssel. API-Schlüssel eingeben oder GEMINI_API_KEY verwenden.¦Sin clave. Introduce una clave API o usa GEMINI_API_KEY.
Ключ сохранён для текущего пользователя Windows.¦Key saved for the current Windows user.¦Ключ збережено для поточного користувача Windows.¦Schlüssel für aktuellen Windows-Benutzer gespeichert.¦Clave guardada para el usuario actual de Windows.
Сохранить ключ¦Save key¦Зберегти ключ¦Schlüssel speichern¦Guardar clave
Удалить ключ¦Delete key¦Видалити ключ¦Schlüssel löschen¦Eliminar clave
Модель Live API¦Live API model¦Модель Live API¦Live-API-Modell¦Modelo Live API
Голос¦Voice¦Голос¦Stimme¦Voz
Громкость голоса¦Voice volume¦Гучність голосу¦Sprachlautstärke¦Volumen de voz
Микрофон включён¦Microphone on¦Мікрофон увімкнено¦Mikrofon an¦Micrófono activado
Микрофон выключен¦Microphone off¦Мікрофон вимкнено¦Mikrofon aus¦Micrófono desactivado
● Голос¦● Voice¦● Голос¦● Stimme¦● Voz
■ Остановить голос¦■ Stop voice¦■ Зупинити голос¦■ Stimme stoppen¦■ Detener voz
Не удалось начать разговор. Проверьте ключ, модель, сеть и разрешение микрофона.¦Could not start voice. Check key, model, network and microphone permission.¦Не вдалося почати розмову. Перевірте ключ, модель, мережу й дозвіл мікрофона.¦Sprachstart fehlgeschlagen. Schlüssel, Modell, Netzwerk und Mikrofonberechtigung prüfen.¦No se pudo iniciar la voz. Comprueba clave, modelo, red y permiso del micrófono.
Сообщение во время голосового сеанса¦Message during voice session¦Повідомлення під час голосового сеансу¦Nachricht während Sprachsitzung¦Mensaje durante la sesión de voz
Отправить в сеанс¦Send to session¦Надіслати в сеанс¦An Sitzung senden¦Enviar a la sesión
Уведомления¦Notifications¦Сповіщення¦Benachrichtigungen¦Notificaciones
Доступ Windows разрешён; исходные обработчики открывают чаты.¦Windows access granted; original handlers open chats.¦Доступ Windows дозволено; початкові обробники відкривають чати.¦Windows-Zugriff erlaubt; Originalhandler öffnen Chats.¦Acceso de Windows permitido; los controladores originales abren chats.
Нет доступа Windows. Доступен счётчик заголовка, если мессенджер его публикует.¦No Windows access. Title unread counts are used if the messenger provides them.¦Немає доступу Windows. Доступний лічильник заголовка, якщо месенджер його надає.¦Kein Windows-Zugriff. Titelzähler wird genutzt, falls verfügbar.¦Sin acceso de Windows. Se usa el contador del título si está disponible.
Музыка¦Music¦Музика¦Musik¦Música
Текст песни¦Song lyrics¦Текст пісні¦Liedtext¦Letra de la canción
Показывать текст при включении музыки¦Show lyrics during playback¦Показувати текст під час відтворення¦Liedtext bei Wiedergabe anzeigen¦Mostrar la letra durante la reproducción
Искать текст¦Find lyrics¦Шукати текст¦Liedtext suchen¦Buscar letra
При включённом поиске LRCLIB получает название, исполнителя, альбом и длительность трека, если они доступны.¦When search is enabled, LRCLIB receives the title, artist, album, and duration when available.¦Якщо пошук увімкнено, LRCLIB отримує назву, виконавця, альбом і тривалість треку, якщо вони доступні.¦Bei aktiver Suche erhält LRCLIB Titel, Interpret, Album und Dauer, falls verfügbar.¦Si activas la búsqueda, LRCLIB recibe el título, el artista, el álbum y la duración si están disponibles.
Импортировать LRC¦Import LRC¦Імпортувати LRC¦LRC importieren¦Importar LRC
Файл текста песни|*.lrc¦Lyrics file|*.lrc¦Файл тексту пісні|*.lrc¦Liedtextdatei|*.lrc¦Archivo de letra|*.lrc
Импортированный файл применяется к текущему треку до закрытия приложения и имеет приоритет над поиском.¦An imported file applies to the current track until the app closes and takes priority over search results.¦Імпортований файл діє для поточного треку до закриття програми та має пріоритет над пошуком.¦Die importierte Datei gilt bis zum Schließen der App für den aktuellen Titel und hat Vorrang vor Suchergebnissen.¦El archivo importado se aplica a la pista actual hasta cerrar la aplicación y tiene prioridad sobre los resultados de búsqueda.
Включите нужный трек, чтобы найти или импортировать текст.¦Play a track to find or import lyrics.¦Увімкніть потрібний трек, щоб знайти або імпортувати текст.¦Spielen Sie einen Titel ab, um Liedtext zu suchen oder zu importieren.¦Reproduce una pista para buscar o importar la letra.
Текст загружен из LRC для этого трека.¦LRC lyrics loaded for this track.¦Текст завантажено з LRC для цього треку.¦LRC-Liedtext für diesen Titel geladen.¦Letra LRC cargada para esta pista.
Поиск выключен. Можно импортировать LRC.¦Search is off. You can import an LRC file.¦Пошук вимкнено. Можна імпортувати LRC.¦Die Suche ist aus. Eine LRC-Datei kann importiert werden.¦La búsqueda está desactivada. Puedes importar un archivo LRC.
Для поиска нужен исполнитель. Можно импортировать LRC.¦Search needs an artist name. You can import an LRC file.¦Для пошуку потрібен виконавець. Можна імпортувати LRC.¦Für die Suche ist ein Interpret nötig. Eine LRC-Datei kann importiert werden.¦La búsqueda necesita el nombre del artista. Puedes importar un archivo LRC.
Ищу синхронизированный текст…¦Searching for synchronized lyrics…¦Шукаю синхронізований текст…¦Synchronisierter Liedtext wird gesucht…¦Buscando letra sincronizada…
Синхронизированный текст найден.¦Synchronized lyrics found.¦Синхронізований текст знайдено.¦Synchronisierter Liedtext gefunden.¦Se encontró la letra sincronizada.
Синхронизированный текст не найден.¦No synchronized lyrics found.¦Синхронізований текст не знайдено.¦Kein synchronisierter Liedtext gefunden.¦No se encontró letra sincronizada.
Не удалось найти текст. Можно импортировать LRC.¦Could not look up lyrics. You can import an LRC file.¦Не вдалося знайти текст. Можна імпортувати LRC.¦Die Liedtextsuche ist fehlgeschlagen. Eine LRC-Datei kann importiert werden.¦No se pudo buscar la letra. Puedes importar un archivo LRC.
Трек сменился. Повторите импорт для нужной песни.¦The track changed. Import the file again while the matching song is playing.¦Трек змінився. Повторіть імпорт для потрібної пісні.¦Der Titel hat gewechselt. Importieren Sie die Datei erneut beim passenden Lied.¦La pista cambió. Vuelve a importar el archivo mientras suena la canción correcta.
Не удалось импортировать LRC. Проверьте файл и временные метки.¦Could not import LRC. Check the file and its timestamps.¦Не вдалося імпортувати LRC. Перевірте файл і часові мітки.¦LRC-Import fehlgeschlagen. Prüfen Sie die Datei und die Zeitmarken.¦No se pudo importar el LRC. Revisa el archivo y sus marcas de tiempo.
Системный канал медиа подключён.¦System media channel connected.¦Системний медіаканал підключено.¦Systemmedienkanal verbunden.¦Canal multimedia conectado.
Нет подключения к системному каналу медиа.¦System media channel disconnected.¦Немає підключення до системного медіаканалу.¦Systemmedienkanal nicht verbunden.¦Canal multimedia desconectado.
Данные получены.¦Data received.¦Дані отримано.¦Daten empfangen.¦Datos recibidos.
Укажите город в настройках погоды.¦Choose a city in weather settings.¦Укажіть місто в налаштуваннях погоди.¦Stadt in Wettereinstellungen wählen.¦Elige una ciudad en los ajustes del tiempo.
Данные ещё не получены. Проверьте интернет и город.¦No data yet. Check internet and city.¦Даних ще немає. Перевірте інтернет і місто.¦Noch keine Daten. Internet und Stadt prüfen.¦Sin datos aún. Comprueba internet y la ciudad.
Интерфейс отрисовывается по необходимости.¦Interface renders on demand.¦Інтерфейс малюється за потреби.¦Oberfläche wird bei Bedarf gezeichnet.¦La interfaz se dibuja cuando es necesario.
Стекло¦Glass¦Скло¦Glas¦Cristal
Включено. Видимость при захвате настраивается отдельно.¦Enabled. Capture visibility is configured separately.¦Увімкнено. Видимість під час захоплення налаштовується окремо.¦Aktiviert. Sichtbarkeit bei Aufnahme separat einstellbar.¦Activado. La visibilidad en capturas se configura aparte.
Выключено в настройках внешнего вида.¦Disabled in appearance settings.¦Вимкнено в налаштуваннях вигляду.¦In Darstellungseinstellungen deaktiviert.¦Desactivado en los ajustes de apariencia.
Автозапуск¦Startup¦Автозапуск¦Autostart¦Inicio automático
Запрошен в настройках; Windows может ограничить запуск.¦Requested in settings; Windows may restrict startup.¦Запитано в налаштуваннях; Windows може обмежувати запуск.¦In Einstellungen angefordert; Windows kann Start einschränken.¦Solicitado en ajustes; Windows puede restringir el inicio.
Выключен.¦Disabled.¦Вимкнено.¦Deaktiviert.¦Desactivado.
Обновления¦Updates¦Оновлення¦Updates¦Actualizaciones
Укажите репозиторий GitHub в разделе обновлений.¦Set a GitHub repository on the Updates page.¦Укажіть репозиторій GitHub у розділі оновлень.¦GitHub-Repository unter Updates angeben.¦Indica un repositorio GitHub en Actualizaciones.
Источник задан. Доступность новой версии проверяется в разделе обновлений.¦Source set. Check for a new version on the Updates page.¦Джерело задано. Перевірте нову версію в розділі оновлень.¦Quelle gesetzt. Neue Version unter Updates prüfen.¦Origen configurado. Busca nuevas versiones en Actualizaciones.
Календарь¦Calendar¦Календар¦Kalender¦Calendario
Импортируйте файл .ics на странице календаря.¦Import an .ics file on the Calendar page.¦Імпортуйте .ics на сторінці календаря.¦.ics-Datei auf der Kalenderseite importieren.¦Importa un archivo .ics en Calendario.
Яркость¦Brightness¦Яскравість¦Helligkeit¦Brillo
Экран поддерживает системную регулировку.¦Display supports system brightness control.¦Екран підтримує системне регулювання.¦Display unterstützt Systemhelligkeit.¦La pantalla permite el control de brillo del sistema.
Экран не публикует яркость через Windows WMI.¦Display does not expose brightness through Windows WMI.¦Екран не надає яскравість через Windows WMI.¦Display stellt keine Helligkeit über Windows WMI bereit.¦La pantalla no publica brillo mediante Windows WMI.
Нужен ваш API-ключ.¦Your API key is required.¦Потрібен ваш API-ключ.¦Ihr API-Schlüssel wird benötigt.¦Se requiere tu clave API.
Голосовой сеанс активен.¦Voice session active.¦Голосовий сеанс активний.¦Sprachsitzung aktiv.¦Sesión de voz activa.
Ключ найден; соединение проверяется при запуске голоса.¦Key found; connection is checked when starting voice.¦Ключ знайдено; з’єднання перевіряється при запуску голосу.¦Schlüssel gefunden; Verbindung wird beim Sprachstart geprüft.¦Clave encontrada; la conexión se comprueba al iniciar voz.
Проверка системы¦System check¦Перевірка системи¦Systemprüfung¦Comprobación del sistema
Отчёт содержит технические статусы, без сообщений, имён собеседников и API-ключей.¦Report contains technical status, without messages, contacts or API keys.¦Звіт містить технічні стани без повідомлень, імен і API-ключів.¦Bericht enthält technische Zustände ohne Nachrichten, Kontakte oder API-Schlüssel.¦El informe contiene estados técnicos, sin mensajes, contactos ni claves API.
Копировать отчёт¦Copy report¦Копіювати звіт¦Bericht kopieren¦Copiar informe
Повторить проверку¦Check again¦Повторити перевірку¦Erneut prüfen¦Comprobar de nuevo
Открыть настройки Windows¦Open Windows settings¦Відкрити налаштування Windows¦Windows-Einstellungen öffnen¦Abrir ajustes de Windows
Настроить¦Configure¦Налаштувати¦Konfigurieren¦Configurar
Указать город¦Set city¦Указати місто¦Stadt festlegen¦Elegir ciudad
Переподключить¦Reconnect¦Перепідключити¦Neu verbinden¦Reconectar
Открыть игровой центр¦Open game center¦Відкрити ігровий центр¦Spielcenter öffnen¦Abrir centro de juegos
Версия приложения¦App version¦Версія програми¦App-Version¦Versión de la aplicación
Обновления через GitHub¦Updates from GitHub¦Оновлення через GitHub¦Updates über GitHub¦Actualizaciones de GitHub
Обновления OC - Dynamic Island загружаются из GitHub. Дополнительные программы не нужны. Проверяются контрольная сумма и подпись установщика ocdynamicisland.exe.¦Updates download from GitHub without extra software. The ocdynamicisland.exe checksum and signature are verified.¦Оновлення з GitHub без додаткових програм. Перевіряються контрольна сума й підпис ocdynamicisland.exe.¦Updates von GitHub ohne Zusatzsoftware. Prüfsumme und Signatur von ocdynamicisland.exe werden geprüft.¦Actualizaciones desde GitHub sin programas adicionales. Se verifica la suma y firma de ocdynamicisland.exe.
Репозиторий owner/repository¦Repository owner/repository¦Репозиторій owner/repository¦Repository owner/repository¦Repositorio owner/repository
Проверить¦Check¦Перевірити¦Prüfen¦Comprobar
Проверяю опубликованные релизы…¦Checking published releases…¦Перевіряю опубліковані релізи…¦Veröffentlichte Versionen werden geprüft…¦Buscando versiones publicadas…
Доступна версия¦Version available¦Доступна версія¦Version verfügbar¦Versión disponible
Установлена актуальная версия.¦You are up to date.¦Встановлено актуальну версію.¦Aktuelle Version installiert.¦Tienes la última versión.
Не удалось проверить релиз. Проверьте адрес публичного репозитория и интернет.¦Could not check release. Check public repository address and internet.¦Не вдалося перевірити реліз. Перевірте адресу репозиторію й інтернет.¦Versionsprüfung fehlgeschlagen. Repository-Adresse und Internet prüfen.¦No se pudo comprobar la versión. Revisa el repositorio público e internet.
Что нового¦What's new¦Що нового¦Neuigkeiten¦Novedades
Открыть релиз¦Open release¦Відкрити реліз¦Version öffnen¦Abrir versión
Загрузить и проверить¦Download and verify¦Завантажити й перевірити¦Herunterladen und prüfen¦Descargar y verificar
Установщик загружен; SHA-256 и подпись издателя проверены.¦Installer downloaded; SHA-256 and publisher signature verified.¦Інсталятор завантажено; SHA-256 і підпис видавця перевірено.¦Installer geladen; SHA-256 und Herausgebersignatur geprüft.¦Instalador descargado; SHA-256 y firma verificados.
Загрузка¦Downloading¦Завантаження¦Download¦Descargando
Загрузка отменена.¦Download canceled.¦Завантаження скасовано.¦Download abgebrochen.¦Descarga cancelada.
Не удалось загрузить и проверить обновление.¦Could not download and verify update.¦Не вдалося завантажити й перевірити оновлення.¦Update konnte nicht geladen und geprüft werden.¦No se pudo descargar y verificar la actualización.
Установить обновление¦Install update¦Установити оновлення¦Update installieren¦Instalar actualización
Новые возможности островка¦New island features¦Нові можливості острівця¦Neue Inselfunktionen¦Nuevas funciones de la isla
Канал проекта¦Project channel¦Канал проєкту¦Projektkanal¦Canal del proyecto
Выберите характер островка.¦Choose your island's character.¦Виберіть персонажа острівця.¦Wählen Sie Ihren Inselcharakter.¦Elige el personaje de tu isla.
Решите, что показывать рядом с часами.¦Choose what appears beside the clock.¦Виберіть вміст поруч із годинником.¦Inhalt neben der Uhr wählen.¦Elige qué mostrar junto al reloj.
Наведите курсор для предпросмотра. Нажмите для раскрытия. Правой кнопкой откройте меню. Ctrl+Alt+O — показать островок.¦Hover to preview. Click to expand. Right-click for the menu. Ctrl+Alt+O shows the island.¦Наведіть курсор для перегляду. Натисніть для розкриття. ПКМ — меню. Ctrl+Alt+O — показати острівець.¦Zeigen für Vorschau. Klicken zum Öffnen. Rechtsklick für Menü. Ctrl+Alt+O zeigt die Insel.¦Pasa el cursor para previsualizar. Haz clic para abrir. Clic derecho para el menú. Ctrl+Alt+O muestra la isla.
Персонаж¦Character¦Персонаж¦Charakter¦Personaje
Глазки¦Eyes¦Очі¦Augen¦Ojos
Щенок¦Puppy¦Цуценя¦Welpe¦Cachorro
Лиса¦Fox¦Лисиця¦Fuchs¦Zorro
Без персонажа¦No character¦Без персонажа¦Kein Charakter¦Sin personaje
Справа от часов¦Right of clock¦Праворуч від годинника¦Rechts von der Uhr¦A la derecha del reloj
Далее¦Next¦Далі¦Weiter¦Siguiente
Начать¦Start¦Почати¦Starten¦Comenzar
Добро пожаловать · шаг {0} из 3¦Welcome · step {0} of 3¦Ласкаво просимо · крок {0} із 3¦Willkommen · Schritt {0} von 3¦Bienvenido · paso {0} de 3
Ваш островок¦Your island¦Ваш острівець¦Ihre Insel¦Tu isla
Музыка, встречи и дела — в одном месте¦Music, meetings and tasks in one place¦Музика, зустрічі й справи в одному місці¦Musik, Termine und Aufgaben an einem Ort¦Música, reuniones y tareas en un lugar
Центр¦Hub¦Центр¦Zentrale¦Centro
Настройки¦Settings¦Налаштування¦Einstellungen¦Ajustes
Главная¦Home¦Головна¦Start¦Inicio
Буфер обмена¦Clipboard¦Буфер обміну¦Zwischenablage¦Portapapeles
Система¦System¦Система¦System¦Sistema
Таймеры¦Timers¦Таймери¦Timer¦Temporizadores
Чат¦Chat¦Чат¦Chat¦Chat
Свернуть¦Collapse¦Згорнути¦Einklappen¦Contraer
Своя тема · импорт / экспорт¦Custom theme · import / export¦Власна тема · імпорт / експорт¦Eigenes Thema · Import / Export¦Tema propio · importar / exportar
Тяните правый / нижний край · ПКМ — закончить¦Drag right / bottom edge · right-click to finish¦Тягніть правий / нижній край · ПКМ — завершити¦Rechten / unteren Rand ziehen · Rechtsklick zum Beenden¦Arrastra borde derecho / inferior · clic derecho para terminar
Спрятать островок¦Hide island¦Приховати острівець¦Insel ausblenden¦Ocultar isla
Показать островок¦Show island¦Показати острівець¦Insel anzeigen¦Mostrar isla
Выровнять по центру¦Align to center¦Вирівняти по центру¦Zentrieren¦Centrar
Проверить обновления¦Check for updates¦Перевірити оновлення¦Nach Updates suchen¦Buscar actualizaciones
Игровой режим¦Game mode¦Ігровий режим¦Spielmodus¦Modo juego
Изменить размер…¦Resize…¦Змінити розмір…¦Größe ändern…¦Cambiar tamaño…
Новые возможности¦New features¦Нові можливості¦Neue Funktionen¦Nuevas funciones
Выйти¦Exit¦Вийти¦Beenden¦Salir
Частота обновления островка¦Island refresh rate¦Частота оновлення острівця¦Bildrate der Insel¦Frecuencia de la isla
Загрузка процессора¦CPU usage¦Навантаження процесора¦CPU-Auslastung¦Uso de CPU
Температура процессора¦CPU temperature¦Температура процесора¦CPU-Temperatur¦Temperatura de CPU
Загрузка графики¦GPU usage¦Навантаження графіки¦GPU-Auslastung¦Uso de GPU
Уведомления приостановлены на время игры¦Notifications paused during gaming¦Сповіщення призупинено під час гри¦Benachrichtigungen während des Spiels pausiert¦Notificaciones pausadas durante el juego
Discord · Голос¦Discord · Voice¦Discord · Голос¦Discord · Sprache¦Discord · Voz
время вышло¦time is up¦час вийшов¦Zeit abgelaufen¦tiempo agotado
начинается сейчас¦starting now¦починається зараз¦beginnt jetzt¦empieza ahora
через 10 минут¦in 10 minutes¦через 10 хвилин¦in 10 Minuten¦en 10 minutos
готово¦done¦готово¦fertig¦listo
новое сообщение¦new message¦нове повідомлення¦neue Nachricht¦nuevo mensaje
Звук выключен¦Muted¦Звук вимкнено¦Stumm¦Silenciado
Громкость¦Volume¦Гучність¦Lautstärke¦Volumen
OC - Dynamic Island 1.1.9 · нажмите, чтобы увидеть новинки¦OC - Dynamic Island 1.1.9 · click for new features¦OC - Dynamic Island 1.1.9 · натисніть для перегляду новинок¦OC - Dynamic Island 1.1.9 · klicken für Neuigkeiten¦OC - Dynamic Island 1.1.9 · pulsa para ver novedades
Измерение игры выключено.¦Game measurement off.¦Вимірювання гри вимкнено.¦Spielmessung deaktiviert.¦Medición del juego desactivada.
Ожидание разрешения Windows…¦Waiting for Windows permission…¦Очікування дозволу Windows…¦Warte auf Windows-Berechtigung…¦Esperando permiso de Windows…
Сборщик подключён. Запускаю трассировку кадров…¦Monitor connected. Starting frame tracing…¦Збирач підключено. Запускаю трасування кадрів…¦Messhilfe verbunden. Frame-Aufzeichnung startet…¦Monitor conectado. Iniciando el seguimiento de fotogramas…
Сборщик не ответил вовремя. Переключите игровой режим.¦The monitor timed out. Toggle game mode off and on.¦Збирач не відповів вчасно. Вимкніть і знову ввімкніть ігровий режим.¦Zeitüberschreitung bei der Messung. Schalten Sie den Spielmodus aus und wieder ein.¦El monitor no respondió a tiempo. Desactiva y vuelve a activar el modo juego.
Измеряется частота кадров активной игры.¦Measuring the active game frame rate.¦Вимірюється частота кадрів активної гри.¦Bildrate des aktiven Spiels wird gemessen.¦Midiendo los FPS del juego activo.
Нет событий отправки кадров у активного окна. Переключитесь на игру.¦No frame submissions from the active window. Switch to the game.¦Активне вікно не надсилає кадри. Перемкніться на гру.¦Keine Frame-Übermittlungen vom aktiven Fenster. Wechseln Sie zum Spiel.¦La ventana activa no envía fotogramas. Cambia al juego.
Разрешение администратора отменено.¦Administrator permission canceled.¦Дозвіл адміністратора скасовано.¦Administratorberechtigung abgebrochen.¦Permiso de administrador cancelado.
Сеанс измерения остановлен.¦Measurement session stopped.¦Сеанс вимірювання зупинено.¦Messung gestoppt.¦Sesión de medición detenida.
Счётчик игры недоступен. Попробуйте включить его снова.¦Game counter unavailable. Try enabling it again.¦Лічильник гри недоступний. Спробуйте ввімкнути знову.¦Spielzähler nicht verfügbar. Erneut aktivieren.¦Contador del juego no disponible. Actívalo de nuevo.
Микрофон недоступен. Проверьте разрешения Windows.¦Microphone unavailable. Check Windows permissions.¦Мікрофон недоступний. Перевірте дозволи Windows.¦Mikrofon nicht verfügbar. Windows-Berechtigungen prüfen.¦Micrófono no disponible. Revisa los permisos de Windows.
Микрофон включён · разговор с Gemini¦Microphone on · talking with Gemini¦Мікрофон увімкнено · розмова з Gemini¦Mikrofon an · Gespräch mit Gemini¦Micrófono activado · hablando con Gemini
Соединение прервано.¦Connection interrupted.¦З’єднання перервано.¦Verbindung unterbrochen.¦Conexión interrumpida.
Голосовой сеанс завершён сервером.¦Voice session ended by server.¦Голосовий сеанс завершено сервером.¦Sprachsitzung vom Server beendet.¦El servidor terminó la sesión de voz.
Gemini отклонил запрос. Проверьте ключ, модель и доступ Live API.¦Gemini rejected the request. Check key, model and Live API access.¦Gemini відхилив запит. Перевірте ключ, модель і доступ Live API.¦Gemini hat Anfrage abgelehnt. Schlüssel, Modell und Live-API-Zugriff prüfen.¦Gemini rechazó la solicitud. Revisa clave, modelo y acceso a Live API.
Ошибка голосового соединения. Проверьте сеть и модель.¦Voice connection error. Check network and model.¦Помилка голосового з’єднання. Перевірте мережу й модель.¦Sprachverbindungsfehler. Netzwerk und Modell prüfen.¦Error de conexión de voz. Revisa red y modelo.
Подключён .ics, событий на 30 дней: {0}.¦ICS connected, events in 30 days: {0}.¦Підключено ICS, подій на 30 днів: {0}.¦ICS verbunden, Termine in 30 Tagen: {0}.¦ICS conectado, eventos en 30 días: {0}.
Прогресс программ вынесен в отдельную капсулу справа и отключается в настройках.¦App progress appears in a separate right capsule with a settings toggle.¦Прогрес програм в окремій капсулі праворуч із перемикачем у налаштуваннях.¦App-Fortschritt in separater rechter Kapsel mit Einstellungsschalter.¦Progreso en una cápsula derecha con interruptor en ajustes.
История буфера обмена хранит текст и скриншоты за последние 24 часа.¦Clipboard history stores text and screenshots for 24 hours.¦Історія буфера обміну зберігає текст і знімки екрана за 24 години.¦Zwischenablage speichert Text und Screenshots für 24 Stunden.¦El historial guarda texto y capturas durante 24 horas.
Исправлены прозрачность стекла, переводы разделов и слежение персонажей за курсором.¦Fixed glass transparency, section translations and characters following the cursor.¦Виправлено прозорість скла, переклади розділів і стеження персонажів за курсором.¦Glastransparenz, Übersetzungen und Cursor-Verfolgung der Charaktere korrigiert.¦Corregidas transparencia, traducciones y seguimiento del cursor de los personajes.
Великий Новгород¦Veliky Novgorod¦Великий Новгород¦Weliki Nowgorod¦Veliki Nóvgorod
Москва¦Moscow¦Москва¦Moskau¦Moscú
Санкт-Петербург¦Saint Petersburg¦Санкт-Петербург¦Sankt Petersburg¦San Petersburgo
Казань¦Kazan¦Казань¦Kasan¦Kazán
Укажите owner/repository или ссылку https://github.com/owner/repository.¦Enter owner/repository or a GitHub repository URL.¦Укажіть owner/repository або посилання на GitHub.¦owner/repository oder GitHub-Repository-URL angeben.¦Introduce owner/repository o la URL del repositorio GitHub.
Ответ сервера слишком большой.¦Server response too large.¦Відповідь сервера завелика.¦Serverantwort zu groß.¦Respuesta del servidor demasiado grande.
Версия релиза должна иметь вид v1.2.3.¦Release version must look like v1.2.3.¦Версія релізу має виглядати як v1.2.3.¦Versionsformat muss v1.2.3 sein.¦La versión debe tener el formato v1.2.3.
Релиз не опубликован.¦Release is not published.¦Реліз не опубліковано.¦Version nicht veröffentlicht.¦La versión no está publicada.
В релизе нет установщика ocdynamicisland.exe.¦Release has no ocdynamicisland.exe installer.¦У релізі немає інсталятора ocdynamicisland.exe.¦Kein Installer ocdynamicisland.exe in der Version.¦La versión no incluye ocdynamicisland.exe.
В релизе нет SHA256SUMS.txt. Обновление отклонено.¦Release has no SHA256SUMS.txt. Update rejected.¦У релізі немає SHA256SUMS.txt. Оновлення відхилено.¦SHA256SUMS.txt fehlt. Update abgelehnt.¦Falta SHA256SUMS.txt. Actualización rechazada.
Файл обновления должен находиться в том же репозитории GitHub.¦Update file must be in the same GitHub repository.¦Файл оновлення має бути в тому самому репозиторії GitHub.¦Updatedatei muss im selben GitHub-Repository liegen.¦El archivo debe estar en el mismo repositorio GitHub.
Ответ GitHub слишком большой.¦GitHub response too large.¦Відповідь GitHub завелика.¦GitHub-Antwort zu groß.¦Respuesta de GitHub demasiado grande.
Некорректный файл контрольных сумм.¦Invalid checksum file.¦Некоректний файл контрольних сум.¦Ungültige Prüfsummendatei.¦Archivo de sumas de verificación inválido.
Нет однозначной SHA-256 для установщика. Обновление отклонено.¦Installer SHA-256 is missing or ambiguous. Update rejected.¦SHA-256 інсталятора відсутня або неоднозначна. Оновлення відхилено.¦Installer-SHA-256 fehlt oder ist mehrdeutig. Update abgelehnt.¦SHA-256 del instalador ausente o ambigua. Actualización rechazada.
Некорректные контрольные суммы.¦Invalid checksums.¦Некоректні контрольні суми.¦Ungültige Prüfsummen.¦Sumas de verificación inválidas.
SHA-256 установщика не совпадает. Обновление отклонено.¦Installer SHA-256 mismatch. Update rejected.¦SHA-256 інсталятора не збігається. Оновлення відхилено.¦Installer-SHA-256 stimmt nicht überein. Update abgelehnt.¦SHA-256 del instalador no coincide. Actualización rechazada.
Windows не подтвердила подпись издателя установщика.¦Windows could not verify the installer publisher signature.¦Windows не підтвердила підпис видавця інсталятора.¦Windows konnte die Herausgebersignatur nicht bestätigen.¦Windows no pudo verificar la firma del editor.
Неизвестный код темы.¦Unknown theme code.¦Невідомий код теми.¦Unbekannter Themencode.¦Código de tema desconocido.
Недопустимые цвета темы.¦Invalid theme colors.¦Неприпустимі кольори теми.¦Ungültige Themenfarben.¦Colores de tema inválidos.
Повреждённый код темы.¦Corrupt theme code.¦Пошкоджений код теми.¦Beschädigter Themencode.¦Código de tema dañado.
Заметки¦Notes¦Нотатки¦Notizen¦Notas
Виджеты¦Widgets¦Віджети¦Widgets¦Widgets
{0} мин¦{0} min¦{0} хв¦{0} Min.¦{0} min
1 ч¦1 hr¦1 год¦1 Std.¦1 h
30 мин¦30 min¦30 хв¦30 Min.¦30 min
2 ч¦2 hr¦2 год¦2 Std.¦2 h
До {0}¦Until {0}¦До {0}¦Bis {0}¦Hasta las {0}
Чтобы задать длительность, выключите текущий режим.¦Turn off the current mode to set a duration.¦Вимкніть поточний режим, щоб задати тривалість.¦Zum Festlegen einer Dauer den aktuellen Modus ausschalten.¦Desactiva el modo actual para elegir una duración.
Введите от 0,05 до 1440 минут.¦Enter 0.05 to 1440 minutes.¦Введіть від 0,05 до 1440 хвилин.¦0,05 bis 1440 Minuten eingeben.¦Introduce entre 0,05 y 1440 minutos.
Время отдохнуть¦Time for a break¦Час відпочити¦Zeit für eine Pause¦Hora de descansar
За работу¦Back to work¦До роботи¦Zurück an die Arbeit¦De vuelta al trabajo
Минуты¦Minutes¦Хвилини¦Minuten¦Minutos
Пауза¦Pause¦Пауза¦Pause¦Pausa
Перерыв¦Break¦Перерва¦Pause¦Descanso
Помодоро¦Pomodoro¦Помодоро¦Pomodoro¦Pomodoro
Продолжить¦Resume¦Продовжити¦Fortsetzen¦Reanudar
Работа · {0}/4¦Focus · {0}/4¦Робота · {0}/4¦Fokus · {0}/4¦Concentración · {0}/4
Сброс¦Reset¦Скинути¦Zurücksetzen¦Reiniciar
Секундомер¦Stopwatch¦Секундомір¦Stoppuhr¦Cronómetro
Старт¦Start¦Старт¦Start¦Iniciar
Таймер завершён¦Timer finished¦Таймер завершено¦Timer abgelaufen¦Temporizador terminado
Ближайшее¦Coming up¦Найближче¦Als Nächstes¦Próximos eventos
Вставьте ссылку HTTPS на календарь в формате ICS или импортируйте файл.¦Paste an HTTPS link to an ICS calendar or import a file.¦Вставте HTTPS-посилання на календар ICS або імпортуйте файл.¦HTTPS-Link zu einem ICS-Kalender einfügen oder Datei importieren.¦Pega un enlace HTTPS a un calendario ICS o importa un archivo.
Введите ссылку HTTPS на календарь.¦Enter an HTTPS calendar link.¦Введіть HTTPS-посилання на календар.¦Einen HTTPS-Kalenderlink eingeben.¦Introduce un enlace HTTPS al calendario.
Проверьте время напоминания.¦Check the reminder time.¦Перевірте час нагадування.¦Zeit der Erinnerung prüfen.¦Comprueba la hora del recordatorio.
18:30, завтра 9:00, через 20 мин, 15.11 14:00¦18:30, tomorrow 9:00, in 20 min, 15.11 14:00¦18:30, завтра 9:00, 20 хв, 15.11 14:00¦18:30, morgen 9:00, 20 min, 15.11 14:00¦18:30, mañana 9:00, 20 min, 15.11 14:00
Добавить¦Add¦Додати¦Hinzufügen¦Añadir
Календарь обновлён¦Calendar updated¦Календар оновлено¦Kalender aktualisiert¦Calendario actualizado
Календарь обновляется…¦Refreshing calendar…¦Календар оновлюється…¦Kalender wird aktualisiert…¦Actualizando calendario…
На неделю ничего не запланировано¦Nothing scheduled for the next week¦На тиждень нічого не заплановано¦Für die nächste Woche ist nichts geplant¦Nada previsto para la próxima semana
Напоминание¦Reminder¦Нагадування¦Erinnerung¦Recordatorio
Напомнить¦Remind me¦Нагадати¦Erinnern¦Recordarme
Не удалось обновить календарь. Проверьте ссылку или файл ICS.¦Could not update the calendar. Check the link or ICS file.¦Не вдалося оновити календар. Перевірте посилання або файл ICS.¦Kalender konnte nicht aktualisiert werden. Link oder ICS-Datei prüfen.¦No se pudo actualizar el calendario. Comprueba el enlace o archivo ICS.
Обновить¦Refresh¦Оновити¦Aktualisieren¦Actualizar
Подключите календарь заново.¦Reconnect the calendar.¦Підключіть календар знову.¦Kalender erneut verbinden.¦Vuelve a conectar el calendario.
Подключить¦Connect¦Підключити¦Verbinden¦Conectar
Подписка подключена¦Subscription connected¦Підписку підключено¦Abonnement verbunden¦Suscripción conectada
Ссылка на календарь¦Calendar link¦Посилання на календар¦Kalenderlink¦Enlace al calendario
Файл ICS импортирован¦ICS file imported¦Файл ICS імпортовано¦ICS-Datei importiert¦Archivo ICS importado
{0:0.0} / {1:0.0} ГБ¦{0:0.0} / {1:0.0} GB¦{0:0.0} / {1:0.0} ГБ¦{0:0.0} / {1:0.0} GB¦{0:0.0} / {1:0.0} GB
Активность не обнаружена¦No activity detected¦Активність не виявлено¦Keine Aktivität erkannt¦Sin actividad detectada
Батарея {0}%¦Battery {0}%¦Батарея {0}%¦Akku {0}%¦Batería {0}%
Батарея {0}% · заряжается¦Battery {0}% · charging¦Батарея {0}% · заряджається¦Akku {0}% · lädt¦Batería {0}% · cargando
Батарея недоступна¦Battery unavailable¦Батарея недоступна¦Akku nicht verfügbar¦Batería no disponible
Блокировка¦Lock¦Заблокувати¦Sperren¦Bloquear
Видеокарта¦Graphics¦Відеокарта¦Grafik¦Gráficos
Включено¦On¦Увімкнено¦An¦Activado
Выключение¦Shut down¦Вимкнути¦Herunterfahren¦Apagar
Выключено¦Off¦Вимкнено¦Aus¦Desactivado
Выключить компьютер сейчас?¦Shut down this computer now?¦Вимкнути комп’ютер зараз?¦Computer jetzt herunterfahren?¦¿Apagar el equipo ahora?
Голосовой канал не обнаружен¦No voice channel detected¦Голосовий канал не виявлено¦Kein Sprachkanal erkannt¦No se detectó un canal de voz
Используется¦In use¦Використовується¦In Verwendung¦En uso
Камера¦Camera¦Камера¦Kamera¦Cámara
Микрофон¦Microphone¦Мікрофон¦Mikrofon¦Micrófono
Микрофон и камера¦Microphone and camera¦Мікрофон і камера¦Mikrofon und Kamera¦Micrófono y cámara
Настройки Windows¦Windows Settings¦Налаштування Windows¦Windows-Einstellungen¦Configuración de Windows
Не удалось перевести компьютер в спящий режим.¦Could not put the computer to sleep.¦Не вдалося перевести комп’ютер у режим сну.¦Der Computer konnte nicht in den Ruhezustand versetzt werden.¦No se pudo suspender el equipo.
Открыть Discord¦Open Discord¦Відкрити Discord¦Discord öffnen¦Abrir Discord
Голосовой звонок определяется даже при свёрнутом Discord.¦Voice calls are detected even when Discord is minimized.¦Голосовий дзвінок визначається навіть зі згорнутим Discord.¦Sprachanrufe werden auch bei minimiertem Discord erkannt.¦Las llamadas de voz se detectan incluso con Discord minimizado.
Таймер показывает время с момента подключения. Нажмите на него, чтобы открыть Discord.¦The timer shows the elapsed time since connection. Click it to open Discord.¦Таймер показує час від підключення. Натисніть на нього, щоб відкрити Discord.¦Der Timer zeigt die Zeit seit dem Verbindungsaufbau. Klicke darauf, um Discord zu öffnen.¦El temporizador muestra el tiempo desde la conexión. Púlsalo para abrir Discord.
Кнопка наушников выбирает выход Discord, а кнопка микрофона переключает устройство записи в Windows.¦The headphones button selects the Discord output, and the microphone button mutes or unmutes the recording device in Windows.¦Кнопка навушників вибирає вихід Discord, а кнопка мікрофона вмикає або вимикає пристрій запису у Windows.¦Die Kopfhörertaste wählt die Discord-Ausgabe, die Mikrofontaste schaltet das Aufnahmegerät in Windows stumm oder wieder ein.¦El botón de auriculares elige la salida de Discord y el del micrófono silencia o activa el dispositivo de grabación en Windows.
Выход звука Discord¦Discord audio output¦Аудіовихід Discord¦Discord-Audioausgabe¦Salida de audio de Discord
Микрофон недоступен¦Microphone unavailable¦Мікрофон недоступний¦Mikrofon nicht verfügbar¦Micrófono no disponible
Включить микрофон¦Unmute microphone¦Увімкнути мікрофон¦Mikrofon einschalten¦Activar micrófono
Выключить микрофон¦Mute microphone¦Вимкнути мікрофон¦Mikrofon stummschalten¦Silenciar micrófono
Действует на все приложения, использующие этот микрофон.¦Applies to all apps using this microphone.¦Діє на всі програми, що використовують цей мікрофон.¦Gilt für alle Apps, die dieses Mikrofon verwenden.¦Se aplica a todas las aplicaciones que usan este micrófono.
Не удалось переключить микрофон.¦Could not toggle the microphone.¦Не вдалося перемкнути мікрофон.¦Das Mikrofon konnte nicht umgeschaltet werden.¦No se pudo cambiar el estado del micrófono.
Поиск аудиоустройств…¦Finding audio devices…¦Пошук аудіопристроїв…¦Audiogeräte werden gesucht…¦Buscando dispositivos de audio…
Выход Windows по умолчанию¦Windows default output¦Стандартний вихід Windows¦Windows-Standardausgabe¦Salida predeterminada de Windows
Сейчас используется¦In use¦Зараз використовується¦Wird verwendet¦En uso
Выход Discord недоступен¦Discord output unavailable¦Аудіовихід Discord недоступний¦Discord-Ausgabe nicht verfügbar¦Salida de Discord no disponible
Микшер громкости Windows¦Windows volume mixer¦Мікшер гучності Windows¦Windows-Lautstärkemixer¦Mezclador de volumen de Windows
Discord остался на прежнем выходе. Выберите «По умолчанию» в настройках звука Discord и переподключитесь.¦Discord stayed on the previous output. Select Default in Discord audio settings and reconnect.¦Discord залишився на попередньому виході. Виберіть «За замовчуванням» у налаштуваннях звуку Discord і підключіться знову.¦Discord nutzt weiterhin die bisherige Ausgabe. Wähle in den Discord-Audioeinstellungen „Standard“ und verbinde dich erneut.¦Discord sigue usando la salida anterior. Selecciona «Predeterminado» en los ajustes de audio de Discord y vuelve a conectarte.
Не удалось изменить выход Discord.¦Could not change Discord's audio output.¦Не вдалося змінити аудіовихід Discord.¦Die Discord-Ausgabe konnte nicht geändert werden.¦No se pudo cambiar la salida de audio de Discord.
Память¦Memory¦Пам’ять¦Arbeitsspeicher¦Memoria
Перезагрузить компьютер сейчас?¦Restart this computer now?¦Перезапустити комп’ютер зараз?¦Computer jetzt neu starten?¦¿Reiniciar el equipo ahora?
Перезагрузка¦Restart¦Перезапустити¦Neu starten¦Reiniciar
Переключить¦Toggle¦Перемкнути¦Umschalten¦Alternar
Процессор¦CPU¦Процесор¦Prozessor¦Procesador
Пункт управления¦Control Center¦Пункт керування¦Kontrollzentrum¦Centro de control
Режим полёта¦Airplane mode¦Режим польоту¦Flugmodus¦Modo avión
Сон¦Sleep¦Сон¦Energie sparen¦Suspender
Состояние недоступно¦Status unavailable¦Стан недоступний¦Status nicht verfügbar¦Estado no disponible
Чистый стол¦Clean desktop¦Чистий робочий стіл¦Aufgeräumter Desktop¦Escritorio limpio
Достигнут предел: 100 заметок.¦Limit reached: 100 notes.¦Досягнуто межі: 100 нотаток.¦Limit von 100 Notizen erreicht.¦Límite de 100 notas alcanzado.
Заметка¦Note¦Нотатка¦Notiz¦Nota
Копировать¦Copy¦Копіювати¦Kopieren¦Copiar
Не удалось сохранить заметку.¦Could not save the note.¦Не вдалося зберегти нотатку.¦Notiz konnte nicht gespeichert werden.¦No se pudo guardar la nota.
Пока пусто — напишите заметку выше¦No notes yet. Write one above.¦Нотаток ще немає. Напишіть вище.¦Noch keine Notizen. Oben eine schreiben.¦Aún no hay notas. Escribe una arriba.
Редактировать¦Edit¦Редагувати¦Bearbeiten¦Editar
Сохранить заметку¦Save note¦Зберегти нотатку¦Notiz speichern¦Guardar nota
{0} завершил ответ¦{0} finished responding¦{0} завершив відповідь¦{0} hat die Antwort abgeschlossen¦{0} terminó de responder
Голосовой ассистент · Gemini Live¦Voice assistant · Gemini Live¦Голосовий асистент · Gemini Live¦Sprachassistent · Gemini Live¦Asistente de voz · Gemini Live
Громкость ИИ-ассистента¦AI assistant volume¦Гучність ШІ-асистента¦Lautstärke des KI-Assistenten¦Volumen del asistente de IA
Ключ не задан. Можно использовать GEMINI_API_KEY.¦No key set. You can use GEMINI_API_KEY.¦Ключ не задано. Можна використати GEMINI_API_KEY.¦Kein Schlüssel gesetzt. GEMINI_API_KEY kann verwendet werden.¦No hay clave. Puedes usar GEMINI_API_KEY.
Ключ Gemini доступен.¦Gemini key available.¦Ключ Gemini доступний.¦Gemini-Schlüssel verfügbar.¦Clave de Gemini disponible.
Не удалось изменить ключ Gemini.¦Could not change the Gemini key.¦Не вдалося змінити ключ Gemini.¦Gemini-Schlüssel konnte nicht geändert werden.¦No se pudo cambiar la clave de Gemini.
Не удалось изменить настройки агента.¦Could not change the agent settings.¦Не вдалося змінити налаштування агента.¦Agenteneinstellungen konnten nicht geändert werden.¦No se pudo cambiar la configuración del agente.
Не удалось прочитать настройки агента.¦Could not read the agent settings.¦Не вдалося прочитати налаштування агента.¦Agenteneinstellungen konnten nicht gelesen werden.¦No se pudo leer la configuración del agente.
Островок покажет уведомление, когда агент завершит ответ.¦The island shows a notification when the agent finishes responding.¦Острівець покаже сповіщення, коли агент завершить відповідь.¦Die Insel meldet, wenn der Agent seine Antwort abgeschlossen hat.¦La isla avisa cuando el agente termina de responder.
Отключить¦Disconnect¦Відключити¦Trennen¦Desconectar
Хук уведомлений записан. Проверьте его в /hooks.¦Notification hook saved. Check it with /hooks.¦Хук сповіщень збережено. Перевірте його через /hooks.¦Benachrichtigungs-Hook gespeichert. Mit /hooks prüfen.¦Hook de avisos guardado. Revísalo con /hooks.
Хук уведомлений записан. Codex может запросить доверие через /hooks.¦Notification hook saved. Codex may ask you to trust it in /hooks.¦Хук сповіщень збережено. Codex може попросити довіру через /hooks.¦Benachrichtigungs-Hook gespeichert. Codex fordert möglicherweise Vertrauen in /hooks an.¦Hook de avisos guardado. Codex puede pedirte que confíes en él desde /hooks.
API-ключ Gemini¦Gemini API key¦Ключ API Gemini¦Gemini-API-Schlüssel¦Clave de API de Gemini
{0:0.0} ГБ свободно¦{0:0.0} GB free¦{0:0.0} ГБ вільно¦{0:0.0} GB frei¦{0:0.0} GB libres
Воспроизвести / пауза¦Play / pause¦Відтворити / пауза¦Wiedergabe / Pause¦Reproducir / pausar
Готовые наборы¦Presets¦Готові набори¦Voreinstellungen¦Conjuntos
Диск¦Disk¦Диск¦Laufwerk¦Disco
Добавить виджет¦Add widget¦Додати віджет¦Widget hinzufügen¦Añadir widget
Добавлено: {0}¦Added: {0}¦Додано: {0}¦Hinzugefügt: {0}¦Añadidos: {0}
Календарь, напоминания, заметки, таймер и фокус¦Calendar, reminders, notes, timer and focus¦Календар, нагадування, нотатки, таймер і фокус¦Kalender, Erinnerungen, Notizen, Timer und Fokus¦Calendario, recordatorios, notas, temporizador y concentración
Лес¦Forest¦Ліс¦Wald¦Bosque
Матовое стекло¦Frosted glass¦Матове скло¦Milchglas¦Vidrio esmerilado
Мой день¦My day¦Мій день¦Mein Tag¦Mi día
На рабочем столе¦On the desktop¦На робочому столі¦Auf dem Desktop¦En el escritorio
Напоминания¦Reminders¦Нагадування¦Erinnerungen¦Recordatorios
Нет активного трека¦No active track¦Немає активного треку¦Kein aktiver Titel¦Sin pista activa
Нет заметок¦No notes¦Немає нотаток¦Keine Notizen¦Sin notas
Нет напоминаний¦No reminders¦Немає нагадувань¦Keine Erinnerungen¦Sin recordatorios
Океан¦Ocean¦Океан¦Ozean¦Océano
Открыть раздел¦Open section¦Відкрити розділ¦Bereich öffnen¦Abrir sección
Поверх всех окон¦Always on top¦Поверх усіх вікон¦Immer im Vordergrund¦Siempre visible
Погода недоступна¦Weather unavailable¦Погода недоступна¦Wetter nicht verfügbar¦Tiempo no disponible
Предыдущий трек¦Previous track¦Попередній трек¦Vorheriger Titel¦Pista anterior
Работа¦Work¦Робота¦Arbeit¦Trabajo
Светлая¦Light¦Світла¦Hell¦Clara
Скрыть с рабочего стола¦Hide from the desktop¦Сховати з робочого столу¦Auf dem Desktop ausblenden¦Ocultar del escritorio
Следующий трек¦Next track¦Наступний трек¦Nächster Titel¦Pista siguiente
Слива¦Plum¦Слива¦Pflaume¦Ciruela
Старт / пауза¦Start / pause¦Старт / пауза¦Start / Pause¦Iniciar / pausar
Тема виджетов¦Widget theme¦Тема віджетів¦Widget-Design¦Tema de los widgets
Тёмная¦Dark¦Темна¦Dunkel¦Oscura
Удалить виджет¦Remove widget¦Видалити віджет¦Widget entfernen¦Quitar widget
Часы¦Clock¦Годинник¦Uhr¦Reloj
Часы, погода, календарь и музыка¦Clock, weather, calendar and music¦Годинник, погода, календар і музика¦Uhr, Wetter, Kalender und Musik¦Reloj, tiempo, calendario y música
Элементов в истории: {0}¦History items: {0}¦Елементів в історії: {0}¦Verlaufseinträge: {0}¦Elementos del historial: {0}
Таймер с круговым индикатором, помодоро и секундомер.¦Timer with a progress ring, Pomodoro and stopwatch.¦Таймер із круговим індикатором, помодоро й секундомір.¦Timer mit Fortschrittsring, Pomodoro und Stoppuhr.¦Temporizador con anillo de progreso, Pomodoro y cronómetro.
Календарь на неделю, напоминания и подписки ICS.¦Weekly agenda, reminders and ICS subscriptions.¦Календар на тиждень, нагадування й підписки ICS.¦Wochenagenda, Erinnerungen und ICS-Abonnements.¦Agenda semanal, recordatorios y suscripciones ICS.
Пункт управления Windows и показатели CPU, GPU и памяти.¦Windows controls and CPU, GPU and memory usage.¦Пункт керування Windows і показники CPU, GPU та пам’яті.¦Windows-Steuerung und Auslastung von CPU, GPU und Speicher.¦Controles de Windows y uso de CPU, GPU y memoria.
Заметки, виджеты рабочего стола и уведомления ИИ-агентов.¦Notes, desktop widgets and AI agent notifications.¦Нотатки, віджети робочого столу й сповіщення ШІ-агентів.¦Notizen, Desktop-Widgets und Benachrichtigungen von KI-Agenten.¦Notas, widgets de escritorio y avisos de agentes de IA.
Не подключено¦Disconnected¦Не підключено¦Nicht verbunden¦Sin conexión
Месяц¦Month¦Місяць¦Monat¦Mes
Мировое время¦World clock¦Світовий час¦Weltuhr¦Reloj mundial
Калькулятор¦Calculator¦Калькулятор¦Taschenrechner¦Calculadora
Сеть¦Network¦Мережа¦Netzwerk¦Red
Быстрые действия¦Quick actions¦Швидкі дії¦Schnellaktionen¦Acciones rápidas
Время работы ПК¦PC uptime¦Час роботи ПК¦PC-Betriebszeit¦Tiempo de actividad
{0} д · {1} ч · {2} мин¦{0} d · {1} h · {2} min¦{0} д · {1} год · {2} хв¦{0} T · {1} Std. · {2} Min.¦{0} d · {1} h · {2} min
Закат¦Sunset¦Захід сонця¦Sonnenuntergang¦Atardecer
Индиго¦Indigo¦Індиго¦Indigo¦Índigo
New York¦New York¦Нью-Йорк¦New York¦Nueva York
Tokyo¦Tokyo¦Токіо¦Tokio¦Tokio
Ошибка¦Error¦Помилка¦Fehler¦Error
Убавить громкость¦Volume down¦Зменшити гучність¦Leiser¦Bajar volumen
Прибавить громкость¦Volume up¦Збільшити гучність¦Lauter¦Subir volumen
Включить / выключить звук¦Toggle mute¦Увімкнути / вимкнути звук¦Ton umschalten¦Silenciar o activar sonido
""";
}
