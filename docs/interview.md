# Recorrido para una entrevista

1. **Problema:** una ingeniería necesita encontrar procedimientos y conocer qué documentos cambian cuando cambia una pieza.
2. **Demostración:** busca «junta torica», abre el resultado, muestra sus bloques y versiones. Cambia una pieza y enseña la tarea de impacto.
3. **Permisos:** pasa por Redactor, Revisor y Aprobador. La separación de funciones está en el backend; ocultar un botón no es suficiente.
4. **Datos:** SQL impone relaciones; Mongo conserva el contenido variable; Elasticsearch busca una proyección. Redis reduce lecturas de fichas.
5. **Fallo:** explica la outbox. No existe una transacción que abarque SQL, Mongo y Elasticsearch; la réplica es eventual y reintentable.
6. **Prueba:** muestra el test que rechaza una revisión concurrente y el que levanta SQL Server con Testcontainers.
7. **Decisión:** comenta el benchmark real de 50.000 piezas y que no mide todo el recorrido HTTP/ETL.

Preguntas para preparar: ¿qué ocurre si Mongo está apagado?, ¿por qué el buscador puede tardar en reflejar un cambio?, ¿cómo evitarías dos aprobaciones simultáneas?, ¿qué migraciones y permisos añadirías antes de un despliegue público?
