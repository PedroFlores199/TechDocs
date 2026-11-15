# Validación

Validado en macOS ARM64 con SDK .NET 10.0.404, Node 22 y Docker Desktop. La solución y el frontend compilan; 21 pruebas pasan con `RUN_CONTAINER_TESTS=1`, incluida SQL Server real en contenedor amd64.

Cobertura funcional: JWT y roles, flujo de revisión/aprobación, concurrencia de piezas, versiones y su contenido anterior, referencias inválidas, búsqueda sin acentos, exportación PDF, parseo XML y bloqueo de entidades externas. El despliegue completo utiliza MongoDB, Elasticsearch, Redis y SQL Server; el modo local tiene adaptadores expresos.

El benchmark de persistencia comprueba el recuento de las 50.000 filas antes y después de cada método. Una sola ejecución sirve para documentar el experimento, no para establecer una garantía de rendimiento.

El README describe las funciones opcionales y los límites del modo local. Las imágenes de bloques son enlaces HTTPS; no se descarga contenido remoto durante la exportación. Las cuentas, contraseñas y catálogo son únicamente de demostración. No se incluye una afirmación de auditoría externa ni de ausencia absoluta de defectos.
