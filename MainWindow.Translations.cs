namespace DynamicIslandWin;

public partial class MainWindow
{
    // Russian source, English, Ukrainian, German, Spanish. User content is never
    // sent to an online translator; this catalog ships with the application.
    private static readonly Dictionary<string, string[]> AdditionalTranslations = TranslationCatalog
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line => line.Split('¦')).ToDictionary(parts => parts[0], parts => parts, StringComparer.Ordinal);

    private const string TranslationCatalog = """
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
Для измерения FPS игры включите встроенный счётчик. Windows запросит права администратора для ETW. Дополнительных программ не требуется.¦Enable the built-in game FPS counter. Windows requests administrator access for ETW. No extra software is needed.¦Увімкніть вбудований лічильник FPS. Windows запитає права адміністратора для ETW. Додаткові програми не потрібні.¦Integrierten FPS-Zähler aktivieren. Windows fragt nach Administratorrechten für ETW. Keine Zusatzsoftware nötig.¦Activa el contador FPS integrado. Windows solicitará permisos de administrador para ETW. No requiere programas adicionales.
Автоматический HUD в полноэкранной игре¦Automatic HUD in fullscreen games¦Автоматичний HUD у повноекранній грі¦Automatisches HUD in Vollbildspielen¦HUD automático en juegos a pantalla completa
Включить FPS игры¦Enable game FPS¦Увімкнути FPS гри¦Spiel-FPS aktivieren¦Activar FPS del juego
Остановить счётчик¦Stop counter¦Зупинити лічильник¦Zähler stoppen¦Detener contador
Измеряются кадры, отправленные через DirectX Present. Для игр OpenGL / Vulkan без событий DXGI счётчик может быть недоступен.¦Counts DirectX Present frames. OpenGL / Vulkan games without DXGI events may be unsupported.¦Рахує кадри DirectX Present. Ігри OpenGL / Vulkan без подій DXGI можуть не підтримуватися.¦Zählt DirectX-Present-Frames. OpenGL / Vulkan ohne DXGI-Ereignisse eventuell nicht unterstützt.¦Cuenta fotogramas DirectX Present. OpenGL / Vulkan sin eventos DXGI pueden no ser compatibles.
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
Караоке¦Karaoke¦Караоке¦Karaoke¦Karaoke
Строка следует позиции системного медиаплеера. Для локального LRC запустите соответствующую песню перед импортом.¦Lyrics follow the system media player. Play the matching song before importing a local LRC.¦Текст слідує за системним плеєром. Перед імпортом LRC запустіть відповідну пісню.¦Text folgt dem Systemplayer. Vor lokalem LRC-Import das passende Lied starten.¦La letra sigue al reproductor del sistema. Reproduce la canción antes de importar su LRC.
Нет активного медиа¦No active media¦Немає активного медіа¦Keine aktiven Medien¦Sin multimedia activa
Показывать текст в островке¦Show lyrics in the island¦Показувати текст в острівці¦Liedtext auf der Insel anzeigen¦Mostrar letra en la isla
Искать текст в LRCLIB (передаётся название и исполнитель)¦Search LRCLIB (sends song title and artist)¦Шукати в LRCLIB (передається назва й виконавець)¦LRCLIB durchsuchen (sendet Titel und Künstler)¦Buscar en LRCLIB (envía título y artista)
Импортировать LRC¦Import LRC¦Імпортувати LRC¦LRC importieren¦Importar LRC
Синхронизированный текст|*.lrc¦Synchronized lyrics|*.lrc¦Синхронізований текст|*.lrc¦Synchronisierter Liedtext|*.lrc¦Letra sincronizada|*.lrc
Выберите LRC или включите поиск текста в сети.¦Import LRC or enable online lyrics search.¦Імпортуйте LRC або ввімкніть пошук тексту в мережі.¦LRC importieren oder Online-Suche aktivieren.¦Importa LRC o activa la búsqueda en línea.
Синхронизированный текст не найден. Можно импортировать LRC.¦No synchronized lyrics found. You can import LRC.¦Синхронізований текст не знайдено. Імпортуйте LRC.¦Kein synchronisierter Text gefunden. LRC importieren.¦No hay letra sincronizada. Puedes importar LRC.
Не удалось получить текст. Проверьте интернет или импортируйте LRC.¦Could not fetch lyrics. Check your connection or import LRC.¦Не вдалося отримати текст. Перевірте інтернет або імпортуйте LRC.¦Textabruf fehlgeschlagen. Verbindung prüfen oder LRC importieren.¦No se pudo obtener la letra. Comprueba internet o importa LRC.
Загружено строк¦Loaded lines¦Завантажено рядків¦Geladene Zeilen¦Líneas cargadas
LRCLIB · строк¦LRCLIB · lines¦LRCLIB · рядків¦LRCLIB · Zeilen¦LRCLIB · líneas
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
Системный канал медиа подключён.¦System media channel connected.¦Системний медіаканал підключено.¦Systemmedienkanal verbunden.¦Canal multimedia conectado.
Нет подключения к системному каналу медиа.¦System media channel disconnected.¦Немає підключення до системного медіаканалу.¦Systemmedienkanal nicht verbunden.¦Canal multimedia desconectado.
Данные получены.¦Data received.¦Дані отримано.¦Daten empfangen.¦Datos recibidos.
Укажите город в настройках погоды.¦Choose a city in weather settings.¦Укажіть місто в налаштуваннях погоди.¦Stadt in Wettereinstellungen wählen.¦Elige una ciudad en los ajustes del tiempo.
Данные ещё не получены. Проверьте интернет и город.¦No data yet. Check internet and city.¦Даних ще немає. Перевірте інтернет і місто.¦Noch keine Daten. Internet und Stadt prüfen.¦Sin datos aún. Comprueba internet y la ciudad.
Интерфейс отрисовывается по необходимости.¦Interface renders on demand.¦Інтерфейс малюється за потреби.¦Oberfläche wird bei Bedarf gezeichnet.¦La interfaz se dibuja cuando es necesario.
Счётчик игры можно включить на странице «Система».¦Enable the game counter on the System page.¦Увімкніть лічильник гри на сторінці «Система».¦Spielzähler auf der Systemseite aktivieren.¦Activa el contador del juego en Sistema.
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
Версия приложения · 1.1.7¦App version · 1.1.7¦Версія програми · 1.1.7¦App-Version · 1.1.7¦Versión de la aplicación · 1.1.7
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
Своя тема · импорт / экспорт¦Custom theme · import / export¦Власна тема · імпорт / експорт¦Eigenes Thema · Import / Export¦Tema propio · importar / exportar
Тяните правый / нижний край · ПКМ — закончить¦Drag right / bottom edge · right-click to finish¦Тягніть правий / нижній край · ПКМ — завершити¦Rechten / unteren Rand ziehen · Rechtsklick zum Beenden¦Arrastra borde derecho / inferior · clic derecho para terminar
Спрятать островок¦Hide island¦Приховати острівець¦Insel ausblenden¦Ocultar isla
Выровнять по центру¦Align to center¦Вирівняти по центру¦Zentrieren¦Centrar
Сменить тему¦Change theme¦Змінити тему¦Thema wechseln¦Cambiar tema
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
OC - Dynamic Island 1.1.7 · нажмите, чтобы увидеть новинки¦OC - Dynamic Island 1.1.7 · click for new features¦OC - Dynamic Island 1.1.7 · натисніть для перегляду новинок¦OC - Dynamic Island 1.1.7 · klicken für Neuigkeiten¦OC - Dynamic Island 1.1.7 · pulsa para ver novedades
Измерение игры выключено.¦Game measurement off.¦Вимірювання гри вимкнено.¦Spielmessung deaktiviert.¦Medición del juego desactivada.
Ожидание разрешения Windows…¦Waiting for Windows permission…¦Очікування дозволу Windows…¦Warte auf Windows-Berechtigung…¦Esperando permiso de Windows…
ETW подключён. Ожидаю игру DirectX.¦ETW connected. Waiting for a DirectX game.¦ETW підключено. Очікую гру DirectX.¦ETW verbunden. Warte auf DirectX-Spiel.¦ETW conectado. Esperando un juego DirectX.
Измеряется частота Present игры (DirectX).¦Measuring game Present rate (DirectX).¦Вимірюється частота Present гри (DirectX).¦Present-Rate des Spiels wird gemessen (DirectX).¦Midiendo frecuencia Present del juego (DirectX).
Нет кадров DirectX у выбранного окна.¦No DirectX frames from selected window.¦Немає кадрів DirectX у вибраному вікні.¦Keine DirectX-Frames vom gewählten Fenster.¦Sin fotogramas DirectX de la ventana elegida.
Разрешение администратора отменено.¦Administrator permission canceled.¦Дозвіл адміністратора скасовано.¦Administratorberechtigung abgebrochen.¦Permiso de administrador cancelado.
Сеанс измерения остановлен.¦Measurement session stopped.¦Сеанс вимірювання зупинено.¦Messung gestoppt.¦Sesión de medición detenida.
Счётчик игры недоступен. Попробуйте включить его снова.¦Game counter unavailable. Try enabling it again.¦Лічильник гри недоступний. Спробуйте ввімкнути знову.¦Spielzähler nicht verfügbar. Erneut aktivieren.¦Contador del juego no disponible. Actívalo de nuevo.
Микрофон недоступен. Проверьте разрешения Windows.¦Microphone unavailable. Check Windows permissions.¦Мікрофон недоступний. Перевірте дозволи Windows.¦Mikrofon nicht verfügbar. Windows-Berechtigungen prüfen.¦Micrófono no disponible. Revisa los permisos de Windows.
Микрофон включён · разговор с Gemini¦Microphone on · talking with Gemini¦Мікрофон увімкнено · розмова з Gemini¦Mikrofon an · Gespräch mit Gemini¦Micrófono activado · hablando con Gemini
Соединение прервано.¦Connection interrupted.¦З’єднання перервано.¦Verbindung unterbrochen.¦Conexión interrumpida.
Голосовой сеанс завершён сервером.¦Voice session ended by server.¦Голосовий сеанс завершено сервером.¦Sprachsitzung vom Server beendet.¦El servidor terminó la sesión de voz.
Gemini отклонил запрос. Проверьте ключ, модель и доступ Live API.¦Gemini rejected the request. Check key, model and Live API access.¦Gemini відхилив запит. Перевірте ключ, модель і доступ Live API.¦Gemini hat Anfrage abgelehnt. Schlüssel, Modell und Live-API-Zugriff prüfen.¦Gemini rechazó la solicitud. Revisa clave, modelo y acceso a Live API.
Ошибка голосового соединения. Проверьте сеть и модель.¦Voice connection error. Check network and model.¦Помилка голосового з’єднання. Перевірте мережу й модель.¦Sprachverbindungsfehler. Netzwerk und Modell prüfen.¦Error de conexión de voz. Revisa red y modelo.
Подключён .ics, событий на 30 дней: {0}. Обновление — повторный импорт.¦.ics connected, events in 30 days: {0}. Import again to refresh.¦Підключено .ics, подій на 30 днів: {0}. Оновіть повторним імпортом.¦.ics verbunden, Termine in 30 Tagen: {0}. Erneut importieren zum Aktualisieren.¦.ics conectado, eventos en 30 días: {0}. Importa de nuevo para actualizar.
Прогресс программ вынесен в отдельную капсулу справа и отключается в настройках.¦App progress appears in a separate right capsule with a settings toggle.¦Прогрес програм в окремій капсулі праворуч із перемикачем у налаштуваннях.¦App-Fortschritt in separater rechter Kapsel mit Einstellungsschalter.¦Progreso en una cápsula derecha con interruptor en ajustes.
История буфера обмена хранит текст и скриншоты за последние 24 часа.¦Clipboard history stores text and screenshots for 24 hours.¦Історія буфера обміну зберігає текст і знімки екрана за 24 години.¦Zwischenablage speichert Text und Screenshots für 24 Stunden.¦El historial guarda texto y capturas durante 24 horas.
Исправлены прозрачность стекла, переводы разделов и слежение персонажей за курсором.¦Fixed glass transparency, section translations and characters following the cursor.¦Виправлено прозорість скла, переклади розділів і стеження персонажів за курсором.¦Glastransparenz, Übersetzungen und Cursor-Verfolgung der Charaktere korrigiert.¦Corregidas transparencia, traducciones y seguimiento del cursor de los personajes.
Своя раскладка, микшер, календарь, таймеры, караоке и Gemini Live доступны в центре.¦Custom layout, mixer, calendar, timers, karaoke and Gemini Live are available in the hub.¦Власне компонування, мікшер, календар, таймери, караоке й Gemini Live доступні в центрі.¦Eigenes Layout, Mixer, Kalender, Timer, Karaoke und Gemini Live in der Zentrale.¦Diseño propio, mezclador, calendario, temporizadores, karaoke y Gemini Live en el centro.
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
""";
}
