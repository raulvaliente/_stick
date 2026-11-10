using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace _stick;

public partial class MarkdownGuideView : UserControl
{
    private const string Example = """
        # Prueba de Markdown

        **Negrita**, *cursiva*, ~~tachado~~ y ==resaltado==.

        | Tarea | Estado |
        | :--- | :---: |
        | Crear una nota | Listo |
        | Probar el menú de formato | Pendiente |

        > Mueve el cursor para ver el formato.

        - [x] Crear una tabla
        - [ ] Probar el menú con clic derecho

        ---

        """ + "\n\u0060\u0060\u0060python\nprint(\"Hola, _stick\")\n\u0060\u0060\u0060";

    public MarkdownGuideView() => InitializeComponent();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Example);
            CopyButton.Content = "Ejemplo copiado";
        }
        catch (ExternalException)
        {
            CopyButton.Content = "Volver a intentar";
        }
    }
}
