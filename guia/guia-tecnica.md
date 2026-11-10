# Guía técnica de _stick

`_stick` guarda notas de escritorio por bibliotecas. Una biblioteca se puede vincular a un ejecutable de Windows para abrir sus notas cuando arranca la aplicación y quitarlas de la vista cuando deja de ejecutarse.

## Cómo leer el proyecto

- Los archivos **.xaml** describen las ventanas y sus controles. Los **.xaml.cs** contienen el código que se ejecuta al pulsar un botón, escribir o arrastrar. `InitializeComponent()` carga el XAML y conecta esos eventos con sus métodos.
- Un **binding** conecta una propiedad de un control con un dato de su `DataContext`. Por ejemplo, el editor enlaza su texto con `NoteViewModel.Text` para actualizar la nota mientras se escribe.
- [`_stick.csproj`](../proyecto/_stick.csproj) configura la compilación para .NET 10 en Windows, habilita WPF y Windows Forms e incluye el icono y la fuente. [`_stick.slnx`](../proyecto/_stick.slnx) agrupa el proyecto para abrirlo en Visual Studio.

## Arranque y bibliotecas

| Archivo o clase | Qué hace |
| --- | --- |
| [`App.xaml`](../proyecto/App.xaml) y [`App.xaml.cs`](../proyecto/App.xaml.cs) | El XAML define estilos comunes y configura el cierre explícito. La clase `App` carga los datos, vigila los procesos y coordina el panel, la bandeja y las ventanas de notas. |
| [`CollectionsWindow.xaml`](../proyecto/CollectionsWindow.xaml) y [`CollectionsWindow.xaml.cs`](../proyecto/CollectionsWindow.xaml.cs) | Muestran el panel de bibliotecas. Sus botones llaman a `App` para vincular, renombrar, cambiar el ejecutable, pausar la detección, abrir notas o restaurar la última nota de la papelera. **Borrar** pide confirmación antes de quitar una biblioteca y sus notas. |
| [`AppPickerWindow.xaml`](../proyecto/AppPickerWindow.xaml) y [`AppPickerWindow.xaml.cs`](../proyecto/AppPickerWindow.xaml.cs) | Permiten elegir una aplicación abierta o buscar un `.exe`. `RefreshApplicationsAsync()` actualiza la lista sin bloquear el panel. |
| [`ExecutableHelpWindow.xaml`](../proyecto/ExecutableHelpWindow.xaml) y [`ExecutableHelpWindow.xaml.cs`](../proyecto/ExecutableHelpWindow.xaml.cs) | Muestran las indicaciones para localizar un ejecutable en Windows. El código de esta ventana solo carga la vista. |
| [`CollectionNameWindow.xaml`](../proyecto/CollectionNameWindow.xaml) y [`CollectionNameWindow.xaml.cs`](../proyecto/CollectionNameWindow.xaml.cs) | Recogen el nombre que se mostrará en la biblioteca y en la cabecera de sus notas. |
| [`ProcessMonitor.cs`](../proyecto/ProcessMonitor.cs) | `FindOpenApplications()` busca procesos de la sesión que tengan ventana principal y devuelve pares de nombre y ruta (`RunningApplication`). `FindRunning()` devuelve los identificadores de los procesos cuya ruta coincide con un ejecutable vigilado. |

1. `App.OnStartup()` carga las bibliotecas y preferencias, prepara la bandeja y arranca la comprobación de procesos. Si ya hay una instancia para ese usuario, el segundo arranque le envía una señal para abrir el panel.
2. `App.Scan()` comprueba cada dos segundos las bibliotecas con detección activa. Si aparece un conjunto de procesos nuevo para un ejecutable, llama a `OpenCollection()`. Si desaparece el último proceso de la ruta, llama a `CloseCollection()`. El nombre que se ve en el panel se puede cambiar con `RenameCollection()` sin cambiar la ruta vigilada.
3. `OpenSettings()` abre o recupera el panel desde la bandeja. La **×** del panel lo minimiza cuando el icono de la barra de tareas está activo. En ambos modos, **Salir de _stick** es la acción que guarda los cambios pendientes y termina la aplicación. El inicio con Windows se configura en el Registro del usuario.

## Notas y papelera

