-- Etapa 10 (seguridad de la web). Ejecutar UNA vez en la base Seguridad (se puede repetir sin problema).
--
-- Ocho triggers de Delphi (ERwin, 1999) en Seg_Grupo, Seg_Grupo_Usuario, Seg_Modulos y Seg_Log_Actividad usan
-- "raiserror @errno @errmsg", una forma que SQL Server ya no acepta (desde la version 2012). En un servidor nuevo el
-- trigger no compila y FALLA SIEMPRE que se dispara: por ejemplo, no se puede agregar un usuario a un grupo
-- (tI_Seg_Grupo_Usuario), ni desde la web ni desde el programa de Seguridad de Delphi.
--
-- Este script cambia SOLO esa linea por "raiserror(@errmsg, 16, 1)" (el mismo mensaje; la logica y el rollback
-- quedan igual). No toca tablas ni datos. Si un trigger ya esta bien, no lo toca.

SET NOCOUNT ON;

DECLARE @nombre sysname, @def nvarchar(max), @pos int;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT t.name, OBJECT_DEFINITION(t.object_id)
    FROM sys.triggers t
    WHERE OBJECT_NAME(t.parent_id) LIKE 'Seg[_]%'
      AND OBJECT_DEFINITION(t.object_id) LIKE '%raiserror @errno @errmsg%';
OPEN c;
FETCH NEXT FROM c INTO @nombre, @def;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @def = REPLACE(@def, 'raiserror @errno @errmsg', 'raiserror(@errmsg, 16, 1)');
    SET @pos = PATINDEX('%create%trigger%', @def);
    -- "create trigger" -> "ALTER TRIGGER" (lo primero del texto)
    SET @def = STUFF(@def, @pos, CHARINDEX('trigger', LOWER(@def), @pos) + 7 - @pos, 'ALTER TRIGGER');
    EXEC sp_executesql @def;
    PRINT 'Corregido: ' + @nombre;
    FETCH NEXT FROM c INTO @nombre, @def;
END
CLOSE c;
DEALLOCATE c;

SELECT COUNT(*) AS QuedanSinCorregir
FROM sys.triggers t
WHERE OBJECT_DEFINITION(t.object_id) LIKE '%raiserror @errno%';