| Archivo o clase | Qué hace |
| --- | --- |
| [`Models.cs`](../proyecto/Models.cs) | `NoteCollection` guarda la ruta, el nombre personalizado, si la detección está activa y las listas de notas y de papelera. `Note` guarda el texto, el color, el tamaño, la posición y las marcas que identifican las notas de tutorial. |
| [`NoteViewModel.cs`](../proyecto/NoteViewModel.cs) | Adapta una `Note` a la ventana. `SetColor()`, `SetSize()` y `SetPosition()` modifican el modelo y solicitan guardado. `INotifyPropertyChanged` avisa a WPF cuando cambia una propiedad que usa la vista, como el fondo. |
| [`NoteWindow.xaml`](../proyecto/NoteWindow.xaml) y [`NoteWindow.xaml.cs`](../proyecto/NoteWindow.xaml.cs) | Dibujan la nota y gestionan el editor, el cambio de color, el tamaño y el arrastre. `PlaceBeside()` busca hueco junto a la nota de origen. `TrackPosition()` recupera las coordenadas guardadas y ajusta la ventana al área útil del monitor. |
| [`NoteColorPickerWindow.xaml`](../proyecto/NoteColorPickerWindow.xaml) y [`NoteColorPickerWindow.xaml.cs`](../proyecto/NoteColorPickerWindow.xaml.cs) | Muestran los colores predeterminados y la paleta personal. `SelectColor()` mantiene sincronizados los controles RGB, el valor HEX y la vista previa. **Guardar en mi paleta** y **Aplicar** son acciones independientes. |
| [`TrashLayout.cs`](../proyecto/TrashLayout.cs) | `TrashAnchor` representa la posición elegida. `TrashLayout.Normalize()` ajusta los valores a sus límites y `GetBounds()` calcula el rectángulo de la papelera dentro del monitor. |
| [`TrashSettingsWindow.xaml`](../proyecto/TrashSettingsWindow.xaml) y [`TrashSettingsWindow.xaml.cs`](../proyecto/TrashSettingsWindow.xaml.cs) | Permiten elegir la posición, el tamaño y el margen de la papelera. `UpdatePreview()` muestra el resultado y `Save_Click()` pasa el ajuste a `App`. |
| [`TrashDropWindow.xaml`](../proyecto/TrashDropWindow.xaml) y [`TrashDropWindow.xaml.cs`](../proyecto/TrashDropWindow.xaml.cs) | Son la ventana temporal que aparece durante un arrastre individual. `Contains()` comprueba si el puntero está dentro y `Highlight()` cambia el estado visual. |
| [`TrashDropVisual.xaml`](../proyecto/TrashDropVisual.xaml) y [`TrashDropVisual.xaml.cs`](../proyecto/TrashDropVisual.xaml.cs) | Definen el aspecto de la papelera que comparten la ventana de arrastre y la vista previa. `IsHighlighted` permite que el XAML cambie su apariencia al pasar por encima. |

En `App`, `ShowNote()` mantiene una ventana por cada nota abierta. Pulsar **+** llama a `AddNote()`, que copia el color de la nota de origen y abre la nueva a su lado si encuentra hueco. La **×** de una nota cierra su ventana. El dato sigue en la biblioteca y **Abrir notas** lo recupera.

`StartDrag()` guarda las posiciones iniciales. Con **Ctrl**, incluye las ventanas visibles de la biblioteca. En un arrastre individual, `MoveDrag()` muestra la papelera y `FinishDrag()` comprueba dónde se soltó la nota. Si cae dentro, `App.DeleteNote()` la mueve de `Notes` a `TrashedNotes`. `RestoreLastNote()` hace el recorrido inverso con la última nota retirada.

## Cómo funciona Markdown

| Archivo | Qué hace |
| --- | --- |
| [`MarkdownLine.cs`](../proyecto/MarkdownLine.cs) | `Parse()` separa el texto en líneas y reconoce bloques, tablas y estilos. `MarkdownLine` conserva el tipo y los índices de cada línea. `MarkdownPart` guarda un fragmento con su formato y `MarkdownTableRow` reúne las celdas y sus alineaciones. |
| [`MarkdownPreview.cs`](../proyecto/MarkdownPreview.cs) | `Attach()` escucha cambios del editor. `RequestRender()` espera a que WPF calcule la posición de las líneas y `OnRender()` dibuja el formato encima del texto visible. La línea activa conserva la sintaxis para que se pueda editar. |
| [`MarkdownEditing.cs`](../proyecto/MarkdownEditing.cs) | `BuildContextMenu()` prepara las acciones de formato e inserción. `UpdateSuggestions()` ofrece plantillas según lo escrito o al pulsar **Ctrl + Espacio**. `Replace()` modifica el texto y agrupa cada acción en un único paso de deshacer. |

El `TextBox` contiene el Markdown original. La vista previa solo cambia cómo se ve. Si se acepta una sugerencia o se aplica negrita desde el menú, `MarkdownEditing` escribe los símbolos en el editor y el enlace con `NoteViewModel.Text` actualiza la nota.

## Tutoriales y estilos

- `App.EnsureBuiltInCollection()` prepara la biblioteca propia de _stick. `NotesToOpen()` decide qué tutorial mostrar primero y `RevealMarkdownGuide()` abre la guía verde al cerrar la amarilla.
- [`WelcomeNoteView.xaml`](../proyecto/WelcomeNoteView.xaml) contiene la guía amarilla. Su [código asociado](../proyecto/WelcomeNoteView.xaml.cs) activa las animaciones de la papelera al pasar el puntero.
- [`MarkdownGuideView.xaml`](../proyecto/MarkdownGuideView.xaml) muestra la guía verde. Su [código asociado](../proyecto/MarkdownGuideView.xaml.cs) copia un ejemplo al portapapeles.
- [`PixelTheme.xaml`](../proyecto/PixelTheme.xaml) reúne los colores y estilos del panel. Carga Pixelify Sans desde [`proyecto/assets/`](../proyecto/assets/).

## Dónde se guardan los datos

[`LocalStore.cs`](../proyecto/LocalStore.cs) convierte los datos de la aplicación a JSON y los recupera al arrancar. Guarda primero en un archivo temporal y sustituye el destino cuando termina de escribir. Los archivos de usuario están en `%LOCALAPPDATA%\_stick`.

| Archivo | Contenido |
| --- | --- |
| `notes.json` | Bibliotecas, notas activas y papelera de cada biblioteca. |
| `colors.json` | Colores guardados en la paleta personal. |
| `trash-layout.json` | Posición, tamaño y margen de la papelera. |
| `taskbar-icon.json` | Preferencia del icono de la barra de tareas. |
| `app-picker-tutorial.seen` | Marca que el tutorial del selector de aplicaciones ya se descartó. |

`App.QueueSave()` espera 500 ms desde el último cambio para evitar guardar en cada pulsación. `Flush()` escribe lo que quede pendiente al cerrar una nota o salir de la aplicación.
